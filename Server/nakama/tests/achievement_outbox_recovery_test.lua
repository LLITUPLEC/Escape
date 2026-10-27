package.path = "Server/nakama/?.lua;Server/nakama/modules/?.lua;Server/nakama/tests/?.lua;" .. package.path
local Fixture = require("storage_fault_fixture")
local passed = 0
local function test(name, fn)
  local f = Fixture.new(); f.install(); f.seed("a"); f.seed("b")
  for name in pairs(package.loaded) do if name:find("duel_match3_achievements", 1, true) or name:find("duel_match3_stats_api", 1, true) then package.loaded[name] = nil end end
  local cfg = { STATS_COLLECTION = "stats", STATS_KEY = "summary", BOT_USER_ID_PREFIX = "zz-bot-" }
  package.loaded["modules.duel_match3_config"] = cfg
  package.loaded["modules.duel_match3_achievement_catalog"] = {}
  package.loaded["modules.duel_leaderboard_scores"] = { flush_match_wins = function() end }
  local j = require("tests.journal_fixture_admission").wrap(require("duel_match3_journal").new({ stats_collection = "stats" }),f)
  local ach = require("modules.duel_match3_achievements")
  ach.configure({ decode_storage_value = function(row) return row.value end, finalization_journal = j })
  local stats = require("duel_match3_stats_api")({ CFG = cfg, Ach = ach, finalization_journal = j })
  local state = { started = true, ended = true, stats_result_id = "finish", mode = "pve", owner_user_id = "a", bot_id = "mine_2",
    players_sorted = { "a", "zz-bot-mine_2" }, stats = { a = { hp = 1 }, ["zz-bot-mine_2"] = { hp = 0 } },
    _ach_counters = { a = { ["uses.cross"] = 3 } }, _ach_hp_was_exactly_one = { a = true } }
  local ok, err = pcall(fn, f, j, ach, stats, state)
  assert(ok, name .. ": " .. tostring(err)); passed = passed + 1; print("PASS " .. name)
end
test("pure builders preserve full effects even when owner is journal-managed", function(f, j, ach, stats, state)
  state._finalization_managed = { a = true }
  local effects = ach.build_match_finish_effects(state, "a", "a", "zz-bot-mine_2", 2)
  assert(effects.a["uses.cross"] == 3 and effects.a["dnn.win_at_one_hp"] == 1)
  local result = stats.build_match_finish_effects(state, "a", false)
  assert(result.a.match_stats.mode == "mine" and result.a.match_stats.won and result.a.match_stats.result_id == "finish")
  assert(#f.writes == 0)
end)
test("managed owner bypasses both legacy writes without dropping frozen outcome effects", function(f, j, ach, stats, state)
  local effects = ach.build_match_finish_effects(state, "a", "a", "zz-bot-mine_2", 2).a
  effects["pve.kill.mine_2"] = 1
  local plan = { achievement_deltas = effects, match_stats = stats.build_match_finish_effects(state, "a", false).a.match_stats }
  assert(j.prepare("a", "finish:pve", "pve", plan))
  assert(j.commit("a", "finish:pve", function()
    local p, version = f.read_progress("a"); p.gold = p.gold + 25
    return { f.build_progress("a", p, version) }, { reward_gold = 25 }
  end))
  state._finalization_managed = { a = true }
  local writes = #f.writes
  assert(ach.flush_match_finish(state, "a", "a", "zz-bot-mine_2", 2)); assert(stats.record_match_finish(state, "a", false))
  assert(#f.writes == writes)
  assert(j.deliver_effects("a", "finish:pve")); assert(j.deliver_effects("a", "finish:pve"))
  local summary = f.get("stats", "a", "summary")
  assert(summary.achievement_stats["uses.cross"] == 3 and summary.achievement_stats["pve.kill.mine_2"] == 1 and summary.wins == 1)
end)
test("achievement read outage after committed wallet leaves durable outbox and no false wallet retry", function(f, j, ach, stats, state)
  local plan = { achievement_deltas = ach.build_match_finish_effects(state, "a").a }
  assert(j.prepare("a", "finish:pve", "pve", plan)); assert(j.commit("a", "finish:pve", function()
    local p, v = f.read_progress("a"); p.gold = p.gold + 25; return { f.build_progress("a", p, v) }, { reward_gold = 25 }
  end))
  local read = f.nk.storage_read
  f.nk.storage_read = function(keys) if keys[1].collection == "stats" then error("achievement database unavailable") end; return read(keys) end
  assert(not j.deliver_effects("a", "finish:pve")); assert(f.read_progress("a").gold == 125)
  assert(j.read("a", "finish:pve").status == "committed")
  f.nk.storage_read = read
  assert(j.recover("a", {})); assert(f.get("stats", "a", "summary").achievement_stats["uses.cross"] == 3)
end)
test("ordinary flush is idempotent across reconstructed state and outbox retry", function(f, j, ach, stats, state)
  assert(ach.flush_match_finish(state, "a", "a", "zz-bot-mine_2", 2))
  assert(ach.flush_match_finish(Fixture.copy(state), "a", "a", "zz-bot-mine_2", 2))
  assert(f.get("stats", "a", "summary").achievement_stats["uses.cross"] == 3)
end)
test("stats outbox read failure recovers both disconnected participants once", function(f, j, ach, stats, state)
  state.mode, state.players_sorted = "pvp", { "a", "b" }
  local read = f.nk.storage_read
  f.nk.storage_read = function(keys) if keys[1].collection == "stats" then error("stats database unavailable") end; return read(keys) end
  assert(not stats.record_match_finish(state, "a", false))
  assert(j.read("a", "finish:stats").status == "committed" and j.read("b", "finish:stats").status == "committed")
  f.nk.storage_read = read
  assert(j.recover("a", {})); assert(j.recover("b", {})); assert(stats.record_match_finish(state, "a", false))
  assert(f.get("stats", "a", "summary").wins == 1 and f.get("stats", "b", "summary").losses == 1)
end)
test("only managed UID is skipped and bots never get a journal", function(f, j, ach, stats, state)
  state.mode, state.players_sorted = "pvp", { "a", "b" }; state._ach_counters.b = { ["uses.shield"] = 2 }
  state._finalization_managed = { a = true }
  assert(ach.flush_match_finish(state, "a")); assert(stats.record_match_finish(state, "a", false))
  assert(not j.read("a", "finish") and not j.read("a", "finish:stats"))
  local summary = f.get("stats", "b", "summary"); assert(summary.achievement_stats["uses.shield"] == 2 and summary.losses == 1)
end)
print("achievement_outbox_recovery_test: " .. passed .. " passed")
