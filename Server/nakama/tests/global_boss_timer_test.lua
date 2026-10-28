package.path='Server/nakama/?.lua;'..package.path
local clock=100000;local nk={json_encode=function(x)return x end,json_decode=function(x)return x end,time=function()return clock end}
package.loaded.nakama=nk
local B=require('modules.duel_global_boss_battle');local CFG=require('modules.duel_match3_config')
local function copy(v)if type(v)~='table'then return v end;local r={};for k,x in pairs(v)do r[k]=copy(x)end;return r end
local seen,choose_calls,buffs,rejects={},0,0,{}
local event={id='week-test',hp=550000,max_hp=550000,ends_at=os.time()+3600}
local function state()
 return{started=true,ended=false,owner_user_id='hero',bot_user_id='zz-bot-global',players_sorted={'hero','zz-bot-global'},
  presences={hero={}},mode='pve',stats={hero={hp=100000,max_hp=100000,initial_hp=100000,mana=100},
  ['zz-bot-global']={hp=550000,max_hp=550000,base_damage=3500,base_crit=.05,mana=100}},
  global_boss={event_id=event.id,attempt_id='a',boss_own_turn=0,next_scream_turn=1,player_turns=0},
  active_user_id='hero',_boss_sequence=0,_boss_config={think_seconds=2,max_turns=30},turn_deadline_paused=true,
  turn_pause_started_tick=0,turn_deadline_tick=0,board={1,2,3},spawn_queues={},cheat_rows={}}
end
local next_action={actionType=1}
B.configure({CFG=CFG,now_ms=function()return clock end,api={get_event=function()return event end,get_player=function()return{}end,
 commit=function(uid,id,seq,damage,snapshot,player,reason)return{event=event,attempt={sequence=seq,snapshot=copy(snapshot),status=reason and'finished'or'bound',end_reason=reason,actual_damage=0}}end},
 filter_messages=function(s,m)return m end,stale=function()return false end,parse=function(x)return x end,
 validate=function()return true end,action_cost=function()return 1 end,
 choose=function()choose_calls=choose_calls+1;return copy(next_action)end,
 resolve=function(s,a)return true,nil,a.extra==true,a.keep==true,{}end,
 tick_buffs=function()buffs=buffs+1 end,tick_cooldowns=function()end,reject=function(_,_,why)rejects[#rejects+1]=why end,
 emit=function(_,s)s._terminal_delivered=true end,
 broadcast=function(_,s,a,extra,steps,tick,actor)seen[#seen+1]={paused=s.turn_deadline_paused,deadline=s.turn_deadline_unix_ms,
  actor=actor,adapt=s.global_boss.adaptation_triggered,cry=s.global_boss.scream_triggered,n=s.global_boss.boss_own_turn}end})
local function msg(op,data)return{{sender={user_id='hero'},op_code=op,data=data}}end
local count=0;local function check(v,n)assert(v,n);count=count+1;print('PASS '..n)end
local s=state();B.loop({},s,1,msg(17))
check(#seen==1 and not seen[1].paused and seen[1].deadline==130000,'OP17 immediately broadcasts one running30second deadline')
clock=100250;B.loop({},s,2,msg(CFG.OP_SNAPSHOT_REQUEST))
check(seen[#seen].deadline==130000,'snapshot/header updates keep one absolute deadline at subsecond resolution')
clock=101000;B.loop({},s,3,msg(17))
check(s.turn_deadline_unix_ms==130000,'duplicate readiness acknowledgement cannot restart a running clock')
B.loop({},s,4,msg(CFG.OP_ACTION_REQUEST,{actionType=5,keep=true}))
check(s.turn_deadline_unix_ms==130000 and not s.turn_deadline_paused,'free shield keeps existing timer instead of starting30seconds again')
B.loop({},s,5,msg(CFG.OP_ACTION_REQUEST,{actionType=6,keep=true}))
check(s.turn_deadline_unix_ms==130000 and not s.turn_deadline_paused,'free fury also keeps existing timer')
B.loop({},s,6,msg(CFG.OP_ACTION_REQUEST,{actionType=1,extra=true}))
check(s.turn_deadline_paused and s.turn_deadline_unix_ms==0 and s.active_user_id=='hero'and buffs==0,
 'extra turn pauses for animation and preserves ongoing buffs')
clock=101500;B.loop({},s,7,msg(17))
check(s.turn_deadline_unix_ms==131500,'extra turn receives a fresh clock only when animation is acknowledged')
B.loop({},s,8,msg(CFG.OP_ACTION_REQUEST,{actionType=1}))
check(s.active_user_id==s.bot_user_id and s.turn_deadline_paused,'actor switch pauses before the boss animation-ready phase')
clock=102000;B.loop({},s,9,msg(17))
check(s.turn_deadline_unix_ms==132000 and seen[#seen].actor=='','boss readiness also publishes its running timer without an action replay')
next_action={actionType=6,keep=true};local ready=s.bot_turn_ready_tick;B.loop({},s,ready,{})
check(s.global_boss.boss_own_turn==1 and seen[#seen].adapt and not seen[#seen].cry,'first boss decision signals only adaptation before any damage action')
local fixed=s.turn_deadline_unix_ms;B.loop({},s,s.bot_turn_ready_tick,{})
check(s.global_boss.boss_own_turn==1 and not seen[#seen].adapt and seen[#seen].cry and s.turn_deadline_unix_ms==fixed,
 'scream is its own free action after adaptation and keeps the same deadline')
B.loop({},s,s.bot_turn_ready_tick,{})
check(not seen[#seen].adapt and not seen[#seen].cry and s.turn_deadline_unix_ms==fixed,'ordinary free fury follows scream without replaying either boss ability')
next_action={actionType=5,keep=true};B.loop({},s,s.bot_turn_ready_tick,{})
check(s.global_boss.boss_own_turn==1 and not seen[#seen].adapt and not seen[#seen].cry and s.turn_deadline_unix_ms==fixed,
 'second free ability does not repeat passive/scream or reset the boss timer')
next_action={actionType=1,extra=true};B.loop({},s,s.bot_turn_ready_tick,{})
check(s.turn_deadline_paused and s.active_user_id==s.bot_user_id,'boss extra turn starts a distinct paused phase')
clock=103000;B.loop({},s,100,msg(17));next_action={actionType=1};B.loop({},s,s.bot_turn_ready_tick,{})
check(s.global_boss.boss_own_turn==2 and seen[#seen].adapt and not seen[#seen].cry,'bonus boss turn signals adaptation again without bypassing scream cooldown')
B.loop({},s,s.bot_turn_ready_tick,{})
check(s.active_user_id=='hero'and not seen[#seen].adapt and not seen[#seen].cry,'ordinary boss move follows adaptation on a scream-cooldown turn')
local fallback=state();clock=200000;B.loop({},fallback,8*CFG.TICK_RATE,{})
check(not fallback.turn_deadline_paused and seen[#seen].deadline==230000,'missingOP17 fallback broadcasts a running clock instead of leaving a hidden timer')
clock=230000;B.loop({},fallback,8*CFG.TICK_RATE+1,{})
check(fallback.active_user_id==fallback.bot_user_id and fallback.turn_deadline_paused,'authoritative expiry follows wall time even if server tick processing is delayed')
local live=state();live.turn_deadline_paused=false;live.turn_deadline_unix_ms=260000;clock=235000
B.join({},live,1)
check(live.turn_deadline_unix_ms==260000 and not live.turn_deadline_paused,'live reconnect snapshot preserves remaining time')
local late=state();late.active_user_id=late.bot_user_id;late.turn_deadline_paused=false;late.turn_deadline_unix_ms=clock-1;late.bot_turn_ready_tick=0
local before=choose_calls;B.loop({},late,500,{})
check(choose_calls==before and late.active_user_id=='hero','expired boss clock passes the turn before executing a late ordinaryAI move')
check(#rejects==0,'normal timer readiness and valid free abilities have no rejection')
for _,a in ipairs({{actionType=1,extra=true},{actionType=4,keep=true},{actionType=5,keep=true},{actionType=6,keep=true}})do
 local h=state();h.turn_deadline_paused=false;h.turn_deadline_unix_ms=clock
 local rejected=#rejects;B.loop({},h,501,msg(CFG.OP_ACTION_REQUEST,a))
 check(#rejects==rejected+1 and rejects[#rejects]=='turn_expired'and h.active_user_id==h.bot_user_id
  and h.turn_deadline_paused and h._boss_sequence==1 and h.global_boss.player_turns==1 and h.stats.hero.mana==100,
  'expired boss player action'..a.actionType..' cannot spend mana renew the deadline or gain an extra turn')
end
local queued=state();queued.turn_deadline_paused=false;queued.turn_deadline_unix_ms=clock-1000
local batch={};for _,a in ipairs({{actionType=5,keep=true},{actionType=6,keep=true},{actionType=1,extra=true}})do batch[#batch+1]=msg(CFG.OP_ACTION_REQUEST,a)[1]end
local rejected=#rejects;B.loop({},queued,501,batch)
check(#rejects==rejected+3 and queued._boss_sequence==1 and queued.global_boss.player_turns==1 and queued.stats.hero.mana==100,
 'several delayed expired requests settle exactly one timeout without ability costs')
local timely=state();timely.turn_deadline_paused=false;timely.turn_deadline_unix_ms=clock+1
B.loop({},timely,1,msg(CFG.OP_ACTION_REQUEST,{actionType=5,keep=true}))
check(timely.active_user_id=='hero'and not timely.turn_deadline_paused and timely.stats.hero.mana==99 and timely.turn_deadline_unix_ms==clock+1,
 'valid free ability immediately before expiry keeps the original deadline')
local paused=state();B.loop({},paused,1,msg(CFG.OP_ACTION_REQUEST,{actionType=1,extra=true}))
check(paused.turn_deadline_paused and paused._boss_sequence==0 and paused.stats.hero.mana==100,
 'paused boss phase cannot accept a move before readiness')
local ready=state();B.loop({},ready,1,{msg(17)[1],msg(CFG.OP_ACTION_REQUEST,{actionType=5,keep=true})[1]})
check(not ready.turn_deadline_paused and ready.active_user_id=='hero'and ready.stats.hero.mana==99 and ready.turn_deadline_unix_ms==clock+30000,
 'readiness and a valid free ability in one batch retain the newly started clock')
-- Inspect actual core serialization, not a parallel client-shaped mock.
local E=dofile('Server/nakama/tests/global_boss_engine_fixture.lua')
E.Runtime.Pvp={cache_player_levels=function()end}
package.loaded.nakama.time=function()return 1234567 end
local f=assert(io.open('Server/nakama/modules/duel_match3.lua'));local src=f:read('*a');f:close()
-- Engine fixture exports the exact shipping serializer as well as resolution.
local q=E.init_state({max_hp=1000},{max_hp=550000});q._boss_sequence=9;q.global_boss.adaptation_triggered=true;q.global_boss.scream_triggered=true;q.global_boss.scream_damage=200
q.turn_deadline_unix_ms=1264567;q.turn_deadline_paused=false
local quiet=E.make_sync(q,nil,false,nil,nil,1,'')
check(not quiet.bossAdaptationTriggered and not quiet.bossScreamTriggered and quiet.bossScreamDamage==0,
 'initial snapshot/reconnect never replays saved adaptation or scream')
local visual=E.make_sync(q,{actionType=1},false,nil,nil,1,q.bot_user_id)
check(visual.bossAdaptationTriggered and visual.bossScreamTriggered and visual.bossActionSequence==9,
 'committed boss action carries sequence-bound adaptation and scream flags')
check(visual.serverNowUnixMs==1234567 and visual.turnEndsAtUnixMs==1264567 and visual.turnDurationSeconds==30,
 'actual core emits precise milliseconds and the unchanged absolute boss deadline')
print('BOSS TIMER '..count..' CHECKS PASSED')
