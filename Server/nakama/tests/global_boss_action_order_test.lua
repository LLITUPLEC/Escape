-- Production boss state machine and ordinary engine; only persistence, transport
-- and decision scheduling are simulated. No live accounts or shared event writes.
local E=dofile('Server/nakama/tests/global_boss_engine_fixture.lua')
local B=require('modules.duel_global_boss_battle')
local function copy(v)if type(v)~='table'then return v end;local r={};for k,x in pairs(v)do r[k]=copy(x)end;return r end
E.Runtime.Pvp={cache_player_levels=function()end}
local clock,choose_calls,resolve_calls,fail_commit=100000,0,0,false
local seen,commits={},{}
local event={id='action-order',hp=550000,max_hp=550000,ends_at=os.time()+3600,config={think_seconds=2,max_turns=30}}
local function state()
 local s=E.init_state({max_hp=31500,base_damage=2022,base_armor=1458,base_crit=.076,base_heal=932},
  {max_hp=550000,base_damage=3500,base_armor=1000,base_crit=.05,base_heal=0})
 s.presences={hero={user_id='hero'}};s.global_boss.event_id=event.id;s.global_boss.attempt_id='attempt'
 s._boss_config=event.config;s._boss_sequence=0;s.turn_deadline_paused=false;s.turn_deadline_unix_ms=130000
 s.active_user_id=s.bot_user_id;s.bot_turn_ready_tick=1
 return s
end
local api={get_event=function()return event end,get_player=function()return{}end,
 commit=function(uid,id,sequence,damage,snapshot,player,reason)
  commits[#commits+1]={sequence=sequence,damage=damage,snapshot=copy(snapshot),player=player}
  if fail_commit then return nil,'storage_unavailable'end
  return{event=event,attempt={sequence=sequence,snapshot=copy(snapshot),status=reason and'finished'or'bound',end_reason=reason,actual_damage=0}}
 end}
api.practice_api=function()return api end
local deps={CFG=E.CFG,now_ms=function()return clock end,api=api,
 filter_messages=function(s,m)return m end,stale=function()return false end,parse=function(x)return x end,
 validate=E.validate,action_cost=E.action_cost,
 choose=function(s,a,o)choose_calls=choose_calls+1;return E.choose(s,a,o)end,
 resolve=function(s,a,actor,opponent)resolve_calls=resolve_calls+1;return E.resolve(s,a,actor,opponent)end,
 tick_buffs=E.tick_buffs,tick_cooldowns=E.tick_cooldowns,reject=function(_,_,why)error(why)end,
 emit=function(_,s)s._terminal_delivered=true end,
 broadcast=function(_,s,a,extra,steps,tick,actor)
  seen[#seen+1]={action=copy(a),actor=actor,extra=extra,adapt=s.global_boss.adaptation_triggered,
   scream=s.global_boss.scream_triggered,damage=s.global_boss.scream_damage,hp=s.stats.hero.hp,
   turn=s.global_boss.boss_own_turn,sequence=s._boss_sequence,deadline=s.turn_deadline_unix_ms,board=copy(s.board)}
 end}
B.configure(deps)
local count=0;local function check(v,name)assert(v,name);count=count+1;print('PASS '..name)end
math.randomseed(318)
local s=state();local board=table.concat(s.board,',');local mana=s.stats[s.bot_user_id].mana
B.loop({},s,1,{})
check(#seen==1 and seen[1].adapt and not seen[1].scream and not seen[1].action and seen[1].hp==31500,
 'adaptation commits before scream as a non-damaging action without a swap')
check(choose_calls==0 and resolve_calls==0 and table.concat(s.board,',')==board and s.stats[s.bot_user_id].mana==mana,
 'adaptation cannot choose or resolve an ordinary action or spend mana')
check(s.global_boss.boss_own_turn==1 and s.active_user_id==s.bot_user_id and s.turn_deadline_unix_ms==130000,
 'adaptation prepares exactly one boss turn and keeps its original deadline')
B.loop({},s,s.bot_turn_ready_tick,{})
check(#seen==2 and not seen[2].adapt and seen[2].scream and not seen[2].action and seen[2].hp==25200 and seen[2].damage==6300,
 '6300 scream is exactly twenty percent of31500 initial HP in its own packet ignoring armor')
check(choose_calls==0 and resolve_calls==0 and s.stats[s.bot_user_id].mana==mana and table.concat(s.board,',')==board,
 'scream does not stack an ordinary move damage mana cost or board changes')
check(s.active_user_id==s.bot_user_id and s.global_boss.boss_own_turn==1 and s.turn_deadline_unix_ms==130000,
 'scream follows Petard free-action semantics and preserves the current turn and deadline')
check(commits[1].sequence==1 and commits[2].sequence==2 and not commits[1].player and not commits[2].player,
 'preparation and scream use separate durable sequences without manufacturing player actions')
B.loop({},s,s.bot_turn_ready_tick,{})
check(choose_calls==1 and resolve_calls==1 and seen[3].action and not seen[3].adapt and not seen[3].scream,
 'ordinary engine and AI run only after the two separately committed boss abilities')
check(s.global_boss.boss_own_turn==1 and s.global_boss.next_scream_turn==3,
 'free ordinary skills cannot advance adaptation or bypass scream cooldown')
-- A persisted preparing turn can be restored before its scream; there is no
-- extra adaptation or increment when the same state is resumed.
local retry=state();choose_calls=0;resolve_calls=0;seen={};commits={}
B.loop({},retry,1,{})
local prepared=B.snapshot(retry);local damage=retry.stats[retry.bot_user_id].base_damage
local restored=state();for k,v in pairs(prepared)do restored[k]=copy(v)end;restored._boss_sequence=1
B.loop({},restored,2,{})
check(restored.global_boss.boss_own_turn==1 and restored.stats[restored.bot_user_id].base_damage==damage and seen[#seen].scream,
 'restored preparation resumes scream without applying adaptation twice')
local pending=state();seen={};commits={};choose_calls=0;resolve_calls=0
B.loop({},pending,1,{});fail_commit=true;B.loop({},pending,pending.bot_turn_ready_tick,{})
check(pending._boss_pending and #seen==1 and pending.stats.hero.hp==25200 and resolve_calls==0,
 'storage failure holds the exact scream action without broadcasting or resolving another move')
local retry_tick=pending._boss_next_retry;fail_commit=false;B.loop({},pending,retry_tick,{})
check(not pending._boss_pending and #seen==2 and seen[2].scream and pending.stats.hero.hp==25200 and pending._boss_sequence==2,
 'retry persists the frozen scream exactly once without repeating damage')
check(commits[2].sequence==commits[3].sequence and commits[2].snapshot.stats.hero.hp==commits[3].snapshot.stats.hero.hp,
 'ambiguous scream settlement retries the same sequence and HP snapshot')
local cooldown=state();cooldown.global_boss.boss_own_turn=1;cooldown.global_boss.next_scream_turn=3
seen={};choose_calls=0;resolve_calls=0;B.loop({},cooldown,1,{});B.loop({},cooldown,cooldown.bot_turn_ready_tick,{})
check(seen[1].adapt and not seen[1].scream and not seen[2].scream and choose_calls==1 and cooldown.global_boss.boss_own_turn==2,
 'a cooldown turn prepares adaptation then goes directly to ordinary AI')
local lethal=state();lethal.stats.hero.hp=1;seen={};choose_calls=0;resolve_calls=0
B.loop({},lethal,1,{});B.loop({},lethal,lethal.bot_turn_ready_tick,{})
check(lethal.ended and lethal.stats.hero.hp==0 and choose_calls==0 and resolve_calls==0 and seen[#seen].scream,
 'a lethal scream finishes after its own impact without selecting a normal move')
-- Actual shipping broadcaster must send upper rows only to the allowlisted
-- owner, in both the paid and practice routes. Client metadata cannot grant it.
local file=assert(io.open('Server/nakama/modules/duel_match3.lua'));local source=file:read('*a');file:close()
local block=assert(source:match('(local function broadcast_sync.-)local function send_reject'))
local env=setmetatable({CFG=E.CFG,nk=package.loaded.nakama,Runtime={arena_mirror_commit=function()end},make_sync_msg=E.make_sync},{__index=_G})
local chunk=assert(loadstring(block..'\nreturn broadcast_sync'));setfenv(chunk,env);local broadcast=chunk()
for _,practice in ipairs({false,true})do
 for _,permission in ipairs({'allowed','denied','unavailable'})do
  local joined=state();joined.started=false;joined.global_boss.practice=practice
  joined.presences.spy={user_id='spy'};joined.owner_session_epoch=7
  api.get_attempt=function()return{id='attempt',sequence=0,snapshot=B.snapshot(joined)}end
  deps.allow_cheat_rows=function(uid)assert(uid=='hero');if permission=='unavailable'then error('unavailable')end;return permission=='allowed'end
  local packets={};local dispatcher={broadcast_message=function(_,msg,targets)for _,p in ipairs(targets or{})do packets[p.user_id]=msg end end}
  deps.broadcast=broadcast;B.configure(deps);B.join(dispatcher,joined,1)
  check(packets.hero and #packets.hero.cheatRows==(permission=='allowed'and 12 or 0)and packets.spy and #packets.spy.cheatRows==0,
   'boss upper rows follow the system allowlist and fail closed: practice='..tostring(practice)..' permission='..permission)
 end
end
print('GLOBAL BOSS ACTION ORDER '..count..' CHECKS PASSED')
