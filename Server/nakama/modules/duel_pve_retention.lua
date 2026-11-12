-- Read-only proofs for the maintenance worker's versioned PvE receipt deletion.
local nk = require("nakama")
local M = { COLLECTION = "duel_pve_entry", GRACE_SECONDS = 120 }
local JOURNAL = "duel_match3_journal"
local SYSTEM = "00000000-0000-0000-0000-000000000000"
local OWNER_PAGE, MATCH_PROBE_LIMIT, DELETE_LIMIT = 100, 16, 8
local NONE = {}

local function decode(row)
  if type(row.value) == "table" then return row.value end
  local ok, value = pcall(nk.json_decode, row.value or "")
  if ok and type(value) == "table" then return value end
end

local function past(value, limit)
  local time = tonumber(value)
  if time and time > 0 and time <= limit and time == math.floor(time) then return time end
end

local function terminal(row, cutoff)
  if type(row) ~= "table" or row.collection ~= M.COLLECTION or type(row.key) ~= "string" or row.key:sub(1, 7) ~= "launch:"
      or type(row.version) ~= "string" or row.version == ""
      or row.permission_read ~= 0 or row.permission_write ~= 0 or not past(row.update_time, cutoff) then return nil end
  local value = decode(row)
  if not value or type(value.token) ~= "string" or value.token == "" or row.key ~= "launch:" .. value.token
      or (value.remaining_refund ~= nil and (type(value.remaining_refund) ~= "table" or next(value.remaining_refund) ~= nil)) then return nil end
  local finished
  if value.status == "consumed" and type(value.match_id) == "string" and value.match_id ~= ""
      and type(value.run_id) == "string" and value.run_id ~= "" then
    finished = past(value.consumed_at, cutoff)
  elseif value.status == "refunded" then
    -- Only a reservation refunded before binding lacks a match ID.
    if (value.run_id ~= nil or value.bound_at ~= nil or value.match_id ~= nil)
        and (type(value.match_id) ~= "string" or value.match_id == "") then return nil end
    finished = past(value.refunded_at, cutoff)
  end
  if not finished then return nil end
  return { key = row.key, version = row.version, value = value, finished = finished,
    reserved = past(value.reserved_at, cutoff) or 0 }
end

local function newer(left, right)
  if left.finished ~= right.finished then return left.finished > right.finished end
  if left.reserved ~= right.reserved then return left.reserved > right.reserved end
  return left.key > right.key
end

local function owner_controls(uid)
  local rows = nk.storage_read({
    { user_id = uid, collection = M.COLLECTION, key = "active" },
    { user_id = uid, collection = M.COLLECTION, key = "refunds" },
    { user_id = uid, collection = JOURNAL, key = "pending" },
    { user_id = uid, collection = JOURNAL, key = "retention_v2" },
  })
  if type(rows) ~= "table" then return nil end
  local controls = { credits = {}, pending = {}, open = {}, open_runs = {}, open_matches = {},
    probes = {}, probes_left = MATCH_PROBE_LIMIT }
  for _, row in ipairs(rows) do
    local value = decode(row)
    if not value or row.user_id ~= uid or row.permission_read ~= 0 or row.permission_write ~= 0 then return nil end
    if row.collection == M.COLLECTION and row.key == "active" then
      if type(value.token) ~= "string" or value.token == "" then return nil end
      controls.active = value
    elseif row.collection == M.COLLECTION and row.key == "refunds" then
      if type(value.tokens) ~= "table" then return nil end
      controls.credits = value.tokens
    elseif row.collection == JOURNAL and row.key == "pending" then
      if type(value.entries) ~= "table" then return nil end
      controls.pending = value.entries
    elseif row.collection == JOURNAL and row.key == "retention_v2" then
      if value.schema ~= 2 or type(value.open) ~= "table" then return nil end
      for _, entry in pairs(value.open) do
        if type(entry) ~= "table" or type(entry.id) ~= "string" or entry.id == "" then return nil end
        controls.open[entry.id] = true
        if entry.origin ~= nil then
          if type(entry.origin) ~= "table" then return nil end
          local run_id, match_id = entry.origin.root_result, entry.origin.match_id
          if type(run_id) == "string" and run_id ~= "" then controls.open_runs[run_id] = true end
          if type(match_id) == "string" and match_id ~= "" then controls.open_matches[match_id] = true end
        end
      end
    else
      return nil
    end
  end
  return controls
end

local function pending(controls, receipt)
  local function has(id)
    return type(id) == "string" and id ~= "" and (controls.pending[id] ~= nil or controls.open[id] == true)
  end
  if receipt.journal_tickets ~= nil then
    if type(receipt.journal_tickets) ~= "table" then return true end
    for id in pairs(receipt.journal_tickets) do if has(id) then return true end end
  end
  local run_id, match_id = receipt.run_id, receipt.match_id
  return has(receipt.journal_id) or has(receipt.result_journal_id)
      or (match_id and (controls.open_matches[match_id] or has("consumable-bind:" .. match_id)))
      or (type(run_id) == "string" and run_id ~= "" and (controls.open_runs[run_id]
        or has(run_id) or has(run_id .. ":pve") or has(run_id .. ":stats")))
end

local function active(controls, receipt)
  local current = controls.active
  return current and (current.token == receipt.token
      or (receipt.run_id and current.run_id == receipt.run_id)
      or (receipt.match_id and current.match_id == receipt.match_id))
end

local function settled(controls, receipt)
  if controls.credits[receipt.token] ~= nil or pending(controls, receipt) then return false end
  if not receipt.match_id then return true end
  -- The consumed state proves that entry was paid, not that the match ended.
  local match_id = receipt.match_id
  if controls.probes[match_id] == nil then
    if controls.probes_left <= 0 then return false end
    controls.probes_left = controls.probes_left - 1
    local ok, match, err = pcall(nk.match_get, match_id)
    controls.probes[match_id] = ok and match == nil and err == nil
  end
  return controls.probes[match_id]
end

function M.new(now, max_owners)
  local cutoff = now - M.GRACE_SECONDS
  local seen, owner_lookups = {}, 0
  local scanner = {}

  function scanner.remaining_owners()
    return max_owners - owner_lookups
  end

  function scanner.deletions(row)
    if type(row) ~= "table" or type(row.user_id) ~= "string" or row.user_id == "" or row.user_id == SYSTEM then return NONE end
    local uid = row.user_id
    if seen[uid] or scanner.remaining_owners() <= 0 then return NONE end
    local trigger = terminal(row, cutoff)
    if not trigger then return NONE end
    seen[uid] = true
    owner_lookups = owner_lookups + 1
    local ok, rows = pcall(nk.storage_list, uid, M.COLLECTION, OWNER_PAGE, "")
    if not ok or type(rows) ~= "table" then return NONE end
    local ranked, trigger_seen = {}, false
    for _, entry in ipairs(rows) do
      if type(entry) == "table" and entry.user_id == uid then
        if entry.key == trigger.key then trigger_seen = true end
        local receipt = terminal(entry, cutoff)
        if receipt then ranked[#ranked + 1] = receipt end
      end
    end
    -- The outer global page can supply an additional candidate outside this
    -- owner's bounded page. Any newer witness must still come from that page.
    if #ranked < 2 then return NONE end
    if not trigger_seen then ranked[#ranked + 1] = trigger end
    if #ranked <= 2 then return NONE end
    table.sort(ranked, newer)
    local controls_ok, controls = pcall(owner_controls, uid)
    if not controls_ok or not controls then return NONE end

    local first, second
    for i, entry in ipairs(ranked) do
      local receipt = entry.value
      if i == #ranked then break end
      if not trigger_seen and entry == trigger then receipt = nil end
      if receipt and (not first or ((not receipt.run_id or first.run_id ~= receipt.run_id)
          and (not receipt.match_id or first.match_id ~= receipt.match_id))) and settled(controls, receipt) then
        if first then second = entry; break end
        first = receipt
      end
    end
    if not second then return NONE end
    local deletes = {}
    for i = #ranked, 1, -1 do
      local entry = ranked[i]
      if not newer(second, entry) or #deletes == DELETE_LIMIT then break end
      if not active(controls, entry.value) and settled(controls, entry.value) then
        deletes[#deletes + 1] = { user_id = uid, collection = M.COLLECTION, key = entry.key, version = entry.version }
      end
    end
    return deletes
  end

  return scanner
end

return M
