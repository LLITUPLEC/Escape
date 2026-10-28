-- Weekly shared HP. Each action atomically updates ONE global health row and
-- the protected owner's sequence/snapshot/contribution. No client damage claim.
local nk=require("nakama")
local function load(n)local ok,m=pcall(require,"modules."..n);if ok then return m end;return require(n)end
local Inventory=load("duel_match3_inventory")
local Rules=load("duel_global_boss_rules")
local Mail=load("duel_mail")
local CFG=load("duel_match3_config")
local Shop=load("duel_shop")
local Ranking=load("duel_global_boss_ranking")
local Practice=load("duel_global_boss_practice")
local M={COLLECTION="duel_global_boss",PLAYER_COLLECTION="duel_global_boss_player",ATTEMPT_COLLECTION="duel_global_boss_attempt"}
local SYSTEM="00000000-0000-0000-0000-000000000000"
local DAY,WEEK=86400,604800
local deps={}
local function copy(v)if type(v)~="table"then return v end;local r={};for k,x in pairs(v)do r[k]=copy(x)end;return r end
local function read(uid,c,k)
  local rows=nk.storage_read({{user_id=uid,collection=c,key=k}});assert(type(rows)=="table","boss_storage_unavailable")
  local row=rows[1];if row and type(row.value)=="string"then row.value=nk.json_decode(row.value)end
  assert(not row or type(row.value)=="table","boss_invalid_record");return row and row.value,row and row.version
end
local function op(uid,c,k,v,ver)return{user_id=uid,collection=c,key=k,value=v,version=ver or"*",permission_read=0,permission_write=0}end
-- Service-match VMs do not execute the duel core. Refunds therefore have a
-- narrow direct Storage fallback, preserving every original sheet field.
local function sheet_read(uid)
 if deps.read_sheet then return deps.read_sheet(uid)end
 local s,v=read(uid,CFG.CHARACTER_SHEET_COLLECTION,CFG.CHARACTER_SHEET_KEY)
 assert(s,"boss_refund_sheet_missing");s._storage_version=v;Inventory.normalize(s);return s
end
local function sheet_write(uid,s)
 if deps.build_sheet_write then return deps.build_sheet_write(uid,s)end
 local value=copy(s);local version=value._storage_version;value._storage_version=nil
 local w=op(uid,CFG.CHARACTER_SHEET_COLLECTION,CFG.CHARACTER_SHEET_KEY,value,version);w.permission_read=1;return w
end
local function item_defs()
 if deps.get_defs then return deps.get_defs()end
 local c=read(SYSTEM,CFG.ITEM_DEFS_COLLECTION,CFG.ITEM_DEFS_KEY)
 return Shop.merge_defs(c and c.items or{})
end
local function retry(fn)
  local last
  for _=1,6 do local ok,a,b,c=pcall(fn);if ok then return a,b,c end;last=a end
  nk.logger_error("global boss deferred: "..tostring(last));return nil,"storage_unavailable"
end
local function integer(v,min,max,fallback)
  local n=tonumber(v);if not n or n~=n or n==math.huge or n==-math.huge then n=fallback end
  return math.max(min,math.min(max,math.floor(n)))
end
local function number(v,min,max,fallback)
  local n=tonumber(v);if not n or n~=n or n==math.huge or n==-math.huge then n=fallback end
  return math.max(min,math.min(max,n))
end
local function week(now)local start=math.floor((now+10800+3*DAY)/WEEK)*WEEK-3*DAY-10800;return"week-"..start,start,start+WEEK end
local function player(e)return{schema=1,event_id=e.id,attempts_used=0,total_damage=0,qualifying_damage=0,qualified=false,requests={}}end
local function live(mid)
  if type(mid)~="string"or mid==""then return false end
  local ok,m,err=pcall(nk.match_get,mid);if not ok or err then return nil end;return m~=nil
end
function M.configure(d)deps=assert(d)end
function M.config()
  local input=read(SYSTEM,M.COLLECTION,"config")or{}
  local c={schema=1,name=tostring(input.name or"Сердце Разлома"),
    max_hp=math.ceil(integer(input.max_hp,1,1000000000,550000)/50000)*50000,base_damage=math.ceil(number(input.base_damage,1,1000000,3500)/500)*500,
    base_armor=integer(input.base_armor,0,100000,1000),base_crit=number(input.base_crit,0,1,.05),
    max_turns=integer(input.max_turns,1,60,30),eligibility_min_damage=integer(input.eligibility_min_damage,1,1000000000,1),
    think_seconds=number(input.think_seconds,.1,10,2),
    rewards={gold=1500,ore=2000,matter=5},reward_title=tostring(input.reward_title or"Глобальный босс повержен"),
    reward_body=tostring(input.reward_body or"Разлом закрыт общими усилиями. Забери награду за свой вклад."),
    recipes={},elixirs={"elixir_assault","elixir_stone","elixir_gambit"}}
  for k,v in pairs(type(input.rewards)=="table"and input.rewards or{})do
    if c.rewards[k]~=nil then c.rewards[k]=integer(v,0,1000000,c.rewards[k])end
  end
  for _,slot in ipairs({"Helmet","Shoulders","Chest","Gloves","Legs","Feet","WeaponLeft","WeaponRight"})do
    c.recipes[#c.recipes+1]="recipe_drop_t1_gold_"..slot
  end
  return c
end
function M.event()
  local now=os.time();local id,start,finish=week(now)
  return retry(function()
    local e=read(SYSTEM,M.COLLECTION,id);if e then return e end
    local index,version=read(SYSTEM,M.COLLECTION,"index");index=index or{schema=1,pending={}}
    index.current=id;index.pending=index.pending or{}
    local cfg=M.config()
    e={schema=1,id=id,starts_at=start,ends_at=finish,status="active",hp=cfg.max_hp,max_hp=cfg.max_hp,revision=0,config=cfg,created_at=now}
    nk.storage_write({op(SYSTEM,M.COLLECTION,id,e),op(SYSTEM,M.COLLECTION,"index",index,version)})
    return e
  end)
end
function M.get_event(id)return read(SYSTEM,M.COLLECTION,id)end
function M.get_attempt(uid,id)return read(uid,M.ATTEMPT_COLLECTION,id)end
function M.get_player(uid,id)local p=read(uid,M.PLAYER_COLLECTION,id);return p end
function M.token_count(sheet)
  Inventory.normalize(sheet);local count=0
  for i=1,Inventory.capacity(sheet)do if sheet.inventory[i]==Rules.TOKEN_ID then count=count+(tonumber(sheet.inventory_counts[i])or 0)end end
  return count
end
local function remove_token(sheet)
  Inventory.normalize(sheet)
  for i=1,Inventory.capacity(sheet)do if sheet.inventory[i]==Rules.TOKEN_ID and sheet.inventory_counts[i]>=1 then
    sheet.inventory_counts[i]=sheet.inventory_counts[i]-1;if sheet.inventory_counts[i]==0 then sheet.inventory[i]=""end;return true
  end end;return false
end
local function bind_result(e,a,p,spent)
  return{ok=true,event_id=e.id,attempt_id=a.id,match_id=a.match_id,bot_id="global_boss",bot_name=e.config.name,
    bot_user_id="zz-bot-global-"..e.id,is_global_boss=true,spent_token=spent==true,resumed=spent~=true,
    attempts_left=math.max(0,3-(p.attempts_used or 0)),join_token=a.join_token,max_turns=e.config.max_turns}
end
function M.refund(uid,attempt_id,why)
  return retry(function()
    local a,av=read(uid,M.ATTEMPT_COLLECTION,attempt_id);if not a then return false,"attempt_not_found"end
    if a.status=="refunded"then return true end
    if why=="admission_cancelled"and a.battle_started==true then return false,"battle_already_started"end
    if a.player_actions and a.player_actions>0 then return false,"attempt_already_started"end
    local p,pv=read(uid,M.PLAYER_COLLECTION,a.event_id);assert(p,"boss_player_missing")
    local sheet=sheet_read(uid);local defs=item_defs()
    Inventory.add_or_queue(sheet,Rules.TOKEN_ID,1,defs)
    if a.consumable_receipt and a.consumable_receipt.item_id and a.consumable_receipt.item_id~=""then
      Inventory.add_or_queue(sheet,a.consumable_receipt.item_id,1,defs)
    end
    a.status="refunded";a.finished_at=os.time();a.end_reason=why
    p.attempts_used=math.max(0,(p.attempts_used or 0)-1)
    if p.active_attempt_id==a.id then p.active_attempt_id=nil end
    nk.storage_write({op(uid,M.ATTEMPT_COLLECTION,a.id,a,av),op(uid,M.PLAYER_COLLECTION,a.event_id,p,pv),sheet_write(uid,sheet)})
    return true
  end)
end
function M.mark_started(uid,attempt_id)
  return retry(function()
    local a,av=read(uid,M.ATTEMPT_COLLECTION,attempt_id)
    if not a or a.status~="bound"then return false,"attempt_finished"end
    if a.battle_started then return true end
    a.battle_started=true
    nk.storage_write({op(uid,M.ATTEMPT_COLLECTION,a.id,a,av)})
    return true
  end)
end
function M.bind_state(state,nonce)
  if state.global_boss.practice==true then return Practice.bind(state)end
  local uid=state.owner_user_id;local id=state.global_boss.attempt_id
  return retry(function()
    local a,av=read(uid,M.ATTEMPT_COLLECTION,id)
    if a and a.status=="bound"and a.match_id==state.match_id then return a end
    assert(a and a.spawn_nonce==nonce,"boss_spawn_claim_invalid")
    assert(a.status=="reserved"or a.status=="bound","boss_attempt_finished")
    a.status="bound";a.match_id=state.match_id;a.bound_at=os.time();a.spawn_nonce=nil;a.spawn_until=nil
    nk.storage_write({op(uid,M.ATTEMPT_COLLECTION,id,a,av)})
    return a
  end)
end
function M.practice_api(state)return Practice.api(state)end
function M.practice_label(uid)return Practice.label(uid)end
-- Practice reads the initial weekly configuration, never the depleted HP or
-- a previous battle snapshot. No event/admission/token record is created.
function M.practice_enter(uid,epoch)
  local current=read(SYSTEM,M.COLLECTION,week(os.time()))
  local cfg=copy(current and current.config or M.config())
  local id="practice-"..nk.uuid_v4();local attempt=nk.uuid_v4();local bot="zz-bot-global-"..id
  local ok,mid=pcall(deps.try_match_create,{mode="pve",owner_user_id=uid,owner_session_epoch=epoch,
    bot_id="global_boss",bot_user_id=bot,global_boss={practice=true,event_id=id,attempt_id=attempt,practice_config=cfg},
    pve_run={affix="",difficulty="easy",arena_suppress_all=true}})
  if not ok or not mid then return{ok=false,err="match_create_failed"}end
  return{ok=true,is_global_boss=true,practice=true,event_id=id,attempt_id=attempt,match_id=mid,
    bot_id="global_boss",bot_user_id=bot,bot_name=cfg.name,spent_token=false,resumed=false}
end
function M.enter(uid,request_id,epoch,expected_event)
  if deps.recover_match then
    local previous=deps.recover_match(uid,epoch)
    if not previous.ok then return previous end
    if previous.active and not previous.is_global_boss then return{ok=false,err="match_in_progress",active_match=previous}end
  end
  local e,err=M.event();if not e then return{ok=false,err=err}end
  if expected_event and expected_event~=e.id then return{ok=false,err="event_changed"}end
  if e.status~="active"or e.hp<=0 or os.time()>=e.ends_at then return{ok=false,err="boss_unavailable"}end
  local reservation,why=retry(function()
    local current,ev=read(SYSTEM,M.COLLECTION,e.id);if current.status~="active"or current.hp<=0 then return nil,"boss_defeated"end
    local p,pv=read(uid,M.PLAYER_COLLECTION,e.id);p=p or player(current)
    if p.active_attempt_id then
      local a,av=read(uid,M.ATTEMPT_COLLECTION,p.active_attempt_id)
      if a and(a.status=="reserved"or a.status=="bound")then
        if a.match_id then
          local exists=live(a.match_id);if exists==nil then return nil,"match_probe_unavailable"end
          if exists then
            pcall(nk.match_signal,a.match_id,nk.json_encode({kind="boss_resume",user_id=uid,session_epoch=epoch}))
            return{event=current,attempt=a,player=p,spent=false}
          end
          if(a.bound_at or 0)+3>os.time()then return nil,"entry_starting"end
        end
        if a.spawn_until and a.spawn_until>os.time()then return nil,"entry_starting"end
        a.spawn_nonce=nk.uuid_v4();a.spawn_until=os.time()+30
        nk.storage_write({op(uid,M.ATTEMPT_COLLECTION,a.id,a,av)})
        return{event=current,attempt=a,player=p,spent=false,spawn=true}
      end
      p.active_attempt_id=nil
    end
    if p.requests and p.requests[request_id]then return nil,"attempt_finished"end
    if(p.attempts_used or 0)>=3 then return nil,"weekly_attempts_exhausted"end
    local sheet=deps.read_sheet(uid);if not remove_token(sheet)then return nil,"teleport_required"end
    local id=e.id..":"..nk.uuid_v4();local nonce=nk.uuid_v4()
    local a={schema=1,id=id,event_id=e.id,request_id=request_id,status="reserved",created_at=os.time(),
      sequence=0,player_actions=0,spawn_nonce=nonce,spawn_until=os.time()+30,join_token=nk.uuid_v4()}
    p.attempts_used=(p.attempts_used or 0)+1;p.active_attempt_id=id;p.requests=p.requests or{};p.requests[request_id]=id
    nk.storage_write({op(SYSTEM,M.COLLECTION,e.id,current,ev),op(uid,M.PLAYER_COLLECTION,e.id,p,pv),
      op(uid,M.ATTEMPT_COLLECTION,id,a),deps.build_sheet_write(uid,sheet)})
    return{event=current,attempt=a,player=p,spent=true,spawn=true}
  end)
  if not reservation then return{ok=false,err=why or"storage_unavailable"}end
  local a,p=reservation.attempt,reservation.player
  if reservation.spawn then
    local ok,mid=pcall(deps.try_match_create,{mode="pve",owner_user_id=uid,owner_session_epoch=epoch,
      bot_id="global_boss",bot_user_id="zz-bot-global-"..e.id,
      global_boss={event_id=e.id,attempt_id=a.id,spawn_nonce=a.spawn_nonce},pve_run={affix="",difficulty="easy"}})
    local stored=M.get_attempt(uid,a.id)
    if stored and stored.match_id and live(stored.match_id)then a=stored
    elseif not ok or not mid then
      if stored and not stored.match_id and(stored.player_actions or 0)==0 then M.refund(uid,a.id,"creation_failed")end
      return{ok=false,err="match_create_failed"}
    else a=stored or a;a.match_id=mid end
  end
  return bind_result(reservation.event,a,p,reservation.spent)
end
-- Persist the first battle snapshot and the consumed elixir together. The
-- attempt itself is the durable admission/receipt, independent of journal TTL.
function M.start(uid,attempt_id,snapshot,consumable_receipt,sheet)
  return retry(function()
    local a,av=read(uid,M.ATTEMPT_COLLECTION,attempt_id);if not a then return nil,"attempt_not_found"end
    if a.snapshot then return a end
    local e,ev=read(SYSTEM,M.COLLECTION,a.event_id)
    if e.status~="active"or e.hp<=0 then return nil,"boss_defeated"end
    a.snapshot=copy(snapshot);a.consumable_receipt=copy(consumable_receipt);a.joined_at=os.time()
    local writes={op(SYSTEM,M.COLLECTION,e.id,e,ev),op(uid,M.ATTEMPT_COLLECTION,a.id,a,av)}
    if sheet then writes[#writes+1]=deps.build_sheet_write(uid,sheet)end
    nk.storage_write(writes);return a
  end)
end
function M.commit(uid,attempt_id,sequence,damage,snapshot,player_action,terminal_reason)
  local result,err=retry(function()
    local a,av=read(uid,M.ATTEMPT_COLLECTION,attempt_id);if not a then return nil,"attempt_not_found"end
    local e,ev=read(SYSTEM,M.COLLECTION,a.event_id)
    if a.sequence==sequence then return{attempt=a,event=e,replayed=true}end
    if a.status~="bound"or a.sequence+1~=sequence then return nil,"stale_action"end
    if e.status~="active"or e.hp<=0 or os.time()>=e.ends_at then return nil,e.hp<=0 and"boss_defeated"or"event_expired"end
    local p,pv=read(uid,M.PLAYER_COLLECTION,a.event_id);assert(p,"boss_player_missing")
    damage=math.max(0,math.floor(tonumber(damage)or 0))
    local actual=math.min(e.hp,damage);e.hp=e.hp-actual;e.revision=(e.revision or 0)+1
    p.total_damage=(p.total_damage or 0)+actual;p.qualifying_damage=(p.qualifying_damage or 0)+damage
    p.qualified=p.qualified or p.qualifying_damage>=e.config.eligibility_min_damage
    a.actual_damage=(a.actual_damage or 0)+actual
    a.sequence=sequence;a.snapshot=copy(snapshot);a.updated_at=os.time()
    if player_action then a.player_actions=(a.player_actions or 0)+1 end
    local writes={}
    if e.hp<=0 then
      e.status="dead";e.defeated_at=os.time();e.mail_done=false
      local index,iv=read(SYSTEM,M.COLLECTION,"index");index=index or{schema=1,pending={}};index.pending=index.pending or{};index.pending[e.id]=true
      writes[#writes+1]=op(SYSTEM,M.COLLECTION,"index",index,iv)
    end
    if terminal_reason or e.hp<=0 then
      a.status="finished";a.finished_at=os.time();a.end_reason=e.hp<=0 and"boss_defeated"or terminal_reason
      if p.active_attempt_id==a.id then p.active_attempt_id=nil end
    end
    writes[#writes+1]=op(SYSTEM,M.COLLECTION,e.id,e,ev)
    writes[#writes+1]=op(uid,M.PLAYER_COLLECTION,e.id,p,pv)
    writes[#writes+1]=op(uid,M.ATTEMPT_COLLECTION,a.id,a,av)
    nk.storage_write(writes)
    return{attempt=a,event=e,actual_damage=actual,qualified=p.qualified,my_damage=p.total_damage}
  end)
  if result then
    local ok,synced=pcall(Ranking.sync,uid,result.event.id)
    if not ok or not synced then pcall(Ranking.ensure_worker,result.event.id)end
  end
  return result,err
end
function M.finish(uid,id,why,snapshot)
  local a=M.get_attempt(uid,id);if not a then return nil,"attempt_not_found"end
  local e=M.get_event(a.event_id)
  if e and e.hp<=0 and(a.player_actions or 0)==0 and(a.status=="reserved"or a.status=="bound")then
    local ok,err=M.refund(uid,id,"boss_defeated_before_action");return ok and{refunded=true,event=e,attempt=M.get_attempt(uid,id)}or nil,err
  end
  return retry(function()
    local current,av=read(uid,M.ATTEMPT_COLLECTION,id)
    if current.status=="finished"or current.status=="refunded"then return{attempt=current,event=e,refunded=current.status=="refunded"}end
    local latest,ev=read(SYSTEM,M.COLLECTION,current.event_id)
    if latest.hp<=0 and(current.player_actions or 0)==0 then
      local ok,err=M.refund(uid,id,"boss_defeated_before_action")
      return ok and{refunded=true,event=latest,attempt=M.get_attempt(uid,id)}or nil,err
    end
    local p,pv=read(uid,M.PLAYER_COLLECTION,current.event_id);assert(p,"boss_player_missing")
    current.status="finished";current.end_reason=why;current.finished_at=os.time();current.snapshot=copy(snapshot or current.snapshot)
    if p.active_attempt_id==id then p.active_attempt_id=nil end
    nk.storage_write({op(SYSTEM,M.COLLECTION,current.event_id,latest,ev),op(uid,M.ATTEMPT_COLLECTION,id,current,av),op(uid,M.PLAYER_COLLECTION,current.event_id,p,pv)})
    return{attempt=current,event=latest,qualified=p.qualified,my_damage=p.total_damage or 0}
  end)
end
function M.status(uid)
  pcall(M.recover_workers)
  local e,err=M.event();if not e then return{ok=false,err=err}end
  local p=M.get_player(uid,e.id)or player(e);local sheet=deps.read_sheet(uid)
  local active=p.active_attempt_id and M.get_attempt(uid,p.active_attempt_id)or nil
  if active and(e.hp<=0 or os.time()>=e.ends_at)then
    M.finish(uid,active.id,e.hp<=0 and"boss_defeated"or"event_expired",active.snapshot)
    p=M.get_player(uid,e.id)or p;active=nil;sheet=sheet_read(uid)
  end
  return{ok=true,event_id=e.id,server_now=os.time(),starts_at=e.starts_at,ends_at=e.ends_at,
    status=e.hp<=0 and"dead"or(os.time()>=e.ends_at and"expired"or e.status),hp=e.hp,hp_max=e.max_hp,name=e.config.name,
    base_damage=e.config.base_damage,base_armor=e.config.base_armor,base_crit=e.config.base_crit,base_heal=0,no_healing=true,
    attempts_used=p.attempts_used or 0,attempts_total=3,attempts_left=math.max(0,3-(p.attempts_used or 0)),
    token_id=Rules.TOKEN_ID,token_count=M.token_count(sheet),my_damage=p.total_damage or 0,
    eligibility_progress=math.min(p.qualifying_damage or 0,e.config.eligibility_min_damage),eligibility_required=e.config.eligibility_min_damage,
    qualified=p.qualified==true,active_attempt=active and{attempt_id=active.id,match_id=active.match_id or"",resumable=active.status=="bound"or active.status=="reserved"}or nil,
    max_turns=e.config.max_turns,abilities={{id="horrifying_scream",title="Ужасающий крик",cooldown_turns=2,mana=0,initial_hp_percent=20},
      {id="adaptation",title="Адаптация",passive=true}},rewards_preview={{def_id="legendary_recipe_random",amount=1},{def_id="tesseract",amount=1},
      {def_id="elixir_random",amount=1},{def_id="scroll_pve_bounty",amount=1},{resource="gold",amount=e.config.rewards.gold},{resource="ore",amount=e.config.rewards.ore},{resource="matter",amount=e.config.rewards.matter}}}
end
local function rpc(ctx,payload,mutate)
  local uid=ctx and ctx.user_id;if type(uid)~="string"or uid==""then return nk.json_encode({ok=false,err="unauthorized"})end
  local p=payload or{};if type(p)=="string"then if#p>512 then return nk.json_encode({ok=false,err="bad_payload"})end;local ok,v=pcall(nk.json_decode,p);p=ok and v or nil end
  if type(p)~="table"then return nk.json_encode({ok=false,err="bad_payload"})end
  for k in pairs(p)do if k~="session_epoch"and(not mutate or(k~="request_id"and k~="event_id"))then return nk.json_encode({ok=false,err="bad_payload"})end end
  if mutate then
    local yes,err=deps.guard_epoch(uid,payload);if not yes then return nk.json_encode({ok=false,err=err})end
    if type(p.request_id)~="string"or#p.request_id<8 or#p.request_id>80 or p.request_id:find("[^%w_%-]")then return nk.json_encode({ok=false,err="bad_request_id"})end
    if type(p.event_id)~="string"or#p.event_id<1 or#p.event_id>80 then return nk.json_encode({ok=false,err="bad_event_id"})end
  end
  local ok,result=pcall(function()if mutate then return M.enter(uid,p.request_id,p.session_epoch,p.event_id)end;return M.status(uid)end)
  if not ok then nk.logger_error("global boss RPC deferred: "..tostring(result));result={ok=false,err="boss_unavailable"}end
  return nk.json_encode(result)
end
function M.rpc_get(ctx,payload)return rpc(ctx,payload,false)end
function M.rpc_enter(ctx,payload)return rpc(ctx,payload,true)end
function M.rpc_practice(ctx,payload)
  local uid=ctx and ctx.user_id;if type(uid)~="string"or uid==""then return nk.json_encode({ok=false,err="unauthorized"})end
  local p=payload or{}
  if type(p)=="string"then if#p>128 then return nk.json_encode({ok=false,err="bad_payload"})end;local ok,v=pcall(nk.json_decode,p);p=ok and v or nil end
  if type(p)~="table"then return nk.json_encode({ok=false,err="bad_payload"})end
  for k in pairs(p)do if k~="session_epoch"then return nk.json_encode({ok=false,err="bad_payload"})end end
  local yes,err=deps.guard_epoch(uid,payload);if not yes then return nk.json_encode({ok=false,err=err})end
  local ok,result=pcall(M.practice_enter,uid,p.session_epoch)
  if not ok then nk.logger_error("boss practice unavailable: "..tostring(result));result={ok=false,err="boss_unavailable"}end
  return nk.json_encode(result)
end
function M.rpc_leaderboard(ctx,payload)
  local uid=ctx and ctx.user_id
  if type(uid)~="string"or uid==""then return nk.json_encode({ok=false,err="unauthorized"})end
  local p=payload or{}
  if type(p)=="string"then
    if#p>4096 then return nk.json_encode({ok=false,err="bad_payload"})end
    local ok,v=pcall(nk.json_decode,p);p=ok and v or nil
  end
  if type(p)~="table"then return nk.json_encode({ok=false,err="bad_payload"})end
  for k in pairs(p)do if k~="event_id"and k~="limit"and k~="cursor"and k~="session_epoch"then return nk.json_encode({ok=false,err="bad_payload"})end end
  if type(p.event_id)~="string"or#p.event_id>80 then return nk.json_encode({ok=false,err="bad_event_id"})end
  local limit=p.limit or 20
  if type(limit)~="number"or limit~=math.floor(limit)or limit<1 or limit>50 or(p.cursor~=nil and(type(p.cursor)~="string"or#p.cursor>2048))then
    return nk.json_encode({ok=false,err="bad_payload"})
  end
  local ok,result=pcall(function()
    local e=M.event();if not e then return{ok=false,err="boss_unavailable"}end
    if p.event_id~=e.id then return{ok=false,err="event_changed"}end
    return Ranking.list(uid,e.id,limit,p.cursor)
  end)
  if not ok then nk.logger_warn("boss ranking unavailable: "..tostring(result));result={ok=false,err="ranking_unavailable"}end
  return nk.json_encode(result)
end
-- Fanout is a recoverable service match, present only while a defeated event
-- has mail to deliver. Per-owner proof and mailbox admission share one CAS.
function M.dispatch_page(event_id)
  local e,ev=M.get_event(event_id);if not e or e.status~="dead"or e.mail_done then return true end
  local rows,cursor=nk.storage_list("",M.PLAYER_COLLECTION,100,e.mail_cursor)
  assert(type(rows)=="table","boss_fanout_unavailable")
  for _,row in ipairs(rows)do
    local uid=row.user_id
    if row.key==event_id and uid then
      local p,pv=read(uid,M.PLAYER_COLLECTION,event_id)
      if p and p.active_attempt_id then
        local a=M.get_attempt(uid,p.active_attempt_id)
        if a and(a.status=="reserved"or a.status=="bound")then assert(M.finish(uid,a.id,"boss_defeated",a.snapshot),"boss_orphan_refund_deferred")end
        p,pv=read(uid,M.PLAYER_COLLECTION,event_id)
      end
      if p and p.qualified and not p.reward_mail_created then
        local c=e.config;local attachments={{def_id=c.recipes[math.random(#c.recipes)],amount=1},{def_id="tesseract",amount=1},
          {def_id=c.elixirs[math.random(#c.elixirs)],amount=1},{def_id="scroll_pve_bounty",amount=1}}
        for _,k in ipairs({"gold","ore","matter"})do attachments[#attachments+1]={resource=k,amount=c.rewards[k]}end
        p.reward_mail_created=true;p.reward_mail_id="global_boss:"..event_id
        local ok=Mail.deliver(uid,{id=p.reward_mail_id,title=c.reward_title,body=c.reward_body,created_at=e.defeated_at,
          attachments=attachments,event_id=event_id},op(uid,M.PLAYER_COLLECTION,event_id,p,pv))
        assert(ok,"boss_mail_deferred")
      end
    end
  end
  return retry(function()
    local current,ver=M.get_event(event_id)
    if current.mail_cursor~=e.mail_cursor then return current.mail_done==true end
    current.mail_cursor=cursor and cursor~=""and cursor or nil
    if not current.mail_cursor then current.mail_done=true;current.mail_completed_at=os.time()end
    local writes={op(SYSTEM,M.COLLECTION,event_id,current,ver)}
    if current.mail_done then
      local i,iv=read(SYSTEM,M.COLLECTION,"index");i.pending=i.pending or{};i.pending[event_id]=nil
      writes[#writes+1]=op(SYSTEM,M.COLLECTION,"index",i,iv)
    end
    nk.storage_write(writes);return current.mail_done==true
  end)
end
function M.ensure_worker(event_id)
  return retry(function()
    local e,ev=M.get_event(event_id);if not e or e.status~="dead"or e.mail_done then return true end
    if e.worker_match_id then local yes=live(e.worker_match_id);if yes then return true end end
    if(e.worker_until or 0)>os.time()then return false end
    e.worker_nonce=nk.uuid_v4();e.worker_until=os.time()+20
    nk.storage_write({op(SYSTEM,M.COLLECTION,event_id,e,ev)})
    nk.match_create("duel_global_boss_dispatcher",{event_id=event_id,nonce=e.worker_nonce});return true
  end)
end
function M.bind_worker(event_id,nonce,mid)
 return retry(function()
  local e,ev=M.get_event(event_id)
  if e and e.worker_match_id==mid then return true end
  assert(e and e.worker_nonce==nonce,"boss_worker_claim_invalid")
  e.worker_match_id=mid;e.worker_until=nil;e.worker_nonce=nil
  nk.storage_write({op(SYSTEM,M.COLLECTION,event_id,e,ev)});return true
 end)
end
function M.recover_workers()
 local i=read(SYSTEM,M.COLLECTION,"index");local all=true
 for id in pairs(i and i.pending or{})do local ok,ready=pcall(M.ensure_worker,id);if not ok or not ready then all=false end end
 if i and i.current then local ok,ready=pcall(Ranking.ensure_worker,i.current);if not ok or not ready then all=false end end
 return all
end
return M
