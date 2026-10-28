-- Actual collection definitions and character stats, including actor/aura order.
package.path="Server/nakama/?.lua;"..package.path
package.loaded["modules.duel_match3_achievement_catalog"]={all_chains=function()return{}end}
local Rewards=require("modules.duel_cosmetics_rewards")
local Stats=require("modules.duel_match3_character_stats")
local current=Rewards.empty()
local mutable,readonly=0,0
local config={CFG={MAX_HP=150,character_stats_base_for_level=function()return{hp=1000,damage=1000,armor=1000,healing=1000,crit_chance=.1}end},
  is_human=function()return true end,read_pve_progress=function()return{level=1}end,read_character_sheet=function()return{}end,
  storage_read_match3_summary_val=function()return{}end,read_stats_snapshot=function()return{}end,read_progress_snapshot=function()return{level=1}end,
  cosmetics_bonuses=function()mutable=mutable+1;return Rewards.fractions(current)end,
  cosmetics_bonuses_read_only=function()readonly=readonly+1;return Rewards.fractions(current)end,
  aura_apply_to_pve_player_stats=function(actor)actor.max_hp=actor.max_hp*2;actor.hp=actor.max_hp;actor.base_heal=actor.base_heal*2 end}
Stats.configure(config)
local passed=0
local function check(name,fn)fn();passed=passed+1;print("PASS "..name)end
check("every appearance applies only its named stats and leaves crit additive",function()
  for id,reward in pairs(Rewards.catalog)do
    current=Rewards.empty();if id~="dawnwarden"then Rewards.add(current,Rewards.receipt(id,nil,1))end
    local result=Stats.compute_character_display_stats("alice")
    assert(result.hp==1000+(reward.hp_basis_points or 0)/10,id)
    assert(result.damage==1000+(reward.damage_basis_points or 0)/10,id)
    assert(result.armor==1000+(reward.armor_basis_points or 0)/10,id)
    assert(result.healing==1000+(reward.heal_basis_points or 0)/10+(reward.heal_flat or 0),id)
    assert(math.abs(result.crit_chance-(.1+(reward.crit_basis_points or 0)/10000))<1e-10,id)
  end
end)
check("collection stacks once to HP12 armor9 damage6 healing7 plus300 crit1pp",function()
  current=Rewards.empty();for id in pairs(Rewards.catalog)do if id~="dawnwarden"then Rewards.add(current,Rewards.receipt(id,nil,1))end end
  local result=Stats.compute_character_display_stats("alice")
  assert(result.hp==1120 and result.armor==1090 and result.damage==1060 and result.healing==1370 and math.abs(result.crit_chance-.11)<1e-10)
  local repeated=Stats.compute_character_display_stats("alice");assert(repeated.hp==1120 and repeated.healing==1370)
  local actor={};Stats.apply_player_combat_stats_from_sheet(actor,"alice",{aura={}})
  assert(actor.max_hp==2240 and actor.hp==2240 and actor.base_heal==2740 and actor.base_damage==1060 and actor.base_armor==1090)
end)
check("practice observes structured bonus snapshot without invoking reconciliation",function()
  local before=mutable;local actor={};Stats.apply_player_combat_stats_from_sheet(actor,"alice",{read_only=true})
  assert(mutable==before and readonly==1 and actor.max_hp==1120 and actor.base_heal==1370)
end)
check("crit chance clamps at one and fractional collection amounts floor identically for actors and display",function()
  config.CFG.character_stats_base_for_level=function()return{hp=101,damage=101,armor=101,healing=101,crit_chance=.999}end
  local result=Stats.compute_character_display_stats("alice");local actor={};Stats.apply_player_combat_stats_from_sheet(actor,"alice")
  assert(result.hp==113 and result.damage==107 and result.armor==110 and result.healing==408 and result.crit_chance==1)
  assert(actor.max_hp==result.hp and actor.base_damage==result.damage and actor.base_heal==result.healing)
end)
check("invalid structured bonuses fail closed including NaN and over-cap flat heal",function()
  for key,value in pairs({hp=.121,armor=.091,damage=.061,healing=.071,crit_chance=.011,healing_flat=301})do
    config.cosmetics_bonuses=function()local b=Rewards.fractions(current);b[key]=value;return b end
    assert(not pcall(Stats.compute_character_display_stats,"alice"),key)
  end
  config.cosmetics_bonuses=function()return{hp=0/0}end
  assert(not pcall(Stats.compute_character_display_stats,"alice"))
end)
print("Cosmetics collection stats: "..passed.." passed")
