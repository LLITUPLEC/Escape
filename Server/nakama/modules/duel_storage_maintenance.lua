-- Bounded, versioned cleanup of disposable server records. No SQL and no client RPC.
local nk = require("nakama")
local retention_ok, JournalRetention = pcall(require, "modules.duel_journal_retention")
if not retention_ok then JournalRetention = require("duel_journal_retention") end
local M = {}
local SYSTEM = "00000000-0000-0000-0000-000000000000"
local COLLECTION, KEY = "duel_storage_maintenance", "v1"
local DAY, BOOTSTRAP_PAGE, PAGE, INTERVAL, LEASE = 86400, 50, 100, 60, 120
local PAGE_BUDGET, TIME_BUDGET_MS = 10, 250
local collections = { "arena_tournament", "duel_api_rate_limits", "duel_race_entry", "duel_pve_entry", "duel_match3_journal" }
local next_try = 0
local recovery, can_prune_receipt

local function value(row)
  if type(row) ~= "table" then return nil end
  if type(row.value) == "table" then return row.value end
  local ok, decoded = pcall(nk.json_decode, row.value or "")
  if ok and type(decoded) == "table" then return decoded end
end
local function old(timestamp, now, age)
  local t = tonumber(timestamp)
  return t ~= nil and t == t and t > 0 and t <= now - age
end
local function read(uid, collection, key)
  local rows = nk.storage_read({ { user_id = uid, collection = collection, key = key } })
  return rows and rows[1]
end
local function write(state, version)
  return nk.storage_write({ { user_id = SYSTEM, collection = COLLECTION, key = KEY,
    value = state, version = version or "*", permission_read = 0, permission_write = 0 } })
end

function M.configure(options)
  options = options or {}
  recovery, can_prune_receipt = options.recover, options.can_prune_receipt
end

local function needs_recovery(collection, row, v)
  if not v or type(row.user_id) ~= "string" or row.user_id == "" or row.user_id == SYSTEM then return false end
  if collection == "duel_race_entry" then return row.key == "active" and v.status == "bound" end
  if collection == "duel_pve_entry" then
    return (row.key == "active" and (v.status == "reserved" or v.status == "bound" or v.status == "refund_pending"))
      or (row.key == "refunds" and type(v.tokens) == "table" and next(v.tokens) ~= nil)
  end
  if collection == "duel_match3_journal" then
    if row.key=="retention_v2" and type(v.open)=="table" and next(v.open)~=nil then return true end
    return (row.key == "pending" and type(v.entries) == "table" and next(v.entries) ~= nil)
      or (type(row.key) == "string" and row.key:sub(1, 7) == "result:" and (v.status == "prepared" or v.status == "committed"))
  end
  return false
end

local function recovered(ok, result)
  return ok and (result == true or (type(result) == "table" and result.ok == true))
end

function M.bootstrap()
  -- Nakama run_once invokes this at startup. Recover old paid receipts before
  -- accepting new matches; this also gives legacy receipts a definite idle proof.
  if not recovery then return false end
  local ok, report = pcall(function()
    local owners, report = {}, { attempted_users = 0, recovered_users = 0, failed_users = 0 }
    for _, collection in ipairs({ "duel_race_entry", "duel_pve_entry", "duel_match3_journal" }) do
      local rows = nk.storage_list("", collection, BOOTSTRAP_PAGE, "")
      for _, row in ipairs(rows or {}) do
        local v = value(row)
        if needs_recovery(collection, row, v) and not owners[row.user_id] and report.attempted_users < BOOTSTRAP_PAGE then
          owners[row.user_id], report.attempted_users = true, report.attempted_users + 1
          local ok, result = pcall(recovery, row.user_id)
          if recovered(ok, result) then report.recovered_users = report.recovered_users + 1
          else report.failed_users = report.failed_users + 1 end
        end
      end
    end
    return report
  end)
  if not ok then nk.logger_warn("startup recovery deferred: " .. tostring(report)); return false end
  return report
end

local function tournament_terminal(v, now)
  if v.phase ~= "done" or not old(v.done_at, now, 30 * DAY) then return false end
  if v.aborted == true then return true end
  if type(v.reward_receipt) == "table" and old(v.reward_receipt.granted_at, now, 30 * DAY) then return true end
  local final = type(v.final) == "table" and v.final[1]
  local winner = type(final) == "table" and final.winner_uid
  local meta = type(v.parts) == "table" and v.parts[winner]
  return type(meta) == "table" and meta.bot == true and final.status == "done"
end

local function eligible(row, now)
  if type(row) ~= "table" or type(row.key) ~= "string" or type(row.version) ~= "string"
      or type(row.user_id) ~= "string" or row.user_id == ""
      or row.version == "" or row.permission_read ~= 0 or row.permission_write ~= 0 then return false end
  local v = value(row)
  if not v then return false end
  if row.collection == "duel_api_rate_limits" then
    -- Every bucket refills in at most 40 seconds. Seven inactive days is far
    -- beyond that; deletion cannot grant more than the legitimate full budget.
    return row.key == "v1" and row.user_id ~= SYSTEM and old(row.update_time, now, 7 * DAY)
  end
  if row.collection == "duel_match3_journal" then return JournalRetention.can_prune(row,now) end
  if not old(row.update_time, now, 30 * DAY) then return false end
  if row.collection == "arena_tournament" then
    return row.user_id == SYSTEM and tournament_terminal(v, now)
  end
  local is_race = row.collection == "duel_race_entry" and row.key:sub(1, 4) == "run:"
  local is_pve = row.collection == "duel_pve_entry" and row.key:sub(1, 7) == "launch:"
  if not (is_race or is_pve)
      or type(row.user_id) ~= "string" or row.user_id == "" or row.user_id == SYSTEM then return false end
  if not ((is_race and v.status == "settled" and old(v.settled_at, now, 30 * DAY))
      or (is_pve and v.status == "consumed" and old(v.consumed_at, now, 30 * DAY))
      or (v.status == "refunded" and old(v.refunded_at, now, 30 * DAY))) then return false end
  local active = value(read(row.user_id, row.collection, "active"))
  if active and ((v.run_id and active.run_id == v.run_id) or (v.receipt_id and active.receipt_id == v.receipt_id)
      or (v.token and active.token == v.token)) then return false end
  -- A recovery journal, if present, must prove all durable work is finished.
  if can_prune_receipt then return can_prune_receipt(row.user_id, v) == true end
  return v.finish_intent == nil and v.journal_id == nil and v.result_journal_id == nil
end

function M.maybe_run()
  local now = math.floor(nk.time() / 1000)
  if now < next_try then return false end
  next_try = now + 60 -- one cheap in-memory check per Lua VM/minute
  local ok, result = pcall(function()
    local row = read(SYSTEM, COLLECTION, KEY)
    local state = value(row) or {}
    if now < (tonumber(state.next_run_at) or 0) or now < (tonumber(state.lease_until) or 0) then return false end
    local token = nk.uuid_v4()
    state.lease_owner, state.lease_until = token, now + LEASE
    state.cursors = type(state.cursors) == "table" and state.cursors or {}
    local acquired = pcall(write, state, row and row.version)
    if not acquired then return false end -- another runtime won the shared lease
    local report = { at = now, scanned = 0, deleted = 0, pages = 0, conflicted_batches = 0, recovery_attempts = 0, recovered_users = 0 }
    local recovered_owners, exhausted = {}, {}
    local started_ms = nk.time()
    local position = math.floor(tonumber(state.next_collection) or 1)
    if position < 1 or position > #collections then position = 1 end
    -- A shared minute lease and persistent round-robin position let busy
    -- collections make progress without rescanning empty ones. Both page and
    -- wall-clock budgets bound synchronous RPC work; eligibility stays strict.
    while report.pages < PAGE_BUDGET do
      if report.pages > 0 and nk.time() - started_ms >= TIME_BUDGET_MS then break end
      local available = false
      for _ = 1, #collections do
        if not exhausted[collections[position]] then available = true; break end
        position = position % #collections + 1
      end
      if not available then break end
      local collection = collections[position]
      local rows, cursor = nk.storage_list("", collection, PAGE, state.cursors[collection] or "")
      report.pages = report.pages + 1
      local deletes = {}
      for _, candidate in ipairs(rows or {}) do
        report.scanned = report.scanned + 1
        local v = value(candidate)
        if recovery and needs_recovery(collection, candidate, v)
            and old(candidate.update_time, now, 120) and report.recovery_attempts < 2
            and not recovered_owners[candidate.user_id] then
          recovered_owners[candidate.user_id] = true
          report.recovery_attempts = report.recovery_attempts + 1
          local recovered_ok, recovered_result = pcall(recovery, candidate.user_id)
          if recovered(recovered_ok, recovered_result) then
            report.recovered_users = report.recovered_users + 1
          end
        end
        if eligible(candidate, now) then
          deletes[#deletes + 1] = { user_id = candidate.user_id, collection = collection,
            key = candidate.key, version = candidate.version }
        end
      end
      if #deletes > 0 then
        -- One concurrent update rejects the whole batch rather than deleting a
        -- newly active record. All rows will be revisited on the next full pass.
        if pcall(nk.storage_delete, deletes) then report.deleted = report.deleted + #deletes
        else report.conflicted_batches = report.conflicted_batches + 1 end
      end
      state.cursors[collection] = cursor or ""
      if not cursor or cursor == "" then exhausted[collection] = true end
      position = position % #collections + 1
    end
    local current = read(SYSTEM, COLLECTION, KEY)
    if not current or value(current).lease_owner ~= token then return false end
    report.elapsed_ms = math.max(0, nk.time() - started_ms)
    report.page_budget, report.time_budget_ms = PAGE_BUDGET, TIME_BUDGET_MS
    state.next_collection = position
    state.next_run_at, state.lease_until, state.last = now + INTERVAL, 0, report
    write(state, current.version)
    return report
  end)
  if not ok then
    nk.logger_warn("storage maintenance deferred: " .. tostring(result))
    return false -- maintenance never blocks the player's requested RPC
  end
  return result
end

return M
