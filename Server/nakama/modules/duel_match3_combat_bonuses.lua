--[[
  Пассивные боевые бонусы из claimed-шагов достижений (каталог achievement_catalog).
  Плоские — прибавляются после процентов.
  Проценты (HP/урон/броня/лечение) умножают сумму «уровень + экипировка» целиком.
  Крит — только плоские прибавки.
]]
local function runtime_lua_require_nested(name_nested, name_root)
  local ok, mod = pcall(require, name_nested)
  if ok and mod ~= nil then return mod end
  return require(name_root)
end

local AchCat = runtime_lua_require_nested("modules.duel_match3_achievement_catalog", "duel_match3_achievement_catalog")

local M = {}

local function claim_has(claimed_arr, tok)
  if claimed_arr == nil then return false end
  for _, c in ipairs(claimed_arr) do
    if c == tok then return true end
  end
  return false
end

local function empty_bonuses()
  return {
    flat_hp = 0,
    flat_dmg = 0,
    flat_armor = 0,
    flat_crit = 0,
    flat_heal = 0,
    pct_hp = 0,
    pct_dmg = 0,
    pct_armor = 0,
    pct_heal = 0,
  }
end

--- Суммировать бонусы по массиву токенов achievement_claimed и каталогу.
function M.accumulate_from_claimed(claimed)
  local b = empty_bonuses()
  if type(claimed) ~= "table" then return b end
  for _, ch in ipairs(AchCat.all_chains()) do
    for step = 0, #ch.steps - 1 do
      local tok = tostring(ch.id) .. ":" .. tostring(step)
      if claim_has(claimed, tok) then
        local rewards = AchCat.step_rewards(ch.id, step)
        AchCat.apply_rewards_to_combat_bonuses(rewards, b)
      end
    end
  end
  return b
end

local function scale_and_add(value, pct, flat)
  local v = tonumber(value) or 0
  local p = tonumber(pct) or 0
  local f = tonumber(flat) or 0
  return math.floor(v * (1 + p) + 0.5) + math.floor(f + 0.5)
end

--- Применить бонусы к merged-статам { hp, damage, armor, healing, crit_chance }.
function M.apply_to_merged_stats(stats, claimed)
  if stats == nil then return stats end
  local b = M.accumulate_from_claimed(claimed)
  stats.hp = math.max(1, scale_and_add(stats.hp, b.pct_hp, b.flat_hp))
  stats.damage = math.max(0, scale_and_add(stats.damage, b.pct_dmg, b.flat_dmg))
  stats.armor = math.max(0, scale_and_add(stats.armor, b.pct_armor, b.flat_armor))
  stats.healing = math.max(0, scale_and_add(stats.healing, b.pct_heal, b.flat_heal or 0))
  local crit = tonumber(stats.crit_chance) or 0
  stats.crit_chance = math.max(0, math.min(1, crit + (b.flat_crit or 0)))
  return stats
end

return M
