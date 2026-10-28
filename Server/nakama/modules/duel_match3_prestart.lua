-- Server-owned reveal/entry boundary. No client-controlled resource amounts,
-- winners or start timestamps. An accepted start and cancel are serialized by
-- the authoritative match; the durable receipt transaction owns each debit.
local M={}
function M.new(d)
  local P={}
  function P.commit(state,tick)
    if state._prestart_pending~=true then return true end
    if state.ended then return false,"match_ended"end
    if d.can_start then local allowed,why=d.can_start(state);if not allowed then return false,why end end
    if tick and tick<(state._start_retry_tick or 0)then return false,"battle_start_pending"end
    if state.global_boss and state.global_boss.practice~=true then
      local ok,err=d.boss.mark_started(state.owner_user_id,state.global_boss.attempt_id)
      if not ok then state._start_requested=true;state._start_retry_tick=(tick or 0)+(d.tick_rate or 6);return false,err end
    elseif not state.global_boss then
      local ok,err=d.consumables.bind_match(state,state.players_sorted)
      if not ok then state._start_requested=true;state._start_retry_tick=(tick or 0)+(d.tick_rate or 6);return false,err end
      d.consumables.apply_match(state)
    end
    state._prestart_pending=false;state._start_requested=nil
    return true
  end
  function P.cancel(dispatcher,state,uid)
    if state._terminal_delivered then return true end
    if state.arena_mirror~=nil then return false,"cancel_not_allowed"end
    if state._prestart_pending~=true then return false,"battle_already_started"end
    if state.ended and not state._prestart_cancel then return false,"match_ended"end
    if not uid then
      uid=state.owner_user_id
      if not uid then
        local users={};for id in pairs(state.invited or{})do if id:sub(1,7)~="zz-bot-"then users[#users+1]=id end end
        table.sort(users);uid=users[1]
      end
    end
    if not state._prestart_cancel then state._prestart_cancel={user_id=uid}end
    state.ended=true;state.bot_turn_pending=false;state.bot_turn_ready_tick=0
    local ok,err,receipt,progress=true
    if state.global_boss then
      if state.global_boss.practice~=true then ok,err=d.boss.refund(state.owner_user_id,state.global_boss.attempt_id,"admission_cancelled")end
    elseif state.pvp_race then
      ok,err=d.race.cancel_before_start(uid,state.stats_result_id,state.match_id)
      if ok and d.progress then progress=d.progress(uid)end
    elseif state.mode=="pve"and state.pve_run and state.pve_run.launch_token then
      ok,err,receipt,progress=d.pve.cancel_before_start(state.owner_user_id,state.pve_run.launch_token,state.stats_result_id,state.match_id)
    end
    if not ok then
      if err=="battle_already_started"then
        -- A start CAS can commit before its transport acknowledgement is lost.
        -- Reconcile that receipt, resume the battle and never refund it.
        state.ended=false;state._prestart_cancel=nil;state._prestart_cancel_pending=nil
        state._start_requested=true;state._start_retry_tick=0;P.commit(state)
        return false,"battle_already_started"
      end
      state._prestart_cancel_pending=true;return false,err
    end
    state._prestart_cancel_pending=nil
    local payload={winnerUserId="",isDraw=true,matchCancelled=true,endReason="admission_cancelled",retryAllowed=true,
      refundPending=receipt and receipt.status=="refund_pending"or false,
      refundRecorded=receipt and(receipt.status=="refund_pending"or receipt.status=="refunded")or false}
    if progress and not state.pvp_race then
      payload.energy=progress.energy;payload.energyMax=d.energy_max(uid)
      payload.gold=progress.gold;payload.ore=progress.ore;payload.matter=progress.matter
    end
    if state.global_boss then payload.globalBoss=true;payload.globalBossPractice=state.global_boss.practice==true;payload.bossAttemptRefunded=state.global_boss.practice~=true end
    return d.emit(dispatcher,state,payload)
  end
  return P
end
return M
