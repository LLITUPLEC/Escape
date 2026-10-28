-- Pure shared rules used by the live boss battle and balance simulations.
local M={TOKEN_ID="global_boss_teleport",ATTEMPTS=3,HP_STEP=50000,DAMAGE_STEP=500,MAX_TURNS=30}
function M.begin_turn(state)
  local g=assert(state.global_boss);local b=assert(state.stats[state.bot_user_id])
  g.boss_own_turn=(tonumber(g.boss_own_turn)or 0)+1
  local n=g.boss_own_turn
  g.adaptation_triggered=true
  -- Keep the fractional carry. Only the ordinary core hit calculation rounds.
  b.base_damage=(tonumber(b.base_damage)or 0)*(1+n/100)
  b.base_crit=math.min(1,(tonumber(b.base_crit)or 0)+n/100)
  b.base_heal=0
  g.scream_triggered=false;g.scream_damage=0
  g.boss_turn_prepared=true
  return n
end
function M.scream_ready(state)
  local g=assert(state.global_boss)
  return g.boss_turn_prepared==true and(tonumber(g.boss_own_turn)or 0)>=(tonumber(g.next_scream_turn)or 1)
end
-- Scream is its own free action, like Petard: no swap, mana cost or turn
-- advance. Preparation and the ordinary AI action commit separately around it.
function M.scream(state)
  if not M.scream_ready(state)then return false end
  local g=state.global_boss;local hero=assert(state.stats[state.owner_user_id])
  g.adaptation_triggered=false
  local amount=math.ceil(math.max(1,tonumber(hero.initial_hp)or tonumber(hero.max_hp)or 1)*.20)
  hero.hp=math.max(0,(tonumber(hero.hp)or 0)-amount)
  g.scream_triggered=true;g.scream_damage=amount;g.next_scream_turn=g.boss_own_turn+2
  return true,amount
end
function M.scream_cooldown(state)
  local g=state.global_boss or{};return math.max(0,(tonumber(g.next_scream_turn)or 1)-(tonumber(g.boss_own_turn)or 0))
end
return M

