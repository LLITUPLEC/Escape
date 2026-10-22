-- Run against the real arena module with Lua 5.4 (yield through pcall).
-- Storage implements Nakama batch atomicity and exact create/version checks.
local function copy(v)
  if type(v) ~= "table" then return v end
  local r = {}
  for k, c in pairs(v) do r[k] = copy(c) end
  return r
end
local system = "00000000-0000-0000-0000-000000000000"
local cfg = { STATS_COLLECTION = "duel_match3_stats", STATS_KEY = "summary",
  PVE_PROGRESS_COLLECTION = "duel_match3_progress", PVE_PROGRESS_KEY = "profile",
  CHARACTER_SHEET_COLLECTION = "duel_match3_character", CHARACTER_SHEET_KEY = "sheet" }
local store, serial, uuid, wallet_writes, fail_next, before_write = {}, 0, 0, 0
local barrier, clock, creates, api_for_creation, last_setup = false, 1000, 0
local original_time = os.time
os.time = function() return clock end
local function key(o) return o.collection .. "/" .. o.key .. "/" .. (o.user_id or system) end
local function put(collection, k, uid, value)
  serial = serial + 1
  store[collection .. "/" .. k .. "/" .. (uid or system)] = { value = copy(value), version = "v" .. serial }
end
local function get(collection, k, uid)
  local row = store[collection .. "/" .. k .. "/" .. (uid or system)]
  return row and row.value
end
local function wo(uid, collection, k, value, version)
  return { user_id = uid, collection = collection, key = k, value = value, version = version or "*" }
end
local nk = {
  json_encode = copy, json_decode = copy,
  uuid_v4 = function() uuid = uuid + 1 return "uuid-" .. uuid end,
  account_get_id = function(uid) return { user = { username = uid } } end,
  storage_read = function(ids)
    local result = {}
    for _, id in ipairs(ids) do
      local row = store[key(id)]
      if row then result[#result + 1] = copy(row) end
    end
    if barrier and ids[1].collection == "arena_tournament" then coroutine.yield("snapshot") end
    if barrier == "queue" and ids[1].collection == "arena_queue" then coroutine.yield("snapshot") end
    return result
  end,
  storage_write = function(objects)
    if before_write then local fn = before_write before_write = nil fn(objects) end
    if fail_next then local err = fail_next fail_next = nil error(err) end
    local seen = {}
    for _, object in ipairs(objects) do
      local k, existing = key(object), store[key(object)]
      assert(not seen[k], "duplicate object in batch")
      seen[k] = true
      assert(object.permission_write == nil or object.permission_write == 0, "client-writable authoritative state")
      if object.version == "*" then
        if existing then error("Storage write rejected - version check failed") end
      elseif object.version ~= nil and (not existing or object.version ~= existing.version) then
        error("Storage write rejected - version check failed")
      end
    end
    local acks = {}
    for _, object in ipairs(objects) do
      put(object.collection, object.key, object.user_id, object.value)
      if object.collection == cfg.PVE_PROGRESS_COLLECTION then wallet_writes = wallet_writes + 1 end
      acks[#acks + 1] = { collection = object.collection, key = object.key, version = store[key(object)].version }
    end
    return acks
  end,
  logger_info = function() end, logger_error = function() end, logger_warn = function() end,
}
package.preload.nakama = function() return nk end
package.preload["modules.duel_match3_config"] = function() return cfg end
package.preload["modules.duel_fake_online"] = function() return {
  arena_eligible_bot_names = function() return { "bot-a", "bot-b", "bot-c" } end,
  is_online_pool_name = function() return false end,
} end
local factory = assert(loadfile("Server/nakama/modules/arena_tournament.lua"))()
local function instance()
  local api
  api = factory({
    guard_assert_client_epoch_matches = function() return true end,
    guard_read_metadata_epoch = function() return 0 end,
    make_bot_user_id = function(name) return "zz-bot-" .. name end,
    try_match_create = function(setup)
      creates, last_setup = creates + 1, copy(setup)
      local mid = "created-" .. creates
      if api_for_creation then
        api_for_creation.on_match_created({ match_id = mid, arena_mirror = copy(setup.arena_mirror) })
      end
      return mid
    end,
    read_pve_progress = function(uid)
      local row = store[cfg.PVE_PROGRESS_COLLECTION .. "/profile/" .. uid]
      return copy(row and row.value or { gold = 0, ore = 0 }), row and row.version
    end,
    build_pve_progress_write = function(uid, value, version)
      return wo(uid, cfg.PVE_PROGRESS_COLLECTION, cfg.PVE_PROGRESS_KEY, value, version)
    end,
    read_character_sheet = function(uid)
      local row = store[cfg.CHARACTER_SHEET_COLLECTION .. "/sheet/" .. uid]
      local sheet = copy(row and row.value or { inventory_counts = {} })
      sheet._storage_version = row and row.version or "*"
      return sheet
    end,
    build_character_sheet_write = function(uid, sheet)
      local value = copy(sheet)
      value._storage_version = nil
      return wo(uid, cfg.CHARACTER_SHEET_COLLECTION, cfg.CHARACTER_SHEET_KEY, value, sheet._storage_version)
    end,
    ensure_sheet_inventory_counts = function(sheet) sheet.inventory_counts = sheet.inventory_counts or {} end,
    inventory_remove_def_total = function(sheet, def, n)
      local have = sheet.inventory_counts[def] or 0
      if have < n then return false end
      sheet.inventory_counts[def] = have - n
      return true
    end,
    inventory_try_add = function(sheet, def, n)
      if sheet.full then return false, "inventory_full" end
      sheet.inventory_counts[def] = (sheet.inventory_counts[def] or 0) + n
      return true
    end,
  })
  return api
end
local function fixture(round)
  local T = { id = "audit", kind = "gold", bet_tier = "600", phase = round, bracket_size = 8,
    parts = {}, eliminated = {}, qf = {}, sf = {}, final = {}, side_bets = {},
    bet_score = {}, bet_wins = {}, bet_losses = {} }
  for i = 1, round == "final" and 1 or 4 do
    local a, b = "user-" .. (i * 2 - 1), "user-" .. (i * 2)
    T.parts[a], T.parts[b] = { display = a, bot = false }, { display = b, bot = false }
    T[round][i] = { uid_a = a, uid_b = b, hp_a = 150, hp_b = 150,
      status = "fighting", match_id = "match-" .. i }
  end
  return T
end
local function reset(round)
  store, serial, wallet_writes, fail_next, before_write, barrier, creates, api_for_creation = {}, 0, 0, nil, nil, false, 0, nil
  clock = 1000
  if round then
    put("arena_tournament", "audit", system, fixture(round))
    for i = 1, 8 do put("arena_user_tid", "tid", "user-" .. i, { tid = "audit", kind = "gold" }) end
  end
end
local function state(round, slot)
  return { match_id = "match-" .. slot, arena_mirror = { tournament_id = "audit", round = round, slot_index = slot } }
end
local function finish(api, round, slot) return api.on_match_finished(state(round, slot), "user-" .. (slot * 2 - 1)) end
local function interleave(first, second, mode)
  barrier = mode or true
  local a, b = coroutine.create(first), coroutine.create(second)
  local function resume(thread)
    local ok, value = coroutine.resume(thread)
    assert(ok, tostring(value))
    return value
  end
  assert(resume(a) == "snapshot" and resume(b) == "snapshot", "missing snapshot barrier")
  barrier = false
  while coroutine.status(a) ~= "dead" do resume(a) end
  while coroutine.status(b) ~= "dead" do resume(b) end
end
local function completed()
  local n = 0
  for _, pr in ipairs(get("arena_tournament", "audit").qf) do if pr.status == "done" then n = n + 1 end end
  return n
end
local function gold(uid) return (get(cfg.PVE_PROGRESS_COLLECTION, "profile", uid or "user-1") or {}).gold or 0 end
local function ach(uid, stat)
  local value = get(cfg.STATS_COLLECTION, "summary", uid)
  return value and value.achievement_stats and value.achievement_stats[stat] or 0
end
local placed = "slaughter.arena_bets_placed"
local function betting()
  reset("qf")
  local T = get("arena_tournament", "audit")
  T.phase, T.countdown_until = "countdown", clock + 20
end
local function bet(api, uid, p)
  local r = api.duel_arena_place_bet({ user_id = uid or "user-1" }, p or { slot = 2, side = "a" })
  assert(r.ok, tostring(r.err))
  return r
end
local count = 0
local function pass(name) count = count + 1 print("PASS " .. name) end

reset("qf")
finish(instance(), "qf", 1) finish(instance(), "qf", 2)
assert(completed() == 2) pass("sequential quarterfinal control")
reset("final")
finish(instance(), "final", 1) finish(instance(), "final", 1)
assert(wallet_writes == 1 and gold() == 3000) pass("sequential final pays once")
reset("qf")
local a, b = instance(), instance()
interleave(function() finish(a, "qf", 1) end, function() finish(b, "qf", 2) end)
assert(completed() == 2) pass("concurrent quarterfinal snapshots preserve both results")
reset("final")
a, b = instance(), instance()
interleave(function() finish(a, "final", 1) end, function() finish(b, "final", 1) end)
assert(gold() == 3000 and wallet_writes == 1) pass("concurrent final pays exactly one prize")

betting() a = instance()
for _ = 1, 10 do bet(a) end
for _ = 1, 4 do
  bet(a, nil, { slot = 2, side = "b" })
  bet(a, nil, { slot = 2, clear = true })
  bet(a, nil, { slot = 2, side = "a" })
end
assert(ach("user-1", placed) == 1) pass("repeated, switched and cleared forecast counts once per slot")
bet(a, nil, { slot = 3, side = "a" })
assert(ach("user-1", placed) == 2) pass("different legal slots count independently")
betting() a, b = instance(), instance()
interleave(function() bet(a) end, function() bet(b) end)
assert(ach("user-1", placed) == 1) pass("concurrent duplicate forecasts increment once")
betting() a, b = instance(), instance()
interleave(function() bet(a) end, function() bet(b, "user-2") end)
assert(ach("user-1", placed) == 1 and ach("user-2", placed) == 1)
assert(get("arena_tournament", "audit").side_bets["user-1"] and get("arena_tournament", "audit").side_bets["user-2"])
pass("concurrent different users preserve both forecasts and counters")
betting() a = instance() fail_next = "injected storage outage"
local rejected = a.duel_arena_place_bet({ user_id = "user-1" }, { slot = 2, side = "a" })
assert(not rejected.ok and ach("user-1", placed) == 0 and not get("arena_tournament", "audit").bet_counted)
bet(a) assert(ach("user-1", placed) == 1) pass("failed forecast batch leaves neither marker nor counter; retry counts once")
reset("final") a = instance() fail_next = "injected storage outage"
assert(not pcall(finish, a, "final", 1))
assert(gold() == 0 and get("arena_tournament", "audit").phase == "final")
finish(a, "final", 1)
assert(gold() == 3000 and wallet_writes == 1) pass("final outage commits neither result nor money; retry recovers")
reset("final") a = instance()
before_write = function() put(cfg.PVE_PROGRESS_COLLECTION, "profile", "user-1", { gold = 100, ore = 0 }) end
finish(a, "final", 1)
assert(gold() == 3100) pass("wallet version conflict preserves unrelated update and one prize")

reset("final") a, b = instance(), instance()
local stale = state("final", 1)
stale.stats = { ["user-1"] = { hp = 150 }, ["user-2"] = { hp = 150 } }
interleave(function() b.mirror_commit(stale) end, function() finish(a, "final", 1) end)
b.mirror_commit(stale)
assert(get("arena_tournament", "audit").phase == "done" and gold() == 3000 and wallet_writes == 1)
pass("stale mirror cannot overwrite terminal receipt or reissue prize")

betting() a, b = instance(), instance()
for _, pr in ipairs(get("arena_tournament", "audit").qf) do pr.status, pr.match_id = "pending", "" end
clock = clock + 21 api_for_creation = a
local function poll(api) return api.duel_arena_queue_poll({ user_id = "user-1" }, { arena_kind = "gold", bet_tier = "600" }) end
interleave(function() assert(poll(a).ok) end, function() assert(poll(b).ok) end)
assert(creates == 4 and get("arena_tournament", "audit").phase == "qf")
for _, pr in ipairs(get("arena_tournament", "audit").qf) do assert(pr.status == "fighting" and pr.spawn_token and pr.match_id ~= "") end
pass("concurrent countdown polls create exactly four uniquely claimed fights")
local token = get("arena_tournament", "audit").qf[1].spawn_token
local bad = state("qf", 1) bad.arena_mirror.spawn_token = "stale"
finish(b, "qf", 1) b.on_match_finished(bad, "user-1")
assert(get("arena_tournament", "audit").qf[1].status == "fighting")
pass("legacy or wrong spawn-token callbacks cannot finish a new claimed fight")

reset("qf") a, b = instance(), instance()
local T = get("arena_tournament", "audit")
T.qf[1].status, T.qf[1].match_id = "spawning", ""
T.qf[1].spawn_token, T.qf[1].spawn_started_at = "expired-token", clock - 31
api_for_creation = a
assert(poll(b).ok and creates == 1)
local late = state("qf", 1) late.match_id, late.arena_mirror.spawn_token = "late-orphan", "expired-token"
assert(not pcall(a.on_match_created, late))
assert(get("arena_tournament", "audit").qf[1].spawn_token ~= "expired-token")
pass("expired creation lease recovers and rejects a delayed old creator")

betting() a = instance() clock = clock + 21
for _, pr in ipairs(get("arena_tournament", "audit").qf) do pr.status, pr.match_id = "pending", "" end
barrier = true
local stale_poll = coroutine.create(function() assert(poll(a).ok) end)
local ok, marker = coroutine.resume(stale_poll)
assert(ok and marker == "snapshot")
local terminal = copy(get("arena_tournament", "audit")) terminal.phase = "done"
put("arena_tournament", "audit", system, terminal)
barrier = false
assert(coroutine.resume(stale_poll))
assert(coroutine.status(stale_poll) == "dead" and creates == 0 and get("arena_tournament", "audit").phase == "done")
pass("stale countdown snapshot cannot resurrect a terminal tournament or spawn fights")

reset() a, b = instance(), instance()
put(cfg.PVE_PROGRESS_COLLECTION, "profile", "alice", { gold = 5000, ore = 0 })
local function join(api) return api.duel_arena_queue_join({ user_id = "alice" }, { arena_kind = "gold", bet_tier = "600" }) end
local r1, r2
interleave(function() r1 = join(a) end, function() r2 = join(b) end, "queue")
assert(r1.ok ~= r2.ok and gold("alice") == 4400)
assert(#get("arena_queue", "state").queues["gold:600"].entries == 1)
pass("cross-VM duplicate queue join charges exactly once")
local l1, l2
interleave(function() l1 = a.duel_arena_queue_leave({ user_id = "alice" }) end,
  function() l2 = b.duel_arena_queue_leave({ user_id = "alice" }) end, "queue")
assert(l1.ok ~= l2.ok and gold("alice") == 5000 and #get("arena_queue", "state").queues["gold:600"].entries == 0)
pass("cross-VM duplicate cancellation refunds exactly once")
reset() a = instance()
put(cfg.PVE_PROGRESS_COLLECTION, "profile", "alice", { gold = 5000, ore = 0 })
fail_next = "injected storage outage"
assert(not join(a).ok and gold("alice") == 5000 and not get("arena_queue", "state"))
assert(join(a).ok) fail_next = "injected storage outage"
assert(not a.duel_arena_queue_leave({ user_id = "alice" }).ok and gold("alice") == 4400)
assert(a.duel_arena_queue_leave({ user_id = "alice" }).ok and gold("alice") == 5000)
pass("queue debit/refund outage cannot lose an entry or double refund")

reset() a = instance()
put(cfg.CHARACTER_SHEET_COLLECTION, "sheet", "smith", { inventory_counts = { ingot_green = 100 } })
assert(a.duel_arena_queue_join({ user_id = "smith" }, { arena_kind = "smith", bet_tier = "green" }).ok)
local smith = get(cfg.CHARACTER_SHEET_COLLECTION, "sheet", "smith")
assert(smith.inventory_counts.ingot_green == 50)
smith = copy(smith) smith.full = true put(cfg.CHARACTER_SHEET_COLLECTION, "sheet", "smith", smith)
assert(a.duel_arena_queue_leave({ user_id = "smith" }).err == "inventory_full")
assert(get("arena_queue", "state").users.smith and get(cfg.CHARACTER_SHEET_COLLECTION, "sheet", "smith").inventory_counts.ingot_green == 50)
smith.full = false put(cfg.CHARACTER_SHEET_COLLECTION, "sheet", "smith", smith)
assert(a.duel_arena_queue_leave({ user_id = "smith" }).ok)
assert(get(cfg.CHARACTER_SHEET_COLLECTION, "sheet", "smith").inventory_counts.ingot_green == 100)
pass("smith refund needs inventory space and preserves paid entry until successful refund")

reset()
local clients = {}
for i = 1, 8 do
  clients[i] = instance()
  put(cfg.PVE_PROGRESS_COLLECTION, "profile", "player-" .. i, { gold = 5000, ore = 0 })
end
barrier = "queue"
local threads = {}
for i = 1, 8 do
  threads[i] = coroutine.create(function()
    local r = clients[i].duel_arena_queue_join({ user_id = "player-" .. i }, { arena_kind = "gold", bet_tier = "600" })
    assert(r.ok, r.err)
  end)
  local good, yielded = coroutine.resume(threads[i])
  assert(good and yielded == "snapshot")
end
barrier = false
for i = 1, 8 do
  while coroutine.status(threads[i]) ~= "dead" do assert(coroutine.resume(threads[i])) end
end
local registry = get("arena_queue", "state")
local tid = registry.users["player-1"].tid
for i = 1, 8 do
  assert(registry.users["player-" .. i].tid == tid and gold("player-" .. i) == 4400)
  assert(get(cfg.STATS_COLLECTION, "summary", "player-" .. i).summary.arena_tournaments.gold.played == 1)
end
assert(#registry.queues["gold:600"].entries == 0)
pass("eight concurrent VM joins form one shared bracket and charge every entrant once")
api_for_creation = clients[1]
local last_winner
for _, rk in ipairs({ "qf", "sf", "final" }) do
  clock = clock + 21
  local waiting = get("arena_tournament", tid)
  local survivor = waiting[rk][1].uid_a
  local p = clients[1].duel_arena_queue_poll({ user_id = survivor }, { arena_kind = "gold", bet_tier = "600" })
  assert(p.ok)
  local current = get("arena_tournament", tid)
  assert(current.phase == rk)
  local pairs = copy(current[rk])
  for index, pr in ipairs(pairs) do
    local s = { match_id = pr.match_id, arena_mirror = { tournament_id = tid, round = rk, slot_index = index, spawn_token = pr.spawn_token } }
    last_winner = pr.uid_a
    clients[index].on_match_finished(s, last_winner)
    clients[index].on_match_finished(s, last_winner)
  end
end
local terminal = get("arena_tournament", tid)
assert(terminal.phase == "done" and terminal.reward_receipt.user_id == last_winner)
assert(gold(last_winner) == 7400 and creates == 7)
assert(next(get("arena_queue", "state").users) == nil)
pass("full three-round tournament reaches one prize and clears every active membership")

os.time = original_time
print("arena_concurrency_audit: " .. count .. " invariants passed")
