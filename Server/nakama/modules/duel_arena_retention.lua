-- Completed tournament brackets are shared by all participants in one queue.
local nk = require("nakama")
local M = {}
local SYSTEM = "00000000-0000-0000-0000-000000000000"
local TOURNAMENTS, QUEUES = "arena_tournament", "arena_queue"
local GRACE_SECONDS = 300
local TIERS = {
  smith = { green = "green", blue = "blue", purple = "purple" },
  ore = { ["500"] = "500", ["2000"] = "2000", fixed = "500" },
  gold = { ["600"] = "600", ["2400"] = "2400", fixed = "600" },
}
local COHORTS = {}
for kind, tiers in pairs(TIERS) do
  for _, tier in pairs(tiers) do COHORTS[kind .. ":" .. tier] = true end
end
local FORECAST_ROUNDS = { "qf", "sf" }
local ROUNDS = { "qf", "sf", "final" }

local function value(row)
  if type(row.value) == "table" then return row.value end
  if type(row.value) ~= "string" then return nil end
  local ok, decoded = pcall(nk.json_decode, row.value)
  if ok and type(decoded) == "table" then return decoded end
end

local function timestamp(input, now)
  local t = tonumber(input)
  if t and t > 0 and t <= now and t == math.floor(t) then return t end
end

local function forecasts_resolved(t)
  if t.side_bets == nil then return true end
  if type(t.side_bets) ~= "table" then return false end
  for _, bets in pairs(t.side_bets) do
    if type(bets) ~= "table" then return false end
    for _, round in ipairs(FORECAST_ROUNDS) do
      local picks = bets[round]
      if picks ~= nil and (type(picks) ~= "table" or (next(picks) ~= nil and
          (type(t.bets_resolved) ~= "table" or t.bets_resolved[round] ~= true))) then
        return false
      end
    end
  end
  return true
end

local function craft_settled(t, player, outcome)
  local meta = t.parts[player]
  if type(meta) ~= "table" then return false end
  if meta.bot == true then return true end
  local entry_version = type(meta.entry_cost) == "table" and meta.entry_cost.version or t.rules_version
  if entry_version == nil or entry_version == 1 then return true end
  local receipt = type(t.craft_receipts) == "table" and t.craft_receipts[player]
  return entry_version == 2 and type(receipt) == "table" and receipt.user_id == player and receipt.outcome == outcome
end

local function smith_settled(t, champion)
  if t.kind ~= "smith" then return true end
  if champion and not craft_settled(t, champion, "champion") then return false end
  for _, round in ipairs(ROUNDS) do
    local pairs = t[round]
    if pairs ~= nil and type(pairs) ~= "table" then return false end
    for _, pair in ipairs(pairs or {}) do
      if type(pair) ~= "table" then return false end
      if pair.status == "done" then
        local winner = pair.winner_uid
        local loser
        if winner == pair.uid_a then loser = pair.uid_b
        elseif winner == pair.uid_b then loser = pair.uid_a end
        if type(winner) ~= "string" or winner == "" or type(loser) ~= "string" or loser == "" or
            not craft_settled(t, loser, "participation") then return false end
      end
    end
  end
  return true
end

local function terminal(row, now)
  if type(row) ~= "table" or row.collection ~= TOURNAMENTS or row.user_id ~= SYSTEM or
      type(row.key) ~= "string" or row.key == "" or type(row.version) ~= "string" or row.version == "" or
      row.permission_read ~= 0 or row.permission_write ~= 0 then return nil end
  local t = value(row)
  if not t or t.id ~= row.key or t.phase ~= "done" or type(t.parts) ~= "table" or next(t.parts) == nil then return nil end
  local tiers = TIERS[t.kind]
  local tier = tiers and tiers[t.bet_tier]
  local done_at, updated_at = timestamp(t.done_at, now), timestamp(row.update_time, now)
  if not tier or not done_at or not updated_at or not forecasts_resolved(t) then return nil end
  local granted_at = done_at
  if t.aborted == true then
    if t.reward_receipt ~= nil then return nil end
    for player, part in pairs(t.parts) do
      if type(part) ~= "table" or (part.bot ~= true and
          (type(t.eliminated) ~= "table" or t.eliminated[player] ~= true)) then return nil end
    end
    if not smith_settled(t) then return nil end
  else
    local final = type(t.final) == "table" and t.final[1]
    if type(final) ~= "table" or final.status ~= "done" then return nil end
    local winner = final.winner_uid
    local part = t.parts[winner]
    if type(winner) ~= "string" or winner == "" or type(part) ~= "table" then return nil end
    if part.bot ~= true then
      local receipt = t.reward_receipt
      if type(receipt) ~= "table" or receipt.user_id ~= winner then return nil end
      granted_at = timestamp(receipt.granted_at, now)
      if not granted_at or not smith_settled(t, winner) then return nil end
    elseif t.reward_receipt ~= nil or not smith_settled(t) then return nil end
  end
  return { row = row, cohort = t.kind .. ":" .. tier, done_at = done_at,
    quiet_at = math.max(done_at, updated_at, granted_at) }
end

local function newer(a, b)
  if a.done_at ~= b.done_at then return a.done_at > b.done_at end
  return a.row.key > b.row.key
end

local function remember(latest, candidate)
  local pair = latest[candidate.cohort] or {}
  latest[candidate.cohort] = pair
  for _, current in ipairs(pair) do if current.row.key == candidate.row.key then return end end
  if not pair[1] or newer(candidate, pair[1]) then pair[2], pair[1] = pair[1], candidate
  elseif not pair[2] or newer(candidate, pair[2]) then pair[2] = candidate end
end

-- The saved top two are only witnesses. Re-read them before deleting anything
-- so stale maintenance state or a removed witness cannot authorize deletion.
function M.deletions(state, rows, now)
  local candidates, by_key = {}, {}
  for _, row in ipairs(rows or {}) do
    local candidate = terminal(row, now)
    if candidate then
      candidates[#candidates + 1] = candidate
      by_key[row.key] = true
    end
  end
  if #candidates == 0 then return {} end

  local reads = { { user_id = SYSTEM, collection = QUEUES, key = "state" } }
  local saved = type(state.arena_latest) == "table" and state.arena_latest or {}
  for cohort in pairs(COHORTS) do
    local pair = saved[cohort]
    if type(pair) == "table" then
      for i = 1, 2 do
        local key = pair[i]
        if type(key) == "string" and key ~= "" and not by_key[key] then
          reads[#reads + 1] = { user_id = SYSTEM, collection = TOURNAMENTS, key = key }
          by_key[key] = true
        end
      end
    end
  end
  local observed = nk.storage_read(reads)
  if type(observed) ~= "table" then return {} end
  local latest, referenced, witnesses = {}, {}, {}
  for _, row in ipairs(observed) do
    if row.collection == QUEUES then
      local registry = value(row)
      if not registry or type(registry.users) ~= "table" then return {} end
      for _, ref in pairs(registry.users) do
        if type(ref) ~= "table" then return {} end
        if type(ref.tid) == "string" and ref.tid ~= "" then referenced[ref.tid] = true end
      end
    else
      local candidate = terminal(row, now)
      if candidate then witnesses[#witnesses + 1] = candidate end
    end
  end
  -- Queue references are excluded from witness selection as well as deletion.
  -- Terminal transitions clear matching refs atomically; no new queue entrant
  -- can point at a previously completed bracket.
  for _, witness in ipairs(witnesses) do
    if not referenced[witness.row.key] then remember(latest, witness) end
  end
  for _, candidate in ipairs(candidates) do
    if not referenced[candidate.row.key] then remember(latest, candidate) end
  end
  local next_latest, deletes = {}, {}
  for cohort, pair in pairs(latest) do
    if pair[1] then next_latest[cohort] = { pair[1].row.key, pair[2] and pair[2].row.key or nil } end
  end
  for _, candidate in ipairs(candidates) do
    local row = candidate.row
    local pair = latest[candidate.cohort]
    if not referenced[row.key] and candidate.quiet_at <= now - GRACE_SECONDS and
        pair and pair[2] and newer(pair[2], candidate) then
      deletes[#deletes + 1] = { user_id = SYSTEM, collection = TOURNAMENTS, key = row.key, version = row.version }
    end
  end
  state.arena_latest = next_latest
  return deletes
end

return M
