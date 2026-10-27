-- Central opportunistic/maintenance recovery. Called from trusted server hooks,
-- never an arbitrary winner/refund payload. Durable CAS records own all effects;
-- the small VM-local throttle is only a read-load optimisation.
local M = {}
function M.new(deps)
  local journal = assert(deps.journal, "recovery_journal_required")
  local race, pve = deps.race_entry, deps.pve_entry
  local handlers = deps.handlers or {}
  local now = deps.now or os.time
  local next_check, in_progress, count = {}, {}, 0
  if race and type(race.configure_recovery) == "function" and type(deps.resume_race_result) == "function" then
    race.configure_recovery(deps.resume_race_result)
  end
  local R = {}
  function R.set_handler(kind, handler)
    assert(type(kind) == "string" and type(handler) == "function", "invalid_recovery_handler")
    handlers[kind] = handler
  end
  function R.maybe_recover(uid, options)
    if type(uid) ~= "string" or uid == "" then return false, "invalid_recovery_user" end
    if in_progress[uid] then return true, nil, { reentrant = true } end
    options = options or {}
    if not options.force and (next_check[uid] or 0) > now() then return true, nil, { throttled = true } end
    in_progress[uid] = true
    local failures, processed = {}, 0
    local function run(label, fn)
      local ok, done, err, report = pcall(fn)
      if not ok or done ~= true then failures[#failures + 1] = { stage = label, err = ok and err or "recovery_exception" }
      elseif type(report) == "table" then processed = processed + (tonumber(report.processed) or 0) end
    end
    -- A prepared reward wins over an orphan refund. consume_writes is included
    -- in its wallet transaction; only then may the launch ledger be inspected.
    run("journal", function() return journal.recover(uid, handlers, options.limit) end)
    if race then run("race", function() return race.recover(uid) end) end
    if pve then run("pve", function() return pve.recover(uid) end) end
    -- Resuming a Race result may atomically create its stats outbox above.
    run("effects", function() return journal.recover(uid, handlers, options.limit) end)
    if #failures == 0 then
      if count >= 1024 then next_check, count = {}, 0 end
      if next_check[uid] == nil then count = count + 1 end
      next_check[uid] = now() + 5
    end
    in_progress[uid] = nil
    return #failures == 0, #failures > 0 and "recovery_pending" or nil, { processed = processed, failures = failures }
  end
  return R
end
return M
