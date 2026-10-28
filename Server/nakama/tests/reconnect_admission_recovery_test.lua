-- Actual core callbacks, Resume/Prestart, receipts, consumables and journal.
-- Only authenticated transport, clock and transactional storage are simulated.
package.path="Server/nakama/?.lua;Server/nakama/modules/?.lua;Server/nakama/tests/?.lua;"..package.path
local Fixture=require("storage_fault_fixture")
local source_file=assert(io.open("Server/nakama/modules/duel_match3.lua"));local source=source_file:read("*a");source_file:close()
local function span(a,b)local first=assert(source:find(a,1,true));return source:sub(first,assert(source:find(b,first,true))-1)end
local passed=0
local function test(name,body)
  local F=Fixture.new();F.install();F.seed("a");F.seed("b")
  for _,name in ipairs({"duel_match3_lifecycle","duel_match3_resume","duel_match3_prestart","duel_consumables"})do package.loaded["modules."..name]=nil;package.loaded[name]=nil end
  local epoch={a=7,b=7}
  local function guard(uid,p)local x=F.nk.json_decode(p);return x and x.session_epoch==epoch[uid],"session_stale"end
  F.nk.account_get_id=function(uid)return{user={username=uid,display_name=uid,metadata={session_epoch=epoch[uid]}}}end
  F.nk.time=function()return F.clock*1000 end
  local J=require("journal_fixture_admission").wrap(require("modules.duel_match3_journal").new(),F)
  local E=require("modules.duel_match3_pve_entry").new({read_pve_progress=F.read_progress,build_pve_progress_write=F.build_progress,
    journal=J,now=function()return F.clock end,energy_max=function()return 100 end})
  local Race=require("modules.duel_match3_race_entry").new({read_pve_progress=F.read_progress,build_pve_progress_write=F.build_progress,finalization_journal=J})
  local function sheet(uid)local s,v=F.get("sheet",uid,"v1");s=s or{};s._storage_version=v or"*";return s end
  local function sheet_write(uid,s)local v=Fixture.copy(s);v._storage_version=nil;return{collection="sheet",key="v1",user_id=uid,value=v,version=s._storage_version or"*",permission_read=1,permission_write=0}end
  F.put("sheet","a","v1",{consumables={armed={item_id="elixir_assault",activation_id="owned-arm",armed_at=1000}}})
  local C=require("modules.duel_consumables").new({read_sheet=sheet,build_sheet_write=sheet_write,is_human=function(u)return u=="a"or u=="b"end,
    journal=function()return J end,bind_extra_writes=function(s,u)if s.mode=="pve"and not s.pvp_race then return E.consume_writes(u,s.pve_run.launch_token,s.stats_result_id)end;return{}end})
  local Lifecycle=require("modules.duel_match3_lifecycle")
  local Presence=require("modules.duel_match3_presence")
  local cfg={TICK_RATE=6,RECONNECT_GRACE_SECONDS=300,OP_BOARD_SYNC=10,OP_PLAYER_LEFT=12,OP_ACTION_REQUEST=13,OP_ACTION_REJECT=14,OP_SELECTION_SYNC=15,OP_SNAPSHOT_REQUEST=16,OP_GAME_OVER=11}
  local sent,kicked={},{}
  local D={broadcast_message=function(op,p,targets)sent[#sent+1]={op=op,payload=p,targets=targets}end,match_kick=function(ps)for _,p in ipairs(ps)do kicked[#kicked+1]=p end end}
  local R={Lifecycle=Lifecycle,Presence=Presence,Consumables=C,Journal=J,Rounds=require("modules.duel_match3_rounds"),
    RateLimit={filter_match_messages=function(_,m)return m end}}
  local env=setmetatable({Runtime=R,CFG=cfg,nk=F.nk,Guard={is_epoch_stale_for_match=function(uid,e)return epoch[uid]>e end},
    count_present_players=function(s)local n=0;for _ in pairs(s.presences)do n=n+1 end;return n end,
    make_sync_msg=function(s)return{board=s.board,activeUserId=s.active_user_id}end,
    turn_seconds_for_state=function()return 30 end,bot_think_delay_ticks=function()return 6 end,broadcast_sync=function()end,
    on_pve_owner_socket_gone=function()end,send_reject=function(_,_,why)sent[#sent+1]={reject=why}end,
    other_player_id=function(s,uid)return s.players_sorted[1]==uid and s.players_sorted[2]or s.players_sorted[1]end,
    is_pvp_race=function(s)return s.pvp_race end}, {__index=_G})
  local function load(code)local f=assert(loadstring(code));setfenv(f,env);return f()end
  load(span("Runtime.finish_arena_result = function","-- Board dimensions:"))
  load(span("Runtime.forfeit_match = function","local function match_leave"))
  local attempt,join=load(span("local function match_join_attempt","local function augment_arena_game_over_payload").."\nreturn match_join_attempt,match_join")
  local leave,loop=load(span("local function match_leave","local function match_terminate").."\nreturn match_leave,match_loop")
  local signal=load(span("local function match_signal","-- Каталог предметов").."\nreturn match_signal")
  R.cancel_unused_journal_tickets=function(s)return require("modules.duel_match3_journal_admission").cancel_unused(s,J)end
  R.Prestart=require("modules.duel_match3_prestart").new({consumables=C,pve=E,race=Race,energy_max=function()return 100 end,emit=R.emit_game_over,
    can_start=function(s)for _,uid in ipairs(s.players_sorted or{})do if uid:sub(1,7)~="zz-bot-"and R.Resume.stale(s,uid)then return false,"session_stale"end end;return true end})
  local function request_id(s,uid)if s.pvp_race then return Race.request_id(uid,s.stats_result_id,s.match_id)end;return(s.pve_run or{}).request_id end
  local LM=Lifecycle.new({guard=guard,forfeit=R.forfeit_match,cancel=R.Prestart.cancel,
    cancel_request=function(uid,id)local ok,err,entry,p=E.cancel_request(uid,id);if err~="admission_not_found"then return ok,err,entry,p end;return Race.cancel_request(uid,id)end,
    request_id=request_id,energy_max=function()return 100 end,
    refund_state=E.refund_state,
    resolve_cancel=function(uid,e)return R.Resume.recover(uid,e,{})end,
    current_progression=function(uid)local p=F.read_progress(uid);return{energy=p.energy,energy_max=100,gold=p.gold,ore=p.ore,matter=p.matter,ingots=23,key_items={miner_key=2,dark_key=1}}end})
  local Resume=require("modules.duel_match3_resume")
  R.Resume=Resume.new({guard=guard,lifecycle=Lifecycle,presence=Presence,epoch=function(u)return epoch[u]end,
    request_id=request_id,
    grace_ticks=function()return 1800 end,expire=R.forfeit_match,now=function()return F.clock end,recover_entries=E.recover,bot_profile=function()return{name="mine"}end})
  local active
  F.nk.match_signal=function(mid,p)
    assert(active and mid==active.match_id,"match_not_found")
    if p.kind=="duel_recover"then local _,reply=R.Resume.signal(D,active,10,p);return reply end
    local _,reply=LM.signal(D,active,p);return reply
  end
  local function mine()
    local paid,why,entry=E.reserve("a",{{resource="energy",amount=15}},{request_key="mine:7",request_id="request-a",floor=1,difficulty="easy"});assert(paid,why)
    local s={match_id="mid",stats_result_id="run",owner_user_id="a",owner_session_epoch=7,mode="pve",bot_user_id="zz-bot-mine_1",bot_id="mine_1",
      pve_run={floor=1,difficulty="easy",launch_token=entry.token,request_id="request-a"},started=true,ended=false,_prestart_pending=true,
      presences={a={user_id="a",session_id="old",node="node"}},players_sorted={"a","zz-bot-mine_1"},
      stats={a={hp=150,max_hp=150},["zz-bot-mine_1"]={hp=150,max_hp=150}},board={1},active_user_id="a",
      turn_deadline_paused=true,turn_pause_started_tick=0,turn_deadline_tick=0,reconnect_deadlines={},invited={}}
    assert(E.bind("a",entry.token,s.match_id,s.stats_result_id));F.matches.mid={match_id="mid"}
    assert(require("modules.duel_match3_journal_admission").admit(s,{journal=J,is_human=function(u)return u=="a"end,consumable_eligible=function()return true end,pve_entry=E}))
    assert(R.Resume.track(s));assert(C.preview_match(s,{"a"}));C.apply_match(s);active=s;return s,entry
  end
  local function rpc(which,uid,p)p=p or{};if p.session_epoch==nil then p.session_epoch=epoch[uid]end;return(which=="recover"and R.Resume.rpc or which=="cancel"and LM.cancel_rpc or LM.rpc)({user_id=uid},p)end
  local ok,err=pcall(body,F,R,E,Race,Resume,epoch,D,env,mine,rpc,attempt,join,leave,loop,sent,kicked,signal)
  assert(ok,name..": "..tostring(err));passed=passed+1;print("PASS "..name)
end

test("current epoch recovery replaces owner binding and revokes old presence before delayed leave",function(F,R,E,Race,M,epoch,D,env,mine,rpc,attempt,join,leave,loop,sent,kicked)
  local s=mine();epoch.a=8;local result=rpc("recover","a",{})
  assert(result.ok and result.active and result.match_id=="mid"and result.a_user_id=="a"and result.b_user_id==s.bot_user_id)
  assert(result.can_cancel and s.owner_session_epoch==8 and not s.presences.a and #kicked==1)
  local fresh={user_id="a",session_id="fresh",node="node"}
  local _,allowed=attempt({},D,11,s,fresh,{session_epoch=8});assert(allowed)
  join({},D,11,s,{fresh});leave({},D,12,s,{{user_id="a",session_id="old",node="node"}})
  assert(s.presences.a==fresh and not s.reconnect_deadlines.a and s.board[1]==1 and F.read_progress("a").energy==85)
end)
test("same epoch recovery and stale or foreign calls cannot advance epoch or clear active pointer",function(F,R,E,Race,M,epoch,D,env,mine,rpc)
  local s=mine();assert(rpc("recover","a",{}).ok and s.presences.a)
  assert(rpc("recover","a",{session_epoch=6}).err=="session_stale")
  assert(rpc("recover","b",{match_id="mid"}).err=="not_participant")
  assert(rpc("cancel","b",{match_id="mid"}).err=="not_participant")
  assert(F.get(M.COLLECTION,"a",M.KEY).status=="active"and s.owner_session_epoch==7 and not s.ended)
end)
test("delayed legacy boss resume cannot downgrade a newer authenticated owner epoch",function(F,R,E,Race,M,epoch,D,env,mine,rpc,attempt,join,leave,loop,sent,kicked,signal)
  local s=mine();s.global_boss={event_id="event",attempt_id="attempt"};s._boss_config={name="Boss"}
  epoch.a=8;s.owner_session_epoch=8;s._recovery_epochs.a=8
  local _,reply=signal({},D,2,s,{kind="boss_resume",user_id="a",session_epoch=7})
  assert(not reply.ok and reply.err=="session_stale"and s.owner_session_epoch==8 and s.presences.a)
end)
test("cluster or terminal storage lookup failure never clears a live pointer",function(F,R,E,Race,M,epoch,D,env,mine,rpc)
  mine();F.probe_failure=true;assert(rpc("recover","a",{}).err=="match_probe_unavailable")
  F.probe_failure=false;local lookup=R.Lifecycle.lookup;R.Lifecycle.lookup=function()error("terminal storage unavailable")end
  assert(rpc("recover","a",{}).err=="recovery_storage_unavailable");R.Lifecycle.lookup=lookup
  assert(F.get(M.COLLECTION,"a",M.KEY).status=="active"and F.read_progress("a").energy==85)
end)
test("lost match proof recovers one orphan refund before clearing only the exact old pointer",function(F,R,E,Race,M,epoch,D,env,mine,rpc)
  mine();F.matches.mid=nil
  assert(rpc("recover","a",{}).pending_admission and F.read_progress("a").energy==85)
  F.clock=F.clock+4;local result=rpc("recover","a",{})
  assert(result.ok and not result.active and F.read_progress("a").energy==100)
  assert(F.get(M.COLLECTION,"a",M.KEY).status=="closed"and rpc("recover","a",{}).ok and F.read_progress("a").energy==100)
end)
test("reveal cancel refunds exact payment once, preserves armed consumable and creates no played result",function(F,R,E,Race,M,epoch,D,env,mine,rpc,attempt,join,leave,loop,sent)
  local s=mine();local result=rpc("cancel","a",{match_id="mid",request_id="request-a"})
  assert(result.ok and result.terminal and result.match_cancelled and result.energy==100 and F.read_progress("a").energy==100)
  assert(result.progression.energy==100 and result.progression.ingots==23 and result.progression.key_items.miner_key==2)
  assert(F.get("sheet","a","v1").consumables.armed.activation_id=="owned-arm"and not F.get("stats","a","summary"))
  assert(rpc("cancel","a",{match_id="mid"}).terminal and #sent==1 and loop({},D,6,s,{})==nil)
  assert(F.read_progress("a").energy==100 and rpc("forfeit","a",{match_id="mid"}).match_cancelled)
end)
test("legacy0.3.16 forfeit during reveal follows the same no-loss refund path",function(F,R,E,Race,M,epoch,D,env,mine,rpc)
  mine();local result=rpc("forfeit","a",{match_id="mid"})
  assert(result.terminal and result.match_cancelled and result.end_reason=="admission_cancelled"and F.read_progress("a").energy==100)
end)
test("capped refund is durably acknowledged and refreshed after the remaining credit is paid",function(F,R,E,Race,M,epoch,D,env,mine,rpc)
  mine();F.seed("a",{energy=100})
  local pending=rpc("cancel","a",{match_id="mid"})
  assert(pending.terminal and pending.refund_pending and pending.refund_recorded and pending.progression.energy==100)
  assert(R.Lifecycle.lookup("a","mid").refund_pending)
  F.seed("a",{energy=80});assert(E.recover("a"));assert(F.read_progress("a").energy==95)
  local complete=rpc("cancel","a",{match_id="mid"})
  assert(complete.terminal and not complete.refund_pending and complete.refund_recorded and complete.progression.energy==95)
  assert(rpc("cancel","a",{match_id="mid"}).terminal and F.read_progress("a").energy==95)
end)
test("actual OP17 atomically commits paid entry and armed consumable then cancels are denied",function(F,R,E,Race,M,epoch,D,env,mine,rpc,attempt,join,leave,loop)
  local s=mine();loop({},D,2,s,{{op_code=17,sender=s.presences.a}})
  assert(not s._prestart_pending and not s.turn_deadline_paused and F.get("duel_pve_entry","a","active").status=="consumed")
  assert(not F.get("sheet","a","v1").consumables.armed and s._consumable_bindings.a.activation_id=="owned-arm")
  local result=rpc("cancel","a",{match_id="mid"})
  assert(not result.ok and result.err=="battle_already_started"and F.read_progress("a").energy==85 and not s.ended)
  assert(R.Prestart.commit(s,3)and F.read_progress("a").energy==85)
end)
test("stale old socket OP17 cannot commit a replaced account's paid entry or armed consumable",function(F,R,E,Race,M,epoch,D,env,mine,rpc,attempt,join,leave,loop)
  local s=mine();epoch.a=8;loop({},D,2,s,{{op_code=17,sender=s.presences.a}})
  assert(s._prestart_pending and s.turn_deadline_paused and F.get("duel_pve_entry","a","active").status=="bound")
  assert(F.get("sheet","a","v1").consumables.armed and F.read_progress("a").energy==85)
  loop({},D,60,s,{})
  assert(s._prestart_pending and F.get("duel_pve_entry","a","active").status=="bound"and F.get("sheet","a","v1").consumables.armed)
end)
test("stale still-current socket OP12 cannot cancel, refund or surrender after another device claims",function(F,R,E,Race,M,epoch,D,env,mine,rpc,attempt,join,leave,loop)
  local s=mine();epoch.a=8;loop({},D,2,s,{{op_code=12,sender=s.presences.a}})
  assert(not s.ended and s._prestart_pending and F.read_progress("a").energy==85 and F.get("sheet","a","v1").consumables.armed)
end)
test("stale started reveal expires at the immutable twenty-second admission deadline with one refund",function(F,R,E,Race,M,epoch,D,env,mine,rpc,attempt,join,leave,loop)
  local s=mine();s.join_deadline_tick=120;epoch.a=8
  assert(loop({},D,119,s,{})==s and not s.ended and F.read_progress("a").energy==85)
  assert(loop({},D,120,s,{})==nil and s._terminal_delivered and F.read_progress("a").energy==100)
  assert(F.get("sheet","a","v1").consumables.armed and not F.get("stats","a","summary"))
  assert(rpc("cancel","a",{match_id="mid"}).terminal and F.read_progress("a").energy==100)
end)
test("deadline refund storage failure keeps a CAS retry instead of acknowledging or losing the debit",function(F,R,E,Race,M,epoch,D,env,mine,rpc,attempt,join,leave,loop)
  local s=mine();s.join_deadline_tick=120;epoch.a=8;F.fail_before=true
  assert(loop({},D,120,s,{})==s and s._prestart_cancel_pending and not s._terminal_delivered and F.read_progress("a").energy==85)
  F.fail_before=false;assert(loop({},D,126,s,{})==nil and F.read_progress("a").energy==100)
  assert(rpc("cancel","a",{match_id="mid"}).terminal and F.read_progress("a").energy==100)
end)
test("boss prestart deadline runs before the boss-loop route and refunds its actual attempt and teleport",function(F,R,E,Race,M,epoch,D,env,mine,rpc,attempt,join,leave,loop)
  local Boss=require("modules.duel_global_boss")
  F.put("sheet","a","v1",{equipment={},inventory={},inventory_counts={},inventory_pending_loot={}})
  Boss.configure({get_defs=function()return{global_boss_teleport={kind="material",max_stack=99}}end,
    read_sheet=function(uid)local s,v=F.get("sheet",uid,"v1");s._storage_version=v;return s end,
    build_sheet_write=function(uid,s)local v=s._storage_version;s._storage_version=nil;return{collection="sheet",key="v1",user_id=uid,value=s,version=v,permission_read=1,permission_write=0}end})
  F.put(Boss.PLAYER_COLLECTION,"a","deadline-event",{attempts_used=1,active_attempt_id="deadline-attempt",requests={}})
  F.put(Boss.ATTEMPT_COLLECTION,"a","deadline-attempt",{id="deadline-attempt",event_id="deadline-event",status="bound",match_id="deadline-boss",player_actions=0})
  R.Prestart=require("modules.duel_match3_prestart").new({boss=Boss,emit=R.emit_game_over})
  R.BossBattle={loop=function()error("expired prestart escaped into the boss loop")end}
  local s={match_id="deadline-boss",stats_result_id="deadline-boss-run",mode="pve",owner_user_id="a",owner_session_epoch=7,
    started=true,ended=false,_prestart_pending=true,join_deadline_tick=120,players_sorted={"a","zz-bot-global"},
    presences={a={user_id="a",session_id="old"}},global_boss={event_id="deadline-event",attempt_id="deadline-attempt"}}
  epoch.a=8;assert(loop({},D,120,s,{})==nil and s._terminal_delivered)
  assert(F.get(Boss.ATTEMPT_COLLECTION,"a","deadline-attempt").status=="refunded")
  assert(F.get(Boss.PLAYER_COLLECTION,"a","deadline-event").attempts_used==0 and Boss.token_count(F.get("sheet","a","v1"))==1)
end)
test("the admission deadline never cancels a played battle or a tournament route",function(F,R,E,Race,M,epoch,D,env,mine,rpc,attempt,join,leave,loop)
  local s=mine();s.join_deadline_tick=120;assert(R.Prestart.commit(s,1));s.turn_deadline_paused=false;s.turn_deadline_tick=9999
  R.Prestart.cancel=function()error("played or arena battle was admission-cancelled")end
  assert(loop({},D,120,s,{})==s and not s.ended and F.read_progress("a").energy==85)
  s._prestart_pending=true;s.arena_mirror={};s.turn_deadline_paused=false
  assert(loop({},D,120,s,{})==s and not s.ended)
end)
test("start/cancel ordering and storage failure never yield consumed elixir plus a free paid match",function(F,R,E,Race,M,epoch,D,env,mine,rpc,attempt,join,leave,loop)
  local s=mine();F.fail_before=true;loop({},D,2,s,{{op_code=17,sender=s.presences.a}})
  assert(s._prestart_pending and s.turn_deadline_paused and F.get("sheet","a","v1").consumables.armed)
  local denied=rpc("cancel","a",{match_id="mid"});assert(not denied.terminal and s._prestart_cancel_pending)
  F.fail_before=false;assert(loop({},D,8,s,{})==nil)
  assert(rpc("cancel","a",{match_id="mid"}).terminal and F.read_progress("a").energy==100 and not R.Prestart.commit(s,9))
end)
test("request identity cancels a reserved lost-ack launch once and rejects a late bind or replay create",function(F,R,E,Race,M,epoch,D,env,mine,rpc)
  local ok,_,entry=E.reserve("a",{{resource="energy",amount=15}},{request_key="pending",request_id="pending-request"});assert(ok)
  assert(rpc("recover","a",{request_id="pending-request"}).pending_admission)
  assert(rpc("cancel","a",{request_id="different"}).err=="admission_not_found")
  local ack=rpc("cancel","a",{request_id="pending-request"});assert(ack.terminal and ack.match_cancelled and F.read_progress("a").energy==100)
  assert(not E.bind("a",entry.token,"late","run")and not E.reserve("a",{{resource="energy",amount=15}},{request_key="pending",request_id="pending-request"}))
end)
test("cold discovery repairs an expired orphan reservation instead of returning pending forever",function(F,R,E,Race,M,epoch,D,env,mine,rpc)
  assert(E.reserve("a",{{resource="energy",amount=15}},{request_key="crashed",request_id="crashed-request"}))
  local pending=rpc("recover","a",{});assert(pending.pending_admission and pending.request_id=="crashed-request")
  F.clock=F.clock+91;local after=rpc("recover","a",{})
  assert(after.ok and not after.active and not after.pending_admission and F.read_progress("a").energy==100)
end)
test("uncorrelated cancel requires the exact pending Race reference and never refunds blindly",function(F,R,E,Race,M,epoch,D,env,mine,rpc)
  assert(Race.pay("a",{{resource="matter",amount=7}}))
  local ack=rpc("cancel","a",{})
  assert(not ack.ok and ack.err=="admission_reference_required"and ack.request_id and F.read_progress("a").matter==93)
  local actual=rpc("cancel","a",{request_id=ack.request_id})
  assert(actual.terminal and actual.progression.matter==100 and rpc("cancel","a",{request_id=ack.request_id}).terminal)
  assert(rpc("cancel","a",{}).terminal and F.read_progress("a").matter==100)
end)

test("paid unbound Race discovery identifies the queue receipt and preserves its exact retry reference",function(F,R,E,Race,M,epoch,D,env,mine,rpc)
  assert(Race.pay("a",{{resource="matter",amount=7}}))
  local paid=rpc("recover","a",{})
  assert(paid.ok and paid.pending_admission and paid.pvp_race and paid.mode=="pvp"and not paid.active and not paid.match_id and paid.request_id)
  assert(F.read_progress("a").matter==93)
  assert(Race.pay("a",{{resource="matter",amount=99}})and F.read_progress("a").matter==93)
  assert(Race.bind_request("a","lost-create-request"))
  local retry=rpc("recover","a",{})
  assert(retry.ok and retry.pending_admission and retry.pvp_race and retry.mode=="pvp"and retry.request_id=="lost-create-request")
  local wrong=rpc("recover","a",{request_id="foreign-request"})
  assert(wrong.ok and not wrong.pending_admission and not wrong.active)
  local cancelled=rpc("cancel","a",{request_id=retry.request_id})
  assert(cancelled.terminal and cancelled.progression.matter==100 and F.read_progress("a").matter==100)
end)

test("pending mine reservation never advertises Race queue permission",function(F,R,E,Race,M,epoch,D,env,mine,rpc)
  assert(E.reserve("a",{{resource="energy",amount=15}},{request_key="mine:pending",request_id="mine-pending"}))
  local pending=rpc("recover","a",{})
  assert(pending.ok and pending.pending_admission and not pending.pvp_race and not pending.match_id and pending.request_id=="mine-pending")
  assert(F.read_progress("a").energy==85)
end)
test("uncorrelated delayed cancel only discovers a new match and never cancels or forfeits it",function(F,R,E,Race,M,epoch,D,env,mine,rpc)
  local s=mine();assert(R.Prestart.commit(s,1))
  local ack=rpc("cancel","a",{})
  assert(not ack.ok and ack.err=="match_reference_required"and ack.match_id==s.match_id and not s.ended and F.read_progress("a").energy==85)
end)
test("old terminal pointer cannot hide a newly paid pending Race admission on cold recovery",function(F,R,E,Race,M,epoch,D,env,mine,rpc)
  mine();assert(rpc("cancel","a",{match_id="mid"}).terminal)
  assert(Race.pay("a",{{resource="matter",amount=7}}))
  local current=rpc("recover","a",{})
  assert(current.ok and current.pending_admission and current.pvp_race and current.mode=="pvp"and current.request_id and F.read_progress("a").matter==93)
end)
test("valid action before OP17 commits the receipt before effects and cannot later refund",function(F,R,E,Race,M,epoch,D,env,mine,rpc,attempt,join,leave,loop)
  local s=mine();local effects=0
  env.parse_action=function()return{actionType=1}end
  env.validate_action_basic=function()return true end
  env.resolve_action=function()
    assert(F.get("duel_pve_entry","a","active").status=="consumed"and not s._prestart_pending)
    effects=effects+1;return true,nil,false,true,nil
  end
  env.finish_turn_and_broadcast=function()end
  loop({},D,1,s,{{op_code=13,sender=s.presences.a,data={}}})
  assert(effects==1 and not s._prestart_pending and rpc("cancel","a",{match_id="mid"}).err=="battle_already_started")
  assert(F.read_progress("a").energy==85 and not F.get("sheet","a","v1").consumables.armed)
end)
test("cancel serialized before an action prevents every gameplay effect and charge reuse",function(F,R,E,Race,M,epoch,D,env,mine,rpc,attempt,join,leave,loop)
  local s=mine();env.parse_action=function()error("cancelled action was parsed")end
  assert(rpc("cancel","a",{match_id="mid"}).terminal)
  assert(loop({},D,1,s,{{op_code=13,sender=s.presences.a,data={}}})==nil and F.read_progress("a").energy==100)
end)
test("request identity cannot cancel a different bound match even with an owned match ID",function(F,R,E,Race,M,epoch,D,env,mine,rpc)
  local s=mine();assert(rpc("cancel","a",{match_id="mid",request_id="different"}).err=="admission_mismatch")
  assert(not s.ended and F.read_progress("a").energy==85)
end)
test("prepared reward or consumed receipt cannot be refunded by the trusted prestart helper",function(F,R,E,Race,M,epoch,D,env,mine,rpc)
  local s,entry=mine();assert(E.consume("a",entry.token,s.stats_result_id))
  local ok,err=E.cancel_before_start("a",entry.token,s.stats_result_id,s.match_id)
  assert(not ok and err=="battle_already_started"and F.read_progress("a").energy==85)
end)
test("Race prestart cancellation refunds both original stakes and is idempotent without result rewards",function(F,R,E,Race,M,epoch,D,env,mine,rpc)
  assert(Race.pay("a",{{resource="matter",amount=7}}));assert(Race.pay("b",{{resource="matter",amount=11}}))
  assert(Race.claim({"a","b"},"race-run","race-mid"))
  assert(Race.cancel_before_start("a","race-run","race-mid"))
  assert(Race.cancel_before_start("b","race-run","race-mid"))
  assert(F.read_progress("a").matter==100 and F.read_progress("b").matter==100)
  assert(not F.get("stats","a","summary")and F.get("duel_race_entry","a","run:race-run").status=="refunded")
end)
test("transient practice neither registers nor recovers an active pointer",function(F,R,E,Race,M,epoch,D,env,mine,rpc)
  local n=#F.writes;assert(R.Resume.track({global_boss={practice=true},owner_user_id="a"}))
  assert(#F.writes==n and not F.get(M.COLLECTION,"a",M.KEY))
end)
test("expired reconnect grace returns its authoritative cancellation without re-binding or reopening entry",function(F,R,E,Race,M,epoch,D,env,mine,rpc,attempt,join,leave,loop)
  local s=mine();local old=s.presences.a;leave({},D,0,s,{old});s.reconnect_deadlines.a=6;epoch.a=8
  local result=rpc("recover","a",{})
  assert(result.terminal and result.match_cancelled and s.owner_session_epoch==7 and not s.presences.a)
  assert(F.read_progress("a").energy==100 and loop({},D,12,s,{})==nil)
  local _,allowed=attempt({},D,12,s,{user_id="a",session_id="late"},{session_epoch=8});assert(not allowed)
end)
for _,one_present in ipairs({false,true})do
test("ownerless friend Race admission refunds both receipts with no win: one-present="..tostring(one_present),function(F,R,E,Race,M,epoch,D,env,mine,rpc,attempt,join,leave,loop,sent)
  F.seed("b",{gold=678})
  local s={match_id="friend-race",stats_result_id="friend-run",mode="pvp",pvp_race=true,started=false,ended=false,_prestart_pending=true,
    invited={a=true,b=true},players_sorted={},stats={},presences=one_present and{a={user_id="a",session_id="a1"}}or{},join_deadline_tick=12,reconnect_deadlines={}}
  assert(Race.pay("a",{{resource="matter",amount=7}})and Race.pay("b",{{resource="matter",amount=11}}))
  assert(require("modules.duel_match3_journal_admission").admit(s,{journal=R.Journal,is_human=function(u)return u=="a"or u=="b"end,consumable_eligible=function()return false end}))
  assert(Race.claim({"a","b"},s.stats_result_id,s.match_id,s._journal_tickets))
  assert(loop({},D,12,s,{})==nil and #sent==1 and sent[1].payload.matchCancelled)
  assert(F.read_progress("a").matter==100 and F.read_progress("b").matter==100 and not F.get("stats","a","summary"))
  local a,b=rpc("cancel","a",{match_id=s.match_id}),rpc("cancel","b",{match_id=s.match_id})
  assert(a.terminal and b.terminal and a.progression.gold==100 and b.progression.gold==678)
end)
end
print("Reconnect admission recovery: "..passed.." passed")
