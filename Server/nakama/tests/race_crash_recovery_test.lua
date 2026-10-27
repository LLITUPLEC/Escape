package.path = "Server/nakama/?.lua;Server/nakama/modules/?.lua;Server/nakama/tests/?.lua;" .. package.path
local Fixture = require("storage_fault_fixture")
local passed = 0
local COST = {{ resource = "matter", amount = 2 }}
local function test(name, fn)
  local f = Fixture.new(); f.install(); f.seed("a"); f.seed("b")
  local module = require("duel_match3_race_entry")
  local deps = { read_pve_progress = f.read_progress, build_pve_progress_write = f.build_progress }
  local entry = module.new(deps)
  local function make_pvp(e, matter)
    return require("duel_match3_pvp")({ nk = f.nk,
      CFG = { RACE_WIN_XP = 200, RACE_WIN_MATTER = matter or 4, RACE_DRAW_XP = 200 },
      read_pve_progress = f.read_progress, write_pve_progress = function(uid, p, v) f.nk.storage_write({ f.build_progress(uid, p, v) }) end,
      current_level_from_xp = function(xp) return math.floor(xp / 100) + 1 end, get_race_entry = function() return e end })
  end
  local pvp = make_pvp(entry)
  entry.configure_recovery(pvp.resume_race_result)
  local function restart(matter)
    local fresh = module.new(deps); local p = make_pvp(fresh, matter); fresh.configure_recovery(p.resume_race_result); return fresh, p
  end
  local ok, err = pcall(fn, f, entry, pvp, restart)
  assert(ok, name .. ": " .. tostring(err)); passed = passed + 1; print("PASS " .. name)
end
local function bind(f, entry, users, costs)
  users = users or { "a", "b" }
  for _, uid in ipairs(users) do if uid:sub(1, 7) ~= "zz-bot-" then assert(entry.pay(uid, costs or COST)) end end
  f.matches.mid = { match_id = "mid" }; assert(entry.claim(users, "run", "mid"))
  return { pvp_race = true, mode = "pvp", stats_result_id = "run", players_sorted = users }
end
test("process crash after bind refunds both immutable human stakes once", function(f, entry, pvp, restart)
  bind(f, entry); f.matches.mid = nil; entry = restart()
  assert(entry.recover("a")); assert(entry.recover("b")); assert(entry.recover("a"))
  assert(f.read_progress("a").matter == 100 and f.read_progress("b").matter == 100)
  assert(f.get("duel_race_entry", "a", "run:run").status == "refunded")
  assert(not entry.settle({ "a", "b" }, "run", "a", false, function() error("refunded result must not execute") end))
end)
test("live match and cluster probe outage cannot mint refunds", function(f, entry)
  bind(f, entry); assert(entry.recover("a")); assert(f.read_progress("a").matter == 98)
  f.probe_failure = true; local ok, err = entry.recover("a")
  assert(not ok and err == "match_probe_unavailable" and f.read_progress("b").matter == 98)
end)
test("known orphan can recover while unrelated matches still run", function(f, entry)
  bind(f, entry); f.matches.mid = nil; f.matches.other = { match_id = "other" }
  assert(entry.recover("a")); assert(f.read_progress("a").matter == 100)
end)
test("legacy missing match ID requires proved globally idle authoritative state", function(f, entry)
  assert(entry.pay("a", COST)); assert(entry.claim({ "a" }, "legacy"))
  f.matches.other = { match_id = "other" }
  local ok, err = entry.recover("a"); assert(not ok and err == "legacy_race_match_unknown")
  assert(f.read_progress("a").matter == 98)
  f.matches.other = nil; assert(entry.recover("a")); assert(f.read_progress("a").matter == 100)
end)
test("orphan refund storage fault is atomic across both humans", function(f, entry)
  bind(f, entry); f.matches.mid = nil; f.fail_before = true
  assert(not entry.recover("a")); assert(f.read_progress("a").matter == 98 and f.read_progress("b").matter == 98)
  f.fail_before = false; assert(entry.recover("a")); assert(f.read_progress("a").matter == 100 and f.read_progress("b").matter == 100)
end)
test("two concurrent recovery workers cannot refund twice", function(f, entry)
  bind(f, entry); f.matches.mid = nil
  f.before_write = function() assert(entry.recover("b")) end
  assert(entry.recover("a")); assert(f.read_progress("a").matter == 100 and f.read_progress("b").matter == 100)
end)
test("crash after finish intent replays winner4 rather than refunding paid result", function(f, entry, pvp, restart)
  local state = bind(f, entry)
  f.before_write = function() f.fail_after = true end
  assert(not pvp.apply_game_over_rewards(state, "a", {}))
  assert(f.read_progress("a").matter == 98 and f.get("duel_race_entry", "a", "run:run").finish_intent)
  entry = restart(999); f.matches.mid = nil
  assert(entry.recover("a")); assert(entry.recover("b"))
  assert(f.read_progress("a").matter == 102 and f.read_progress("a").xp == 200)
  assert(f.read_progress("b").matter == 98 and f.read_progress("b").xp == 10)
end)
test("settlement acknowledgement lost after atomic commit cannot duplicate winner payout", function(f, entry, pvp, restart)
  local state = bind(f, entry)
  f.before_write = function() f.before_write = function() f.fail_after = true end end
  assert(not pvp.apply_game_over_rewards(state, "a", {}))
  assert(f.read_progress("a").matter == 102 and f.get("duel_race_entry", "a", "run:run").status == "settled")
  entry, pvp = restart(); assert(entry.recover("a")); assert(pvp.apply_game_over_rewards(state, "a", {}))
  assert(f.read_progress("a").matter == 102 and f.read_progress("b").matter == 98)
end)
test("race result cash and stats outbox are stored in the same transaction", function(f, entry, pvp)
  local state = bind(f, entry); assert(pvp.apply_game_over_rewards(state, "a", {}))
  local receipt = f.get("duel_match3_journal", "a", "result:run:stats")
  assert(receipt and receipt.status == "committed" and receipt.plan.match_stats.won)
  local j = require("tests.journal_fixture_admission").wrap(require("duel_match3_journal").new({ stats_collection = "stats" }),f)
  assert(j.recover("a", {})); assert(j.recover("a", {}))
  assert(f.get("stats", "a", "summary").wins == 1)
end)
for _, outcome in ipairs({ "human", "bot", "draw" }) do
  test("bot crash recovery returns exact legacy stake with no monetary prize: " .. outcome, function(f, entry, pvp, restart)
    local users = { "a", "zz-bot-race_bot" }
    local state = bind(f, entry, users, {{ resource = "matter", amount = 7 }, { resource = "gold", amount = 11 }, { resource = "ore", amount = 13 }})
    state.mode = "pve"
    f.before_write = function() f.fail_after = true end
    if outcome == "draw" then assert(not pvp.apply_race_draw_rewards(state, {}))
    else assert(not pvp.apply_game_over_rewards(state, outcome == "human" and "a" or users[2], {})) end
    entry = restart(999); f.matches.mid = nil; assert(entry.recover("a")); assert(entry.recover("a"))
    local p = f.read_progress("a"); assert(p.matter == 100 and p.gold == 100 and p.ore == 100)
    assert(p.xp == (outcome == "bot" and 0 or 200)); assert(entry.cancel("a")); assert(f.read_progress("a").matter == 100)
  end)
end
test("new paid entry is unaffected by old recovery replays", function(f, entry)
  bind(f, entry); f.matches.mid = nil; assert(entry.recover("a")); assert(entry.pay("a", COST))
  local next_id = f.get("duel_race_entry", "a", "active").receipt_id
  assert(entry.refund_orphan("a", "run")); assert(f.read_progress("a").matter == 98)
  assert(f.get("duel_race_entry", "a", "active").receipt_id == next_id)
end)
test("finish intent winning refund CAS is never replaced by neutral orphan refund", function(f, entry, pvp)
  local state = bind(f, entry); f.matches.mid = nil
  f.before_write = function()
    assert(entry.prepare_finish(state.players_sorted, "run", "a", false, { mode = "pvp", against_bot = false, draw_xp = 200,
      loss_xp = 10, win_rewards = { xp = 200, matter = 4 } }))
  end
  local ok, err = entry.refund_orphan("a", "run"); assert(not ok and err == "race_result_pending")
  assert(entry.recover("a")); assert(f.read_progress("a").matter == 102 and f.read_progress("b").matter == 98)
end)
print("race_crash_recovery_test: " .. passed .. " passed")
