-- Authoritative boss attempts reuse the ordinary match-3 engine, but persist
-- their actions and shared health before sending visual state to the client.
local nk=require('nakama')
local function load(n)local ok,m=pcall(require,'modules.'..n);if ok then return m end;return require(n)end
local Rules=load('duel_global_boss_rules')
local Rounds=load('duel_match3_rounds')
local M={}
local d={}
local FIELDS={'stats','board','spawn_queues','cheat_rows','active_user_id','global_boss','_consumable_bindings',
 'round_number','round_first_user_id',
 'bot_fury_open_mana','bot_survival_mode','bot_kill_plan','last_crit','_action_mana_drain'}
local function copy(v)if type(v)~='table'then return v end;local o={};for k,x in pairs(v)do o[k]=copy(x)end;return o end
local function snapshot(s)local v={};for _,k in ipairs(FIELDS)do v[k]=copy(s[k])end;return v end
local function restore(s,v)for _,k in ipairs(FIELDS)do s[k]=copy(v[k])end end
function M.configure(deps)d=deps end
local function reject(dispatcher,p,why)d.reject(dispatcher,p,why)end
local function deadline(s,tick)
 s.turn_deadline_paused=true;s.turn_pause_started_tick=tick;s.turn_deadline_tick=tick
 s.turn_deadline_unix_ms=0
 s.bot_turn_pending=s.active_user_id==s.bot_user_id
 s.bot_turn_ready_tick=tick+math.ceil((s._boss_config.think_seconds or 2)*d.CFG.TICK_RATE)
end
local function now_ms()
 return type(d.now_ms)=='function'and d.now_ms()or(type(nk.time)=='function'and nk.time()or os.time()*1000)
end
local function expired(s,tick)
 if s.turn_deadline_paused then return false end
 if(s.turn_deadline_unix_ms or 0)>0 then return now_ms()>=s.turn_deadline_unix_ms end
 return s.turn_deadline_tick~=nil and tick>=s.turn_deadline_tick
end
local function start_clock(dispatcher,s,tick)
 s.turn_deadline_paused=false
 s.turn_deadline_tick=tick+30*d.CFG.TICK_RATE
 s.turn_deadline_unix_ms=math.floor(now_ms()+30000)
 s.bot_turn_ready_tick=tick+math.ceil(s._boss_config.think_seconds*d.CFG.TICK_RATE)
 -- The initial state advertises a paused clock. Publish its transition now;
 -- waiting for an unrelated action left the client with turnEndsAt=0.
 d.broadcast(dispatcher,s,nil,false,nil,tick,'')
end
local function sync(dispatcher,s,tick,action,extra,steps,actor)
 d.broadcast(dispatcher,s,action,extra,steps,tick,actor)
end
local function terminal(dispatcher,s,tick,result,reason,visual)
 local e=result.event or d.api.get_event(s.global_boss.event_id)
 if e and s.stats[s.bot_user_id]then s.stats[s.bot_user_id].hp=e.hp end
 s.ended=true;s.global_boss.my_damage=result.attempt and result.attempt.actual_damage or s.global_boss.my_damage or 0
 s.global_boss.total_damage=result.my_damage or s.global_boss.total_damage or 0
 s.global_boss.qualified=result.qualified==true or s.global_boss.qualified==true
 local payload={globalBoss=true,bossAttemptFinished=true,endReason=reason or(result.attempt and result.attempt.end_reason)or'finished',
  winnerUserId=e and e.hp<=0 and s.owner_user_id or s.bot_user_id,isDraw=false,
  bossGlobalHp=e and e.hp or 0,bossGlobalMaxHp=e and e.max_hp or 0,bossMyDamage=s.global_boss.my_damage,bossTotalDamage=s.global_boss.total_damage,
  bossQualified=s.global_boss.qualified,bossAttemptRefunded=result.refunded==true}
 if s.started then sync(dispatcher,s,tick,visual and visual.action,visual and visual.extra or false,visual and visual.steps,visual and visual.actor or'')end
 s._boss_terminal=payload;d.emit(dispatcher,s,payload)
 if e and e.hp<=0 then local ok,ready=pcall(d.api.ensure_worker,e.id);s._boss_mail_pending=not(ok and ready)end
end
function M.finish(dispatcher,s,tick,why)
 if s.ended then if s._boss_terminal and not s._terminal_delivered then d.emit(dispatcher,s,s._boss_terminal)end;return end
 -- A frozen action must settle before surrender; otherwise its accepted HP
 -- change could be silently discarded on a storage outage.
 if s._boss_pending then return false,'action_pending' end
 local result,err=d.api.finish(s.owner_user_id,s.global_boss.attempt_id,why,s.started and snapshot(s)or nil)
 if not result then s._boss_pending_finish=why;return false,err end
 s._boss_pending_finish=nil;terminal(dispatcher,s,tick,result,why);return true
end
function M.join(dispatcher,s,tick)
 if s.started then sync(dispatcher,s,tick,nil,false,nil,'');return s end
 local a=d.api.get_attempt(s.owner_user_id,s.global_boss.attempt_id)
 local e=d.api.get_event(s.global_boss.event_id);s._boss_config=e.config
 if e.hp<=0 or os.time()>=e.ends_at then M.finish(dispatcher,s,tick,e.hp<=0 and'boss_defeated'or'event_expired');return s end
 s.players_sorted={s.owner_user_id,s.bot_user_id};s.player_levels=s.player_levels or{};s.player_levels[s.bot_user_id]=12
 if a.snapshot then restore(s,a.snapshot);Rounds.restore(s,s.owner_user_id)
 else
  s.stats[s.owner_user_id]=d.new_stats();d.apply_player(s.stats[s.owner_user_id],s.owner_user_id)
  local b=d.new_stats();b.hp=e.hp;b.initial_hp=e.max_hp;b.max_hp=e.max_hp;b.base_damage=e.config.base_damage
  b.base_armor=e.config.base_armor;b.base_crit=e.config.base_crit;b.base_heal=0;s.stats[s.bot_user_id]=b
  s.board=d.new_board();d.ensure_board(s);s.cheat_rows=d.init_cheat_rows();s.spawn_queues={}
  for x=0,d.CFG.SIZE-1 do d.ensure_queue(s,x)end;d.update_cheat_rows(s)
  Rounds.start(s,s.owner_user_id);s.global_boss.boss_own_turn=0;s.global_boss.next_scream_turn=1;s.global_boss.player_turns=0
  local sheet=d.read_sheet(s.owner_user_id);local effects=d.effects.normalize(sheet);local receipt=copy(effects.armed or{})
  receipt.bounty=copy(effects.bounty);effects.armed=nil;sheet.consumables=effects
  d.effects.apply(s.stats[s.owner_user_id],receipt);s._consumable_bindings={[s.owner_user_id]=receipt}
  local started,err=d.api.start(s.owner_user_id,a.id,snapshot(s),receipt,sheet)
  if not started then
   if err=='boss_defeated'then M.finish(dispatcher,s,tick,'boss_defeated')else s._boss_join_pending=true end
   return s
  end
  a=started;restore(s,a.snapshot)
 end
 s._boss_sequence=a.sequence or 0;s._boss_join_pending=nil;s.started=true;s.cheat_rows_allowed={}
 local p=d.api.get_player(s.owner_user_id,e.id)or{};s.global_boss.my_damage=a.actual_damage or 0;s.global_boss.total_damage=p.total_damage or 0;s.global_boss.qualified=p.qualified==true
 s.stats[s.bot_user_id].hp=e.hp;deadline(s,tick);sync(dispatcher,s,tick,nil,false,nil,'');return s
end
local function persist(dispatcher,s,tick,pending)
 local r,err=d.api.commit(s.owner_user_id,s.global_boss.attempt_id,pending.sequence,pending.damage,pending.snapshot,pending.player_action,pending.reason)
 if not r then
  if err=='boss_defeated'or err=='event_expired'then
   s._boss_pending=nil;restore(s,pending.before);M.finish(dispatcher,s,tick,err);return true
  end
  s._boss_pending=pending;s._boss_next_retry=tick+d.CFG.TICK_RATE;return false
 end
 s._boss_pending=nil;s._boss_sequence=r.attempt.sequence;restore(s,r.attempt.snapshot)
 s.stats[s.bot_user_id].hp=r.event.hp
 local p=d.api.get_player(s.owner_user_id,r.event.id)or{};s.global_boss.my_damage=r.attempt.actual_damage or 0;s.global_boss.total_damage=p.total_damage or 0;s.global_boss.qualified=p.qualified==true
 if r.attempt.status=='finished'then terminal(dispatcher,s,tick,{event=r.event,attempt=r.attempt,my_damage=p.total_damage,qualified=p.qualified},r.attempt.end_reason,pending)
 else
  if not pending.keep then deadline(s,tick)
  elseif s.active_user_id==s.bot_user_id then s.bot_turn_ready_tick=tick+math.ceil(s._boss_config.think_seconds*d.CFG.TICK_RATE)end
  sync(dispatcher,s,tick,pending.action,pending.extra,pending.steps,pending.actor)
 end
 return true
end
local function action(dispatcher,s,tick,a,actor,pass)
 local before=snapshot(s);local boss=actor==s.bot_user_id;local opp=boss and s.owner_user_id or s.bot_user_id
 local e=d.api.get_event(s.global_boss.event_id)
 if e.hp<=0 or os.time()>=e.ends_at then M.finish(dispatcher,s,tick,e.hp<=0 and'boss_defeated'or'event_expired');return end
 s.stats[s.bot_user_id].hp=e.hp;s._global_boss_damage_raw=0
 s.global_boss.scream_triggered=false;s.global_boss.scream_damage=0;s.global_boss.adaptation_triggered=false
 local extra,keep,steps=false,false,nil
 if boss and not s.global_boss.boss_turn_prepared then Rules.begin_turn(s)end
 if s.stats[s.owner_user_id].hp>0 and not pass then
   if not a then s._bot_pre_mana=s.stats[actor].mana;a=d.choose(s,actor,opp)end
  if a then
   local valid=d.validate(s,actor,a);if not valid then restore(s,before);return false,'invalid_action'end
   local st=s.stats[actor];local t=a.actionType
   if t>=2 and t<=6 then
    st.mana=math.max(0,st.mana-d.action_cost(s,t))
    local key=({'cross_cd','square_cd','petard_cd','shield_cd','fury_cd'})[t-1]
    local cd=({d.CFG.CROSS_ABILITY_COOLDOWN,d.CFG.SQUARE_ABILITY_COOLDOWN,d.CFG.PETARD_ABILITY_COOLDOWN,d.CFG.SHIELD_ABILITY_COOLDOWN,d.CFG.FURY_ABILITY_COOLDOWN})[t-1]
    st[key]=cd
   end
   local ok,err;ok,err,extra,keep,steps=d.resolve(s,a,actor,opp)
   if not ok then restore(s,before);return false,err end
  end
 end
 local reason
 if s.stats[s.owner_user_id].hp<=0 then reason='hero_defeated'
 elseif not keep then
  if actor==s.owner_user_id then
   s.global_boss.player_turns=(s.global_boss.player_turns or 0)+1
   if s.global_boss.player_turns>=s._boss_config.max_turns then reason='turn_limit'end
  end
  if boss then s.global_boss.boss_turn_prepared=false end
  if not extra then
   d.tick_buffs(s.stats[actor]);Rounds.set_active(s,opp);d.tick_cooldowns(s.stats[opp])
   if boss then s.bot_kill_plan=nil;s.bot_fury_open_mana=nil end
  end
 end
 local p={sequence=s._boss_sequence+1,damage=s._global_boss_damage_raw or 0,snapshot=snapshot(s),before=before,
  player_action=not boss,reason=reason,action=a,actor=actor,extra=extra,keep=keep,steps=steps}
 return persist(dispatcher,s,tick,p)
end
function M.loop(dispatcher,s,tick,messages)
 if s.ended then
  if not s._terminal_delivered then d.emit(dispatcher,s,s._boss_terminal);return s end
  if s._boss_mail_pending then local ok,ready=pcall(d.api.ensure_worker,s.global_boss.event_id);if not(ok and ready)then return s end end
  return nil
 end
 if s._boss_pending then if tick>=(s._boss_next_retry or 0)then persist(dispatcher,s,tick,s._boss_pending)end;return s end
 if s._boss_pending_finish then M.finish(dispatcher,s,tick,s._boss_pending_finish);return s end
 if not s.started then
  if s._boss_join_pending then M.join(dispatcher,s,tick)
  elseif tick>=(s.join_deadline_tick or math.huge)then M.finish(dispatcher,s,tick,'join_timeout')end
  return s
 end
 if not s.presences[s.owner_user_id]then
  s._boss_disconnect_tick=s._boss_disconnect_tick or tick
  if tick>=s._boss_disconnect_tick+20*d.CFG.TICK_RATE then M.finish(dispatcher,s,tick,'reconnect_timeout')end
  return s
 end
 s._boss_disconnect_tick=nil
 local e=d.api.get_event(s.global_boss.event_id)
 if e.hp<=0 or os.time()>=e.ends_at then M.finish(dispatcher,s,tick,e.hp<=0 and'boss_defeated'or'event_expired');return s end
 if s.stats[s.bot_user_id].hp~=e.hp then s.stats[s.bot_user_id].hp=e.hp;sync(dispatcher,s,tick,nil,false,nil,'')end
 messages=d.filter_messages(s,messages,tick,dispatcher)
 for _,m in ipairs(messages or{})do
  if m.sender.user_id==s.owner_user_id then
   if m.op_code==d.CFG.OP_PLAYER_LEFT then
    if d.stale(s)then reject(dispatcher,m.sender,'session_stale')else M.finish(dispatcher,s,tick,'player_left');return s end
   elseif m.op_code==d.CFG.OP_SNAPSHOT_REQUEST then sync(dispatcher,s,tick,nil,false,nil,'')
   elseif m.op_code==17 and s.turn_deadline_paused then
    if d.stale(s)then reject(dispatcher,m.sender,'session_stale')else start_clock(dispatcher,s,tick)end
   elseif m.op_code==d.CFG.OP_ACTION_REQUEST and not s.turn_deadline_paused and s.active_user_id==s.owner_user_id then
    if d.stale(s)then reject(dispatcher,m.sender,'session_stale')
    elseif expired(s,tick)then reject(dispatcher,m.sender,'turn_expired')else
     local a=d.parse(m.data);local valid,why=d.validate(s,m.sender.user_id,a)
     if not valid then reject(dispatcher,m.sender,why)else local ok,err=action(dispatcher,s,tick,a,s.owner_user_id);if ok==false and err then reject(dispatcher,m.sender,err)end end
    end
   end
  end
  if s.ended or s._boss_pending then return s end
 end
 if s.turn_deadline_paused and tick>=s.turn_pause_started_tick+8*d.CFG.TICK_RATE then start_clock(dispatcher,s,tick)end
 if not s.turn_deadline_paused then
  if expired(s,tick)then action(dispatcher,s,tick,nil,s.active_user_id,true)
  elseif s.active_user_id==s.bot_user_id and tick>=s.bot_turn_ready_tick then action(dispatcher,s,tick,nil,s.bot_user_id)end
 end
 return s
end
M.snapshot=snapshot
return M
