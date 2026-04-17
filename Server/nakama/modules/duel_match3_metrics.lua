-- Метрики баланса DPS×EHP для ботов и отладки каталога (крутить коэффициенты в конфиге при необходимости).
local function runtime_cfg()
  local ok, mod = pcall(require, "modules.duel_match3_config")
  if ok and mod ~= nil then return mod end
  return require("duel_match3_config")
end

local CFG = runtime_cfg()

local M = {}

--- Упрощённый DPS: урон × (1 + крит), крит ограничен [0,1].
function M.bot_effective_dps(bot)
  if type(bot) ~= "table" then return 0 end
  local d = tonumber(bot.base_damage) or tonumber(bot.damage) or 0
  local c = tonumber(bot.base_crit) or tonumber(bot.crit_chance) or 0
  c = math.max(0, math.min(1, c))
  return math.max(0, d * (1 + c))
end

--- EHP: (MAX_HP + hp_bonus) × (1 + k_arm × armor); k_arm из конфига.
function M.bot_ehp(bot)
  if type(bot) ~= "table" then return 0 end
  local k = tonumber(CFG.METRICS_ARMOR_TO_EHP_K) or 0.05
  local base_hp = tonumber(CFG.MAX_HP) or 150
  local bonus = math.max(0, tonumber(bot.hp_bonus) or 0)
  local hp = base_hp + bonus
  local ar = math.max(0, tonumber(bot.base_armor) or tonumber(bot.armor) or 0)
  return hp * (1 + k * ar)
end

function M.bot_dps_ehp_product(bot)
  local a = M.bot_effective_dps(bot)
  local b = M.bot_ehp(bot)
  return a * b, a, b
end

--- Игрок (суммы с экипа + база боя): DPS и «EHP» в тех же единицах, что §3.2 (броня в сотнях/тысячах).
--- total_crit — итог 0..1 (база уровня + сумма с вещей).
function M.player_effective_dps(damage_total, total_crit01)
  local d = math.max(0, tonumber(damage_total) or 0)
  local c = math.max(0, math.min(1, tonumber(total_crit01) or 0))
  return d * (1 + c)
end

function M.player_ehp_from_totals(hp_total, armor_total)
  local hp = math.max(0, tonumber(hp_total) or 0)
  local ar = math.max(0, tonumber(armor_total) or 0)
  local ref = tonumber(CFG.METRICS_PLAYER_ARMOR_REF) or 1000
  if ref <= 0 then ref = 1000 end
  return hp * (1 + ar / ref)
end

function M.player_dps_ehp_product_from_totals(damage_total, armor_total, hp_total, total_crit01)
  local dps = M.player_effective_dps(damage_total, total_crit01)
  local ehp = M.player_ehp_from_totals(hp_total, armor_total)
  return dps * ehp, dps, ehp
end

return M
