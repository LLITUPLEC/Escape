-- Shipping round helper + engine completion/serializer + actual timeout callback.
-- Only transport is stubbed; no account, currency or statistics persistence.
local E=dofile('Server/nakama/tests/global_boss_engine_fixture.lua')
local R=E.Runtime.Rounds
E.Runtime.Pvp={cache_player_levels=function()end}
local n=0;local function check(v,name)assert(v,name);n=n+1;print('PASS '..name)end
local snapshots={};local dispatcher={broadcast_message=function(_,payload)snapshots[#snapshots+1]=payload end}
local function state(mode,first)
 local s=E.init_state({max_hp=100000},{max_hp=100000});s.global_boss=nil;s.mode=mode=='mine'and'pve'or'pvp'
 s.pvp_pro=mode=='pro';s.pvp_race=mode=='race';s.race_goal_mana=10000;s.race_first_user_id=first
 s.turn_deadline_paused=true;s.presences={hero={user_id='hero'}};s.player_levels={}
 R.start(s,first);return s
end
for _,mode in ipairs({'classic','pro','race','mine','tournament'})do
 for _,first in ipairs({'hero','zz-bot-global'})do
  local s=state(mode,first);local second=first=='hero'and s.bot_user_id or'hero'
  local function sync()return E.make_sync(s,nil,false,nil,nil,1,'')end
  check(sync().roundNumber==1,mode..' '..first..' starts with authoritative round1')
  for _,action in ipairs({{kind=1,extra=true},{kind=4,keep=true},{kind=5,keep=true},{kind=6,keep=true}})do
   E.finish_turn(dispatcher,s,{actionType=action.kind},action.extra==true,action.keep==true,1,E.CFG.TICK_RATE,{})
   check(s.active_user_id==first and sync().roundNumber==1,mode..' extra/free action'..action.kind..' keeps round1')
  end
  E.finish_turn(dispatcher,s,{actionType=1},false,false,2,E.CFG.TICK_RATE,{})
  check(s.active_user_id==second and sync().roundNumber==1,mode..' response belongs to round1')
  local snapshot=sync();local replay=sync()
  check(snapshot.roundNumber==1 and replay.roundNumber==1 and s.round_number==1,mode..' repeated/reconnect snapshot cannot increment round')
  E.finish_turn(dispatcher,s,{actionType=1},true,false,3,E.CFG.TICK_RATE,{})
  check(s.active_user_id==second and sync().roundNumber==1,mode..' opponent extra action remains round1')
  E.finish_turn(dispatcher,s,{actionType=1},false,false,4,E.CFG.TICK_RATE,{})
  check(s.active_user_id==first and sync().roundNumber==2,mode..' only return to first actor starts round2')
  E.finish_turn(dispatcher,s,{actionType=1},false,false,5,E.CFG.TICK_RATE,{})
  check(s.active_user_id==second and sync().roundNumber==2,mode..' response stays in round2')
 end
end

-- Timeout uses the actual shipping match_loop, including delayed skipped turns.
local file=assert(io.open('Server/nakama/modules/duel_match3.lua'));local source=file:read('*a');file:close()
local a=assert(source:find('local function match_loop',1,true));local b=assert(source:find('local function match_terminate',a,true))
local env=setmetatable({Runtime={Rounds=R,Presence={expired=function()return{}end,filter_messages=function(_,m)return m end},RateLimit={filter_match_messages=function(_,m)return m end}},
 CFG=E.CFG,nk={time=function()return 100000 end},Guard={is_epoch_stale_for_match=function()return false end},
 Ach={clear_five_plus_streak=function()end},other_player_id=function(s,u)return u=='hero'and s.bot_user_id or'hero'end,
 tick_buffs_end_turn=function()end,tick_cooldowns=function()end,try_finish_race_on_last_turn_forfeit=function()return false end,
 turn_seconds_for_state=function()return 30 end,broadcast_sync=function()end},{__index=_G})
local chunk=assert(loadstring(source:sub(a,b-1)..'\nreturn match_loop'));setfenv(chunk,env);local loop=chunk()
local s=state('classic','hero');s.turn_deadline_paused=false;s.turn_deadline_tick=1;s.presences[s.bot_user_id]={user_id=s.bot_user_id}
loop({},dispatcher,1,s,{})
check(s.active_user_id==s.bot_user_id and R.number(s)==1,'first timeout passes control but keeps round1')
s.turn_deadline_paused=false;s.turn_deadline_tick=2;loop({},dispatcher,2,s,{})
check(s.active_user_id=='hero'and R.number(s)==2,'response timeout starts round2 only on return')

-- Race's forced last response must not count as an additional full round.
s=state('race','hero');s.stats.hero.mana=s.race_goal_mana
E.finish_turn(dispatcher,s,{actionType=1},false,false,1,E.CFG.TICK_RATE,{})
check(s.race_last_turn_user_id==s.bot_user_id and s.active_user_id==s.bot_user_id and R.number(s)==1,
 'Race threshold passes to last response inside the same round')

-- Durable boss snapshots retain both the first actor and completed control cycles.
local B=require('modules.duel_global_boss_battle')
s=state('mine','hero');R.set_active(s,s.bot_user_id);R.set_active(s,'hero');local saved=B.snapshot(s)
check(saved.round_number==2 and saved.round_first_user_id=='hero','boss durable snapshot includes round2 and first actor')
local restored={active_user_id='hero',round_number=saved.round_number,round_first_user_id=saved.round_first_user_id};R.restore(restored,'hero')
R.set_active(restored,'zz-bot-global');check(R.number(restored)==2,'restored boss response retains saved round2')
R.set_active(restored,'hero');check(R.number(restored)==3,'restored boss advances once after complete next response')
local legacy={active_user_id='hero'};R.restore(legacy,'hero');R.set_active(legacy,'zz-bot-global');R.set_active(legacy,'hero')
check(R.number(legacy)==0,'legacy boss history remains unknown instead of inventing an action-based round')

-- The real boss actor must persist the counter before publishing, including
-- an unavailable commit, retry, recreated actor, and a live reconnect.
local clock,fail_commit,attempt_snapshot=100000,true,nil
local event={id='round-week',hp=100000,max_hp=100000,ends_at=os.time()+3600,
 config={think_seconds=2,max_turns=30,base_damage=0,base_armor=0,base_crit=0}}
local function copy(v)if type(v)~='table'then return v end;local t={}for k,x in pairs(v)do t[k]=copy(x)end return t end
B.configure({CFG=E.CFG,now_ms=function()return clock end,
 api={get_event=function()return event end,get_player=function()return{}end,
  get_attempt=function()return{sequence=1,snapshot=copy(attempt_snapshot),actual_damage=0}end,
  commit=function(_,_,seq,damage,snapshot,_,reason)
   if fail_commit then return nil,'storage_unavailable' end
   attempt_snapshot=copy(snapshot)
   return{event=event,attempt={sequence=seq,snapshot=copy(snapshot),status='bound',actual_damage=0}}
  end},
 filter_messages=function(_,messages)return messages end,stale=function()return false end,
 parse=function(data)return data end,validate=function()return true end,action_cost=function()return 1 end,
 choose=function()return{actionType=1}end,resolve=function(_,action)return true,nil,action.extra==true,action.keep==true,{}end,
 tick_buffs=E.tick_buffs,tick_cooldowns=E.tick_cooldowns,reject=function()error('unexpected rejection')end,
 broadcast=function(_,s,action,extra,steps,tick,actor)snapshots[#snapshots+1]=E.make_sync(s,action,extra,steps,nil,tick,actor)end,
 emit=function()error('unexpected terminal')end})
local boss=E.init_state({max_hp=100000},{max_hp=100000});boss.global_boss.event_id=event.id;boss.global_boss.attempt_id='round-attempt'
boss._boss_sequence=0;boss._boss_config=event.config;boss.presences={hero={user_id='hero'}}
R.start(boss,'hero');R.set_active(boss,boss.bot_user_id);boss.turn_deadline_paused=false;boss.turn_deadline_unix_ms=clock+30000;boss.bot_turn_ready_tick=0
local before_sync=#snapshots;B.loop(dispatcher,boss,1,{})
check(boss._boss_pending and boss.round_number==2 and boss._boss_pending.snapshot.round_number==2 and #snapshots==before_sync,
 'boss response freezes round2 in pending durable commit without premature broadcast')
B.loop(dispatcher,boss,1+E.CFG.TICK_RATE,{})
check(boss.round_number==2 and boss._boss_pending.snapshot.round_number==2 and #snapshots==before_sync,
 'repeated unavailable commit cannot count boss response twice')
fail_commit=false;B.loop(dispatcher,boss,1+2*E.CFG.TICK_RATE,{})
check(not boss._boss_pending and boss.round_number==2 and attempt_snapshot.round_number==2 and snapshots[#snapshots].roundNumber==2,
 'successful retry publishes the committed round2 exactly once')
local function recovered()
 return{started=false,owner_user_id='hero',bot_user_id='zz-bot-global',mode='pve',presences={hero={user_id='hero'}},
  stats={},global_boss={event_id=event.id,attempt_id='round-attempt'}}
end
local recreated=recovered();B.join(dispatcher,recreated,30)
check(recreated.round_number==2 and recreated.round_first_user_id=='hero'and snapshots[#snapshots].roundNumber==2,
 'recreated boss actor restores exact durable round on join')
B.join(dispatcher,recreated,31)
check(recreated.round_number==2 and snapshots[#snapshots].roundNumber==2,'live boss reconnect does not reinitialize round')
local function message(op,data)return{{sender={user_id='hero'},op_code=op,data=data}}end
B.loop(dispatcher,recreated,32,message(17))
B.loop(dispatcher,recreated,33,message(E.CFG.OP_ACTION_REQUEST,{actionType=5,keep=true}))
B.loop(dispatcher,recreated,34,message(E.CFG.OP_ACTION_REQUEST,{actionType=1,extra=true}))
check(recreated.round_number==2 and recreated.active_user_id=='hero','real restored boss free ability and extra turn keep round2')
B.loop(dispatcher,recreated,35,message(17));B.loop(dispatcher,recreated,36,message(E.CFG.OP_ACTION_REQUEST,{actionType=1}))
check(recreated.round_number==2 and recreated.active_user_id==recreated.bot_user_id,'real restored boss response starts within round2')
B.loop(dispatcher,recreated,37,message(17));B.loop(dispatcher,recreated,recreated.bot_turn_ready_tick,{})
check(recreated.round_number==3 and recreated.active_user_id=='hero'and snapshots[#snapshots].roundNumber==3,
 'real restored boss response advances to round3 only when hero regains control')
attempt_snapshot.round_number=nil;attempt_snapshot.round_first_user_id=nil
local old=recovered();B.join(dispatcher,old,50)
check(old.round_number==0 and snapshots[#snapshots].roundNumber==0,'actual legacy boss join advertises unknown historical round')
print('BATTLE ROUNDS '..n..' CHECKS PASSED')
