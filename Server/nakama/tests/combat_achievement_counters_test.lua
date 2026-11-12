local E = dofile('Server/nakama/tests/global_boss_engine_fixture.lua')
local Ach = require('modules.duel_match3_achievements')
local petard_key = 'dnn.petard_50_pvp'
local blocked_key = 'slaughter.damage_blocked_arena'
local crit_key = 'slaughter.critical_hits'

local function state(mode, opponent_id)
  local s = E.init_state({max_hp=150, base_damage=0, base_crit=1}, {max_hp=150, base_armor=0})
  s.mode, s.global_boss = mode, nil
  s.stats[opponent_id] = s.stats[s.bot_user_id]
  s.players_sorted = {'hero', opponent_id}
  return s
end

local function count(s, uid, key)
  return ((s._ach_counters or {})[uid] or {})[key] or 0
end

local function petard(s, target_id)
  assert(E.resolve(s, {actionType=4}, 'hero', target_id))
end

local function fury(s)
  local first_active = E.CFG.ACTIVE_Y_MIN * E.CFG.SIZE + 1
  for i = first_active, #s.board do s.board[i] = i < first_active + 10 and 4 or 1 end
  assert(E.resolve(s, {actionType=6}, 'hero', s.players_sorted[2]))
  assert(s.stats.hero.fury_bomb_bonus == 10)
end

do
  local s = state('pvp', 'rival')
  fury(s)
  petard(s, 'rival')
  assert(s.stats.rival.hp == 100 and count(s, 'hero', petard_key) == 1 and count(s, 'hero', crit_key) == 1)
  s.stats.rival.base_armor = 1
  petard(s, 'rival')
  assert(s.stats.rival.hp == 51 and count(s, 'hero', petard_key) == 1 and count(s, 'rival', blocked_key) == 1)
  s.stats.rival.hp, s.stats.rival.base_armor = 10, 0
  petard(s, 'rival')
  assert(s.stats.rival.hp == 0 and count(s, 'hero', petard_key) == 1 and count(s, 'hero', crit_key) == 3)
  local effects = Ach.build_match_finish_effects(s, 'hero', 'hero', 'rival', 4)
  assert(effects.hero[crit_key] == 3 and effects.rival[blocked_key] == 1)
  print('PASS Classic Petard uses actual HP damage; armor, overkill and final effects')
end

do
  local s = state('pve', 'zz-bot-global')
  s.arena_mirror = {round='qf'}
  fury(s)
  petard(s, s.bot_user_id)
  assert(count(s, 'hero', petard_key) == 1)
  s.stats.hero.base_armor = 13
  assert(E.resolve(s, {actionType=5}, 'hero', s.bot_user_id))
  assert(E.resolve(s, {actionType=4}, s.bot_user_id, 'hero'))
  assert(s.stats.hero.hp == 150 and count(s, 'hero', blocked_key) == E.CFG.PETARD_DAMAGE)
  assert(count(s, s.bot_user_id, petard_key) == 0 and count(s, s.bot_user_id, crit_key) == 0)
  print('PASS tournament bot can be a Petard target and its hit credits effective shield to defender')
end

do
  local s = state('pvp', 'rival')
  s.stats.rival.base_armor = 3
  assert(E.resolve(s, {actionType=2, cx=2, cy=2}, 'hero', 'rival'))
  assert(s.stats.rival.hp < 150 and count(s, 'hero', petard_key) == 0)
  assert(count(s, 'hero', crit_key) == 1 and count(s, 'rival', blocked_key) == 3)
  print('PASS combined ability and cascade damage is counted as a single hit')
end

for _, kind in ipairs({'mine', 'pro', 'race', 'simulation', 'boss'}) do
  local s = state(kind == 'mine' and 'pve' or 'pvp', 'rival')
  if kind == 'pro' then s.pvp_pro = true end
  if kind == 'race' then s.pvp_race = true end
  if kind == 'simulation' then s._sim_metrics = {} end
  if kind == 'boss' then s.global_boss = {} end
  fury(s)
  s.stats.hero.base_damage = 5
  s.stats.rival.base_armor = 10
  petard(s, 'rival')
  assert(count(s, 'hero', petard_key) == 0 and count(s, 'rival', blocked_key) == 0)
  assert(count(s, 'hero', crit_key) == ((kind == 'mine' or kind == 'pro') and 1 or 0))
  s.stats.rival.base_armor = 100
  petard(s, 'rival')
  assert(count(s, 'hero', crit_key) == ((kind == 'mine' or kind == 'pro') and 1 or 0))
  print('PASS mode and non-damaging critical hit filters: ' .. kind)
end
