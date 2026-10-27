package.path = "Server/nakama/?.lua;Server/nakama/modules/?.lua;Server/nakama/tests/?.lua;" .. package.path
local Fixture = require("storage_fault_fixture")
local passed = 0
local COST, META = {{ resource = "energy", amount = 15 }}, { request_key = "easy:1:mine_1", floor = 1, difficulty = "easy" }
local function test(name, fn)
  local f = Fixture.new(); f.install(); f.seed("a")
  local j = require("tests.journal_fixture_admission").wrap(require("duel_match3_journal").new(),f)
  local module = require("duel_match3_pve_entry")
  local deps = { read_pve_progress = f.read_progress, build_pve_progress_write = f.build_progress, journal = j,
    now = function() return f.clock end, energy_max = function() return 100 end }
  local e = module.new(deps)
  local ok, err = pcall(fn, f, e, j, module, deps)
  assert(ok, name .. ": " .. tostring(err)); passed = passed + 1; print("PASS " .. name)
end
local function reserve(e)
  local ok, err, receipt = e.reserve("a", COST, META); assert(ok, tostring(err)); return receipt
end
local function launch(f, e)
  local receipt = reserve(e); f.matches.mid = { match_id = "mid" }
  assert(e.bind("a", receipt.token, "mid", "run")); return receipt
end
test("reservation debit and immutable token commit atomically", function(f, e)
  local first = reserve(e); local second = reserve(e)
  assert(first.token == second.token and f.read_progress("a").energy == 85)
  local batch = f.writes[1]; assert(#batch == 3 and batch[1].collection == "progress")
end)
test("actual launch failure refunds once immediately", function(f, e)
  local receipt = reserve(e); assert(e.refund_launch("a", receipt.token, "match_create_failed"))
  assert(e.refund_launch("a", receipt.token, "duplicate_failure")); assert(f.read_progress("a").energy == 100)
end)
test("debit failure cannot create token or spend energy", function(f, e)
  f.fail_before = true; assert(not e.reserve("a", COST, META))
  assert(f.read_progress("a").energy == 100 and not f.get("duel_pve_entry", "a", "active"))
end)
test("crash between debit and match init recovers expired reservation", function(f, e, j, module, deps)
  local receipt = reserve(e); f.clock = f.clock + 91; e = module.new(deps)
  assert(e.recover("a")); assert(e.recover("a")); assert(f.read_progress("a").energy == 100)
  assert(not e.bind("a", receipt.token, "late-mid", "late-run"))
end)
test("bound live match is never refunded by timeout", function(f, e)
  local receipt = launch(f, e); f.clock = f.clock + 10000
  assert(e.recover("a")); assert(f.read_progress("a").energy == 85)
  assert(f.get("duel_pve_entry", "a", "launch:" .. receipt.token).status == "bound")
end)
test("probe outage fails closed without a free match", function(f, e)
  launch(f, e); f.probe_failure = true
  local ok, err = e.recover("a"); assert(not ok and err == "match_probe_unavailable")
  assert(f.read_progress("a").energy == 85)
end)
test("crash after binding refunds abandoned unstarted launch", function(f, e, j)
  launch(f, e); f.matches.mid = nil
  assert(e.recover("a")); assert(f.read_progress("a").energy == 100)
  assert(j.read("a", "run:pve").status == "aborted")
  assert(not j.prepare("a", "run:pve", "pve", { gold = 25 }))
end)
test("prepared outcome prevents orphan refund then consumes in same reward transaction", function(f, e, j)
  local receipt = launch(f, e); f.matches.mid = nil
  assert(j.prepare("a", "run:pve", "pve", { gold = 25, launch_token = receipt.token }))
  local ok, err = e.recover("a"); assert(not ok and err == "result_pending")
  assert(j.commit("a", "run:pve", function(plan)
    local p, version = f.read_progress("a"); p.gold = p.gold + plan.gold
    local writes = assert(e.consume_writes("a", plan.launch_token, "run"))
    writes[#writes + 1] = f.build_progress("a", p, version)
    return writes, { reward_gold = plan.gold }
  end))
  assert(e.recover("a")); assert(f.read_progress("a").energy == 85 and f.read_progress("a").gold == 125)
end)
test("refund racing durable result intent gives the intent exclusive ownership", function(f, e, j)
  local receipt = launch(f, e); f.matches.mid = nil
  f.before_write = function() assert(j.prepare("a", "run:pve", "pve", { gold = 25 })) end
  local ok, err = e.refund_launch("a", receipt.token, "orphan"); assert(not ok and err == "result_pending")
  assert(f.read_progress("a").energy == 85)
end)
test("intent racing committed refund cannot later award a free victory", function(f, e, j)
  local receipt = launch(f, e); f.matches.mid = nil
  f.before_write = function() assert(e.refund_launch("a", receipt.token, "orphan")) end
  assert(not j.prepare("a", "run:pve", "pve", { gold = 25 }))
  assert(f.read_progress("a").energy == 100 and f.read_progress("a").gold == 100)
end)
test("refund acknowledgement lost after commit cannot credit twice", function(f, e)
  local receipt = reserve(e); f.fail_after = true
  assert(not e.refund_launch("a", receipt.token, "failed")); assert(e.recover("a"))
  assert(f.read_progress("a").energy == 100)
end)
test("energy overflow stays durable until space becomes available", function(f, e)
  local receipt = reserve(e); local p = f.read_progress("a"); p.energy = 100; f.put("progress", "a", "profile", p)
  assert(e.refund_launch("a", receipt.token, "failed"))
  assert(f.get("duel_pve_entry", "a", "launch:" .. receipt.token).remaining_refund[1].amount == 15)
  reserve(e); assert(f.read_progress("a").energy == 85)
  assert(e.recover("a")); assert(f.read_progress("a").energy == 100)
  assert(f.get("duel_pve_entry", "a", "launch:" .. receipt.token).status == "refunded")
end)
test("started match consumes once and process loss does not refund used energy", function(f, e)
  local receipt = launch(f, e); assert(e.consume("a", receipt.token, "run")); assert(e.consume("a", receipt.token, "run"))
  f.matches.mid = nil; assert(e.recover("a")); assert(f.read_progress("a").energy == 85)
end)
test("duplicate resource lines are aggregated before affordability checks", function(f, e)
  local ok, err = e.reserve("a", {{ resource = "energy", amount = 60 }, { resource = "energy", amount = 60 }}, META)
  assert(not ok and err == "not_enough_energy" and f.read_progress("a").energy == 100)
end)
test("unknown item-backed entry costs cannot diverge scalar and inventory ledgers", function(f, e)
  local ok, err = e.reserve("a", {{ resource = "ingots", amount = 5 }}, META)
  assert(not ok and err == "unsupported_launch_cost" and f.read_progress("a").energy == 100)
end)
print("pve_entry_recovery_test: " .. passed .. " passed")
