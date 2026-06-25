--[[
  Расчёт итоговых статов персонажа (уровень + экип + достижения) и применение к actor в матче.
  Вынесено из duel_match3.lua из-за лимита локальных переменных в основном chunk.
]]
local function runtime_lua_require_nested(name_nested, name_root)
  local ok, mod = pcall(require, name_nested)
  if ok and mod ~= nil then return mod end
  return require(name_root)
end

local CombatBon = runtime_lua_require_nested("modules.duel_match3_combat_bonuses", "duel_match3_combat_bonuses")

local M = {}
local deps = {}

function M.configure(d)
  if type(d) == "table" then deps = d end
end

local function cfg()
  return deps.CFG or {}
end

local function read_achievement_claimed(user_id)
  if user_id == nil or user_id == "" then return {} end
  local fn = deps.storage_read_match3_summary_val
  if type(fn) ~= "function" then return {} end
  local val, _ = fn(user_id)
  if type(val.achievement_claimed) == "table" then
    return val.achievement_claimed
  end
  return {}
end

local function merged_stats_for_user(user_id)
  local CFG = cfg()
  local max_hp = tonumber(CFG.MAX_HP) or 150
  if user_id == nil or user_id == "" then
    return { hp = max_hp, damage = 0, armor = 0, healing = 0, crit_chance = 0 }
  end
  local ensure_sheet = deps.ensure_character_sheet_initialized
  local read_progress = deps.read_pve_progress
  local read_sheet = deps.read_character_sheet
  local ensure_counts = deps.ensure_sheet_inventory_counts
  local sum_equip = deps.sum_equipment_bonuses
  local merge = deps.merge_stats_with_equipment
  local base_for_level = CFG.character_stats_base_for_level
  local clamp_int = CFG.clamp_int
  local max_level = CFG.PVE_MAX_LEVEL or 12

  if type(ensure_sheet) == "function" then ensure_sheet(user_id) end
  local progress = {}
  if type(read_progress) == "function" then
    progress, _ = read_progress(user_id)
  end
  local level = 1
  if type(clamp_int) == "function" then
    level = clamp_int(progress.level or 1, 1, max_level)
  else
    level = math.max(1, math.min(max_level, tonumber(progress.level) or 1))
  end
  local sheet = type(read_sheet) == "function" and read_sheet(user_id) or {}
  if type(ensure_counts) == "function" then ensure_counts(sheet) end
  local base_stats = type(base_for_level) == "function" and base_for_level(level) or { hp = max_hp, damage = 0, armor = 0, healing = 0, crit_chance = 0 }
  local bonus = type(sum_equip) == "function" and sum_equip(sheet) or {}
  local stats = type(merge) == "function" and merge(base_stats, bonus) or base_stats
  CombatBon.apply_to_merged_stats(stats, read_achievement_claimed(user_id))
  return stats
end

--- Итоговые статы для экрана персонажа / RPC (без боевой ауры PvE).
function M.compute_character_display_stats(user_id)
  return merged_stats_for_user(user_id)
end

function M.apply_merged_combat_stats_to_actor(actor, merged)
  if actor == nil or merged == nil then return end
  local CFG = cfg()
  local max_hp = tonumber(CFG.MAX_HP) or 150
  actor.max_hp = math.max(1, math.floor(tonumber(merged.hp) or max_hp))
  actor.hp = actor.max_hp
  actor.initial_hp = actor.max_hp
  actor.base_damage = math.max(0, math.floor(tonumber(merged.damage) or 0))
  actor.base_armor = math.max(0, math.floor(tonumber(merged.armor) or 0))
  actor.base_crit = math.max(0, tonumber(merged.crit_chance) or 0)
  actor.base_heal = math.max(0, math.floor(tonumber(merged.healing) or 0))
end

--- Уровень + экип + достижения; opts.aura — таблица ауры PvE (применяется после достижений).
function M.apply_player_combat_stats_from_sheet(actor, user_id, opts)
  if actor == nil or user_id == nil or user_id == "" then return end
  local is_human = deps.is_human
  if type(is_human) == "function" and not is_human(user_id) then return end
  local merged = merged_stats_for_user(user_id)
  M.apply_merged_combat_stats_to_actor(actor, merged)
  if opts ~= nil and opts.aura ~= nil then
    local aura_fn = deps.aura_apply_to_pve_player_stats
    if type(aura_fn) == "function" then
      aura_fn(actor, opts.aura)
    end
  end
end

return M
