package.path="Server/nakama/?.lua;"..package.path
package.loaded["modules.duel_match3_achievement_catalog"]={all_chains=function()return {}end}
local Stats=require("modules.duel_match3_character_stats")
local calls,bonus=0,0.005
Stats.configure({CFG={MAX_HP=150,character_stats_base_for_level=function()return {hp=500,damage=4,armor=2,healing=3,crit_chance=.1}end},
  read_pve_progress=function()return {level=1}end,read_character_sheet=function()return {}end,sum_equipment_bonuses=function()return {}end,
  merge_stats_with_equipment=function(s,b)s.hp=1000;return s end,storage_read_match3_summary_val=function()return {}end,is_human=function()return true end,
  cosmetics_hp_bonus=function(uid)assert(uid=="alice");calls=calls+1;return bonus end,
  aura_apply_to_pve_player_stats=function(actor,aura)actor.max_hp=actor.max_hp*2;actor.hp=actor.max_hp end})
assert(Stats.compute_character_display_stats("alice").hp==1005 and calls==1)
bonus=.045;assert(Stats.compute_character_display_stats("alice").hp==1045 and calls==2)
local actor={};Stats.apply_player_combat_stats_from_sheet(actor,"alice",{aura={}});assert(actor.max_hp==2090 and calls==3)
bonus=0;assert(Stats.compute_character_display_stats("alice").hp==1000)
bonus=1;assert(not pcall(Stats.compute_character_display_stats,"alice"))
print("cosmetics HP stats: 5 passed")
