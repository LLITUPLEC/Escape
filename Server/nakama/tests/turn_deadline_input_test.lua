-- Actual incoming match_loop and validation: late actions cannot evade timeout
-- with extra turns or free abilities in any ordinary server mode.
local E=dofile('Server/nakama/tests/global_boss_engine_fixture.lua')
local f=assert(io.open('Server/nakama/modules/duel_match3.lua'));local source=f:read('*a');f:close()
local start=assert(source:find('local function match_loop',1,true))
local finish=assert(source:find('local function match_terminate',start,true))
local count=0
local function check(v,n)assert(v,n);count=count+1;print('PASS '..n)end
for _,mode in ipairs({'classic','pro','race','mine','tournament'})do
 local resolved,rejects,syncs,last_turns=0,{},0,0
 local R={Rounds=require('modules.duel_match3_rounds'),Presence={expired=function()return{}end,filter_messages=function(_,m)return m end},
  RateLimit={filter_match_messages=function(_,m)return m end}}
 local env=setmetatable({Runtime=R,CFG=E.CFG,nk={time=function()return 100000 end},
  Guard={is_epoch_stale_for_match=function()return false end},Ach={clear_five_plus_streak=function()end},
  validate_action_basic=E.validate,parse_action=function(a)return a end,send_reject=function(_,_,why)rejects[#rejects+1]=why end,
  other_player_id=function(s,u)return u=='hero'and'peer'or'hero'end,
  tick_buffs_end_turn=function()end,tick_cooldowns=function()end,
  try_finish_race_on_last_turn_forfeit=function()last_turns=last_turns+1;return false end,
  turn_seconds_for_state=function()return 30 end,broadcast_sync=function()syncs=syncs+1 end,
  resolve_action=function()resolved=resolved+1;return true,nil,true,true,{}end,
  finish_turn_and_broadcast=function()end},{__index=_G})
 local chunk=assert(loadstring(source:sub(start,finish-1)..'\nreturn match_loop'));setfenv(chunk,env);local loop=chunk()
 local function state()
  local s=E.init_state({max_hp=1000,mana=100},{max_hp=1000})
  s.global_boss=nil;s.players_sorted={'hero','peer'};s.stats.peer=s.stats[s.bot_user_id];s.stats[s.bot_user_id]=nil
  s.mode=(mode=='mine'or mode=='tournament')and'pve'or'pvp';s.owner_user_id='hero';s.bot_user_id='peer'
  s.arena_mirror=mode=='tournament'and{}or nil;s.pvp_pro=mode=='pro';s.pvp_race=mode=='race'
  s.presences={hero={user_id='hero'},peer={user_id='peer'}};s.active_user_id='hero';s.turn_deadline_paused=false;s.turn_deadline_tick=100
  return s
 end
 local function msg(a)return{sender={user_id='hero'},op_code=E.CFG.OP_ACTION_REQUEST,data=a}end
 for _,a in ipairs({{actionType=1,fromX=0,fromY=0,toX=1,toY=0},{actionType=4},{actionType=5},{actionType=6}})do
  local s=state();local mana=s.stats.hero.mana;local before=#rejects
  loop({}, {},100,s,{msg(a)})
  check(#rejects==before+1 and rejects[#rejects]=='turn_expired'and resolved==0 and s.stats.hero.mana==mana
   and s.active_user_id=='peer'and s.turn_deadline_paused,
   mode..' expired input'..a.actionType..' is rejected before cost and follows the existing timeout path')
 end
 local s=state();loop({}, {},99,s,{msg({actionType=1,fromX=0,fromY=0,toX=1,toY=0})})
 check(resolved==1 and s.active_user_id=='hero',mode..' valid input before the boundary still resolves')
 s=state();s.turn_deadline_paused=true;s.turn_pause_started_tick=100
 loop({}, {},100,s,{{sender={user_id='hero'},op_code=17},msg({actionType=1,fromX=0,fromY=0,toX=1,toY=0})})
 check(resolved==2 and not s.turn_deadline_paused and s.turn_deadline_tick==100+30*E.CFG.TICK_RATE,
  mode..' OP17 starts a valid fresh deadline before a same-batch request')
end
print('TURN DEADLINE INPUT '..count..' CHECKS PASSED')
