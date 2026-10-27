-- Execute the actual core functions/whole-heal block, not a duplicate formula.
package.path="Server/nakama/?.lua;"..package.path
local Effects=require("modules.duel_consumable_effects")
local CFG=require("modules.duel_match3_config")
local f=assert(io.open("Server/nakama/modules/duel_match3.lua"));local source=f:read("*a");f:close()
local a=assert(source:find("local function get_shield_stacks",1,true));local b=assert(source:find("local function apply_shield_stack",a,true))
local h=assert(source:find("  -- Одно лечение за ход",b,true));local e=assert(source:find("  -- Один расчёт урона за ход",h,true))
local random_value=.99
local env=setmetatable({CFG=CFG,Runtime={ConsumableEffects=Effects},is_pvp_race=function(s)return s.pvp_race==true end,
 math=setmetatable({random=function()return random_value end},{__index=math})},{__index=_G})
local chunk=assert(loadstring(source:sub(a,b-1).."\nlocal function heal_turn(state,actor_id)\n"..source:sub(h,e-1)..[[
 end
 return {armor=get_armor,outgoing=roll_outgoing_damage,damage=deal_damage,heal=heal_turn}
]]));setfenv(chunk,env);local Core=chunk()
local function actor(id)
 local t={hp=200,max_hp=1000,base_damage=40,base_armor=20,base_heal=10,base_crit=.15,
 shield_t1=2,shield_t2=0,shield_t3=1};Effects.apply(t,{item_id=id});return t
end
local shield=2*CFG.SHIELD_ARMOR_PER_STACK;local state={mode="pvp",pvp_pro=true};local count=0
local function check(label,fn)fn();count=count+1;print("PASS actual core "..label)end
check("Assault modifies total armor including shield stacks once",function()
 assert(Core.armor(actor("elixir_assault"))==math.floor((20+shield)*.85+.5))
end)
check("Stone modifies total armor including shield stacks once",function()
 assert(Core.armor(actor("elixir_stone"))==math.floor((20+shield)*1.2+.5))
end)
check("Assault final damage contains ability, equipment and fury exactly once",function()
 local t=actor("elixir_assault");t.fury_active=true;t.fury_bomb_bonus=10
 local damage,crit=Core.outgoing(state,{},t,15);assert(damage==78 and not crit)
end)
check("Stone outgoing damage reduction is authoritative",function()
 local damage,crit=Core.outgoing(state,{},actor("elixir_stone"),10);assert(damage==45 and not crit)
end)
check("Gambit adds ten percentage points to real random threshold",function()
 random_value=.20;local damage,crit=Core.outgoing(state,{},actor("elixir_gambit"),10);assert(damage==100 and crit)
 local normal,normal_crit=Core.outgoing(state,{},actor(nil),10);assert(normal==50 and not normal_crit);random_value=.99
end)
check("Crit, affix and elixir factors compose once before target armor",function()
 random_value=.01;local t=actor("elixir_assault");local target=actor("elixir_stone");target.hp=1000
 local crit=Core.damage({mode="pve",pve_run={affix="bare_current"}}, {},t,target,10)
 assert(crit and target.hp==1000-(240-Core.armor(target)));random_value=.99
end)
check("Real HP damage subtracts the target's elixir-adjusted armor",function()
 local target=actor("elixir_assault");local before=target.hp
 Core.damage(state,{},actor("elixir_stone"),target,60)
 assert(target.hp==before-math.max(0,90-Core.armor(target)))
end)
check("Whole healing reduction covers all capsules plus base and shield once",function()
 local t=actor("elixir_gambit");local s={mode="pve",stats={u=t},_action_ankh_count=7}
 Core.heal(s,"u");assert(t.hp==200+math.floor((7*CFG.ANKH_HEAL+10+2*CFG.SHIELD_HEAL_PER_STACK)*.5))
 assert(s._action_ankh_count==0);local before=t.hp;Core.heal(s,"u");assert(t.hp==before)
end)
check("Whole healing respects maximum HP and Race never heals",function()
 local t=actor("elixir_gambit");t.hp=999;local s={mode="pvp",pvp_pro=true,stats={u=t},_action_ankh_count=100}
 Core.heal(s,"u");assert(t.hp==1000);s.pvp_race=true;t.hp=500;s._action_ankh_count=100;Core.heal(s,"u");assert(t.hp==500)
end)
print("Consumable actual core combat: "..count.." deterministic wiring/HP/healing tests passed")
