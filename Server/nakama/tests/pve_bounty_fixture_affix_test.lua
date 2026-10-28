-- A natural-victory fixture must choose an affix compatible with its real moves.
-- Execute the production match-effects function, not a copied combat formula.
package.path="Server/nakama/?.lua;"..package.path
local CFG=require("modules.duel_match3_config")
local f=assert(io.open("Server/nakama/modules/duel_match3.lua"));local source=f:read("*a");f:close()
local first=assert(source:find("local function apply_match_effects",1,true))
local last=assert(source:find("local function other_player_id",first,true))
local env=setmetatable({CFG=CFG,
  current_affix_id=function(s)return s.pve_run.affix end,
  is_pvp_race=function()return false end,
  in_active_server=function(x,y)return x>=0 and x<6 and y>=2 and y<8 end,
},{__index=_G})
local chunk=assert(loadstring(source:sub(first,last-1).."\nreturn apply_match_effects"));setfenv(chunk,env)
local apply=chunk()
local matches={{type=4,count=3,cells={{x=0,y=2},{x=1,y=2},{x=2,y=2}}}}
local function run(affix)
  local state={pve_run={affix=affix},stats={a={base_damage=619},b={hp=150}},_action_damage_flat=0}
  apply(state,"a","b",matches,false);return state
end
local blocked=run("energy_block")
assert(blocked._action_damage_flat==0 and blocked.stats.a.base_damage==634 and blocked.stats.b.hp==150)
print("PASS actual energy_block converts a real skull match to stat growth without a damage event")
local normal=run("mana_vampire")
assert(normal._action_damage_flat==15 and normal.stats.a.base_damage==619)
print("PASS canonical mana_vampire permits the bounty fixture's real skull-match damage")
