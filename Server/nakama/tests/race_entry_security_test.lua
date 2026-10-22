-- In-memory transactional storage with injected interleavings. No live accounts.
package.path = "Server/nakama/?.lua;Server/nakama/modules/?.lua;" .. package.path
local function copy(value)
  if type(value) ~= "table" then return value end
  local out = {}; for k, v in pairs(value) do out[k] = copy(v) end; return out
end
local db, before_write, fail_write, fail_read, sequence, batches
local function id(uid, key, collection) return (collection or "duel_race_entry") .. ":" .. uid .. ":" .. key end
local function put(uid, key, value, collection)
  local key_id = id(uid, key, collection); local previous = db[key_id]
  db[key_id] = { value = copy(value), version = tostring((previous and tonumber(previous.version) or 0) + 1) }
end
local nk = {
  json_encode = copy, json_decode = copy,
  uuid_v4 = function() sequence = sequence + 1; return "receipt-" .. sequence end,
  logger_error = function() end, logger_info = function() end, logger_warn = function() end,
  storage_read = function(requests)
    if fail_read then error("database unavailable") end
    local rows = {}
    for _, request in ipairs(requests) do
      local row = db[id(request.user_id, request.key, request.collection)]
      if row then rows[#rows + 1] = copy(row) end
    end
    return rows
  end,
  storage_write = function(writes)
    batches[#batches + 1] = copy(writes)
    if before_write then local callback = before_write; before_write = nil; callback() end
    if fail_write then error("database unavailable") end
    for _, write in ipairs(writes) do
      local row = db[id(write.user_id, write.key, write.collection)]
      if write.version == "*" then assert(not row, "version check failed")
      else assert(row and row.version == write.version, "version check failed") end
    end
    for _, write in ipairs(writes) do put(write.user_id, write.key, write.value, write.collection) end
    return {}
  end,
}
package.loaded.nakama = nk
local Module = require("duel_match3_race_entry")
local function progress(uid)
  local row = db[id(uid, "profile", "progress")]
  if row then return copy(row.value), row.version end
  return { matter = 0, gold = 0, ore = 0, energy = 100, xp = 0, level = 1 }, nil
end
local function build(uid, value, version)
  return { collection = "progress", key = "profile", user_id = uid, value = copy(value), version = version or "*", permission_read = 1, permission_write = 0 }
end
local Entry = Module.new({ read_pve_progress = progress, build_pve_progress_write = build })
local COST = { { resource = "matter", amount = 2 } }
local function active(uid) local row = db[id(uid, "active")]; return row and row.value end
local function receipt(uid, run) local row = db[id(uid, "run:" .. run)]; return row and row.value end
local function wallet(uid) return progress(uid) end
local function seed(uid, amount)
  put(uid, "profile", { matter = amount or 100, xp = 0, gold = 0, ore = 0, energy = 100, level = 1 }, "progress")
end
local function reset()
  db, before_write, fail_write, fail_read, sequence, batches = {}, nil, false, false, 0, {}
  seed("a"); seed("b")
end
local Pvp = require("duel_match3_pvp")({
  nk = nk, CFG = { RACE_WIN_XP = 200, RACE_WIN_MATTER = 10, RACE_DRAW_XP = 200, PVE_MAX_LEVEL = 100 },
  clamp_int = function(x) return x end, read_pve_progress = progress,
  write_pve_progress = function(uid, value, version) return nk.storage_write({ build(uid, value, version) }) end,
  current_level_from_xp = function(xp) return math.floor(xp / 100) + 1 end,
  get_bot_profile = function() return {} end, normalize_mine_difficulty = function(x) return x end,
  get_race_entry = function() return Entry end,
})
local function state(run, users) return { pvp_race = true, mode = "pvp", players_sorted = users or { "a", "b" }, stats_result_id = run } end
local passed = 0
local function test(name, fn)
  reset(); local ok, err = pcall(fn)
  assert(ok, name .. ": " .. tostring(err)); passed = passed + 1; print("PASS " .. name)
end

test("unpaid receipt required for bot and human claim", function()
  local ok, err = Module.require_pending("a"); assert(not ok and err == "race_entry_required")
  assert(not Entry.claim({ "a", "zz-bot-race_bot" }, "run-a"))
  assert(not Entry.claim({ "a", "b" }, "run-b"))
  assert(wallet("a").matter == 100 and not active("a"))
end)
test("pay transaction is atomic and pending replay charges once", function()
  assert(Entry.pay("a", COST)); assert(wallet("a").matter == 98)
  local receipt_id = active("a").receipt_id
  local ok, _, _, entry = Entry.pay("a", { { resource = "matter", amount = 5 } })
  assert(ok and not entry.newly_paid and entry.costs[1].amount == 2)
  assert(active("a").receipt_id == receipt_id and wallet("a").matter == 98)
  assert(#batches == 1 and #batches[1] == 2)
end)
test("cancel pending refunds recorded fee once after config change", function()
  assert(Entry.pay("a", COST)); assert(Entry.cancel("a")); assert(Entry.cancel("a"))
  assert(wallet("a").matter == 100 and active("a").status == "cancelled")
end)
test("insufficient funds and duplicate resource lines cannot overdraw", function()
  seed("a", 3)
  local ok, err = Entry.pay("a", { { resource = "matter", amount = 2 }, { resource = "matter", amount = 2 } })
  assert(not ok and err == "not_enough_matter" and wallet("a").matter == 3 and not active("a"))
end)
test("item-backed and malformed prices fail closed", function()
  for _, costs in ipairs({ {}, { { resource = "ingots", amount = 1 } }, { { resource = "matter", amount = 0 } }, { { resource = "matter", amount = math.huge } } }) do
    assert(not Entry.pay("a", costs))
  end
  assert(wallet("a").matter == 100 and not active("a"))
end)
test("all humans claim atomically or neither does", function()
  assert(Entry.pay("a", COST))
  local ok, err = Entry.claim({ "a", "b" }, "run")
  assert(not ok and err == "race_entry_required")
  assert(active("a").status == "pending" and not receipt("a", "run"))
  assert(Entry.pay("b", COST)); assert(Entry.claim({ "a", "b" }, "run"))
  assert(active("a").status == "bound" and active("b").status == "bound")
  assert(#batches[#batches] == 4)
end)
test("same match reconnect keeps receipt and cannot pay or cancel bound entry", function()
  assert(Entry.pay("a", COST)); assert(Entry.claim({ "a" }, "run"))
  assert(Entry.claim({ "a", "zz-bot-arena_bot" }, "run"))
  local ok, err = Entry.cancel("a"); assert(not ok and err == "race_entry_in_use")
  assert(not Entry.pay("a", COST)); assert(not Entry.claim({ "a" }, "other-run"))
  assert(wallet("a").matter == 98)
end)
test("concurrent claim gives one match the receipt", function()
  assert(Entry.pay("a", COST)); assert(Entry.pay("b", COST))
  before_write = function() assert(Entry.claim({ "a", "b" }, "winner")) end
  local ok, err = Entry.claim({ "a", "b" }, "loser")
  assert(not ok and err == "race_entry_in_use")
  assert(receipt("a", "winner") and receipt("b", "winner"))
  assert(not receipt("a", "loser") and not receipt("b", "loser"))
end)
test("cancel racing bind cannot refund a bound entry", function()
  assert(Entry.pay("a", COST))
  before_write = function() assert(Entry.claim({ "a" }, "run")) end
  local ok, err = Entry.cancel("a")
  assert(not ok and err == "race_entry_in_use" and wallet("a").matter == 98)
end)
test("claim racing cancel cannot start refunded entry", function()
  assert(Entry.pay("a", COST))
  before_write = function() assert(Entry.cancel("a")) end
  assert(not Entry.claim({ "a" }, "run"))
  assert(wallet("a").matter == 100 and not receipt("a", "run"))
end)
test("entry debit preserves concurrent wallet update", function()
  before_write = function() local value = wallet("a"); value.matter = value.matter - 10; put("a", "profile", value, "progress") end
  assert(Entry.pay("a", COST)); assert(wallet("a").matter == 88)
end)
test("storage failure cannot charge bind cancel or refund partially", function()
  fail_write = true; assert(not Entry.pay("a", COST)); assert(wallet("a").matter == 100)
  fail_write = false; assert(Entry.pay("a", COST))
  fail_write = true; assert(not Entry.claim({ "a" }, "run")); assert(not Entry.cancel("a"))
  assert(wallet("a").matter == 98 and active("a").status == "pending" and not receipt("a", "run"))
  fail_read = true; local ok, err = Module.require_pending("a"); assert(not ok and err == "storage_read_failed")
end)
test("legacy paid entry migrates without another debit and clears legacy fields", function()
  local value = wallet("a"); value.race_entry_pending, value.race_entry_costs = true, { { resource = "matter", amount = 3 } }
  put("a", "profile", value, "progress")
  assert(Entry.pay("a", COST)); assert(wallet("a").matter == 100 and not wallet("a").race_entry_pending)
  assert(active("a").costs[1].amount == 3); assert(Entry.cancel("a")); assert(wallet("a").matter == 103)
end)
test("unknown legacy price cannot manufacture a refund", function()
  local value = wallet("a"); value.race_entry_pending = true; put("a", "profile", value, "progress")
  assert(not Entry.pay("a", COST)); assert(not Entry.cancel("a")); assert(wallet("a").matter == 100)
end)
test("draw refunds each actual paid fee plus xp only once", function()
  assert(Entry.pay("a", COST)); assert(Entry.pay("b", { { resource = "matter", amount = 3 } }))
  assert(Entry.claim({ "a", "b" }, "run"))
  local match = state("run"); match.race_entry_costs = { { resource = "matter", amount = 999 } }
  assert(Pvp.apply_race_draw_rewards(match, {})); assert(Pvp.apply_race_draw_rewards(match, {}))
  assert(wallet("a").matter == 100 and wallet("b").matter == 100)
  assert(wallet("a").xp == 200 and wallet("b").xp == 200)
  assert(receipt("a", "run").reward.reward_matter == 2 and receipt("b", "run").reward.reward_matter == 3)
end)
test("race winner paid receipt is settled once including bot loss", function()
  assert(Entry.pay("a", COST)); assert(Entry.pay("b", COST)); assert(Entry.claim({ "a", "b" }, "run"))
  local match = state("run"); assert(Pvp.apply_game_over_rewards(match, "a", {})); assert(Pvp.apply_game_over_rewards(match, "a", {}))
  assert(wallet("a").matter == 108 and wallet("a").xp == 200 and wallet("b").matter == 98 and wallet("b").xp == 10)
  assert(active("b").status == "settled"); assert(Entry.pay("b", COST))
  assert(not Pvp.apply_race_draw_rewards(match, {})); assert(wallet("a").matter == 108)
end)
test("bot victory consumes human receipt without inventing rewards", function()
  assert(Entry.pay("a", COST)); assert(Entry.claim({ "a" }, "run"))
  local match = state("run", { "a", "zz-bot-race_bot" }); match.mode = "pve"
  assert(Pvp.apply_game_over_rewards(match, "zz-bot-race_bot", {}))
  assert(wallet("a").matter == 98 and wallet("a").xp == 0 and active("a").status == "settled")
end)
test("human PvP loss retains base 10 XP and replay cannot duplicate it", function()
  assert(Entry.pay("a", COST)); assert(Entry.pay("b", COST)); assert(Entry.claim({ "a", "b" }, "run"))
  local match = state("run")
  assert(Pvp.apply_game_over_rewards(match, "b", {}))
  assert(Pvp.apply_game_over_rewards(match, "b", {}))
  assert(wallet("a").xp == 10 and wallet("a").matter == 98 and wallet("b").xp == 200)
  assert(receipt("a", "run").reward.reward_xp == 10)
end)
test("bot Race human winner receives regular Race reward exactly once", function()
  assert(Entry.pay("a", COST)); assert(Entry.claim({ "a" }, "run"))
  local match = state("run", { "a", "zz-bot-race_bot" }); match.mode = "pve"
  assert(Pvp.apply_game_over_rewards(match, "a", {})); assert(Pvp.apply_game_over_rewards(match, "a", {}))
  assert(wallet("a").xp == 200 and wallet("a").matter == 108)
end)
test("unpaid result cannot award winner or draw prizes", function()
  assert(not Pvp.apply_game_over_rewards(state("run"), "a", {}))
  assert(not Pvp.apply_race_draw_rewards(state("run"), {})); assert(wallet("a").matter == 100 and wallet("a").xp == 0)
end)
test("settlement transaction failure leaves every wallet and receipt unchanged", function()
  assert(Entry.pay("a", COST)); assert(Entry.pay("b", COST)); assert(Entry.claim({ "a", "b" }, "run"))
  fail_write = true; assert(not Pvp.apply_race_draw_rewards(state("run"), {}))
  assert(wallet("a").matter == 98 and wallet("b").matter == 98 and receipt("a", "run").status == "bound")
  fail_write = false; assert(Pvp.apply_race_draw_rewards(state("run"), {})); assert(wallet("a").matter == 100)
end)
test("concurrent duplicate draw settles exactly once", function()
  assert(Entry.pay("a", COST)); assert(Entry.pay("b", COST)); assert(Entry.claim({ "a", "b" }, "run"))
  before_write = function() assert(Pvp.apply_race_draw_rewards(state("run"), {})) end
  assert(Pvp.apply_race_draw_rewards(state("run"), {})); assert(wallet("a").xp == 200 and wallet("b").xp == 200)
end)
test("old result replay cannot consume or refund next paid entry", function()
  assert(Entry.pay("a", COST)); assert(Entry.claim({ "a" }, "run"))
  local match = state("run", { "a", "zz-bot-race_bot" }); assert(Pvp.apply_race_draw_rewards(match, {}))
  assert(Entry.pay("a", COST)); local next_id = active("a").receipt_id
  assert(Pvp.apply_race_draw_rewards(match, {})); assert(wallet("a").matter == 98 and wallet("a").xp == 200)
  assert(active("a").status == "pending" and active("a").receipt_id == next_id)
end)
test("private receipts never grant client read or write access", function()
  assert(Entry.pay("a", COST)); assert(Entry.claim({ "a" }, "run"))
  for _, batch in ipairs(batches) do for _, row in ipairs(batch) do
    if row.collection == Module.COLLECTION then assert(row.permission_read == 0 and row.permission_write == 0) end
  end end
end)

local rpc_created = 0
local RaceApi = require("duel_match3_race_api")({
  CFG = { RACE_ENTRY_MATTER = 2 },
  ContestGoals = { race_entry_costs = function() return COST end,
    race_public_config = function() return { goal_mana = 200, entry = COST, rewards = {} } end },
  Guard = { assert_client_epoch_matches = function(_, payload) return payload ~= "{}", "session_stale" end,
    read_metadata_epoch = function() return 0 end },
  read_pve_progress = progress, build_pve_progress_write = build,
  build_resource_payload = function(value) return { matter = value.matter } end,
  make_bot_user_id = function(bot) return "zz-bot-" .. bot end,
  try_match_create = function() rpc_created = rpc_created + 1; return "match-" .. rpc_created end,
  race_goal_mana = function() return 200 end,
})
test("actual bot RPC requires entry before creating any match", function()
  rpc_created = 0
  local result = RaceApi.duel_match3_race_create({ user_id = "a" }, "epoch")
  assert(not result.ok and result.err == "race_entry_required" and rpc_created == 0)
  assert(RaceApi.duel_match3_race_enter({ user_id = "a" }, "epoch").ok)
  assert(RaceApi.duel_match3_race_create({ user_id = "a" }, "epoch").ok and rpc_created == 1)
  assert(Entry.claim({ "a" }, "run"))
  result = RaceApi.duel_match3_race_create({ user_id = "a" }, "epoch")
  assert(not result.ok and result.err == "race_entry_in_use" and rpc_created == 1)
end)
test("read-only info remains compatible with Unity empty payload", function()
  assert(RaceApi.duel_match3_race_info({ user_id = "a" }, "{}").ok)
  assert(not RaceApi.duel_match3_race_enter({ user_id = "a" }, "{}").ok)
end)

local hooks, matched = {}, nil
nk.register_rt_before = function(fn, name) hooks[name] = fn end
nk.register_matchmaker_matched = function(fn) matched = fn end
package.loaded["modules.duel_api_rate_limit"] = { consume = function() end }
dofile("Server/nakama/modules/duel_api_rate_guard.lua")
dofile("Server/nakama/modules/duel_matchmaker.lua")
test("actual realtime hooks reject unpaid tickets before creation", function()
  local payload = { matchmaker_add = { string_properties = { mode = "match3", rules = "race" } } }
  local ok, err = pcall(hooks.MatchmakerAdd, { user_id = "a" }, payload)
  assert(not ok and err[1] == "race_entry_required")
  assert(Entry.pay("a", COST)); assert(hooks.MatchmakerAdd({ user_id = "a" }, payload) == payload)
  assert(Entry.cancel("a")); assert(not pcall(hooks.MatchmakerAdd, { user_id = "a" }, payload))
  assert(not pcall(hooks.PartyMatchmakerAdd, { user_id = "a" }, { party_matchmaker_add = payload.matchmaker_add }))
end)
test("matched callback validates every human even if ticket hook bypassed", function()
  assert(Entry.pay("a", COST))
  assert(not pcall(matched, {}, {
    { presence = { user_id = "a" }, properties = { mode = "match3", rules = "race" } },
    { presence = { user_id = "b" }, properties = { mode = "match3", rules = "race" } },
  }))
end)

-- Exercise the real main-chunk start branches with an unpaid opponent, not a
-- reimplementation of their guard. Any board/stat work before claim fails.
local file = assert(io.open("Server/nakama/modules/duel_match3.lua", "r"))
local source = file:read("*a"); file:close()
local start = assert(source:find("local function match_join(context", 1, true))
local stop = assert(source:find("local function augment_arena_game_over_payload", start, true))
local env = setmetatable({
  nk = nk, RaceApi = { Entry = Entry }, Runtime = { Presence = require("modules.duel_match3_presence") },
  count_present_players = function(match) local n = 0; for _ in pairs(match.presences) do n = n + 1 end; return n end,
  sorted_two_players = function() return { "a", "b" } end,
  new_stats = function() error("stats initialized before entry claim") end,
}, { __index = _G })
local chunk = assert(loadstring(source:sub(start, stop - 1) .. "\nreturn match_join")); setfenv(chunk, env)
local join = chunk()
test("actual human match cannot start when either entry is unpaid", function()
  assert(Entry.pay("a", COST))
  local match = { mode = "pvp", pvp_race = true, stats_result_id = "run", stats = {},
    presences = {}, invited = {}, reconnect_deadlines = {} }
  local kicked, broadcasts = 0, 0
  local dispatcher = { match_kick = function() kicked = kicked + 1 end, broadcast_message = function() broadcasts = broadcasts + 1 end }
  assert(join({}, dispatcher, 1, match, { { user_id = "a", session_id = "a1" }, { user_id = "b", session_id = "b1" } }) == nil)
  assert(match.ended and not match.started and kicked == 1 and broadcasts == 0 and match.board == nil)
  assert(active("a").status == "pending" and not receipt("a", "run"))
end)
test("actual bot match cannot start without entry after cancel", function()
  assert(Entry.pay("a", COST)); assert(Entry.cancel("a"))
  local match = { mode = "pve", arena_pvp_style = true, pvp_race = true, stats_result_id = "run", stats = {},
    bot_user_id = "zz-bot-race_bot", presences = {}, invited = {}, reconnect_deadlines = {} }
  local broadcasts = 0
  assert(join({}, { match_kick = function() end, broadcast_message = function() broadcasts = broadcasts + 1 end }, 1,
    match, { { user_id = "a", session_id = "a1" } }) == nil)
  assert(match.ended and not match.started and broadcasts == 0 and wallet("a").matter == 100)
end)

print("race_entry_security_test: " .. passed .. " passed")
