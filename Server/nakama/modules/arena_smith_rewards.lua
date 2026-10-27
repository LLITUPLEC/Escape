-- Pure inventory mutations. The arena commits sheet + receipt + result together.
local ok, Inventory = pcall(require, "modules.duel_match3_inventory")
if not ok then Inventory = require("duel_match3_inventory") end
local drops_ok, MineRecipes = pcall(require,"modules.duel_mine_recipe_drops")
if not drops_ok then MineRecipes=require("duel_mine_recipe_drops") end
local M = { VERSION = 2 }
local TIERS = {
  green = { entry = 10, champion = 20, consolation = 2, fallback_ingots = 10 },
  blue = { entry = 15, champion = 30, consolation = 3, fallback_ingots = 15 },
  purple = { entry = 20, champion = 40, consolation = 4, fallback_ingots = 20 },
}
function M.tier(bet) return assert(TIERS[bet], "invalid_smith_tier") end
function M.cost(bet)
  return { version = M.VERSION, amount = M.tier(bet).entry, def_id = "ingot_" .. bet }
end
local function new_recipe(sheet, defs, bet, progress)
  local owned = {}
  for _, id in ipairs(sheet.learned_recipes or {}) do owned[id] = true end
  for _, id in pairs(sheet.inventory or {}) do owned[id] = true end
  for _, row in ipairs(sheet.inventory_pending_loot or {}) do owned[row.def_id] = true end
  local candidates = {}
  for _,id in ipairs(MineRecipes.eligible(progress,bet)) do
    local def=defs[id]
    if type(def) == "table" and def.kind == "recipe" and not owned[id] then
      candidates[#candidates + 1] = id
    end
  end
  table.sort(candidates)
  return candidates[1]
end
function M.grant(sheet, defs, bet, champion, progress)
  Inventory.normalize(sheet)
  local cfg = M.tier(bet)
  local receipt = { version = M.VERSION, recipe_policy_version = 3, outcome = champion and "champion" or "participation",
    items = {}, delivered_items = {}, pending_items = {}, granted_at = os.time() }
  local function give(id, amount)
    assert(type(defs[id]) == "table", "smith_reward_definition_missing:" .. id)
    local result = Inventory.add_or_queue(sheet, id, amount, defs)
    assert(result.delivered + result.pending == amount, "smith_reward_delivery_mismatch")
    receipt.items[#receipt.items + 1] = { def_id = id, amount = amount }
    if result.delivered > 0 then receipt.delivered_items[#receipt.delivered_items + 1] = { def_id = id, amount = result.delivered } end
    if result.pending > 0 then receipt.pending_items[#receipt.pending_items + 1] = { def_id = id, amount = result.pending } end
  end
  local recipe = champion and new_recipe(sheet, defs, bet, progress) or nil
  give("ingot_" .. bet, champion and (cfg.champion + (not recipe and cfg.fallback_ingots or 0)) or cfg.consolation)
  if champion then
    if recipe then give(recipe, 1) end
  end
  return receipt
end
return M
