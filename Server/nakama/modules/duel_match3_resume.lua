-- Server-owned, bounded current-match discovery. A pointer is only a candidate:
-- cluster liveness AND the authoritative participant signal prove recovery.
local nk = require("nakama")
local M = { COLLECTION = "duel_match3_active", KEY = "v1", DISCOVERY_GRACE=3 }
local function human(uid) return type(uid)=="string" and uid~="" and uid:sub(1,7)~="zz-bot-" end
local function read(uid, collection, key)
  local rows=nk.storage_read({{collection=collection,key=key,user_id=uid}})
  assert(type(rows)=="table","recovery_storage_unavailable")
  local row=rows[1]
  if row and type(row.value)=="string" then row.value=nk.json_decode(row.value) end
  assert(not row or type(row.value)=="table","invalid_recovery_record")
  return row
end
local function write(uid,value,version)
  return {collection=M.COLLECTION,key=M.KEY,user_id=uid,value=value,version=version or"*",permission_read=0,permission_write=0}
end
local function probe(mid)
  local ok,match,err=pcall(nk.match_get,mid)
  if not ok or err~=nil then return nil,"match_probe_unavailable" end
  return match~=nil
end
local function valid_id(v) return type(v)=="string" and #v>0 and #v<=160 end
local function valid_request(v) return v==nil or type(v)=="string" and #v>0 and #v<=80 and v:match("^[%w_-]+$")~=nil end
M.valid_request=valid_request
function M.new(d)
  local R={}
  local now=d.now or os.time
  function R.stale(state,uid)
    local snapshot=(state._recovery_epochs or{})[uid]
    if snapshot==nil and uid==state.owner_user_id then snapshot=state.owner_session_epoch end
    if snapshot==nil then return false end
    local ok,current=pcall(d.epoch,uid)
    return not ok or type(current)~="number"or current~=snapshot
  end
  function R.describe(state,uid)
    local run,arena=state.pve_run or{},state.arena_mirror or{}
    local ids={};for _,id in ipairs(state.players_sorted or{})do ids[#ids+1]=id end
    if #ids==0 then
      if state.mode=="pve" then ids={state.owner_user_id,state.bot_user_id}
      else for id in pairs(state.invited or{})do ids[#ids+1]=id end;table.sort(ids)end
    end
    local opponent="";for _,id in ipairs(ids)do if id~=uid then opponent=id;break end end
    local solo=state.mode=="pve"and not state.pvp_race and state.arena_mirror==nil
    local bot=solo and type(d.bot_profile)=="function" and d.bot_profile(state.bot_id,run.difficulty)or{}
    local name=state.bot_name or(bot and bot.name)or""
    local bracket_name=opponent==arena.uid_a and arena.display_a or opponent==arena.uid_b and arena.display_b or nil
    if type(bracket_name)=="string"and bracket_name~=""then name=bracket_name end
    if state.global_boss then
      local event=d.boss_event and d.boss_event(state.global_boss.event_id)
      local config=state._boss_config or(event and event.config)or{}
      name=config.name or name
    end
    if human(opponent)and not(type(bracket_name)=="string"and bracket_name~="")then
      local ok,a=pcall(nk.account_get_id,opponent)
      if ok and a and a.user then name=a.user.display_name or a.user.username or""end
    end
    return {ok=true,active=not state.ended,terminal=state._terminal_delivered==true,match_id=state.match_id,
      mode=state.mode,pvp_pro=state.pvp_pro==true,pvp_race=state.pvp_race==true,
      started=state.started==true,battle_started=state._prestart_pending==false,
      can_cancel=state._prestart_pending==true and not state.ended and state.arena_mirror==nil,
      a_user_id=ids[1]or"",b_user_id=ids[2]or"",opponent_user_id=opponent,
      opponent_is_bot=not human(opponent),opponent_name=name,bot_id=state.bot_id or"",
      bot_user_id=state.bot_user_id or"",bot_name=name,name=name,
      floor=tonumber(run.floor)or 0,difficulty=run.difficulty or"easy",affix=run.affix or"",
      request_id=(d.request_id and d.request_id(state,uid))or run.request_id or"",is_global_boss=state.global_boss~=nil,
      event_id=state.global_boss and state.global_boss.event_id or"",
      attempt_id=state.global_boss and state.global_boss.attempt_id or"",
      arena_tournament_id=arena.tournament_id or"",arena_round=arena.round or"",
      arena_kind=arena.kind or"",arena_bet_tier=arena.bet_tier or""}
  end
  local function clear(uid,row,reason)
    if not row or row.value.status~="active"then return end
    local value={schema=1,status="closed",match_id=row.value.match_id,closed_at=os.time(),reason=reason}
    nk.storage_write({write(uid,value,row.version)})
  end
  -- Called at server admission, before any board can accept input. Never runs
  -- for a transient boss practice; one row per account has bounded retention.
  function R.track(state,extra)
    if state.global_boss and state.global_boss.practice==true then return true end
    local users,seen={},{}
    local function add(uid)if human(uid)and not seen[uid]then users[#users+1]=uid;seen[uid]=true end end
    add(state.owner_user_id);for uid in pairs(state.invited or{})do add(uid)end
    for _,uid in ipairs(state.players_sorted or{})do add(uid)end
    for _,p in ipairs(extra or{})do add(p.user_id)end
    for attempt=1,5 do
      local ok,result,err=pcall(function()
        local writes={}
        for _,uid in ipairs(users)do
          local row=read(uid,M.COLLECTION,M.KEY)
          if not row or row.value.status~="active"or row.value.match_id~=state.match_id then
            if row and row.value.status=="active"then
              local live,why=probe(row.value.match_id);if live==nil then return false,why end
              local terminal=d.lifecycle.lookup(uid,row.value.match_id)
              if not terminal and(live or now()<(row.value.created_at or 0)+M.DISCOVERY_GRACE)then return false,"match_in_progress"end
            end
            writes[#writes+1]=write(uid,{schema=1,status="active",match_id=state.match_id,
              request_id=(d.request_id and d.request_id(state,uid))or(state.pve_run or{}).request_id,created_at=now()},row and row.version)
          end
        end
        if #writes>0 then nk.storage_write(writes)end
        state._recovery_epochs=state._recovery_epochs or{}
        for _,uid in ipairs(users)do if state._recovery_epochs[uid]==nil and d.epoch then
          state._recovery_epochs[uid]=uid==state.owner_user_id and state.owner_session_epoch or d.epoch(uid)
        end end
        return true
      end)
      if ok then return result,err end
      if not tostring(result):find("version",1,true)or attempt==5 then return false,"recovery_storage_unavailable"end
    end
  end
  function R.signal(dispatcher,state,tick,p)
    if not d.lifecycle.participant(state,p.user_id)then return state,{ok=false,err="not_participant"}end
    if state.global_boss and state.global_boss.practice==true then return state,{ok=false,err="practice_not_recoverable"}end
    local allowed,err=d.guard(p.user_id,nk.json_encode({session_epoch=p.session_epoch}))
    if not allowed then return state,{ok=false,err=err}end
    local current_ok,current=pcall(d.epoch,p.user_id)
    if not current_ok then return state,{ok=false,err="session_lookup_unavailable"}end
    if current~=tonumber(p.session_epoch)then return state,{ok=false,err="session_stale"}end
    if p.request_id then
      local expected=(d.request_id and d.request_id(state,p.user_id))or(state.pve_run or{}).request_id
      if p.request_id~=expected then return state,{ok=false,err="admission_mismatch"}end
    end
    if state.ended then
      local terminal=d.lifecycle.lookup(p.user_id,state.match_id)
      return state,terminal or{ok=false,err="result_pending"}
    end
    if (state.reconnect_deadlines or{})[p.user_id]and tick>=state.reconnect_deadlines[p.user_id]
        and not(state.presences or{})[p.user_id]then
      if d.expire then d.expire(dispatcher,state,p.user_id,"reconnect_timeout")end
      local terminal=d.lifecycle.lookup(p.user_id,state.match_id)
      return state,terminal or{ok=false,active=false,err="reconnect_timeout"}
    end
    state._recovery_epochs=state._recovery_epochs or{}
    local previous=state._recovery_epochs[p.user_id]
    if previous==nil and p.user_id==state.owner_user_id then previous=state.owner_session_epoch end
    if previous~=nil and tonumber(p.session_epoch)>previous then
      local old=(state.presences or{})[p.user_id]
      if old then
        -- Revoke the old socket BEFORE updating the combat epoch. A delayed
        -- leave then cannot erase a subsequently joined replacement.
        d.presence.leave(state,{old},tick,d.grace_ticks(state))
        if dispatcher.match_kick then dispatcher.match_kick({old})end
      end
    end
    state._recovery_epochs[p.user_id]=tonumber(p.session_epoch)
    if p.user_id==state.owner_user_id then state.owner_session_epoch=tonumber(p.session_epoch)end
    local descriptor=R.describe(state,p.user_id)
    if descriptor.is_global_boss and state._boss_config then descriptor.bot_name=state._boss_config.name;descriptor.opponent_name=state._boss_config.name end
    return state,descriptor
  end
  -- Internal entry point receives only an already epoch-checked user. Even an
  -- explicit match ID never authorizes a foreign match: its signal must prove it.
  function R.recover(uid,epoch,p)
    p=p or{}
    local row=read(uid,M.COLLECTION,M.KEY)
    local candidate=p.match_id or(row and row.value.status=="active"and row.value.match_id)
    if p.request_id and not p.match_id and row and row.value.request_id~=p.request_id then candidate=nil end
    local entry
    if not candidate then
      local paid=read(uid,"duel_pve_entry","active");entry=paid and paid.value
      if entry and (entry.status=="reserved"or entry.status=="bound"or entry.status=="consumed")
          and(not p.request_id or(entry.metadata or{}).request_id==p.request_id)then
        if entry.status=="reserved"and now()>=(entry.expires_at or 0)then
          local recovered,why=d.recover_entries(uid)
          if not recovered then return {ok=false,err=why or"recovery_pending"}end
          local fresh=read(uid,"duel_pve_entry","active");entry=fresh and fresh.value
          if not entry or(entry.status~="reserved"and entry.status~="bound"and entry.status~="consumed")then return {ok=true,active=false}end
        end
        candidate=p.match_id or entry.match_id
        if not candidate and entry.status=="reserved"then return {ok=true,active=false,pending_admission=true,request_id=(entry.metadata or{}).request_id or""}end
      end
      if not candidate then
        local race=read(uid,"duel_race_entry","active");local paid_race=race and race.value
        if paid_race and(not p.request_id or paid_race.request_id==p.request_id or paid_race.receipt_id==p.request_id)then
          if paid_race.status=="bound"then candidate=paid_race.match_id;entry=paid_race
          elseif paid_race.status=="pending"then return {ok=true,active=false,pending_admission=true,mode="pvp",pvp_race=true,request_id=paid_race.request_id or paid_race.receipt_id}end
        end
      end
    end
    if not valid_id(candidate)then return {ok=true,active=false}end
    local live,err=probe(candidate);if live==nil then return {ok=false,err=err}end
    local terminal=d.lifecycle.lookup(uid,candidate)
    if terminal then
      if row and row.value.match_id==candidate then clear(uid,row,"terminal")end
      if not p.match_id and not p.request_id then
        -- A completed pointer is historical, not a cancellation/recovery
        -- target for a newly paid queue or reservation in the same account.
        local mine=read(uid,"duel_pve_entry","active");local current=mine and mine.value
        if current and current.status=="reserved"and now()>=(current.expires_at or 0)then
          local recovered,why=d.recover_entries(uid);if not recovered then return{ok=false,err=why or"recovery_pending"}end
          mine=read(uid,"duel_pve_entry","active");current=mine and mine.value
        end
        if current and current.status=="reserved"then
          return{ok=true,active=false,pending_admission=true,request_id=(current.metadata or{}).request_id or""}
        end
        if current and current.status=="bound"and current.match_id and current.match_id~=candidate then
          return R.recover(uid,epoch,{match_id=current.match_id})
        end
        local race=read(uid,"duel_race_entry","active");current=race and race.value
        if current and current.status=="pending"then
          return{ok=true,active=false,pending_admission=true,mode="pvp",pvp_race=true,request_id=current.request_id or current.receipt_id}
        end
        if current and current.status=="bound"and current.match_id and current.match_id~=candidate then
          return R.recover(uid,epoch,{match_id=current.match_id})
        end
      end
      terminal.active=false;terminal.match_id=candidate;return terminal
    end
    if not live then
      if row and row.value.status=="active"and row.value.match_id==candidate
          and now()<(row.value.created_at or 0)+M.DISCOVERY_GRACE then
        return {ok=true,active=false,pending_admission=true,match_id=candidate,request_id=p.request_id or""}
      end
      -- The server-owned pointer/paid ledger proves this user's admission;
      -- never acknowledge an arbitrary nonexistent foreign ID as their result.
      local own=row and row.value.match_id==candidate or entry and entry.match_id==candidate
      if p.match_id and not own then return {ok=false,err="match_unavailable"}end
      if d.recover_entries then local ok,why=d.recover_entries(uid);if not ok then return {ok=false,err=why or"recovery_pending"}end end
      if row and row.value.match_id==candidate then clear(uid,row,"match_gone")end
      return {ok=true,active=false,match_id=candidate}
    end
    local ok,answer=pcall(nk.match_signal,candidate,nk.json_encode({kind="duel_recover",user_id=uid,session_epoch=epoch,request_id=p.request_id}))
    if not ok then return {ok=false,err="match_signal_unavailable"}end
    local decoded,value=pcall(nk.json_decode,answer)
    if not decoded or type(value)~="table"then return {ok=false,err="match_signal_unavailable"}end
    return value
  end
  function R.rpc(ctx,payload)
    local uid=ctx and ctx.user_id;if not human(uid)then return nk.json_encode({ok=false,err="unauthorized"})end
    local p=payload
    if type(p)=="string"and #p<=512 then local ok,v=pcall(nk.json_decode,p);p=ok and v or nil end
    if type(p)~="table"then return nk.json_encode({ok=false,err="bad_payload"})end
    for k in pairs(p)do if k~="session_epoch"and k~="match_id"and k~="request_id"then return nk.json_encode({ok=false,err="bad_payload"})end end
    if p.match_id~=nil and not valid_id(p.match_id)or not valid_request(p.request_id)then return nk.json_encode({ok=false,err="bad_payload"})end
    local allowed,why=d.guard(uid,payload);if not allowed then return nk.json_encode({ok=false,err=why})end
    local ok,result=pcall(R.recover,uid,tonumber(p.session_epoch),p)
    if not ok then nk.logger_error("match recovery deferred: "..tostring(result));result={ok=false,err="recovery_storage_unavailable"}end
    return nk.json_encode(result)
  end
  return R
end
return M
