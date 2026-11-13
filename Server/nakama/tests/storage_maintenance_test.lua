-- Actual cleanup module with two-VM contention, transaction conflicts and faults.
package.path = "Server/nakama/?.lua;" .. package.path
local DAY, SYSTEM = 86400, "00000000-0000-0000-0000-000000000000"
local now, db, revision, list_calls, delete_calls, before_delete, fail_read, recover_calls, fail_list, list_trace, elapsed_ms, list_delay_ms, owner_lists, matches
local RACE, BOSS = "duel_race_entry", "duel_global_boss_attempt"
local COLLECTIONS = { "arena_tournament", "duel_api_rate_limits", RACE,
  "duel_pve_entry", "duel_match3_journal", BOSS }
local function clone(v)
  if type(v) ~= "table" then return v end
  local out = {}; for k, val in pairs(v) do out[k] = clone(val) end; return out
end
local function key(uid, col, id) return col .. ":" .. uid .. ":" .. id end
local function put(uid, col, id, v, updated)
  revision = revision + 1
  db[key(uid, col, id)] = { user_id = uid, collection = col, key = id, value = clone(v),
    version = tostring(revision), permission_read = 0, permission_write = 0,
    create_time = DAY, update_time = updated or now }
end
local function exists(uid, col, id) return db[key(uid, col, id)] ~= nil end
local nk = {
  time = function() return now * 1000 + elapsed_ms end, uuid_v4 = function() revision = revision + 1; return "lease-" .. revision end,
  logger_warn = function() end, json_decode = function() error("invalid JSON") end,
  match_get = function(id)
    if matches[id] == "error" then error("match registry unavailable") end
    return matches[id]
  end,
  match_list = function() return matches end,
  storage_read = function(ids)
    if fail_read then error("database unavailable") end
    local rows = {}; for _, id in ipairs(ids) do local row = db[key(id.user_id, id.collection, id.key)]
      if row then rows[#rows + 1] = clone(row) end end
    return rows
  end,
  storage_write = function(objects)
    for _, obj in ipairs(objects) do
      local old = db[key(obj.user_id, obj.collection, obj.key)]
      assert((old and obj.version == old.version) or (not old and obj.version == "*"), "version conflict")
    end
    local ack = {}; for _, obj in ipairs(objects) do put(obj.user_id, obj.collection, obj.key, obj.value)
      ack[#ack + 1] = { version = tostring(revision) } end
    return ack
  end,
  storage_list = function(uid, collection, limit, cursor)
    assert(type(uid) == "string" and limit >= 1 and limit <= 100 and type(cursor) == "string")
    elapsed_ms = elapsed_ms + list_delay_ms
    if uid == "" then
      list_calls = list_calls + 1
      list_trace[#list_trace + 1] = { collection = collection, cursor = cursor, limit = limit }
    else
      assert((collection == "duel_pve_entry" or collection == RACE or collection == BOSS) and limit == 100 and cursor == "")
      owner_lists[#owner_lists + 1] = uid
    end
    if fail_list == collection then error("storage list unavailable") end
    local names = {}; for name, row in pairs(db) do
      if row.collection == collection and (uid == "" or uid == row.user_id) then names[#names + 1] = name end
    end
    table.sort(names)
    local out, last = {}, nil
    for _, name in ipairs(names) do if name > cursor then
      if #out == limit then return out, last end
      out[#out + 1], last = clone(db[name]), name
    end end
    return out, nil
  end,
  storage_delete = function(ids)
    delete_calls = delete_calls + 1
    if before_delete then local fn = before_delete; before_delete = nil; fn() end
    for _, id in ipairs(ids) do local row = db[key(id.user_id, id.collection, id.key)]
      assert(row and row.version == id.version, "delete version conflict") end
    for _, id in ipairs(ids) do db[key(id.user_id, id.collection, id.key)] = nil end
  end,
}
package.preload.nakama = function() return nk end
local function fresh()
  return assert(loadfile("Server/nakama/modules/duel_storage_maintenance.lua"))()
end
local function reset()
  now, db, revision, list_calls, delete_calls, recover_calls = 50 * DAY, {}, 0, 0, 0, 0
  before_delete, fail_read = nil, false
  fail_list, list_trace = nil, {}
  owner_lists, matches = {}, {}
  elapsed_ms, list_delay_ms = 0, 0
end
local function tournament(id, phase, other)
  local v = { id = id, kind = "gold", bet_tier = "600", phase = phase, done_at = DAY,
    parts = { human = {} }, final = { { status = "done", winner_uid = "human" } },
    reward_receipt = { user_id = "human", granted_at = DAY } }
  if other then for k, val in pairs(other) do v[k] = val end end
  put(SYSTEM, "arena_tournament", id, v, DAY)
end
reset()
tournament("closed", "done")
tournament("active", "qf")
tournament("no-proof", "done", { reward_receipt = false })
tournament("aborted", "done", { done_at = 2 * DAY, reward_receipt = false, aborted = true, eliminated = { human = true } })
tournament("bot-final", "done", { done_at = 3 * DAY, reward_receipt = false,
  final = { { status = "done", winner_uid = "bot" } }, parts = { bot = { bot = true } } })
tournament("recent", "done", { done_at = 4 * DAY }); db[key(SYSTEM, "arena_tournament", "recent")].update_time = now
db[key(SYSTEM, "arena_tournament", "aborted")].value.reward_receipt = nil
db[key(SYSTEM, "arena_tournament", "bot-final")].value.reward_receipt = nil
put("old-user", "duel_api_rate_limits", "v1", { create = { tokens = 0, updated_ms = DAY * 1000 } }, DAY)
put("recent-user", "duel_api_rate_limits", "v1", {}, now)
put("old-user", "duel_api_rate_limits", "not-v1", {}, DAY)
put("race-user", "duel_race_entry", "active", { status = "bound", run_id = "current", receipt_id = "current" }, DAY)
put("race-user", "duel_race_entry", "run:old", { status = "settled", run_id = "old", receipt_id = "old", settled_at = DAY }, DAY)
put("race-user", "duel_race_entry", "run:current", { status = "settled", run_id = "current", receipt_id = "current", settled_at = DAY }, DAY)
put("race-user", "duel_race_entry", "run:unresolved", { status = "bound", run_id = "unresolved" }, DAY)
put("race-user", "duel_race_entry", "run:journal", { status = "settled", run_id = "journal", receipt_id = "journal", settled_at = DAY, journal_id = "pending" }, DAY)
local cleanup = fresh()
local report = assert(cleanup.maybe_run())
assert(report.deleted == 3)
assert(not exists(SYSTEM, "arena_tournament", "closed") and not exists(SYSTEM, "arena_tournament", "aborted"))
assert(not exists("old-user", "duel_api_rate_limits", "v1"))
for _, id in ipairs({ "active", "recent", "no-proof", "bot-final" }) do assert(exists(SYSTEM, "arena_tournament", id)) end
for _, id in ipairs({ "active", "run:old", "run:current", "run:unresolved", "run:journal" }) do assert(exists("race-user", RACE, id)) end
assert(exists("recent-user", "duel_api_rate_limits", "v1") and exists("old-user", "duel_api_rate_limits", "not-v1"))
local listed = list_calls
assert(cleanup.maybe_run() == false and fresh().maybe_run() == false and list_calls == listed, "shared minute lease")

reset()
put("racing", "duel_api_rate_limits", "v1", {}, DAY)
before_delete = function() put("racing", "duel_api_rate_limits", "v1", { refreshed = true }, now) end
report = assert(fresh().maybe_run())
assert(report.deleted == 0 and report.conflicted_batches == 1 and exists("racing", "duel_api_rate_limits", "v1"))

reset()
fail_read = true
assert(fresh().maybe_run() == false and delete_calls == 0, "storage failure must never delete")

reset()
for i = 1, 5 do put("offline-" .. i, "duel_race_entry", "active", { status = "bound" }, DAY) end
cleanup = fresh(); cleanup.configure({ recover = function() recover_calls = recover_calls + 1; error("recovery unavailable") end })
report = assert(cleanup.maybe_run())
assert(recover_calls == 2 and report.recovery_attempts == 2 and report.recovered_users == 0 and report.deleted == 0)

reset()
for i = 1, 675 do put(string.format("u%03d", i), "duel_api_rate_limits", "v1", {}, DAY) end
cleanup = fresh(); report = assert(cleanup.maybe_run()); assert(report.deleted == 500 and report.pages == 10)
now = now + 61; report = assert(cleanup.maybe_run()); assert(report.deleted == 175, "cursor survives deletion of previous pages")
print("Storage maintenance: terminal-only retention, active/latest protection, shared lease, CAS race, storage faults, bounded recovery and pagination passed")

-- Five-collection and startup regression cases. Run each case even if another
-- fails, so the report identifies all independent retention/liveness problems.
local passed, failures = 0, {}
local function test(name, fn)
  reset()
  local ok, err = pcall(fn)
  if ok then passed = passed + 1; print("PASS " .. name)
  else failures[#failures + 1] = name .. ": " .. tostring(err); print("FAIL " .. failures[#failures]) end
end
local function journal(uid, id, status, other, updated)
  local v = { result_id = id, status = status, completed_at = DAY }
  for k, x in pairs(other or {}) do v[k] = x end
  put(uid, "duel_match3_journal", "result:" .. id, v, updated or DAY)
end
local function launch(uid, id, status, other, updated)
  local v = { token = id, run_id = id, match_id = "match:" .. id, status = status, consumed_at = DAY, refunded_at = DAY }
  for k, x in pairs(other or {}) do v[k] = x end
  put(uid, "duel_pve_entry", "launch:" .. id, v, updated or DAY)
end
local function pending(uid, id, updated)
  put(uid, "duel_match3_journal", "pending", { entries = { [id] = { kind = "pve" } } }, updated or DAY)
end
local function race(uid, id, status, ended, other)
  local value = { run_id = id, receipt_id = "receipt-" .. uid .. "-" .. id, status = status,
    participants = { uid }, match_id = "race-match-" .. id, paid_at = ended - 1 }
  if status == "settled" then
    value.settled_at, value.winner, value.is_draw = ended, uid, false
    value.finish_intent = { run_id = id, winner = uid, is_draw = false }
  elseif status == "refunded" then
    value.refunded_at = ended
  end
  for k, v in pairs(other or {}) do value[k] = v end
  put(uid, RACE, "run:" .. id, value, ended)
  return value
end

test("Race keeps the newest two paid or refunded receipts per owner", function()
  race("a", "oldest", "settled", now - 300)
  race("a", "old", "refunded", now - 280)
  race("a", "previous", "settled", now - 210)
  local latest = race("a", "latest", "refunded", now - 180)
  race("b", "only", "settled", now - 360)
  put("a", RACE, "active", latest, now - 180)
  local report = assert(fresh().maybe_run())
  assert(report.deleted == 2 and report.race_owner_lookups == 2)
  assert(not exists("a", RACE, "run:oldest") and not exists("a", RACE, "run:old"))
  for _, key in ipairs({ "active", "run:previous", "run:latest" }) do assert(exists("a", RACE, key)) end
  assert(exists("b", RACE, "run:only"))
end)

test("Race protects active pointers, outstanding journal work and live or unknown matches", function()
  local active = race("u", "active-target", "settled", now - 600)
  put("u", RACE, "active", active, now - 600)
  race("u", "pending", "settled", now - 590)
  pending("u", "pending:stats")
  race("u", "open", "refunded", now - 580)
  put("u", "duel_match3_journal", "retention_v2", {
    schema = 2, open = { { id = "open:stats", origin = { root_result = "open" } } },
  }, now)
  race("u", "bound", "bound", now - 570, { finish_intent = { run_id = "bound" } })
  race("u", "live", "settled", now - 560)
  race("u", "unknown", "refunded", now - 550)
  matches["race-match-live"], matches["race-match-unknown"] = {}, "error"
  race("u", "safe-old", "settled", now - 400)
  race("u", "previous", "refunded", now - 200)
  race("u", "latest", "settled", now - 180)
  assert(fresh().maybe_run().deleted == 1 and not exists("u", RACE, "run:safe-old"))
  for _, key in ipairs({ "active-target", "pending", "open", "bound", "live", "unknown", "previous", "latest" }) do
    assert(exists("u", RACE, "run:" .. key))
  end
end)

test("Race keeps a receipt while another human owner can still need it for recovery", function()
  race("a", "peer", "settled", now - 350, { participants = { "a", "b" } })
  race("b", "peer", "bound", now - 350, { participants = { "a", "b" }, finish_intent = { run_id = "peer" } })
  race("a", "previous", "settled", now - 200)
  race("a", "latest", "settled", now - 180)
  local worker = fresh()
  assert(worker.maybe_run().deleted == 0)
  race("b", "peer", "settled", now - 350, { participants = { "a", "b" } })
  now = now + 61
  assert(worker.maybe_run().deleted == 1 and not exists("a", RACE, "run:peer") and exists("b", RACE, "run:peer"))
  race("b", "previous", "settled", now - 200)
  race("b", "latest", "settled", now - 180)
  now = now + 61
  assert(worker.maybe_run().deleted == 1 and not exists("b", RACE, "run:peer"))
end)

test("Race legacy receipt requires an idle registry and a versioned delete", function()
  local old = race("u", "old", "refunded", now - 330)
  old.match_id = nil
  put("u", RACE, "run:old", old, now - 330)
  race("u", "previous", "settled", now - 200)
  race("u", "latest", "refunded", now - 180)
  matches.unrelated = {}
  local worker = fresh()
  assert(worker.maybe_run().deleted == 0)
  now = now + 61
  matches.unrelated = nil
  before_delete = function() put("u", RACE, "run:old", old, now) end
  local report = assert(worker.maybe_run())
  assert(report.deleted == 0 and report.conflicted_batches == 1 and exists("u", RACE, "run:old"))
  now = now + 121
  assert(worker.maybe_run().deleted == 1)
end)

test("boss attempts retain two closed snapshots and preserve active, live and unproved requests", function()
  local requests = {}
  for index, state in ipairs({ "finished", "refunded", "finished", "finished", "finished", "finished", "finished", "reserved" }) do
    local id, request = "week-100:a" .. index, "request" .. index
    if index ~= 5 then requests[request] = id end
    local mid = "boss-match-" .. index
    if index == 3 then matches[mid] = {} end
    local attempt = { schema = 1, id = id, event_id = "week-100", request_id = request,
      status = state, created_at = now - 400 + index, finished_at = now - 300 + index,
      match_id = mid, sequence = 0, player_actions = 0 }
    if index == 2 then attempt.match_id = nil end
    put("u", BOSS, id, attempt, now - 300 + index)
  end
  local weekly = { schema = 1, event_id = "week-100", requests = requests, active_attempt_id = "week-100:a4",
    reward_mail_created = false, total_damage = 1234 }
  put("u", "duel_global_boss_player", "week-100", weekly, now)
  assert(fresh().maybe_run().deleted == 2)
  assert(not exists("u", BOSS, "week-100:a1") and not exists("u", BOSS, "week-100:a2"))
  for index = 3, 8 do assert(exists("u", BOSS, "week-100:a" .. index)) end
  local untouched = db[key("u", "duel_global_boss_player", "week-100")].value
  assert(untouched.requests.request1 == "week-100:a1" and untouched.total_damage == 1234 and not untouched.reward_mail_created)
end)

test("journal prunes only result complete or aborted with a terminal timestamp", function()
  journal("u", "complete", "complete"); journal("u", "aborted", "aborted")
  for _, status in ipairs({ "prepared", "committed", "unknown" }) do journal("u", status, status) end
  journal("u", "no-time", "complete", { completed_at = false })
  put("u", "duel_match3_journal", "pending", { entries = {} }, DAY)
  put("u", "duel_match3_journal", "other-key", { status = "complete", completed_at = DAY }, DAY)
  local report = assert(fresh().maybe_run()); assert(report.deleted == 0)
  assert(exists("u", "duel_match3_journal", "result:complete") and exists("u", "duel_match3_journal", "result:aborted"), "four recent completed results are retained")
  for _, id in ipairs({ "prepared", "committed", "unknown", "no-time" }) do assert(exists("u", "duel_match3_journal", "result:" .. id)) end
  assert(exists("u", "duel_match3_journal", "pending") and exists("u", "duel_match3_journal", "other-key"))
end)

test("pending index protects both complete and aborted journal references", function()
  journal("u", "kept-complete", "complete"); journal("u", "kept-aborted", "aborted"); journal("u", "free", "complete")
  put("u", "duel_match3_journal", "pending", { entries = { ["kept-complete"] = {}, ["kept-aborted"] = true } }, DAY)
  local report = assert(fresh().maybe_run()); assert(report.deleted == 0)
  assert(exists("u", "duel_match3_journal", "result:kept-complete") and exists("u", "duel_match3_journal", "result:kept-aborted"))
end)

test("journal keeps four recent receipts without a thirty-day wait and ignores invalid timestamps", function()
  journal("u", "at-cutoff", "complete", { completed_at = now - 30 * DAY }, now - 30 * DAY)
  journal("u", "terminal-recent", "complete", { completed_at = now - 30 * DAY + 1 })
  journal("u", "update-recent", "aborted", nil, now - 30 * DAY + 1)
  journal("u", "nan", "complete", { completed_at = 0 / 0 })
  journal("u", "future", "complete", { completed_at = now + DAY })
  local report = assert(fresh().maybe_run()); assert(report.deleted == 0)
  for _, id in ipairs({ "terminal-recent", "update-recent", "nan", "future" }) do assert(exists("u", "duel_match3_journal", "result:" .. id)) end
end)

test("public or unversioned journal metadata never reaches deletion", function()
  journal("u", "public", "complete"); journal("u", "writable", "complete"); journal("u", "no-version", "complete")
  db[key("u", "duel_match3_journal", "result:public")].permission_read = 1
  db[key("u", "duel_match3_journal", "result:writable")].permission_write = 1
  db[key("u", "duel_match3_journal", "result:no-version")].version = ""
  assert(fresh().maybe_run().deleted == 0 and delete_calls == 0)
end)

test("PvE keeps two settled launches and protects the active target even if older", function()
  put("u", "duel_pve_entry", "active", { status = "consumed", token = "protected", run_id = "protected" }, DAY)
  launch("u", "latest", "consumed", { consumed_at = now - 130 }, now - 130)
  launch("u", "previous", "consumed", { consumed_at = now - 140 }, now - 140)
  launch("u", "old", "consumed"); launch("u", "refund", "refunded"); launch("u", "protected", "consumed")
  for _, status in ipairs({ "reserved", "bound", "refund_pending" }) do launch("u", status, status) end
  put("u", "duel_pve_entry", "refunds", { tokens = { refund_pending = true } }, DAY)
  local report = assert(fresh().maybe_run()); assert(report.deleted == 2)
  assert(#owner_lists == 1 and report.pve_owner_lookups == 1)
  assert(not exists("u", "duel_pve_entry", "launch:old") and not exists("u", "duel_pve_entry", "launch:refund"))
  for _, id in ipairs({ "latest", "previous", "protected", "reserved", "bound", "refund_pending" }) do assert(exists("u", "duel_pve_entry", "launch:" .. id)) end
  assert(exists("u", "duel_pve_entry", "active") and exists("u", "duel_pve_entry", "refunds"))
end)

test("terminal statuses are exact for each receipt collection", function()
  put("u", "duel_race_entry", "run:wrong-consumed", { status = "consumed", consumed_at = DAY, run_id = "wrong-consumed" }, DAY)
  launch("u", "wrong-settled", "settled", { settled_at = DAY })
  put("u", "duel_race_entry", "run:valid", { status = "settled", settled_at = DAY, run_id = "valid" }, DAY)
  launch("u", "valid", "consumed")
  local report = assert(fresh().maybe_run()); assert(report.deleted == 0)
  assert(exists("u", "duel_race_entry", "run:wrong-consumed") and exists("u", "duel_pve_entry", "launch:wrong-settled"))
  assert(exists("u", "duel_pve_entry", "launch:valid"), "a lone terminal PvE receipt is retained")
end)

test("PvE protects newly updated receipts during the grace window", function()
  launch("u", "newer", "consumed", { consumed_at = now - 180 }, now - 180)
  launch("u", "newest", "consumed", { consumed_at = now - 170 }, now - 170)
  launch("u", "recent-update", "refunded", { refunded_at = now - 240 }, now)
  launch("u", "missing-time", "consumed", { consumed_at = false })
  cleanup = fresh(); assert(cleanup.maybe_run().deleted == 0)
  now = now + 121; assert(cleanup.maybe_run().deleted == 1)
  assert(not exists("u", "duel_pve_entry", "launch:recent-update") and exists("u", "duel_pve_entry", "launch:missing-time"))
end)

test("PvE receipt keeps its pending journal reference until delivery finishes", function()
  launch("u", "protected", "consumed", { journal_id = "pending-result" })
  launch("u", "previous", "consumed", { consumed_at = now - 180 })
  launch("u", "latest", "consumed", { consumed_at = now - 170 })
  pending("u", "pending-result")
  cleanup = fresh()
  assert(cleanup.maybe_run().deleted == 0)
  put("u", "duel_match3_journal", "pending", { entries = {} }, now)
  now = now + 61; assert(cleanup.maybe_run().deleted == 1)
end)

test("changed journal version rejects its whole deletion batch", function()
  journal("u", "a", "complete"); journal("u", "b", "aborted")
  for i=1,4 do journal("u","z"..i,"complete",{completed_at=DAY+i}) end
  cleanup=fresh();assert(cleanup.maybe_run().deleted==0);now=now+61
  before_delete = function() journal("u", "a", "complete", { updated = true }, now) end
  local report = assert(cleanup.maybe_run())
  assert(report.deleted == 0 and report.conflicted_batches == 1)
  assert(exists("u", "duel_match3_journal", "result:a") and exists("u", "duel_match3_journal", "result:b"))
end)

test("one minute pass is bounded to ten pages across six collections", function()
  for _, collection in ipairs(COLLECTIONS) do
    for i = 1, 240 do put("u", collection, string.format("opaque-%03d", i), {}, DAY) end
  end
  local report = assert(fresh().maybe_run())
  assert(list_calls == 10 and report.pages == 10 and report.scanned == 544 and report.deleted == 0)
  local limits = { [RACE] = report.race_owner_budget, ["duel_pve_entry"] = report.pve_owner_budget,
    [BOSS] = report.boss_attempt_owner_budget }
  for i, call in ipairs(list_trace) do
    assert(i <= 6 and call.cursor == "" or i > 6 and call.cursor ~= "")
    assert(call.limit == (limits[call.collection] or 100))
  end
end)

test("exhausted PvE owner budget resumes at the next owner on the following pass", function()
  for i = 1, 18 do launch(string.format("u%02d", i), "only", "consumed") end
  cleanup = fresh()
  local first = assert(cleanup.maybe_run())
  assert(first.pve_owner_lookups == first.pve_owner_budget and first.deleted == 0 and #owner_lists == 12)
  assert(owner_lists[1] == "u01" and owner_lists[12] == "u12")
  now = now + 61; owner_lists = {}
  local second = assert(cleanup.maybe_run())
  assert(second.pve_owner_lookups == 6 and second.deleted == 0 and owner_lists[1] == "u13" and owner_lists[6] == "u18")
end)

test("wall-clock budget persists round-robin position instead of starving later collections", function()
  for _, collection in ipairs({ "arena_tournament", "duel_api_rate_limits", "duel_race_entry", "duel_pve_entry", "duel_match3_journal" }) do
    for i = 1, 150 do put("u", collection, string.format("opaque-%03d", i), {}, DAY) end
  end
  list_delay_ms = 150; cleanup = fresh()
  local report = assert(cleanup.maybe_run())
  assert(report.pages == 2 and report.elapsed_ms == 300 and report.scanned == 200)
  local saved = db[key(SYSTEM, "duel_storage_maintenance", "v1")].value
  assert(saved.next_collection == 3 and saved.next_run_at == now + 60)
  local previous_calls = list_calls; now = now + 30
  assert(fresh().maybe_run() == false and list_calls == previous_calls)
  now = now + 31; list_trace = {}; list_delay_ms = 0
  report = assert(cleanup.maybe_run())
  assert(list_trace[1].collection == "duel_race_entry" and report.pages <= 10)
end)

test("hourly recovery budget and UID dedup are shared by PvE and journal pages", function()
  put("a", "duel_pve_entry", "active", { status = "bound" }, DAY)
  put("a", "duel_pve_entry", "refunds", { tokens = { t = true } }, DAY)
  pending("a", "r"); pending("b", "s"); pending("c", "t")
  local seen = {}; cleanup = fresh(); cleanup.configure({ recover = function(uid) seen[uid] = (seen[uid] or 0) + 1; return true end })
  local report = assert(cleanup.maybe_run())
  assert(report.recovery_attempts == 2 and report.recovered_users == 2 and seen.a == 1 and seen.b == 1 and not seen.c)
end)

test("hourly callback exceptions and false results do not abort remaining collections", function()
  put("a", "duel_pve_entry", "active", { status = "bound" }, DAY); pending("b", "r")
  cleanup = fresh(); cleanup.configure({ recover = function(uid) recover_calls = recover_calls + 1; if uid == "a" then error("one user unavailable") end; return false end })
  local report = assert(cleanup.maybe_run())
  assert(report.recovery_attempts == 2 and report.recovered_users == 0 and recover_calls == 2 and list_calls == 6)
end)

test("fresh unresolved work is deferred hourly but is eligible at bootstrap", function()
  put("a", "duel_pve_entry", "active", { status = "reserved" }, now); pending("b", "r", now)
  cleanup = fresh(); cleanup.configure({ recover = function() recover_calls = recover_calls + 1; return true end })
  assert(cleanup.maybe_run().recovery_attempts == 0)
  local boot = assert(cleanup.bootstrap()); assert(boot.attempted_users == 2 and boot.recovered_users == 2)
end)

test("bootstrap deduplicates owner across active refund-index and result rows", function()
  put("a", "duel_race_entry", "active", { status = "bound" }, DAY)
  put("a", "duel_pve_entry", "active", { status = "refund_pending" }, DAY)
  put("a", "duel_pve_entry", "refunds", { tokens = { t = true } }, DAY); pending("a", "r")
  journal("b", "intent", "prepared")
  local seen = {}; cleanup = fresh(); cleanup.configure({ recover = function(uid) seen[uid] = (seen[uid] or 0) + 1; return { ok = true } end })
  local boot = assert(cleanup.bootstrap())
  assert(boot.attempted_users == 2 and boot.recovered_users == 2 and boot.failed_users == 0 and seen.a == 1 and seen.b == 1)
  assert(list_calls == 3 and delete_calls == 0)
end)

test("bootstrap shared fifty-owner cap never loops all pages", function()
  for i = 1, 60 do
    put(string.format("race-%03d", i), "duel_race_entry", "active", { status = "bound" }, DAY)
    put(string.format("pve-%03d", i), "duel_pve_entry", "active", { status = "reserved" }, DAY)
    pending(string.format("journal-%03d", i), "r")
  end
  cleanup = fresh(); cleanup.configure({ recover = function() recover_calls = recover_calls + 1; return true end })
  local boot = assert(cleanup.bootstrap())
  assert(boot.attempted_users == 50 and recover_calls == 50 and list_calls == 3 and delete_calls == 0)
  for _, call in ipairs(list_trace) do assert(call.cursor == "") end
end)

test("one bootstrap callback error cannot prevent the next owner recovery", function()
  put("a-bad", "duel_race_entry", "active", { status = "bound" }, DAY)
  put("b-good", "duel_race_entry", "active", { status = "bound" }, DAY)
  local good = 0; cleanup = fresh(); cleanup.configure({ recover = function(uid) if uid == "a-bad" then error("corrupt owner") end; good = good + 1; return true end })
  local boot = assert(cleanup.bootstrap())
  assert(good == 1 and boot.attempted_users == 2 and boot.failed_users == 1 and boot.recovered_users == 1)
end)

test("minute cursor continues journal recovery beyond the startup and run budgets", function()
  for i = 1, 900 do journal(string.format("u%04d", i), "complete", "complete", nil, now) end
  pending("z-offline", "unpaid-result")
  cleanup = fresh(); cleanup.configure({ recover = function(uid) assert(uid == "z-offline"); recover_calls = recover_calls + 1; return true end })
  assert(cleanup.bootstrap().attempted_users == 0)
  assert(cleanup.maybe_run().recovery_attempts == 0)
  now = now + 61
  local report = assert(cleanup.maybe_run()); assert(report.recovery_attempts == 1 and recover_calls == 1)
end)

test("bootstrap storage failure is caught and performs no deletions", function()
  put("a", "duel_pve_entry", "active", { status = "bound" }, DAY)
  cleanup = fresh(); cleanup.configure({ recover = function() recover_calls = recover_calls + 1; return true end })
  fail_list = "duel_race_entry"
  assert(cleanup.bootstrap() == false and recover_calls == 0 and delete_calls == 0)
  fail_list = nil; assert(cleanup.bootstrap().attempted_users == 1)
end)

test("bootstrap without configured recovery never scans or deletes", function()
  assert(fresh().bootstrap() == false and list_calls == 0 and delete_calls == 0)
end)

test("actual core run-once hook calls configured bootstrap and tolerates absent API", function()
  local file = assert(io.open("Server/nakama/modules/duel_match3.lua", "r")); local source = file:read("*a"):gsub("\r\n", "\n"); file:close()
  local start = assert(source:find('if type(nk.run_once) == "function" then', 1, true))
  local stop = assert(source:find("\nreturn {", start, true))
  assert(source:find("Runtime.StorageMaintenance.configure({", 1, true) < start)
  local calls, boot, seeded = 0, 0, 0
  local env = setmetatable({ nk = { run_once = function(fn) calls = calls + 1; fn() end },
    CFG = { BOTS_KEYS_BY_DIFFICULTY = {} },
    Runtime = { StorageMaintenance = { bootstrap = function() boot = boot + 1 end },
      MinePrices = { upgrade_catalogs = function() return true, 0 end },
      seed_cheat_whitelist = function() seeded = seeded + 1 end } }, { __index = _G })
  local chunk = assert(loadstring(source:sub(start, stop - 1))); setfenv(chunk, env); chunk()
  assert(calls == 1 and boot == 1 and seeded == 1)
  local dispatches = 0
  env.Runtime.GlobalBoss = {}
  env.nk.match_create = function(name, params)
    assert(name == "duel_global_boss_dispatcher" and params.bootstrap == true)
    dispatches = dispatches + 1
  end
  chunk(); assert(calls == 2 and boot == 2 and seeded == 2 and dispatches == 1)
  env.nk.run_once = nil; chunk(); assert(calls == 2 and boot == 2 and seeded == 2 and dispatches == 1)
end)

print("Storage maintenance extended: " .. passed .. " passed, " .. #failures .. " failed; original baseline passed")
assert(#failures == 0, table.concat(failures, "\n"))
