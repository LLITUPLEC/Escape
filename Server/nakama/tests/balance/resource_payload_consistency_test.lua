-- Regression: character progression and the periodic resources RPC must show the
-- same spendable inventory balance, including zero after the last item is used.
-- Execute with Lua 5.1 from the repository root; does not contact Nakama.
package.path = "Server/nakama/?.lua;" .. package.path
local CFG = require("modules.duel_match3_config")
local file = assert(io.open(os.getenv("INVENTORY_CORE_SOURCE") or "Server/nakama/modules/duel_match3.lua", "r"))
local source = file:read("*a"); file:close()
local start = assert(source:find("local function workshop_payload_arrays", 1, true))
local finish = assert(source:find("local function sheet_has_learned", start, true))
local sheet = { inventory = {}, inventory_counts = {} }
local nk = { json_encode = function(v) return v end, logger_error = function() end }
package.preload.nakama = function() return nk end
local defs = {
  ingot_green = { kind = "material" }, ingot_blue = { kind = "material" },
  ingot_purple = { kind = "material" }, tesseract = { kind = "tesseract" },
}
local env = setmetatable({
  CFG = CFG,
  Runtime = { Inventory = require("modules.duel_match3_inventory") },
  nk = nk,
  CharStats = { compute_character_display_stats = function() return {} end },
  character_sheet_payload_arrays = function(s) return s.equipment or {}, s.inventory, s.inventory_counts end,
  learned_recipes_payload = function() return {} end,
  ensure_sheet_workshop = function(s)
    s.workshop_slots = s.workshop_slots or {}
    for i = 1, 8 do s.workshop_slots[i] = s.workshop_slots[i] or {} end
  end,
  ensure_sheet_inventory_counts = function(s)
    s.inventory = s.inventory or {}; s.inventory_counts = s.inventory_counts or {}
  end,
  get_merged_item_defs = function() return defs end,
  read_character_sheet = function(uid) assert(uid == "player"); return sheet end,
  inventory_count_def = function(s, id)
    local count = 0
    for i = 1, require("modules.duel_match3_inventory").capacity(s) do
      if s.inventory[i] == id then count = count + (s.inventory_counts[i] or 0) end
    end
    return count
  end,
  pve_energy_max_for_user = function() return 20000 end,
}, { __index = _G })
local chunk = assert(loadstring(source:sub(start, finish - 1)
  .. "\nreturn build_resource_payload, build_progression_payload, build_progression_payload_auto, encode_character_ok_response"))
setfenv(chunk, env)
local resources, progression, auto_progression, character_response = chunk()
local progress = {
  level = 5, xp = 2000, gold = 123, ore = 456, matter = 7,
  energy = 37, ingots = 1000, tesseracts = 900, -- Deliberately stale lifetime counters.
  key_items = { miner_key = 2, dark_key = 3 },
}
local economy = require("modules.duel_match3_economy_rpc")({
  CFG = CFG, read_pve_progress = function() return progress end,
  read_character_sheet = function(uid) assert(uid == "player"); return sheet end,
  build_resource_payload = resources, encode_character_ok_response = character_response,
})
local cases = 0
local function check(label, expected_ingots, expected_tesseracts)
  local p = progression(progress, "player", sheet)
  local a = auto_progression(progress, "player")
  local r = economy.duel_player_resources_get({user_id = "player"}, "{}")
  local c = economy.duel_character_get({user_id = "player"}, "{}")
  assert(r.ok == true and c.ok == true, label .. ": actual resource/character RPC failed")
  assert(c.progression.ingots == expected_ingots, label .. ": character RPC disagrees with inventory")
  assert(p.ingots == expected_ingots, label .. ": character progression uses stale ingots")
  assert(a.ingots == expected_ingots, label .. ": automatic progression uses stale ingots")
  assert(r.ingots == expected_ingots, label .. ": resource poll disagrees with inventory")
  assert(r.tesseracts == expected_tesseracts, label .. ": resource poll uses stale tesseracts")
  if p.tesseracts ~= nil then assert(p.tesseracts == expected_tesseracts, label .. ": stale progression tesseracts") end
  for _, snapshot in ipairs({p, a, r}) do
    assert(snapshot.gold == 123 and snapshot.ore == 456 and snapshot.matter == 7 and snapshot.energy == 37,
      label .. ": ordinary wallet changed")
    assert(snapshot.keys == 5, label .. ": key aggregation changed")
  end
  cases = cases + 1
end

sheet.inventory = { "ingot_green", "ingot_blue", "ingot_purple", "tesseract", "eq_t1_normal_helmet" }
sheet.inventory_counts = { 4, 3, 2, 3, 1 }
check("three colors and unrelated equipment", 9, 3)
sheet.inventory_counts[1] = 0; sheet.inventory[1] = "" -- Craft consumes green stack.
check("craft consumes entire stack", 5, 3)
sheet.inventory_counts[3] = 0; sheet.inventory[3] = "" -- Sell purple stack.
check("sale consumes entire stack", 3, 3)
sheet.inventory_counts[4] = 1 -- Legendary craft consumes tesseracts.
check("legendary craft consumes tesseracts", 3, 1)
sheet.inventory_counts[2] = 0; sheet.inventory[2] = "" -- Arena consumes remaining ingots.
check("last ingot spent", 0, 1)
sheet.inventory_counts[4] = 0; sheet.inventory[4] = ""
check("last tesseract spent", 0, 0)
sheet = { inventory = {}, inventory_counts = {} }
check("empty inventory with positive historical counters", 0, 0)
sheet.inventory = { "ingot_green", "ingot_green", "ingot_blue" }
sheet.inventory_counts = { 200, 7, 11 }
check("multiple stacks of the same color", 218, 0)
sheet.inventory = { "ingot_green", "tesseract" }; sheet.inventory_counts = { 2, 1 }
check("new loot after empty balance", 2, 1)
sheet.inventory_extra_slots = 25
sheet.inventory[50] = "ingot_purple"; sheet.inventory_counts[50] = 7
sheet.inventory_pending_loot = { {def_id="ingot_green",amount=100}, {def_id="tesseract",amount=20}, {def_id="recipe_green",amount=6} }
check("paid page item included but durable pending material excluded", 9, 1)
sheet.inventory[49] = "recipe_green"; sheet.inventory_counts[49] = 2
local r = economy.duel_player_resources_get({user_id="player"}, "{}")
assert(r.blueprint_green == 2, "resource HUD advertises historical/pending blueprints as available")
print("resource payload consistency: " .. tostring(cases) .. " inventory/spend/zero cases passed through resource and character RPCs")
