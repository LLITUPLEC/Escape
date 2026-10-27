-- Lua 5.1+: tests the real character storage helpers and economy RPC module.
-- Run from the repository root: lua Server/nakama/tests/economy_security_test.lua
package.path = "Server/nakama/?.lua;" .. package.path
local Inventory = require("modules.duel_match3_inventory")
local function copy(value)
  if type(value) ~= "table" then return value end
  local result = {}
  for k, v in pairs(value) do result[k] = copy(v) end
  return result
end
local cfg = {
  CHARACTER_SHEET_COLLECTION = "duel_match3_character", CHARACTER_SHEET_KEY = "sheet",
  PVE_PROGRESS_COLLECTION = "duel_match3_progress", PVE_PROGRESS_KEY = "profile",
  WORKSHOP_CRAFT_RUSH_GOLD = 10, WORKSHOP_CRAFT_RUSH_SECONDS = 1200,
  PVE_MAX_LEVEL = 100, MINE_RESPAWN_NORMAL_SECONDS = 60, MINE_RESPAWN_BOSS_SECONDS = 300,
  MINE_AFFIX_POOL = { "normal" },
  clamp_int = function(value, lo, hi) return math.max(lo, math.min(hi, math.floor(tonumber(value) or 0))) end,
}
local function key(collection, object_key) return collection .. "/" .. object_key end
local sheet_key = key(cfg.CHARACTER_SHEET_COLLECTION, cfg.CHARACTER_SHEET_KEY)
local progress_key = key(cfg.PVE_PROGRESS_COLLECTION, cfg.PVE_PROGRESS_KEY)
local db, batches, before_write, fail_write
local function put(id, value)
  local old = db[id]
  db[id] = { value = copy(value), version = tostring((old and tonumber(old.version) or 0) + 1) }
end
local nk = {
  json_encode = copy, json_decode = copy, logger_error = function() end,
  storage_read = function(ids)
    local rows = {}
    for _, object in ipairs(ids) do
      local row = db[key(object.collection, object.key)]
      if row then rows[#rows + 1] = copy(row) end
    end
    return rows
  end,
  storage_write = function(writes)
    batches[#batches + 1] = copy(writes)
    if before_write then local fn = before_write; before_write = nil; fn() end
    if fail_write then error("database unavailable") end
    for _, op in ipairs(writes) do
      local row = db[key(op.collection, op.key)]
      assert(op.permission_write == 0)
      if op.version == "*" then assert(not row, "version check failed")
      else assert(row and op.version == row.version, "version check failed") end
    end
    local acks = {}
    for _, op in ipairs(writes) do
      local id = key(op.collection, op.key)
      put(id, op.value)
      acks[#acks + 1] = { collection = op.collection, key = op.key, version = db[id].version }
    end
    return acks
  end,
}
package.preload.nakama = function() return nk end
local function normalized_sheet(value)
  local sheet = copy(value or {})
  sheet.inventory = sheet.inventory or {}
  sheet.inventory_counts = sheet.inventory_counts or {}
  sheet.equipment = sheet.equipment or {}
  sheet.workshop_slots = sheet.workshop_slots or {}
  sheet.learned_recipes = sheet.learned_recipes or { "recipe_sword" }
  for i = 1, 25 do sheet.inventory[i] = sheet.inventory[i] or ""; sheet.inventory_counts[i] = sheet.inventory_counts[i] or 0 end
  for i = 1, 8 do sheet.equipment[i] = sheet.equipment[i] or ""; sheet.workshop_slots[i] = sheet.workshop_slots[i] or { output_def_id = "", ends_at = 0 } end
  return sheet
end
-- Extract the actual character helpers; the complete match module has runtime
-- initialization and match callbacks unrelated to these storage transactions.
local file = assert(io.open(os.getenv("INVENTORY_CORE_SOURCE") or "Server/nakama/modules/duel_match3.lua", "r"))
local source = file:read("*a"); file:close()
local start = assert(source:find("local function read_character_sheet", 1, true))
local finish = assert(source:find("local SET_BONUS_FULL_EQUIP_STATS", start, true))
local helpers_source = source:sub(start, finish - 1)
local constructor = assert((loadstring or load)("return function(nk, CFG, normalize_character_sheet, decode_storage_value, Inventory, helper_defs)\nlocal Runtime = { Inventory = Inventory }\nlocal function get_merged_item_defs() return helper_defs end\n" .. helpers_source .. "\nreturn read_character_sheet, write_character_sheet, ensure_character_sheet_initialized, Runtime.build_character_sheet_writes end"))()
local helper_defs = { recipe = { kind = "recipe", quality = "green" }, recipe_sword = { kind = "recipe", max_stack = 1 } }
local read_sheet, write_sheet, initialize_sheet, build_sheet_writes = constructor(nk, cfg, normalized_sheet, function(row) return row.value end, Inventory, helper_defs)
local function read_progress()
  local row = db[progress_key]
  return copy(row.value), row.version
end
local defs = {
  sword = { kind = "equipment", slot = "hand", tier = 1, quality = "normal", craft_recipe_id = "recipe_sword", sale_price = 100 },
  ingot = { kind = "material", sale_price = 20 },
  recipe = { kind = "recipe", quality = "green", max_stack = 1 },
  recipe_sword = { kind = "recipe", max_stack = 1 },
}
local function count_item(sheet, item)
  local count = 0
  for i, id in ipairs(sheet.inventory) do if id == item then count = count + sheet.inventory_counts[i] end end
  return count
end
local deps = {
  CFG = cfg, Guard = { assert_client_epoch_matches = function() return true end },
  EQUIP_ORDER = { "hand" }, read_character_sheet = read_sheet, write_character_sheet = write_sheet,
  read_pve_progress = read_progress,
  ensure_sheet_inventory_counts = function() end, ensure_sheet_workshop = function() end,
  ensure_character_sheet_initialized = function() end,
  get_merged_item_defs = function() return defs end,
  item_def_is_equipment = function(def) return def.kind == "equipment" end,
  sheet_has_learned_for_craft = function() return true end,
  workshop_craft_cost_from_def = function() return { ore = 2, gold = 20, ingot_def = "ingot", ingot_n = 1, tesseract_n = 0 } end,
  workshop_craft_duration_seconds = function() return 10000 end,
  inventory_count_def = count_item,
  inventory_remove_def_total = function(sheet, id, count)
    for i, item in ipairs(sheet.inventory) do
      if item == id and sheet.inventory_counts[i] >= count then
        sheet.inventory_counts[i] = sheet.inventory_counts[i] - count
        if sheet.inventory_counts[i] == 0 then sheet.inventory[i] = "" end
        return true
      end
    end
    return false
  end,
  inventory_try_add = function(sheet, id, count)
    for i, item in ipairs(sheet.inventory) do
      if item == "" then sheet.inventory[i] = id; sheet.inventory_counts[i] = count; return true end
    end
    return false
  end,
  encode_character_ok_response = function(sheet, progress) return { ok = true, sheet = copy(sheet), progression = copy(progress) } end,
}
local economy = require("modules.duel_match3_economy_rpc")(deps)
local function reset(item)
  db, batches, before_write, fail_write = {}, {}, nil, false
  local sheet = normalized_sheet()
  sheet.inventory[1] = item or "sword"; sheet.inventory_counts[1] = item == "ingot" and 2 or 1
  put(sheet_key, sheet)
  put(progress_key, { xp = 0, gold = 100, ore = 10, matter = 0, ingots = 0, blueprint_green = 0, energy = 19, mine = { selected_floor = 7 } })
end
local ctx = { user_id = "player" }
local function sell() return economy.duel_character_item_sell(ctx, { source = "inventory", inv_index = 0 }) end
local function craft() return economy.duel_workshop_craft_start(ctx, { slot_index = 0, output_def_id = "sword" }) end
local function rush() return economy.duel_workshop_craft_rush(ctx, { slot_index = 0 }) end
local passed = 0
local function test(name, run)
  reset()
  run()
  passed = passed + 1
  print("PASS " .. name)
end
test("sale removes item and awards gold in one CAS transaction", function()
  assert(sell().ok)
  assert(#batches == 1 and #batches[1] == 2)
  assert(db[sheet_key].value.inventory[1] == "" and db[progress_key].value.gold == 200)
  assert(db[sheet_key].value._storage_version == nil)
end)
test("failed sale persists neither item deletion nor currency", function()
  fail_write = true
  assert(not sell().ok)
  assert(db[sheet_key].value.inventory[1] == "sword" and db[progress_key].value.gold == 100)
end)
test("simultaneous sale of the same item awards gold once", function()
  local other
  before_write = function() other = sell() end
  assert(not sell().ok and other.ok)
  assert(db[progress_key].value.gold == 200 and db[sheet_key].value.inventory[1] == "")
end)
test("stale inventory move cannot restore a sold item", function()
  local stale = read_sheet("player")
  assert(sell().ok)
  stale.inventory[1] = ""; stale.equipment[1] = "sword"
  assert(not pcall(write_sheet, "player", stale))
  assert(db[sheet_key].value.equipment[1] == "" and db[progress_key].value.gold == 200)
end)
test("craft atomically consumes wallet materials and starts timer", function()
  reset("ingot")
  assert(craft().ok and #batches == 1 and #batches[1] == 2)
  assert(db[progress_key].value.gold == 80 and db[progress_key].value.ore == 8)
  assert(db[sheet_key].value.inventory_counts[1] == 1 and db[sheet_key].value.workshop_slots[1].output_def_id == "sword")
end)
test("failed craft does not charge or consume materials", function()
  reset("ingot"); fail_write = true
  assert(not craft().ok)
  assert(db[progress_key].value.gold == 100 and db[progress_key].value.ore == 10)
  assert(db[sheet_key].value.inventory_counts[1] == 2 and db[sheet_key].value.workshop_slots[1].output_def_id == "")
end)
test("racing craft starts only once and charges once", function()
  reset("ingot")
  local other
  before_write = function() other = craft() end
  assert(not craft().ok and other.ok)
  assert(db[progress_key].value.gold == 80 and db[sheet_key].value.inventory_counts[1] == 1)
end)
test("rush commits timer and price together and retries concurrent spend", function()
  local sheet = copy(db[sheet_key].value)
  sheet.workshop_slots[1] = { output_def_id = "sword", ends_at = os.time() + 10000 }
  put(sheet_key, sheet)
  before_write = function()
    local progress = copy(db[progress_key].value); progress.gold = progress.gold - 7; put(progress_key, progress)
  end
  assert(rush().ok and #batches == 2 and #batches[1] == 2)
  assert(db[progress_key].value.gold == 83)
  assert(db[sheet_key].value.workshop_slots[1].ends_at == sheet.workshop_slots[1].ends_at - 1200)
end)
test("failed rush does not charge or reduce timer", function()
  local sheet = copy(db[sheet_key].value)
  sheet.workshop_slots[1] = { output_def_id = "sword", ends_at = os.time() + 10000 }; put(sheet_key, sheet)
  fail_write = true
  assert(not rush().ok and db[progress_key].value.gold == 100)
  assert(db[sheet_key].value.workshop_slots[1].ends_at == sheet.workshop_slots[1].ends_at)
end)
test("missing character sheet uses create-only CAS and read errors fail closed", function()
  db[sheet_key] = nil
  local first, stale = read_sheet("player"), read_sheet("player")
  write_sheet("player", first)
  assert(not pcall(write_sheet, "player", stale))
  local read = nk.storage_read
  nk.storage_read = function() error("temporary read failure") end
  assert(not pcall(read_sheet, "player"))
  nk.storage_read = read
end)

test("concurrent first initialization accepts existing sheet without overwriting inventory", function()
  db[sheet_key] = nil
  before_write = function() put(sheet_key, normalized_sheet({ inventory = { "sword" }, inventory_counts = { 1 } })) end
  assert(pcall(initialize_sheet, "player"))
  assert(db[sheet_key].value.inventory[1] == "sword" and #batches == 1)
end)

test("initialization read and write failures propagate without creating an empty sheet", function()
  db[sheet_key] = nil
  local read = nk.storage_read
  nk.storage_read = function() error("temporary read failure") end
  assert(not pcall(initialize_sheet, "player") and db[sheet_key] == nil and #batches == 0)
  nk.storage_read = read
  fail_write = true
  assert(not pcall(initialize_sheet, "player") and db[sheet_key] == nil)
end)

-- Load only the actual progress serializer/writer. Reward finalization is now
-- an explicit module contract; never extract neighboring bootstrap code.
local function load_env(code, env)
  local fn
  if setfenv then fn = assert(loadstring(code)); setfenv(fn, env)
  else fn = assert(load(code, "progress-helpers", "t", env)) end
  fn()
end
local env = setmetatable({ Runtime = {}, nk = nk, CFG = cfg }, { __index = _G })
local progress_start = assert(source:find("Runtime.build_pve_progress_write = function", 1, true))
local progress_end = assert(source:find("function random_affix_for_floor", progress_start, true))
load_env(source:sub(progress_start, progress_end - 1) .. "\nexport_write_progress = write_pve_progress", env)

-- Exercise the production PveRewards + Journal + Inventory modules together
-- with the real core sheet/progress CAS batch builder used above. Only the
-- catalog, RNG-independent drops and level policy are controlled inputs.
local journal = require("tests.journal_fixture_admission").wrap(require("modules.duel_match3_journal").new())
local rewards = require("modules.duel_match3_pve_rewards").new({
  normalize_difficulty = function() return "easy" end,
  get_bot_profile = function() return { floor = 1, reward_xp = 5, reward_gold = 10, reward_ore = 2 } end,
  clamp_floor = function(f) return cfg.clamp_int(f, 1, cfg.PVE_MAX_LEVEL) end,
  max_floor = cfg.PVE_MAX_LEVEL, is_boss_floor = function() return false end,
  reward_multiplier = function() return 1 end, stat_multiplier = function() return 1 end,
  apply_reward_xp = function(xp) return xp end,
  ingot_drop_chance = function() return 1 end, ingot_def = function() return "ingot" end,
  uses_recipe_drop = function() return true end, recipe_color = function() return "green" end,
  pick_recipe = function() return "recipe" end,
  respawn_seconds = function() return 60 end, next_affix = function() return "normal" end,
  read_progress = read_progress, read_sheet = read_sheet, get_defs = function() return defs end,
  inventory = function() return Inventory end, build_sheet_writes = build_sheet_writes,
  empty_keys = function() return { miner_key = 0, dark_key = 0 } end,
  add_blueprint = function(progress, color)
    local k = "blueprint_" .. color; progress[k] = (tonumber(progress[k]) or 0) + 1
  end,
  level_from_xp = function() return 1 end,
  normalize_unlocked = function() return { easy = 1, medium = 0, hard = 0 } end,
  floor_key = function() return "easy_1" end,
  journal = function() return journal end, is_stale = function() return false end,
})
local function award() return rewards.award("player", "mine_1", 0, { difficulty = "easy", floor = 1 }, "economy-fixture") end
local function contains(batch, collection, object_key)
  for _, op in ipairs(batch) do if op.collection == collection and op.key == object_key then return true end end
  return false
end
local journal_key = key("duel_match3_journal", "result:economy-fixture:pve")

test("progression create-only CAS cannot overwrite a racing first write", function()
  local stale = copy(db[progress_key].value)
  assert(not pcall(env.export_write_progress, "player", stale, nil))
  db[progress_key] = nil
  env.export_write_progress("player", stale, nil)
  assert(db[progress_key].value.gold == 100)
  assert(not pcall(env.export_write_progress, "player", stale, nil))
end)

test("PvE currency blueprint recipe and committed journal share one CAS transaction", function()
  local result = award()
  assert(result.reward_gold == 10 and result.reward_recipe_item_id == "recipe" and not result.reward_pending)
  assert(#batches == 3 and #batches[1] == 1 and #batches[2] == 3 and #batches[3] == 5)
  assert(contains(batches[1], "duel_match3_journal", "retention_v2"))
  assert(not contains(batches[2], cfg.PVE_PROGRESS_COLLECTION, cfg.PVE_PROGRESS_KEY))
  assert(contains(batches[3], cfg.CHARACTER_SHEET_COLLECTION, cfg.CHARACTER_SHEET_KEY))
  assert(contains(batches[3], cfg.PVE_PROGRESS_COLLECTION, cfg.PVE_PROGRESS_KEY))
  assert(contains(batches[3], "duel_match3_journal", "result:economy-fixture:pve"))
  assert(db[progress_key].value.gold == 110 and db[progress_key].value.blueprint_green == 1)
  assert(count_item(db[sheet_key].value, "recipe") == 1 and db[journal_key].value.reward_status == "committed")
end)

test("PvE blueprint grant preserves concurrent wallet spend on actual reward CAS retry", function()
  local write, raced = nk.storage_write, false
  nk.storage_write = function(writes)
    if not raced and contains(writes, cfg.PVE_PROGRESS_COLLECTION, cfg.PVE_PROGRESS_KEY) then
      raced = true
      local progress = copy(db[progress_key].value); progress.gold = progress.gold - 17; put(progress_key, progress)
    end
    return write(writes)
  end
  local ok, result = pcall(award)
  nk.storage_write = write
  assert(ok and raced and result.reward_gold == 10 and not result.reward_pending)
  assert(#batches == 4 and #batches[3] == 5 and #batches[4] == 5 and db[progress_key].value.gold == 93)
  assert(db[progress_key].value.blueprint_green == 1 and count_item(db[sheet_key].value, "recipe") == 1)
end)

test("failed PvE reward commit retains frozen intent and applies no currency blueprint or recipe", function()
  local write = nk.storage_write
  nk.storage_write = function(writes)
    if contains(writes, cfg.PVE_PROGRESS_COLLECTION, cfg.PVE_PROGRESS_KEY) then fail_write = true end
    return write(writes)
  end
  local ok, result = pcall(award)
  nk.storage_write = write; fail_write = false
  assert(ok and result.reward_pending and result.err == "storage_unavailable")
  assert(db[progress_key].value.gold == 100 and db[progress_key].value.blueprint_green == 0)
  assert(count_item(db[sheet_key].value, "recipe") == 0)
  assert(result.reward_xp == 0 and result.reward_gold == 0 and result.reward_recipe_item_id == "")
  assert(db[journal_key].value.status == "prepared" and db[journal_key].value.plan.items[1].def_id == "recipe")
  local retried = award()
  assert(not retried.reward_pending and retried.reward_gold == 10 and db[progress_key].value.gold == 110)
  assert(count_item(db[sheet_key].value, "recipe") == 1 and db[progress_key].value.blueprint_green == 1)
end)

test("PvE storage read failure returns pending zero credits without throwing or writing", function()
  local read, before = nk.storage_read, copy(db)
  nk.storage_read = function() error("temporary inventory/journal read failure") end
  local ok, result = pcall(award)
  nk.storage_read = read
  assert(ok and result.reward_pending and result.err == "storage_unavailable")
  assert(result.reward_xp == 0 and result.reward_gold == 0 and result.reward_ingots == 0)
  assert(result.reward_blueprint == "" and result.reward_recipe_item_id == "" and (result.reward_key_amount or 0) == 0)
  assert(db[progress_key].value.gold == before[progress_key].value.gold and #batches == 0)
  assert(count_item(db[sheet_key].value, "recipe") == 0 and db[journal_key] == nil)
end)

test("replayed committed PvE result cannot grant currency blueprint or recipe twice", function()
  local result = award(); local count = #batches
  assert(not result.reward_pending and award().reward_recipe_item_id == "recipe" and #batches == count)
  assert(db[progress_key].value.gold == 110 and db[progress_key].value.blueprint_green == 1)
  assert(count_item(db[sheet_key].value, "recipe") == 1)
end)
print("economy security: " .. passed .. " tests passed")
