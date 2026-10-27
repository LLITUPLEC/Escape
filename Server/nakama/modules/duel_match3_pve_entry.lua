-- Durable PvE launch reservation. Debit, bind, consume and refund use immutable
-- tokens and CAS receipts. There is no client-controlled refund RPC.
local nk = require("nakama")
local ok_journal, Journal = pcall(require, "modules.duel_match3_journal")
if not ok_journal then Journal = require("duel_match3_journal") end
local M = { COLLECTION = "duel_pve_entry", RESERVATION_SECONDS = 90 }
local ORDER = { "energy", "gold", "ore", "matter", "miner_key", "dark_key" }
local ALLOWED = {}; for _, res in ipairs(ORDER) do ALLOWED[res] = true end
local function copy(value)
  if type(value) ~= "table" then return value end
  local out = {}; for k, v in pairs(value) do out[k] = copy(v) end; return out
end
local function read(uid, key)
  local rows = nk.storage_read({{ collection = M.COLLECTION, key = key, user_id = uid }})
  local row = rows and rows[1]
  return row and (type(row.value) == "table" and row.value or nk.json_decode(row.value)) or nil, row and row.version
end
local function object(uid, key, value, version)
  return { collection = M.COLLECTION, key = key, user_id = uid, value = value,
    version = version or "*", permission_read = 0, permission_write = 0 }
end
local function retry(fn)
  for attempt = 1, 5 do
    local ok, a, b, c, d = pcall(fn)
    if ok then return a, b, c, d end
    if not tostring(a):find("version", 1, true) or attempt == 5 then
      nk.logger_error("pve_entry: " .. tostring(a)); return false, "storage_unavailable"
    end
  end
end
local function normalize(costs)
  if type(costs) ~= "table" then return nil end
  local sums, result = {}, {}
  for _, line in ipairs(costs) do
    if type(line) ~= "table" then return nil end
    local res, n = tostring(line.resource or ""), tonumber(line.amount)
    if not ALLOWED[res] or not n or n ~= n or n == math.huge or n < 0 or n ~= math.floor(n) then return nil end
    sums[res] = (sums[res] or 0) + n
  end
  for _, res in ipairs(ORDER) do if (sums[res] or 0) > 0 then result[#result + 1] = { resource = res, amount = sums[res] } end end
  return result
end
local function amount(progress, resource)
  if resource == "miner_key" or resource == "dark_key" then return tonumber((progress.key_items or {})[resource]) or 0 end
  return tonumber(progress[resource]) or 0
end
local function add(progress, resource, delta)
  if resource == "miner_key" or resource == "dark_key" then
    progress.key_items = progress.key_items or {}; progress.key_items[resource] = amount(progress, resource) + delta
  else progress[resource] = amount(progress, resource) + delta end
end

function M.new(deps)
  local E = {}
  local progress_read, progress_write = assert(deps.read_pve_progress), assert(deps.build_pve_progress_write)
  local journal = deps.journal or Journal.new()
  local now = deps.now or os.time
  local function alive(match_id)
    if tostring(match_id or "") == "" then return nil, "pve_match_proof_missing" end
    local ok, value, err = pcall(deps.match_get or nk.match_get, match_id)
    if not ok or err ~= nil then return nil, "match_probe_unavailable" end
    return value ~= nil
  end
  local function credit(progress, uid, costs)
    local remaining = {}
    for _, line in ipairs(costs) do
      local n = line.amount
      if line.resource == "energy" and type(deps.energy_max) == "function" then
        n = math.min(n, math.max(0, deps.energy_max(uid) - amount(progress, "energy")))
      end
      add(progress, line.resource, n)
      if n < line.amount then remaining[#remaining + 1] = { resource = line.resource, amount = line.amount - n } end
    end
    return remaining
  end

  function E.refund_launch(uid, token, reason, expired_only)
    return retry(function()
      local receipt, version = read(uid, "launch:" .. tostring(token or ""))
      if type(receipt) ~= "table" then return false, "pve_entry_required" end
      if receipt.status == "refunded" or receipt.status == "consumed" then return true, nil, receipt end
      if receipt.status ~= "reserved" and receipt.status ~= "bound" and receipt.status ~= "refund_pending" then return false, "invalid_pve_entry" end
      if receipt.status == "reserved" and expired_only and now() < receipt.expires_at then return true, nil, receipt end
      if receipt.status == "bound" then
        local live, probe_err = alive(receipt.match_id)
        if live == nil then return false, probe_err end
        if live then return true, nil, receipt end
      end
      local writes = {}
      if receipt.run_id and receipt.run_id ~= "" and receipt.status ~= "refund_pending" then
        local abort, err = journal.abort_writes(uid, receipt.run_id .. ":pve", reason)
        if not abort then return false, err end
        for _, op in ipairs(abort) do writes[#writes + 1] = op end
      end
      local costs = normalize(receipt.remaining_refund or receipt.costs)
      if not costs then return false, "invalid_pve_receipt" end
      local progress, progress_version = progress_read(uid)
      local remaining = credit(progress, uid, costs)
      receipt.remaining_refund, receipt.refund_reason = remaining, tostring(reason or receipt.refund_reason or "launch_aborted")
      receipt.status = #remaining == 0 and "refunded" or "refund_pending"
      receipt.refund_started_at = receipt.refund_started_at or now()
      if #remaining == 0 then receipt.refunded_at = now() end
      local active, active_version = read(uid, "active")
      if active and active.token == token then writes[#writes + 1] = object(uid, "active", copy(receipt), active_version) end
      local refunds, refunds_version = read(uid, "refunds"); refunds = refunds or { tokens = {} }
      if #remaining > 0 then refunds.tokens[token] = true else refunds.tokens[token] = nil end
      refunds.updated_at = now()
      writes[#writes + 1] = progress_write(uid, progress, progress_version)
      writes[#writes + 1] = object(uid, "launch:" .. token, receipt, version)
      writes[#writes + 1] = object(uid, "refunds", refunds, refunds_version)
      nk.storage_write(writes)
      return true, nil, receipt, progress
    end)
  end

  function E.recover(uid)
    local ok, active = pcall(read, uid, "active")
    if not ok then return false, "storage_unavailable" end
    if active and (active.status == "reserved" or active.status == "bound") then
      local recovered, err = E.refund_launch(uid, active.token, "orphaned_launch", true)
      if not recovered then return false, err end
    end
    local read_ok, refunds = pcall(read, uid, "refunds")
    if not read_ok then return false, "storage_unavailable" end
    local count = 0
    for token in pairs(refunds and refunds.tokens or {}) do
      if count >= 8 then break end
      local recovered, err = E.refund_launch(uid, token, "refund_credit", true)
      if not recovered then return false, err end
      count = count + 1
    end
    return true
  end

  -- validate(progress) may prepare selection/affix fields but MUST NOT write.
  -- request_key is server-built from bot/floor/difficulty, not a payment token.
  function E.reserve(uid, costs, metadata, validate)
    if type(metadata) ~= "table" or tostring(metadata.request_key or "") == "" then return false, "invalid_launch_request" end
    costs = normalize(costs)
    if not costs then return false, "unsupported_launch_cost" end
    local recovered, recovery_err = E.recover(uid)
    if not recovered then return false, recovery_err end
    return retry(function()
      local active, version = read(uid, "active")
      local progress, progress_version = progress_read(uid)
      if active and (active.status == "reserved" or active.status == "bound") then
        if active.request_key ~= metadata.request_key then return false, "pve_launch_in_use" end
        return true, nil, active, progress
      end
      if active and active.status == "consumed" then
        local live, err = alive(active.match_id)
        if live == nil then return false, err end
        if live then return false, "pve_match_in_progress" end
      end
      if type(validate) == "function" then
        local valid, err = validate(progress)
        if valid ~= true then return false, err or "invalid_launch" end
      end
      for _, line in ipairs(costs) do if amount(progress, line.resource) < line.amount then return false, "not_enough_" .. line.resource end end
      for _, line in ipairs(costs) do add(progress, line.resource, -line.amount) end
      -- Preserve energy regeneration's existing debit anchor.
      for _, line in ipairs(costs) do if line.resource == "energy" then progress.energy_updated_at = now() end end
      local token = nk.uuid_v4()
      local receipt = { token = token, request_key = metadata.request_key, metadata = copy(metadata), status = "reserved",
        costs = copy(costs), reserved_at = now(), expires_at = now() + M.RESERVATION_SECONDS }
      nk.storage_write({ progress_write(uid, progress, progress_version), object(uid, "active", receipt, version), object(uid, "launch:" .. token, copy(receipt)) })
      return true, nil, receipt, progress
    end)
  end

  function E.bind(uid, token, match_id, run_id)
    return retry(function()
      local receipt, version = read(uid, "launch:" .. tostring(token or ""))
      local active, active_version = read(uid, "active")
      if not receipt or not active or active.token ~= token then return false, "pve_entry_required" end
      if receipt.status == "bound" or receipt.status == "consumed" then
        if receipt.match_id == match_id and receipt.run_id == run_id then return true, nil, receipt end
        return false, "pve_entry_in_use"
      end
      if receipt.status ~= "reserved" or now() >= receipt.expires_at then return false, "pve_entry_expired" end
      if tostring(match_id or "") == "" or tostring(run_id or "") == "" then return false, "invalid_pve_match" end
      receipt.status, receipt.match_id, receipt.run_id, receipt.bound_at = "bound", match_id, run_id, now()
      nk.storage_write({ object(uid, "active", copy(receipt), active_version), object(uid, "launch:" .. token, receipt, version) })
      return true, nil, receipt
    end)
  end

  function E.attach_journal_tickets(uid,token,run_id,tickets)
    return retry(function()
      local receipt,version=read(uid,"launch:"..tostring(token or ""));local active,active_version=read(uid,"active")
      if not receipt or receipt.run_id~=run_id or not active or active.token~=token then return false,"pve_entry_required" end
      if receipt.journal_tickets then return true end
      receipt.journal_tickets=copy(tickets);active.journal_tickets=copy(tickets)
      nk.storage_write({object(uid,"launch:"..token,receipt,version),object(uid,"active",active,active_version)})
      return true
    end)
  end

  -- Pure read/prepare, for the SAME batch as sheet + progress + result journal.
  function E.consume_writes(uid, token, run_id)
    local receipt, version = read(uid, "launch:" .. tostring(token or ""))
    if not receipt or receipt.run_id ~= run_id then return nil, "pve_entry_required" end
    if receipt.status == "consumed" then return {} end
    if receipt.status ~= "bound" then return nil, "pve_entry_not_bound" end
    local active, active_version = read(uid, "active")
    if not active or active.token ~= token then return nil, "pve_entry_required" end
    receipt.status, receipt.consumed_at = "consumed", now()
    return { object(uid, "active", copy(receipt), active_version), object(uid, "launch:" .. token, receipt, version) }
  end

  function E.consume(uid, token, run_id)
    return retry(function()
      local writes, err = E.consume_writes(uid, token, run_id)
      if not writes then return false, err end
      if #writes > 0 then nk.storage_write(writes) end
      return true
    end)
  end

  return E
end
return M
