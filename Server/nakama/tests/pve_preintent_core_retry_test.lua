-- Actual R.award + actual core initial PvE pending hook, emit/retry helpers and
-- ended match_loop body. Reuse only the existing test's storage/catalog fixture.
-- No core/module mutation, running server, client, network or account writes.
package.path = "Server/nakama/?.lua;Server/nakama/tests/?.lua;" .. package.path
local original_random, original_time = math.random, os.time
local setup_file = assert(io.open("Server/nakama/tests/pve_rewards_inventory_journal_integration_test.lua", "r"))
local setup_source = setup_file:read("*a"); setup_file:close()
local setup_end = assert(setup_source:find("local function eq", 1, true))
local fixture = assert(loadstring(setup_source:sub(1, setup_end - 1) .. "\nreturn fixture"))()
local source_file = assert(io.open("Server/nakama/modules/duel_match3.lua", "r"))
local source = source_file:read("*a"); source_file:close()
local uid = "pve-reward-fixture"
local results = { passed = 0, failed = 0, cases = {} }
local function span(start_marker, end_marker)
  local first = assert(source:find(start_marker, 1, true), "missing actual source: " .. start_marker)
  local last = assert(source:find(end_marker, first, true), "missing actual source boundary: " .. end_marker)
  return source:sub(first, last - 1)
end
local function core(F)
  local messages = {}
  local state = { ended = true, mode = "pve", owner_user_id = uid, bot_id = "mine_2", owner_session_epoch = 1,
    pve_run = { floor = 2, difficulty = "easy", launch_token = "token-run-a" }, stats_result_id = "run-a" }
  local runtime = { PveRewards = F.rewards(), ConsumableEffects = require("modules.duel_consumable_effects"),
    ServerAura = { get_active = function() return nil end, currency_multiplier = function() return 1 end }, StatsApi = {
    build_match_finish_effects = function() return { [uid] = { match_stats = { mode = "mine", won = true, result_id = "run-a" } } } end,
  } }
  local env = setmetatable({ Runtime = runtime, nk = F.db.nk, CFG = { TICK_RATE = 10, OP_GAME_OVER = 77 },
    Ach = { build_match_finish_effects = function() return { [uid] = { ["combat.cross"] = 3 } } end },
  }, { __index = _G })
  local helpers = span("Runtime.emit_game_over = function", "-- Board dimensions:")
  local award_wrapper = span("Runtime.award_pve_victory = function", "Runtime.award_pve_defeat = function")
  local helper_chunk = assert(loadstring(helpers .. "\n" .. award_wrapper)); setfenv(helper_chunk, env); helper_chunk()
  local loop_source = span("local function match_loop", "local function match_terminate")
  local loop_chunk = assert(loadstring(loop_source .. "\nreturn match_loop")); setfenv(loop_chunk, env)
  local match_loop = loop_chunk()
  local first = assert(source:find("        local achievement_effects = Ach.build_match_finish_effects(state, winner, actor, opponent, action_type)", 1, true))
  local last = assert(source:find("\n      end\n      game_over_payload.rewardXp", first, true))
  local initial_chunk = assert(loadstring("return function(state, winner, actor, opponent, action_type)\n" .. source:sub(first, last - 1) .. "\nend"))
  setfenv(initial_chunk, env); local initial = initial_chunk()
  local dispatcher = { broadcast_message = function(op, payload)
    assert(op == 77); messages[#messages + 1] = require("storage_fault_fixture").copy(payload)
  end }
  return {
    runtime = runtime, state = state, messages = messages,
    initial = function() initial(state, uid, uid, "zz-bot-mine_2", 2) end,
    emit = function() return runtime.emit_game_over(dispatcher, state, { winnerUserId = uid }) end,
    tick = function(tick) return match_loop({}, dispatcher, tick, state, {}) end,
  }
end
local function eq(a, b) assert(a == b, tostring(a) .. " != " .. tostring(b)) end
local function granted_once(F)
  local p = F.progress(); eq(p.gold, 111); eq(p.ore, 107); eq(p.matter, 103); eq(p.xp, 10)
  eq(p.defeated.mine_2, 1); eq(p.mine.floor_states["easy:2"].wins, 1); eq(p.key_items.key_green, 1)
  local summary = F.db.get("stats", uid, "summary")
  eq(summary.played, 1); eq(summary.achievement_stats["pve.kill.mine_2"], 1); eq(summary.achievement_stats["combat.cross"], 3)
end
local function test(name, fn)
  local ok, err = pcall(fn); math.random, os.time = original_random, original_time
  results.cases[#results.cases + 1] = { name = name, passed = ok, error = ok and nil or tostring(err) }
  if ok then results.passed = results.passed + 1; print("PASS " .. name)
  else results.failed = results.failed + 1; print("FAIL " .. name .. ": " .. tostring(err)) end
end

test("first intent write failure retains ended match and cached random plan until one successful retry", function()
  local F = fixture(false); local C = core(F); F.db.fail_before = true
  C.initial()
  assert(C.state.last_reward.reward_pending and not C.state.last_reward.intent_persisted)
  assert(C.state._pve_pending_result and C.state._pve_pending_result.attempt.plan)
  eq(F.counts.random, 3); eq(F.counts.recipe, 1); eq(F.progress().gold, 100)
  eq(F.journal().read(uid, "run-a:pve"), nil)
  assert(not C.emit()); eq(#C.messages, 0)
  F.no_more_rolls()
  assert(C.tick(0) == C.state)
  for tick = 1, 9 do assert(C.tick(tick) == C.state); eq(#C.messages, 0) end
  eq(F.counts.random, 3); eq(F.counts.recipe, 1)
  F.db.fail_before = false
  eq(C.tick(10), nil); eq(C.state._pve_pending_result, nil); eq(#C.messages, 1)
  eq(C.messages[1].rewardXp, 10); eq(C.messages[1].rewardGold, 11); assert(not C.messages[1].rewardPending)
  eq(C.tick(20), nil); eq(#C.messages, 1); granted_once(F)
end)

test("pre-intent storage read outage emits nothing and freezes once when storage recovers", function()
  local F = fixture(false); local C = core(F); F.db.read_failure = true
  C.initial(); assert(C.state._pve_pending_result); assert(not C.state.last_reward.intent_persisted)
  eq(F.counts.random, 0); assert(not C.emit()); eq(#C.messages, 0)
  assert(C.tick(0) == C.state); eq(#C.messages, 0); eq(F.counts.random, 0)
  F.db.read_failure = false
  eq(C.tick(10), nil); eq(#C.messages, 1); eq(F.counts.random, 3); eq(F.counts.recipe, 1); granted_once(F)
end)

test("after durable prepare wallet failure releases match with rewardPending and real journal recovers once", function()
  local F = fixture(true); local C = core(F); F.db.fail_before = true
  C.initial(); assert(C.state._pve_pending_result); assert(not C.emit()); eq(#C.messages, 0)
  F.no_more_rolls(); F.db.fail_before = false
  F.before_wallet(function() F.db.fail_before = true end)
  eq(C.tick(0), nil); eq(C.state._pve_pending_result, nil); eq(#C.messages, 1)
  assert(C.messages[1].rewardPending); eq(C.messages[1].rewardGold, 0)
  eq(F.progress().gold, 100); eq(F.journal().read(uid, "run-a:pve").status, "prepared")
  F.db.fail_before = false; F.restart()
  local handlers = { pve = function(user, id, plan) return F.rewards().build(user, id, plan) end }
  for _ = 1, 5 do assert(F.journal().recover(uid, handlers)) end
  granted_once(F); eq(F.counts.random, 3); eq(F.counts.recipe, 1)
  eq(F.sheet().inventory_pending_loot[1].def_id, "ingot_green"); eq(F.sheet().inventory_pending_loot[1].amount, 2)
  eq(#F.sheet().inventory_pending_loot, 3); eq(F.journal().read(uid, "run-a:pve").status, "complete")
end)

test("confirmed prepare with lost acknowledgement is safe to emit and never holds or duplicates match", function()
  local F = fixture(false); local C = core(F)
  F.db.before_write = function() F.db.fail_after = true end
  C.initial(); assert(C.state.last_reward.intent_persisted); assert(not C.state._pve_pending_result)
  assert(C.emit()); eq(#C.messages, 1); eq(C.tick(0), nil)
  F.no_more_rolls(); for _ = 1, 5 do assert(not F.award().reward_pending) end; granted_once(F)
end)

math.random, os.time = original_random, original_time
print("pve_preintent_core_retry_test: " .. results.passed .. " passed; " .. results.failed .. " failed")
assert(results.failed == 0, "pre-intent core retry failures")
return results
