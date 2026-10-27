-- Durable per-user finalization and achievement/stat outbox. A reward builder
-- MUST be pure preparation: return fresh CAS writes + receipt, never write or
-- reroll loot. Its writes and our committed marker share one storage transaction.
local nk = require("nakama")
local retention_ok, Retention = pcall(require, "modules.duel_journal_retention")
if not retention_ok then Retention = require("duel_journal_retention") end
local M = { COLLECTION = "duel_match3_journal", INDEX_KEY = "pending", MAX_PENDING = 128 }

local function clone(v, seen)
  if type(v) ~= "table" then
    assert(type(v) ~= "function" and type(v) ~= "userdata" and type(v) ~= "thread", "non_serializable_plan")
    return v
  end
  seen = seen or {}; assert(not seen[v], "cyclic_plan"); seen[v] = true
  local out = {}; for k, x in pairs(v) do out[k] = clone(x, seen) end
  seen[v] = nil; return out
end
local function same(a, b)
  if type(a) ~= type(b) then return false end
  if type(a) ~= "table" then return a == b end
  for k, v in pairs(a) do if not same(v, b[k]) then return false end end
  for k in pairs(b) do if a[k] == nil then return false end end
  return true
end
local function key(id)
  id = tostring(id or "")
  if id == "" or #id > 192 or id:find("[^%w_:%-%.]") then error("invalid_result_id") end
  return "result:" .. id
end
local function read(uid, k)
  local rows = nk.storage_read({{ collection = M.COLLECTION, key = k, user_id = uid }})
  local row = rows and rows[1]
  if not row then return nil, nil end
  local value = type(row.value) == "table" and row.value or nk.json_decode(row.value)
  assert(type(value) == "table", "invalid_journal_record")
  return value, row.version
end
local function write(uid, k, value, version)
  return { collection = M.COLLECTION, key = k, user_id = uid, value = value,
    version = version or "*", permission_read = 0, permission_write = 0 }
end
local function retry(fn)
  for attempt = 1, 5 do
    local ok, a, b, c, d = pcall(fn)
    if ok then return a, b, c, d end
    if not tostring(a):find("version", 1, true) or attempt == 5 then
      nk.logger_error("finalization_journal: " .. tostring(a))
      return false, "storage_unavailable"
    end
  end
end
local function index(uid)
  local value, version = read(uid, M.INDEX_KEY)
  value = value or { entries = {} }
  assert(type(value.entries) == "table", "invalid_journal_index")
  return value, version
end
local function effects(plan)
  return (type(plan.achievement_deltas) == "table" and next(plan.achievement_deltas) ~= nil)
    or type(plan.match_stats) == "table"
end

function M.new(deps)
  deps = deps or {}
  local J = {}
  local stats_collection = deps.stats_collection or "duel_match3_stats"
  local stats_key = deps.stats_key or "summary"
  local allow_legacy = deps.allow_legacy_admission == true -- explicit migration/test fixture only; production is strict

  function J.issue_many(specs)
    return retry(function()
      if type(specs)~="table" or #specs<1 or #specs>32 then return false,"invalid_ticket_batch" end
      local contexts,tickets={},{}
      for i,spec in ipairs(specs) do
        if type(spec)~="table" or tostring(spec.uid or "")=="" or type(spec.kind)~="string" or spec.kind=="" then return false,"invalid_ticket_admission" end
        key(spec.id)
        local ctx=contexts[spec.uid] or Retention.load(spec.uid);contexts[spec.uid]=ctx
        local ticket,err=Retention.issue(ctx,spec.id,spec.kind,spec.origin);if not ticket then return false,err end
        tickets[i]=ticket
      end
      local writes={};for _,ctx in pairs(contexts) do if ctx.dirty then writes[#writes+1]=Retention.write(ctx) end end
      if #writes>0 then nk.storage_write(writes) end
      return true,nil,tickets
    end)
  end
  function J.cancel_unused_many(specs)
    return retry(function()
      local contexts={}
      for _,spec in ipairs(specs or {}) do
        local ctx=contexts[spec.uid] or Retention.load(spec.uid);contexts[spec.uid]=ctx
        local ok,err=Retention.cancel_unused(ctx,spec.id,spec.kind,spec.ticket)
        if not ok and err~="result_pending" then return false,err end
      end
      local writes={};for _,ctx in pairs(contexts) do if ctx.dirty then writes[#writes+1]=Retention.write(ctx) end end
      if #writes>0 then nk.storage_write(writes) end;return true
    end)
  end
  local function authorize(ctx,id,kind,ticket)
    if not ticket and allow_legacy then ticket=Retention.issue(ctx,id,kind) end
    local ok,err=Retention.authorize(ctx,id,kind,ticket)
    return ok,err,ticket
  end
  function J.ticket(uid,id) return Retention.find(Retention.load(uid),id) end

  function J.read(uid, id) return read(uid, key(id)) end

  -- Short all-or-nothing operations (inventory activation / match binding).
  -- There is no prepared stage: the caller's fresh CAS writes and immutable
  -- complete receipt share the same storage transaction, including both users
  -- when a Pro match starts. A lost acknowledgement only re-reads the receipt.
  function J.atomic_many(specs)
    if type(specs) ~= "table" or #specs < 1 or #specs > 16 then return false, "invalid_atomic_batch" end
    return retry(function()
      local writes, receipts, identities, contexts, victims = {}, {}, {}, {}, {}
      for i, spec in ipairs(specs) do
        if type(spec) ~= "table" or type(spec.uid) ~= "string" or spec.uid == ""
            or type(spec.kind) ~= "string" or spec.kind == "" or type(spec.plan) ~= "table"
            or type(spec.build) ~= "function" then return false, "invalid_atomic_operation" end
        local k = key(spec.id)
        local identity = spec.uid .. ":" .. k
        if identities[identity] then return false, "duplicate_atomic_operation" end
        identities[identity] = true
        local previous = read(spec.uid, k)
        if previous then
          if previous.kind ~= spec.kind or not same(previous.plan, spec.plan) then return false, "result_plan_conflict" end
          if previous.status ~= "complete" or previous.reward_status ~= "committed" then return false, "invalid_atomic_receipt" end
          receipts[i] = clone(previous.reward)
        else
          local ctx=contexts[spec.uid] or Retention.load(spec.uid);contexts[spec.uid]=ctx
          local accepted,why,ticket=authorize(ctx,spec.id,spec.kind,spec.ticket)
          if not accepted then return false,why end
          local operations, receipt, err = spec.build(clone(spec.plan))
          if not operations then return false, err or "invalid_atomic_builder" end
          if type(operations) ~= "table" or type(receipt) ~= "table" then return false, "invalid_atomic_builder" end
          for _, op in ipairs(operations) do
            if op.user_id ~= spec.uid or op.collection == M.COLLECTION
                or op.version == nil or op.version == "" then return false, "unsafe_atomic_write" end
            writes[#writes + 1] = op
          end
          local now = os.time()
          local completed={ schema = 2, ticket=ticket, result_id = spec.id, kind = spec.kind,
            plan = clone(spec.plan), reward = clone(receipt), status = "complete", reward_status = "committed",
            created_at = now, committed_at = now, completed_at = now }
          local evicted,finish_err=Retention.finish(ctx,completed);if not evicted then return false,finish_err end
          victims[spec.uid]=victims[spec.uid] or {};for _,id in ipairs(evicted) do victims[spec.uid][#victims[spec.uid]+1]=id end
          writes[#writes + 1] = write(spec.uid, k, completed)
          receipts[i] = receipt
        end
      end
      for _,ctx in pairs(contexts) do writes[#writes+1]=Retention.write(ctx) end
      if #writes > 0 then nk.storage_write(writes) end
      for uid,ids in pairs(victims) do Retention.prune_ids(uid,ids) end
      return true, nil, receipts
    end)
  end

  function J.atomic(uid, id, kind, plan, builder, ticket)
    local ok, err, receipts = J.atomic_many({ {uid=uid,id=id,kind=kind,plan=plan,build=builder,ticket=ticket} })
    return ok, err, receipts and receipts[1] or nil
  end

  -- Include these create-only writes in an orphan-launch refund transaction.
  -- An abort and a reward intent race on the SAME object, so neither a refund
  -- followed by a free reward nor a refund after reward commit is possible.
  function J.abort_writes(uid, id, reason, ticket)
    local k = key(id)
    local existing = read(uid, k)
    if existing then
      if existing.status == "aborted" then return {}, nil end
      return nil, "result_pending"
    end
    local ctx=Retention.load(uid)
    -- Legacy orphan refunds are already proved by the paid entry's CAS. This
    -- no-grant abort may backfill admission; it can never re-open a reward.
    local kind="launch_abort"
    if ticket then local entry=ctx.value.open[tostring(ticket.sequence)];kind=entry and entry.kind or kind
    else ticket=Retention.issue(ctx,id,kind,nil,true);local entry=ticket and ctx.value.open[tostring(ticket.sequence)];kind=entry and entry.kind or kind end
    local accepted,why=Retention.authorize(ctx,id,kind,ticket);if not accepted then return nil,why end
    local record={schema=2,ticket=ticket,result_id=id,kind=kind,status="aborted",reward_status="aborted",
      reason=tostring(reason or "launch_aborted"),completed_at=os.time(),created_at=os.time()}
    local evicted,err=Retention.finish(ctx,record);if not evicted then return nil,err end
    return {write(uid,k,record),Retention.write(ctx)}
  end

  function J.prepare_writes(uid, id, kind, plan, ticket)
    if tostring(uid or "") == "" or type(plan) ~= "table" or type(kind) ~= "string" or kind == "" then
      return nil, "invalid_finalization"
    end
      local k = key(id)
      local previous = read(uid, k)
      if previous then
        if previous.kind ~= kind or not same(previous.plan, plan) then return nil, "result_plan_conflict" end
        return {}, nil, previous
      end
      local control=Retention.load(uid)
      local accepted,why,original=authorize(control,id,kind,ticket);if not accepted then return nil,why end
      local pending, version = index(uid)
      local count = 0; for _ in pairs(pending.entries) do count = count + 1 end
      if count >= M.MAX_PENDING then return nil, "recovery_backlog_full" end
      local now = os.time()
      local record = { schema = 2, ticket=original, result_id = id, kind = kind, plan = clone(plan), created_at = now,
        status = kind == "effects" and "committed" or "prepared", reward_status = kind == "effects" and "none" or "pending" }
      pending.entries[id] = { kind = kind, created_at = now }; pending.updated_at = now
      return { write(uid, k, record), write(uid, M.INDEX_KEY, pending, version),Retention.write(control) }, nil, record
  end

  function J.prepare(uid, id, kind, plan, ticket)
    return retry(function()
      local writes, err, record = J.prepare_writes(uid, id, kind, plan,ticket)
      if not writes then return false, err end
      if #writes > 0 then nk.storage_write(writes) end
      return true, nil, record
    end)
  end

  function J.commit(uid, id, builder)
    if type(builder) ~= "function" then return false, "reward_builder_required" end
    return retry(function()
      local k = key(id)
      local record, version = read(uid, k)
      if not record then return false, "result_intent_missing" end
      if record.reward_status == "committed" then return true, nil, record.reward, record.status ~= "complete" end
      if record.status ~= "prepared" or record.reward_status ~= "pending" then return false, "invalid_result_state" end
      local control=Retention.load(uid)
      local admitted,admission_err=Retention.adopt_pending(control,record);if not admitted then return false,admission_err end
      local writes, receipt = builder(clone(record.plan))
      if type(writes) ~= "table" or type(receipt) ~= "table" then return false, "invalid_reward_builder" end
      for _, op in ipairs(writes) do
        if op.collection == M.COLLECTION then return false, "journal_write_in_reward_builder" end
        if op.user_id ~= uid then return false, "cross_user_reward_builder" end
        if op.version == nil or op.version == "" then return false, "unversioned_reward_builder" end
      end
      local pending, pending_version = index(uid)
      record.reward_status, record.reward, record.committed_at = "committed", clone(receipt), os.time()
      record.status = effects(record.plan) and "committed" or "complete"
      if record.status == "complete" then record.completed_at = record.committed_at; pending.entries[id] = nil end
      local evicted={}
      if record.status=="complete" then local why;evicted,why=Retention.finish(control,record);if not evicted then return false,why end end
      pending.updated_at = os.time()
      writes[#writes + 1] = write(uid, k, record, version)
      writes[#writes + 1] = write(uid, M.INDEX_KEY, pending, pending_version)
      writes[#writes+1]=Retention.write(control)
      nk.storage_write(writes)
      Retention.prune_ids(uid,evicted)
      return true, nil, receipt, record.status ~= "complete"
    end)
  end

  local function apply_summary(summary, plan, result_id)
    local changed = false
    summary.achievement_stats = type(summary.achievement_stats) == "table" and summary.achievement_stats or {}
    for stat, delta in pairs(plan.achievement_deltas or {}) do
      assert(type(stat) == "string" and stat ~= "", "invalid_achievement_delta")
      delta = tonumber(delta)
      assert(delta and delta == delta and delta ~= math.huge and delta ~= -math.huge and delta == math.floor(delta), "invalid_achievement_delta")
      if delta ~= 0 then summary.achievement_stats[stat] = (tonumber(summary.achievement_stats[stat]) or 0) + delta; changed = true end
    end
    local stats = plan.match_stats
    if type(stats) == "table" then
      result_id = tostring(stats.result_id or result_id)
      local mode = tostring(stats.mode or "")
      assert(mode == "duel" or mode == "pro" or mode == "race" or mode == "arena" or mode == "mine", "invalid_match_stats_mode")
      local recent = type(summary.match_result_ids) == "table" and summary.match_result_ids or {}
      local recorded = false; for _, existing in ipairs(recent) do if existing == result_id then recorded = true end end
      if not recorded then
        local won = stats.won == true
        summary.played = (tonumber(summary.played) or 0) + 1
        summary.wins = (tonumber(summary.wins) or 0) + (won and 1 or 0)
        summary.losses = (tonumber(summary.losses) or 0) + (won and 0 or 1)
        summary.modes = type(summary.modes) == "table" and summary.modes or {}
        local bag = type(summary.modes[mode]) == "table" and summary.modes[mode] or {}
        bag.played = (tonumber(bag.played) or 0) + 1; bag.wins = (tonumber(bag.wins) or 0) + (won and 1 or 0)
        bag.losses = (tonumber(bag.losses) or 0) + (won and 0 or 1); summary.modes[mode] = bag
        local ids = {}; for i = math.max(1, #recent - 2), #recent do ids[#ids + 1] = recent[i] end
        ids[#ids + 1] = result_id; summary.match_result_ids = ids; changed = true
        if stats.daily_day ~= nil then
          local day = tonumber(stats.daily_day)
          local uses = tonumber(stats.ability_uses) or 0
          assert(day and day == math.floor(day) and day >= 0 and uses == math.floor(uses) and uses >= 0, "invalid_daily_stats")
          local daily = type(summary.daily) == "table" and summary.daily or {}
          -- A delayed old journal never erases progress for a newer day.
          if day >= (tonumber(daily.day) or -1) then
            if daily.day ~= day then daily = {day=day, mine_wins=0, pvp_played=0, ability_uses=0} end
            daily.mine_wins = (tonumber(daily.mine_wins) or 0) + (mode == "mine" and won and 1 or 0)
            daily.pvp_played = (tonumber(daily.pvp_played) or 0) + (mode ~= "mine" and stats.daily_pvp_eligible == true and 1 or 0)
            daily.ability_uses = (tonumber(daily.ability_uses) or 0) + uses
            summary.daily = daily
          end
        end
      end
    end
    return changed
  end

  function J.deliver_effects(uid, id)
    return retry(function()
      local k = key(id)
      local record, version = read(uid, k)
      if not record then return false, "result_intent_missing" end
      if record.status == "complete" then return true, nil, record end
      if record.status ~= "committed" then return false, "reward_pending" end
      local control=Retention.load(uid)
      local admitted,admission_err=Retention.adopt_pending(control,record);if not admitted then return false,admission_err end
      local writes = {}
      if effects(record.plan) then
        local rows = nk.storage_read({{ collection = stats_collection, key = stats_key, user_id = uid }})
        local row = rows and rows[1]
        local summary = row and (type(row.value) == "table" and row.value or nk.json_decode(row.value)) or {}
        assert(type(summary) == "table", "invalid_stats_summary")
        if apply_summary(summary, record.plan, id) then
          summary.updated_at = os.time()
          writes[#writes + 1] = { collection = stats_collection, key = stats_key, user_id = uid, value = summary,
            version = row and row.version or "*", permission_read = 1, permission_write = 0 }
        end
      end
      local pending, pending_version = index(uid)
      record.status, record.effects_status, record.completed_at = "complete", "complete", os.time()
      pending.entries[id], pending.updated_at = nil, os.time()
      local evicted,why=Retention.finish(control,record);if not evicted then return false,why end
      writes[#writes + 1] = write(uid, k, record, version)
      writes[#writes + 1] = write(uid, M.INDEX_KEY, pending, pending_version)
      writes[#writes+1]=Retention.write(control)
      -- The counter increment and delivered marker cannot be separated by a
      -- process crash or by a duplicate maintenance worker.
      nk.storage_write(writes)
      Retention.prune_ids(uid,evicted)
      return true, nil, record
    end)
  end

  function J.recover(uid, handlers, limit)
    pcall(Retention.sweep_unused,uid)
    local ok, pending = pcall(index, uid)
    if not ok then return false, "storage_unavailable" end
    local ids = {}; for id in pairs(pending.entries) do ids[#ids + 1] = id end; table.sort(ids)
    local processed, failed = 0, {}
    for i = 1, math.min(#ids, math.max(1, math.min(32, tonumber(limit) or 8))) do
      local id = ids[i]
      local read_ok, record = pcall(read, uid, key(id))
      local done, err = false, "storage_unavailable"
      if read_ok and record then
        if record.status == "prepared" then
          local handler = type(handlers) == "table" and handlers[record.kind] or nil
          if type(handler) == "function" then done, err = J.commit(uid, id, function(plan) return handler(uid, id, plan) end)
          else err = "recovery_handler_missing" end
        else done = true end
        if done then done, err = J.deliver_effects(uid, id) end
      elseif read_ok then err = "journal_index_orphan" end
      if done then processed = processed + 1 else failed[#failed + 1] = { result_id = id, err = err } end
    end
    return #failed == 0, #failed > 0 and "recovery_pending" or nil, { processed = processed, failed = failed, remaining = math.max(0, #ids - processed) }
  end

  return J
end

return M
