-- Bounded owner-scoped scans. A deletion needs two strictly newer witnesses.
local nk = require("nakama")
local M = {}
local SYSTEM = "00000000-0000-0000-0000-000000000000"
local OWNER_PAGE, PROBE_LIMIT, DELETE_LIMIT = 100, 16, 8

function M.past(value, cutoff)
  local timestamp = tonumber(value)
  if timestamp and timestamp > 0 and timestamp <= cutoff and timestamp == math.floor(timestamp) then
    return timestamp
  end
end

function M.value(row)
  if type(row) ~= "table" or type(row.version) ~= "string" or row.version == ""
      or row.permission_read ~= 0 or row.permission_write ~= 0 then return nil end
  if type(row.value) == "table" then return row.value end
  local ok, decoded = pcall(nk.json_decode, row.value or "")
  if ok and type(decoded) == "table" then return decoded end
end

function M.read(identities)
  local ok, rows = pcall(nk.storage_read, identities)
  if ok and type(rows) == "table" then return rows end
end

function M.match_probe()
  local seen, remaining = {}, PROBE_LIMIT
  return function(id)
    if type(id) ~= "string" or id == "" then return false end
    if seen[id] == nil then
      if remaining <= 0 then return false end
      remaining = remaining - 1
      local ok, match, err = pcall(nk.match_get, id)
      seen[id] = ok and match == nil and err == nil
    end
    return seen[id]
  end
end

local function newer(left, right)
  if left.finished ~= right.finished then return left.finished > right.finished end
  if left.started ~= right.started then return left.started > right.started end
  return left.key > right.key
end

function M.new(now, max_owners, policy)
  local collection, cutoff = policy.collection, now - 120
  local seen, owner_lookups = {}, 0
  local scanner = {}

  local function terminal(row, uid)
    if type(row) ~= "table" or row.user_id ~= uid or row.collection ~= collection
        or type(row.key) ~= "string" or not M.past(row.update_time, cutoff) then return nil end
    local value = M.value(row)
    if not value then return nil end
    local finished, started = policy.terminal(uid, row.key, value, cutoff)
    if finished then
      return { key = row.key, version = row.version, value = value, finished = finished, started = started or 0 }
    end
  end

  function scanner.remaining_owners()
    return max_owners - owner_lookups
  end

  function scanner.deletions(row)
    local uid = type(row) == "table" and row.user_id
    if type(uid) ~= "string" or uid == "" or uid == SYSTEM or seen[uid] or owner_lookups >= max_owners then return {} end
    local trigger = terminal(row, uid)
    if not trigger then return {} end
    seen[uid] = true
    owner_lookups = owner_lookups + 1
    local ok, rows = pcall(nk.storage_list, uid, collection, OWNER_PAGE, "")
    if not ok or type(rows) ~= "table" then return {} end
    local ranked, keys = {}, {}
    for _, owner_row in ipairs(rows) do
      if type(owner_row) ~= "table" or type(owner_row.key) ~= "string" or owner_row.user_id ~= uid
          or owner_row.collection ~= collection then return {} end
      if keys[owner_row.key] then return {} end
      keys[owner_row.key] = true
      local receipt = terminal(owner_row, uid)
      if receipt then ranked[#ranked + 1] = receipt end
    end
    -- An outer scan may reach a row beyond the bounded owner page. The
    -- trigger can be a deletion target, but only owner-page rows witness it.
    if not keys[trigger.key] then ranked[#ranked + 1] = trigger end
    if #ranked <= 2 then return {} end
    table.sort(ranked, newer)
    local assess = policy.assess(uid, ranked, M.match_probe())
    if not assess then return {} end

    local first, second
    for index = 1, #ranked - 1 do
      local entry = ranked[index]
      if keys[entry.key] and assess(entry.value) then
        if not first then first = entry
        elseif policy.distinct(first.value, entry.value) then second = entry; break end
      end
    end
    if not second then return {} end
    local deletes = {}
    for index = #ranked, 1, -1 do
      local entry = ranked[index]
      if not newer(second, entry) or #deletes == DELETE_LIMIT then break end
      local complete, protected = assess(entry.value)
      if complete and not protected then
        deletes[#deletes + 1] = { user_id = uid, collection = collection, key = entry.key, version = entry.version }
      end
    end
    return deletes
  end

  return scanner
end

return M
