-- Server-only Race payment receipts. Wallet changes and receipt transitions share
-- one Nakama storage transaction; an entry can be bound to exactly one server run.
local nk = require("nakama")
local M = { COLLECTION = "duel_race_entry" }
local ALLOWED = { matter = true, gold = true, ore = true, energy = true }

local function decode(row)
  if row == nil then return nil end
  if type(row.value) == "table" then return row.value end
  return nk.json_decode(row.value)
end

local function read(uid, key)
  local rows = nk.storage_read({ { collection = M.COLLECTION, key = key, user_id = uid } })
  local row = rows and rows[1]
  return decode(row), row and row.version or nil
end

local function object(uid, key, value, version)
  return { collection = M.COLLECTION, key = key, user_id = uid, value = value,
    version = version or "*", permission_read = 0, permission_write = 0 }
end

local function costs_normalize(costs)
  if type(costs) ~= "table" then return nil, "invalid_entry_costs" end
  local totals, out = {}, {}
  for _, row in ipairs(costs) do
    local res = type(row) == "table" and string.lower(tostring(row.resource or "")) or ""
    local amount = type(row) == "table" and tonumber(row.amount) or nil
    -- Item-backed ingots are intentionally unsupported here: a scalar debit
    -- would diverge from the inventory ledger.
    if not ALLOWED[res] then return nil, "unsupported_entry_resource" end
    if not amount or amount ~= amount or amount == math.huge or amount <= 0 or amount ~= math.floor(amount) then
      return nil, "invalid_entry_costs"
    end
    totals[res] = (totals[res] or 0) + amount
  end
  for _, res in ipairs({ "matter", "gold", "ore", "energy" }) do
    if totals[res] then out[#out + 1] = { resource = res, amount = totals[res] } end
  end
  if #out == 0 then return nil, "invalid_entry_costs" end
  return out
end

function M.require_pending(uid)
  local ok, entry = pcall(read, uid, "active")
  if not ok then return false, "storage_read_failed" end
  if type(entry) ~= "table" then return false, "race_entry_required" end
  if entry.status == "bound" then return false, "race_entry_in_use" end
  if entry.status ~= "pending" or not costs_normalize(entry.costs) then return false, "race_entry_required" end
  return true, nil, entry
end

function M.new(deps)
  local E = {}
  E.require_pending = M.require_pending
  E.normalize_costs = costs_normalize
  local read_progress = assert(deps.read_pve_progress)
  local build_progress = assert(deps.build_pve_progress_write)

  local function retry(fn)
    for attempt = 1, 5 do
      local ok, a, b, c, d = pcall(fn)
      if ok then return a, b, c, d end
      if not string.find(tostring(a), "version", 1, true) or attempt == 5 then
        nk.logger_error("race_entry: " .. tostring(a))
        return false, "storage_write_failed"
      end
    end
    return false, "retry_exhausted"
  end

  function E.pay(uid, configured_costs)
    local costs, cost_err = costs_normalize(configured_costs)
    if not costs then return false, cost_err end
    return retry(function()
      costs = assert(costs_normalize(configured_costs))
      local entry, entry_version = read(uid, "active")
      local progress, version = read_progress(uid)
      if type(entry) == "table" and entry.status == "bound" then return false, "race_entry_in_use" end
      if type(entry) == "table" and entry.status == "pending" then
        if not costs_normalize(entry.costs) then return false, "invalid_entry_receipt" end
        return true, nil, progress, entry
      end
      -- Migrate an already-paid entry from the previous runtime atomically.
      -- Never substitute today's price when the original debit is unknown.
      local legacy_paid = progress.race_entry_pending == true
      if legacy_paid then
        costs, cost_err = costs_normalize(progress.race_entry_costs)
        if not costs then return false, "invalid_legacy_entry_receipt" end
      else
        for _, line in ipairs(costs) do
          if (tonumber(progress[line.resource]) or 0) < line.amount then return false, "not_enough_" .. line.resource end
        end
        for _, line in ipairs(costs) do progress[line.resource] = progress[line.resource] - line.amount end
      end
      progress.race_entry_pending, progress.race_entry_costs = false, nil
      entry = { status = "pending", receipt_id = nk.uuid_v4(), costs = costs, paid_at = os.time() }
      nk.storage_write({ build_progress(uid, progress, version), object(uid, "active", entry, entry_version) })
      entry.newly_paid = not legacy_paid -- Response-only; not stored.
      return true, nil, progress, entry
    end)
  end

  function E.cancel(uid)
    return retry(function()
      local entry, entry_version = read(uid, "active")
      local progress, version = read_progress(uid)
      if type(entry) == "table" and entry.status == "bound" then return false, "race_entry_in_use" end
      local legacy = (type(entry) ~= "table" or entry.status ~= "pending") and progress.race_entry_pending == true
      if (type(entry) ~= "table" or entry.status ~= "pending") and not legacy then return true, nil, progress, nil end
      local costs = costs_normalize(legacy and progress.race_entry_costs or entry.costs)
      if not costs then return false, "invalid_entry_receipt" end
      for _, line in ipairs(costs) do progress[line.resource] = (tonumber(progress[line.resource]) or 0) + line.amount end
      progress.race_entry_pending, progress.race_entry_costs = false, nil
      local cancelled = { status = "cancelled", costs = costs,
        receipt_id = entry and entry.receipt_id or nk.uuid_v4(), cancelled_at = os.time() }
      nk.storage_write({ build_progress(uid, progress, version), object(uid, "active", cancelled, entry_version) })
      return true, nil, progress, cancelled
    end)
  end

  local function human_users(users)
    local out, seen = {}, {}
    for _, uid in ipairs(users or {}) do
      uid = tostring(uid or "")
      if uid ~= "" and string.sub(uid, 1, 7) ~= "zz-bot-" and not seen[uid] then
        out[#out + 1], seen[uid] = uid, true
      end
    end
    return out
  end

  function E.claim(users, run_id)
    local humans = human_users(users)
    if tostring(run_id or "") == "" or #humans < 1 or #humans > 2 then return false, "invalid_race_run" end
    return retry(function()
      local writes, costs_by_user = {}, {}
      for _, uid in ipairs(humans) do
        local entry, version = read(uid, "active")
        local receipt = read(uid, "run:" .. run_id)
        if type(entry) == "table" and entry.status == "bound" and entry.run_id == run_id
            and type(receipt) == "table" and receipt.status == "bound" and receipt.receipt_id == entry.receipt_id then
          costs_by_user[uid] = receipt.costs
        elseif type(entry) == "table" and entry.status == "pending" and receipt == nil then
          local costs = costs_normalize(entry.costs)
          if not costs then return false, "invalid_entry_receipt" end
          entry.status, entry.run_id, entry.bound_at = "bound", run_id, os.time()
          costs_by_user[uid] = costs
          writes[#writes + 1] = object(uid, "active", entry, version)
          writes[#writes + 1] = object(uid, "run:" .. run_id, {
            status = "bound", run_id = run_id, receipt_id = entry.receipt_id,
            costs = costs, paid_at = entry.paid_at, bound_at = entry.bound_at,
          }, nil)
        else
          return false, (type(entry) == "table" and entry.status == "bound") and "race_entry_in_use" or "race_entry_required"
        end
      end
      if #writes > 0 then nk.storage_write(writes) end
      return true, nil, costs_by_user
    end)
  end

  -- Mutator returns the reward actually added to the profile. Settlement is
  -- all-or-nothing for every human and survives duplicate end callbacks/rejoins.
  function E.settle(users, run_id, winner, is_draw, mutator)
    local humans = human_users(users)
    if tostring(run_id or "") == "" or #humans < 1 then return false, "invalid_race_run" end
    return retry(function()
      local writes, rewards = {}, {}
      for _, uid in ipairs(humans) do
        local receipt, receipt_version = read(uid, "run:" .. run_id)
        if type(receipt) ~= "table" then return false, "race_entry_required" end
        if receipt.status == "settled" then
          if receipt.winner ~= tostring(winner or "") or receipt.is_draw ~= (is_draw == true) then return false, "race_result_conflict" end
          rewards[uid] = receipt.reward
        elseif receipt.status == "bound" then
          local entry, entry_version = read(uid, "active")
          if type(entry) ~= "table" or entry.status ~= "bound" or entry.run_id ~= run_id or entry.receipt_id ~= receipt.receipt_id then
            return false, "invalid_entry_receipt"
          end
          local costs = costs_normalize(receipt.costs)
          if not costs then return false, "invalid_entry_receipt" end
          local progress, version = read_progress(uid)
          local reward = mutator(progress, uid, costs)
          receipt.status, receipt.winner, receipt.is_draw = "settled", tostring(winner or ""), is_draw == true
          receipt.reward, receipt.settled_at = reward, os.time()
          entry.status, entry.settled_at = "settled", receipt.settled_at
          writes[#writes + 1] = build_progress(uid, progress, version)
          writes[#writes + 1] = object(uid, "active", entry, entry_version)
          writes[#writes + 1] = object(uid, "run:" .. run_id, receipt, receipt_version)
          rewards[uid] = reward
        else return false, "invalid_entry_receipt" end
      end
      if #writes > 0 then nk.storage_write(writes) end
      return true, nil, rewards
    end)
  end

  return E
end

return M
