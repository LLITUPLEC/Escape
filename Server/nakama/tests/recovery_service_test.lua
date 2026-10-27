package.path = "Server/nakama/?.lua;Server/nakama/modules/?.lua;Server/nakama/tests/?.lua;" .. package.path
local Fixture = require("storage_fault_fixture")
local passed = 0
local function test(name, fn)
  local f = Fixture.new(); f.install(); f.seed("a")
  local j = require("tests.journal_fixture_admission").wrap(require("duel_match3_journal").new({ stats_collection = "stats" }),f)
  local e = require("duel_match3_pve_entry").new({ read_pve_progress = f.read_progress, build_pve_progress_write = f.build_progress,
    journal = j, now = function() return f.clock end, energy_max = function() return 100 end })
  local Recovery = require("duel_match3_recovery")
  local ok, err = pcall(fn, f, j, e, Recovery)
  assert(ok, name .. ": " .. tostring(err)); passed = passed + 1; print("PASS " .. name)
end
local function bind(f, e)
  local ok, _, receipt = e.reserve("a", {{ resource = "energy", amount = 15 }}, { request_key = "easy:1" }); assert(ok)
  f.matches.mid = { match_id = "mid" }; assert(e.bind("a", receipt.token, "mid", "run")); f.matches.mid = nil
  return receipt
end
test("recovery completes durable outcome before inspecting refundable launch", function(f, j, e, Recovery)
  local receipt = bind(f, e); assert(j.prepare("a", "run:pve", "pve", { gold = 25, launch_token = receipt.token, achievement_deltas = { kill = 1 } }))
  local r = Recovery.new({ journal = j, pve_entry = e, now = function() return f.clock end,
    handlers = { pve = function(uid, id, plan)
      local p, version = f.read_progress(uid); p.gold = p.gold + plan.gold
      local writes = assert(e.consume_writes(uid, plan.launch_token, "run")); writes[#writes + 1] = f.build_progress(uid, p, version)
      return writes, { reward_gold = plan.gold }
    end } })
  assert(r.maybe_recover("a", { force = true })); assert(r.maybe_recover("a", { force = true }))
  local p = f.read_progress("a"); assert(p.gold == 125 and p.energy == 85)
  assert(f.get("stats", "a", "summary").achievement_stats.kill == 1)
end)
test("missing builder leaves intent and charge recoverable rather than refunding a real win", function(f, j, e, Recovery)
  local receipt = bind(f, e); assert(j.prepare("a", "run:pve", "pve", { launch_token = receipt.token }))
  local r = Recovery.new({ journal = j, pve_entry = e })
  local ok, err = r.maybe_recover("a", { force = true }); assert(not ok and err == "recovery_pending")
  assert(f.read_progress("a").energy == 85 and j.read("a", "run:pve").status == "prepared")
end)
test("failed recoveries are not throttled and next healthy call drains outbox", function(f, j, e, Recovery)
  assert(j.prepare("a", "effect", "effects", { achievement_deltas = { shield = 1 } }))
  local r = Recovery.new({ journal = j, pve_entry = e, now = function() return f.clock end })
  f.fail_before = true; assert(not r.maybe_recover("a")); f.fail_before = false
  assert(r.maybe_recover("a")); local ok, _, report = r.maybe_recover("a"); assert(ok and report.throttled)
  assert(f.get("stats", "a", "summary").achievement_stats.shield == 1)
end)
test("nested resource reads cannot recursively run recovery", function(f, j, e, Recovery)
  assert(j.prepare("a", "p", "pve", {}))
  local r
  r = Recovery.new({ journal = j, handlers = { pve = function(uid)
    local ok, _, nested = r.maybe_recover(uid, { force = true }); assert(ok and nested.reentrant)
    local p, version = f.read_progress(uid); p.gold = p.gold + 1; return { f.build_progress(uid, p, version) }, { reward_gold = 1 }
  end } })
  assert(r.maybe_recover("a", { force = true })); assert(f.read_progress("a").gold == 101)
end)
print("recovery_service_test: " .. passed .. " passed")
