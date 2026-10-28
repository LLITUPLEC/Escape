-- Actual core set/merge/encoder functions + character stats. No Nakama writes/network.
package.path = "Server/nakama/?.lua;" .. package.path
package.loaded["modules.duel_match3_achievement_catalog"] = { all_chains=function() return {} end }
local Stats = require("modules.duel_match3_character_stats")
local function copy(v) if type(v)~="table" then return v end local r={} for k,x in pairs(v)do r[k]=copy(x)end return r end
local slots={"Helmet","Shoulders","Chest","Gloves","Legs","Feet","WeaponLeft","WeaponRight"}
local defs,full={}, {equipment={}}
for i,slot in ipairs(slots) do
  local id="normal_"..slot
  defs[id]={kind="equipment",slot=slot,tier=1,quality="normal",hp=i==1 and 11 or 9,
    damage=i==1 and 2 or 1,armor=i==1 and 2 or 1,healing=i==4 and 3 or 0,crit_chance=i==4 and .02 or 0}
  full.equipment[i]=id
end
local file=assert(io.open("Server/nakama/modules/duel_match3.lua"));local source=file:read("*a");file:close()
local a=assert(source:find("local SET_BONUS_FULL_EQUIP_STATS",1,true))
local b=assert(source:find("local function character_sheet_payload_arrays",a,true))
local env=setmetatable({get_merged_item_defs=function()return defs end,
 item_def_is_equipment=function(d)return d.kind=="equipment"end,
 CFG={clamp_int=function(n,lo,hi)return math.max(lo,math.min(hi,n))end}},{__index=_G})
local chunk=assert(loadstring(source:sub(a,b-1).."\nreturn equipment_full_set_stat_multiplier,sum_equipment_bonuses,merge_stats_with_equipment"));setfenv(chunk,env)
local multiplier,sum,merge=chunk()
local passed=0
local function check(label,fn) fn();passed=passed+1;print("PASS "..label) end
local stored=copy(full)
local reads=0
Stats.configure({CFG={MAX_HP=150,PVE_MAX_LEVEL=12,character_stats_base_for_level=function()return{hp=100,damage=10,armor=4,healing=3,crit_chance=.05}end},
 read_pve_progress=function()return{level=1}end,read_character_sheet=function()reads=reads+1;return copy(stored)end,
 storage_read_match3_summary_val=function()return{}end,sum_equipment_bonuses=sum,merge_stats_with_equipment=merge,
 is_human=function()return true end,cosmetics_hp_bonus=function()return 0 end})
check("each equip1..7 has no set bonus",function()
 local sheet={equipment={}}
 for i=1,7 do sheet.equipment[i]=full.equipment[i];assert(multiplier(sheet)==1)end
 assert(sum(sheet).hp==65)
end)
check("eighth compatible item activates25% from equipment only",function()
 assert(multiplier(full)==1.25)
 local bonus=sum(full);assert(bonus.hp==92.5 and bonus.damage==11.25 and bonus.armor==11.25 and bonus.healing==3.75)
 assert(math.abs(bonus.crit_chance-.02)<1e-9,"critical chance must not be multiplied")
end)
check("display integer values equal actual combat actor",function()
 local displayed=Stats.compute_character_display_stats("alice")
 local actor={};Stats.apply_player_combat_stats_from_sheet(actor,"alice")
 assert(displayed.hp==193 and displayed.damage==21 and displayed.armor==15 and displayed.healing==7)
 assert(displayed.hp==actor.max_hp and displayed.damage==actor.base_damage and displayed.armor==actor.base_armor and displayed.healing==actor.base_heal)
 assert(math.abs(displayed.crit_chance-.07)<1e-9 and actor.base_crit==displayed.crit_chance)
end)
check("unequip immediately removes set and preserves base stats",function()
 local seven=copy(full);seven.equipment[8]=""
 local stats=Stats.compute_character_display_stats("alice",seven)
 assert(stats.hp==165 and stats.damage==18 and stats.armor==12 and stats.healing==6)
end)
check("mixed quality or tier never activates",function()
 local id=full.equipment[8];local original=copy(defs[id])
 defs[id].quality="rare";assert(multiplier(full)==1)
 defs[id].quality="normal";defs[id].tier=2;assert(multiplier(full)==1)
 defs[id]=original
end)
check("duplicate and wrong-slot gear do not activate or contribute in wrong slot",function()
 local bad=copy(full);bad.equipment[8]=bad.equipment[1]
 assert(multiplier(bad)==1 and sum(bad).hp==65)
 local swapped=copy(full);swapped.equipment[1],swapped.equipment[2]=swapped.equipment[2],swapped.equipment[1]
 assert(multiplier(swapped)==1 and sum(swapped).hp==54)
 assert(multiplier(nil)==1 and sum(nil).hp==0)
end)
check("actual character encoder calculates stats from accepted sheet snapshot",function()
 stored=copy(full);stored.equipment[8]="" -- emulate a stale second storage read after the equip commit
 a=assert(source:find("local function encode_character_ok_response",1,true));b=assert(source:find("local function sheet_has_learned",a,true))
 local encoderEnv=setmetatable({CharStats=Stats,nk={json_encode=function(v)return v end},
  Runtime={Inventory={capacity=function()return 25 end,SLOT_PRICE_MATTER=3,pending_payload=function()return{}end},
    ConsumableModule={status=function()return{}end},readProgressSnapshot=function()return{level=1}end},
  ensure_sheet_inventory_counts=function()end,character_sheet_payload_arrays=function(s)return s.equipment,{},{}end,
  workshop_payload_arrays=function()return{},{}end,learned_recipes_payload=function()return{}end,
  build_progression_payload=function(p)return p end},{__index=_G})
 local compile=assert(loadstring(source:sub(a,b-1).."\nreturn encode_character_ok_response"));setfenv(compile,encoderEnv)
 local encode=compile();local before=reads;local response=encode(copy(full),{},"alice")
 assert(response.ok and response.stats.hp==193 and response.stats.damage==21)
 assert(response.equipment_def_ids[8]==full.equipment[8] and reads==before,"accepted sheet must not be reread")
 assert(encode(copy(full),{},"alice").stats.hp==193,"repeated responses must not reapply the multiplier")
end)
check("read-only stats fail closed for every missing snapshot dependency",function()
 for _,missing in ipairs({"read_progress_snapshot","read_stats_snapshot","cosmetics_hp_bonus_read_only"})do
  local mutations=0
  local function mutate() mutations=mutations+1;return {} end
  local config={CFG={MAX_HP=100},is_human=function()return true end,
   read_character_sheet=function()return{}end,ensure_character_sheet_initialized=mutate,
   read_pve_progress=mutate,storage_read_match3_summary_val=mutate,cosmetics_hp_bonus=mutate,
   read_progress_snapshot=function()return{}end,read_stats_snapshot=function()return{}end,
   cosmetics_hp_bonus_read_only=function()return 0 end}
  config[missing]=nil;Stats.configure(config)
  assert(not pcall(Stats.apply_player_combat_stats_from_sheet,{},"alice",{read_only=true}),missing.." should fail closed")
  assert(mutations==0,"missing readonly dependency fell back to account-mutating path")
 end
end)
print("Equipment set stats: "..passed.." actual-logic cases passed")
