-- Actual PvE reward module + actual finalization journal + actual inventory,
-- with the actual core sheet CAS builder and economy profile auto-delivery.
-- Only the Nakama storage transport, catalog/policy dependencies and RNG are
-- controlled. No running server, network, user account or live storage is used.
package.path = "Server/nakama/?.lua;Server/nakama/tests/?.lua;" .. package.path
local Fixture = require("storage_fault_fixture")
local original_random, original_time = math.random, os.time
local results = { passed = 0, failed = 0, cases = {} }
local uid = "pve-reward-fixture"
local source_file = assert(io.open(os.getenv("INVENTORY_CORE_SOURCE") or "Server/nakama/modules/duel_match3.lua", "r"))
local core_source = source_file:read("*a"); source_file:close()
local first = assert(core_source:find("local function normalize_character_sheet", 1, true))
local last = assert(core_source:find("local SET_BONUS_FULL_EQUIP_STATS", first, true))

local function fixture(full)
  local f = Fixture.new(); f.install(); f.seed(uid)
  package.loaded["modules.duel_match3_pve_rewards"] = nil
  package.loaded["modules.duel_match3_economy_rpc"] = nil
  local I = require("modules.duel_match3_inventory")
  local Journal = require("modules.duel_match3_journal")
  local Reward = require("modules.duel_match3_pve_rewards")
  local copy = Fixture.copy
  local defs = {
    helmet = { kind = "equipment", max_stack = 1 },
    ingot_green = { kind = "material", max_stack = 100 },
    tesseract = { kind = "material", max_stack = 1 },
    recipe_drop_t1_green_Helmet = { kind = "recipe", quality = "uncommon", max_stack = 1 },
  }
  local sheet = I.normalize({ inventory_extra_slots = 25, equipment = {}, learned_recipes = {} })
  if full then for i = 1, 50 do sheet.inventory[i], sheet.inventory_counts[i] = "helmet", 1 end end
  f.put("character", uid, "sheet", sheet)
  f.put("launch", uid, "token-run-a", { status = "active" })
  f.put("launch", uid, "token-run-b", { status = "active" })
  local runtime = { Inventory = I }
  local env = setmetatable({ Runtime = runtime, nk = f.nk,
    CFG = { CHARACTER_SHEET_COLLECTION = "character", CHARACTER_SHEET_KEY = "sheet", PVE_PROGRESS_COLLECTION = "progress", PVE_PROGRESS_KEY = "profile" },
    get_merged_item_defs = function() return defs end,
    decode_storage_value = function(row) return row.value end,
  }, { __index = _G })
  local chunk = assert(loadstring(core_source:sub(first, last - 1) .. "\nreturn read_character_sheet, write_character_sheet"))
  setfenv(chunk, env)
  local read_sheet, write_sheet = chunk()
  local counts = { random = 0, recipe = 0, affix = 0, build_reads = 0, consume = 0 }
  math.random = function(lo, hi)
    counts.random = counts.random + 1
    if lo ~= nil then assert(lo <= 3 and hi >= 3); return 3 end
    return .10 -- deterministic successful ingot/tesseract rolls
  end
  os.time = function() return f.clock end
  local journal = require("tests.journal_fixture_admission").wrap(Journal.new({ stats_collection = "stats", stats_key = "summary" }),f)
  local bot = { floor = 2, reward_xp = 10, reward_gold = 11, reward_ore = 7,
    reward_matter_min = 2, reward_matter_max = 7, reward_ingots = 2,
    reward_tesseract_chance = .75, reward_key_id = "key_green", reward_key_amount = 1 }
  local d = {
    normalize_difficulty = function(diff) return diff or "easy" end,
    get_bot_profile = function() return bot end, clamp_floor = function(n) return math.max(1, math.min(12, n)) end,
    is_boss_floor = function(n) return n == 4 or n == 8 or n == 12 end,
    reward_multiplier = function() return 1 end, apply_reward_xp = function(n) return n end,
    stat_multiplier = function() return 1.25 end, respawn_seconds = function() return 60 end,
    next_affix = function() counts.affix = counts.affix + 1; return "frozen-affix-" .. counts.affix end,
    ingot_drop_chance = function() return .9 end, ingot_def = function() return "ingot_green" end,
    uses_recipe_drop = function() return true end,
    pick_recipe = function() counts.recipe = counts.recipe + 1; return "recipe_drop_t1_green_Helmet" end,
    recipe_color = function() return "green" end,
    read_progress = function(user) counts.build_reads = counts.build_reads + 1; return f.read_progress(user) end,
    read_sheet = read_sheet, get_defs = function() return defs end,
    empty_keys = function() return {} end,
    add_blueprint = function(p) p.blueprint_green = (p.blueprint_green or 0) + 1 end,
    level_from_xp = function(xp) return 1 + math.floor(xp / 100) end,
    normalize_unlocked = function(v) return v or { easy = 1, medium = 0, hard = 0 } end,
    floor_key = function(diff, floor) return diff .. ":" .. floor end, max_floor = 12,
    inventory = function() return I end,
    build_sheet_writes = runtime.build_character_sheet_writes,
    consume_writes = function(user, token, run_id)
      counts.consume = counts.consume + 1
      local value, version = f.get("launch", user, token)
      assert(value and (value.status ~= "consumed" or value.run_id == run_id), "launch_bound_to_another_result")
      value.status, value.run_id = "consumed", run_id
      return {{ collection = "launch", key = token, user_id = user, value = value, version = version, permission_read = 0, permission_write = 0 }}
    end,
    journal = function() return journal end, is_stale = function() return false end,
  }
  local R = Reward.new(d)
  local F = { db = f, I = I, d = d, counts = counts, defs = defs }
  function F.progress() return f.read_progress(uid) end
  function F.sheet() return f.get("character", uid, "sheet") end
  function F.journal() return journal end
  function F.award(run_id, attempt)
    run_id = run_id or "run-a"
    return R.award(uid, "mine_2", 1, { floor = 2, difficulty = "easy", launch_token = "token-" .. run_id, affix = "old" }, run_id,
      { achievement_deltas = { ["combat.cross"] = 3 }, match_stats = { mode = "mine", won = true, result_id = run_id } }, attempt)
  end
  function F.rewards() return R end
  function F.restart()
    -- Keep only persisted storage: production modules are reloaded with fresh
    -- closures, as on another Nakama node. Catalog/learned recipe can change.
    package.loaded["modules.duel_match3_journal"] = nil
    package.loaded["modules.duel_match3_pve_rewards"] = nil
    journal = require("tests.journal_fixture_admission").wrap(require("modules.duel_match3_journal").new({ stats_collection = "stats", stats_key = "summary" }),f)
    R = require("modules.duel_match3_pve_rewards").new(d)
  end
  function F.no_more_rolls()
    math.random = function() error("frozen result rerolled RNG") end
    d.pick_recipe = function() error("frozen result consulted learned state again") end
    d.next_affix = function() error("frozen result rerolled affix") end
  end
  function F.before_wallet(fn)
    local probe
    probe = function(writes)
      for _, op in ipairs(writes) do if op.collection == "progress" then fn(writes); return end end
      f.before_write = probe
    end
    f.before_write = probe
  end
  function F.get_profile()
    local economy = require("modules.duel_match3_economy_rpc")({
      CFG = {}, Guard = { assert_client_epoch_matches = function() return true end },
      read_character_sheet = read_sheet, read_pve_progress = f.read_progress,
      write_character_sheet = write_sheet, get_merged_item_defs = function() return defs end,
      encode_character_ok_response = function(s, p) return { ok = true, sheet = copy(s), progression = copy(p) } end,
    })
    return economy.duel_character_get({ user_id = uid }, {})
  end
  function F.free_cell(index)
    local s = F.sheet(); s.inventory[index], s.inventory_counts[index] = "", 0
    f.put("character", uid, "sheet", s)
  end
  return F
end

local function eq(a, b, message) assert(a == b, (message or "value mismatch") .. ": " .. tostring(a) .. " != " .. tostring(b)) end
local function count_item(s, item)
  local n = 0; for i = 1, 50 do if s.inventory[i] == item then n = n + s.inventory_counts[i] end end; return n
end
local function pending_item(s, item)
  local n = 0; for _, row in ipairs(s.inventory_pending_loot) do if row.def_id == item then n = n + row.amount end end; return n
end
local function counters_once(F)
  local p = F.progress(); eq(p.gold, 111); eq(p.ore, 107); eq(p.matter, 103); eq(p.xp, 10)
  eq(p.key_items.key_green, 1); eq(p.defeated.mine_2, 1); eq(p.mine.floor_states["easy:2"].wins, 1)
  local summary = F.db.get("stats", uid, "summary")
  eq(summary.achievement_stats["pve.kill.mine_2"], 1); eq(summary.achievement_stats["combat.cross"], 3)
  eq(summary.played, 1); eq(summary.wins, 1); eq(summary.modes.mine.played, 1)
end
local function test(name, fn)
  local ok, err = pcall(fn)
  math.random, os.time = original_random, original_time
  results.cases[#results.cases + 1] = { name = name, passed = ok, error = ok and nil or tostring(err) }
  if ok then results.passed = results.passed + 1; print("PASS " .. name)
  else results.failed = results.failed + 1; print("FAIL " .. name .. ": " .. tostring(err)) end
end

test("full inventory queues exact frozen items and excludes legacy delivered rewards", function()
  local F = fixture(true); local r = F.award()
  assert(not r.reward_pending and not r.achievement_sync_pending)
  eq(r.reward_ingots, 0); eq(r.reward_tesseract, 0); eq(r.reward_recipe_item_id, ""); eq(r.reward_blueprint, "")
  eq(#r.delivered_items, 0); eq(#r.pending_items, 3); eq(r.item_loot_pending_count, 4)
  eq(pending_item(F.sheet(), "ingot_green"), 2); eq(pending_item(F.sheet(), "tesseract"), 1)
  eq(pending_item(F.sheet(), "recipe_drop_t1_green_Helmet"), 1)
  eq(F.progress().ingots, 0); eq(F.progress().tesseracts, 0); eq(F.progress().blueprint_green, 0)
  eq(F.db.get("launch", uid, "token-run-a").status, "consumed"); counters_once(F)
end)

test("roomy inventory reports only genuinely delivered ingots tesseract recipe", function()
  local F = fixture(false); local r = F.award()
  eq(r.reward_ingots, 2); eq(r.reward_tesseract, 1); eq(r.reward_recipe_item_id, "recipe_drop_t1_green_Helmet")
  eq(r.reward_blueprint, "green"); eq(#r.pending_items, 0); eq(r.item_loot_pending_count, 0); eq(#r.delivered_items, 3)
  eq(F.progress().ingots, 2); eq(F.progress().tesseracts, 1); eq(F.progress().blueprint_green, 1); counters_once(F)
end)

test("intent index CAS conflict retries the same frozen random plan", function()
  local F = fixture(false)
  F.db.before_write = function()
    F.no_more_rolls()
    -- Another worker creates the journal index after our initial missing read.
    F.db.put("duel_match3_journal", uid, "pending", { entries = {} })
  end
  local r = F.award(); assert(not r.reward_pending)
  eq(F.counts.random, 3); eq(F.counts.recipe, 1); eq(F.counts.affix, 1)
  eq(r.reward_ingots, 2); eq(r.reward_tesseract, 1); counters_once(F)
end)

test("lost intent acknowledgement rereads saved frozen plan before wallet commit", function()
  local F = fixture(false)
  F.db.before_write = function() F.no_more_rolls(); F.db.fail_after = true end
  local r = F.award(); assert(not r.reward_pending)
  eq(F.counts.random, 3); eq(F.counts.recipe, 1); eq(F.counts.affix, 1)
  eq(F.journal().read(uid, "run-a:pve").status, "complete"); counters_once(F)
end)

test("durable refunded launch abort prevents freeze wallet items and achievements", function()
  local F = fixture(true)
  F.db.nk.storage_write(assert(F.journal().abort_writes(uid, "run-a:pve", "refunded")))
  local r = F.award(); assert(not r.reward_pending and r.err == "launch_refunded")
  eq(F.counts.random, 0); eq(F.counts.recipe, 0); eq(F.progress().gold, 100); eq(F.progress().xp, 0)
  eq(#F.sheet().inventory_pending_loot, 0); eq(F.db.get("stats", uid, "summary"), nil)
end)

test("wallet and sheet CAS retry reuse frozen RNG learned recipe affix and respawn", function()
  local F = fixture(false)
  F.before_wallet(function()
    F.no_more_rolls()
    local p = F.progress(); p.gold = p.gold - 4; F.db.put("progress", uid, "profile", p)
    local s = F.sheet(); s.inventory[50], s.inventory_counts[50] = "helmet", 1; F.db.put("character", uid, "sheet", s)
    F.db.clock = F.db.clock + 300
  end)
  local r = F.award(); assert(not r.reward_pending)
  eq(F.counts.random, 3); eq(F.counts.recipe, 1); eq(F.counts.affix, 1); assert(F.counts.build_reads >= 2)
  eq(F.progress().gold, 107); eq(F.progress().mine.floor_states["easy:2"].next_spawn_at, 1060)
  eq(F.progress().mine.floor_states["easy:2"].last_affix, "frozen-affix-1"); eq(F.sheet().inventory[50], "helmet")
end)

test("durable intent survives pre-wallet DB failure and restart without reroll", function()
  local F = fixture(true)
  F.before_wallet(function() F.db.fail_before = true end)
  local r = F.award(); assert(r.reward_pending); eq(F.progress().gold, 100)
  eq(#F.sheet().inventory_pending_loot, 0); eq(F.journal().read(uid, "run-a:pve").status, "prepared")
  local frozen = F.journal().read(uid, "run-a:pve").plan
  F.db.fail_before = false; F.db.clock = 5000; F.no_more_rolls(); F.restart()
  r = F.award(); assert(not r.reward_pending); eq(r.reward_matter, frozen.reward_matter)
  eq(F.progress().mine.floor_states["easy:2"].next_spawn_at, frozen.next_spawn_at); counters_once(F)
end)

test("lost wallet commit acknowledgement and restart do not grant items wallet or counters twice", function()
  local F = fixture(true); F.before_wallet(function() F.db.fail_after = true end)
  local r = F.award(); assert(r.reward_pending); eq(F.progress().gold, 111)
  eq(pending_item(F.sheet(), "ingot_green"), 2); eq(F.journal().read(uid, "run-a:pve").reward_status, "committed")
  F.no_more_rolls(); F.restart(); r = F.award(); assert(not r.reward_pending)
  for _ = 1, 5 do assert(not F.award().reward_pending) end
  eq(pending_item(F.sheet(), "ingot_green"), 2); eq(F.counts.recipe, 1); counters_once(F)
end)

test("wallet commit plus achievement DB write failure returns receipt without throwing or rollback", function()
  local F = fixture(true)
  local original = F.db.nk.storage_write
  F.db.nk.storage_write = function(writes)
    for _, op in ipairs(writes) do if op.collection == "stats" then error("achievement database unavailable") end end
    return original(writes)
  end
  local ok, r = pcall(F.award); assert(ok and not r.reward_pending and r.achievement_sync_pending)
  eq(r.reward_gold, 11); eq(F.progress().gold, 111); eq(F.journal().read(uid, "run-a:pve").status, "committed")
  eq(F.db.get("stats", uid, "summary"), nil)
  F.db.nk.storage_write = original; F.no_more_rolls(); F.restart()
  r = F.award(); assert(not r.achievement_sync_pending); counters_once(F)
end)

test("achievement DB read failure cannot escape or cancel a committed wallet", function()
  local F = fixture(false)
  local original = F.db.nk.storage_read
  F.db.nk.storage_read = function(keys)
    for _, op in ipairs(keys) do if op.collection == "stats" then error("achievement read unavailable") end end
    return original(keys)
  end
  local ok, r = pcall(F.award); assert(ok and r.achievement_sync_pending and not r.reward_pending)
  eq(F.progress().gold, 111); eq(count_item(F.sheet(), "ingot_green"), 2)
  F.db.nk.storage_read = original; F.no_more_rolls(); F.restart(); F.award(); counters_once(F)
end)

test("achievement postcommit acknowledgement loss repeats neither counter nor wallet", function()
  local F = fixture(false)
  local original = F.db.nk.storage_write
  local once = true
  F.db.nk.storage_write = function(writes)
    if once then for _, op in ipairs(writes) do if op.collection == "stats" then once = false; F.db.fail_after = true; break end end end
    return original(writes)
  end
  local r = F.award(); assert(r.achievement_sync_pending and not r.reward_pending)
  F.no_more_rolls(); F.restart(); for _ = 1, 5 do F.award() end; counters_once(F)
end)

test("two workers awarding same run freeze once and share one atomic wallet receipt", function()
  local F = fixture(true); local other
  F.before_wallet(function() F.no_more_rolls(); other = F.award() end)
  local r = F.award(); assert(not r.reward_pending and other and not other.reward_pending)
  eq(F.counts.recipe, 1); eq(F.counts.random, 3); eq(pending_item(F.sheet(), "ingot_green"), 2); counters_once(F)
end)

test("profile read auto-delivers freed cells once without replaying old result grant", function()
  local F = fixture(true); local initial = F.award(); eq(initial.item_loot_pending_count, 4)
  F.free_cell(50); local profile = F.get_profile(); assert(profile.ok)
  eq(count_item(F.sheet(), "ingot_green"), 2); eq(F.progress().ingots, 2); eq(pending_item(F.sheet(), "tesseract"), 1)
  F.get_profile(); eq(count_item(F.sheet(), "ingot_green"), 2)
  F.free_cell(49); assert(F.get_profile().ok); eq(count_item(F.sheet(), "tesseract"), 1)
  F.free_cell(48); assert(F.get_profile().ok); eq(count_item(F.sheet(), "recipe_drop_t1_green_Helmet"), 1)
  eq(#F.sheet().inventory_pending_loot, 0); eq(F.progress().blueprint_green, 1)
  F.no_more_rolls(); F.restart(); for _ = 1, 5 do F.get_profile(); F.award() end
  eq(count_item(F.sheet(), "ingot_green"), 2); eq(count_item(F.sheet(), "tesseract"), 1)
  eq(count_item(F.sheet(), "recipe_drop_t1_green_Helmet"), 1); counters_once(F)
end)

test("two distinct concurrent outcomes retain wallet and pending loot through CAS conflict", function()
  local F = fixture(true)
  F.before_wallet(function() local r = F.award("run-b"); assert(not r.reward_pending) end)
  local r = F.award("run-a"); assert(not r.reward_pending)
  eq(F.progress().gold, 122); eq(F.progress().defeated.mine_2, 2); eq(pending_item(F.sheet(), "ingot_green"), 4)
  eq(pending_item(F.sheet(), "tesseract"), 2); eq(pending_item(F.sheet(), "recipe_drop_t1_green_Helmet"), 2)
  local summary = F.db.get("stats", uid, "summary")
  eq(summary.played, 2); eq(summary.achievement_stats["pve.kill.mine_2"], 2); eq(summary.achievement_stats["combat.cross"], 6)
end)

math.random, os.time = original_random, original_time
print("pve_rewards_inventory_journal_integration_test: " .. results.passed .. " passed; " .. results.failed .. " failed")
assert(results.failed == 0, "PvE reward integration failures: " .. results.failed)
return results
