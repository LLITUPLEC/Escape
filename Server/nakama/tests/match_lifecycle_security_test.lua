-- Actual core result/leave/loop callbacks + Lifecycle + RaceEntry + Pvp + Journal.
-- Only Nakama transport/storage is simulated; deterministic ticks avoid waiting.
package.path="Server/nakama/?.lua;Server/nakama/modules/?.lua;Server/nakama/tests/?.lua;"..package.path
local Fixture=require("storage_fault_fixture")
local f=assert(io.open("Server/nakama/modules/duel_match3.lua","r"));local source=f:read("*a");f:close()
local function span(a,b)local p=assert(source:find(a,1,true));return source:sub(p,assert(source:find(b,p,true))-1)end
local passed=0
local function test(name,fn)
  local F=Fixture.new();F.install();for _,u in ipairs({"a","b","c"})do F.seed(u)end
  package.loaded["modules.duel_match3_lifecycle"]=nil;package.loaded["modules.duel_match3_stats_api"]=nil
  local codec,serial={},0
  F.nk.json_encode=function(v)serial=serial+1;local k="encoded:"..serial;codec[k]=Fixture.copy(v);return k end
  F.nk.json_decode=function(v)if type(v)=="table"then return Fixture.copy(v)end;assert(codec[v],"bad_json");return Fixture.copy(codec[v])end
  F.nk.time=function()return 1000 end
  local CFG={TICK_RATE=6,RECONNECT_GRACE_SECONDS=300,ARENA_RECONNECT_GRACE_SECONDS=20,MATCH_ADMISSION_SECONDS=20,
    TURN_READY_TIMEOUT_SECONDS=8,OP_GAME_OVER=11,OP_PLAYER_LEFT=12,OP_PEER_DISCONNECT=18,OP_BOARD_SYNC=10,
    OP_ACTION_REQUEST=13,OP_SELECTION_SYNC=15,OP_SNAPSHOT_REQUEST=16,RACE_WIN_XP=200,RACE_WIN_MATTER=4,RACE_DRAW_XP=200}
  local Ach={is_human=function(u)return u:sub(1,7)~="zz-bot-"end,flush_match_finish=function()end,
    storage_read_match3_summary_val=function(u)local v,ver=F.get("stats",u,"summary");return v or {},ver end}
  local J=require("tests.journal_fixture_admission").wrap(require("modules.duel_match3_journal").new({stats_collection="stats",stats_key="summary"}),F)
  local Stats=require("modules.duel_match3_stats_api")({CFG={STATS_COLLECTION="stats",STATS_KEY="summary"},Ach=Ach,finalization_journal=J})
  local E=require("modules.duel_match3_race_entry").new({read_pve_progress=F.read_progress,build_pve_progress_write=F.build_progress,finalization_journal=J})
  local P=require("modules.duel_match3_pvp")({nk=F.nk,CFG=CFG,read_pve_progress=F.read_progress,
    write_pve_progress=function(u,p,v)F.nk.storage_write({F.build_progress(u,p,v)})end,
    current_level_from_xp=function()return 1 end,get_race_entry=function()return E end,
    build_match_finish_effects=Stats.build_match_finish_effects,other_player_id=function(s,u)return s.players_sorted[1]==u and s.players_sorted[2] or s.players_sorted[1]end})
  E.configure_recovery(P.resume_race_result)
  local LM=require("modules.duel_match3_lifecycle")
  local sent,arena={},0
  local D={broadcast_message=function(op,p,targets)sent[#sent+1]={op=op,payload=F.nk.json_decode(p),targets=targets}end,match_kick=function()end}
  local R={Rounds=require("modules.duel_match3_rounds"),Lifecycle=LM,Presence=require("modules.duel_match3_presence"),Pvp=P,StatsApi=Stats,
    RateLimit={filter_match_messages=function(_,m)return m end},arena_on_match_finished=function()arena=arena+1 end}
  local env=setmetatable({Runtime=R,CFG=CFG,nk=F.nk,Ach=Ach,is_pvp_race=function(s)return s.pvp_race==true end,
    other_player_id=function(s,u)return s.players_sorted[1]==u and s.players_sorted[2] or s.players_sorted[1]end,
    new_stats=function()return{hp=150,max_hp=150,mana=0}end,augment_arena_game_over_payload=function()end,
    on_pve_owner_socket_gone=function()end,broadcast_sync=function()end,turn_seconds_for_state=function()return 30 end,
    count_present_players=function(s)local n=0;for _ in pairs(s.presences)do n=n+1 end;return n end,
    send_reject=function()end},{__index=_G})
  local function load(s)local c=assert(loadstring(s));setfenv(c,env);return c()end
  load(span("Runtime.finish_arena_result = function","-- Board dimensions:"))
  load(span("local function finalize_pvp_forfeit_win","local function match_leave"))
  local leave,loop=load(span("local function match_leave","local function match_terminate").."\nreturn match_leave,match_loop")
  local active
  local L=LM.new({guard=function(_,p)local v=F.nk.json_decode(p);if v.session_epoch==nil then return false,"session_epoch_required"end;return v.session_epoch==7,v.session_epoch==7 and nil or "session_stale"end,
    forfeit=function(...)return R.forfeit_match(...)end})
  F.nk.match_signal=function(mid,data)assert(active and active.match_id==mid,"match_not_found");local _,reply=L.signal(D,active,data);return reply end
  local function state(race,bot)
    local peer=bot and "zz-bot-race"or"b"
    local s={match_id="match:"..F.sequence,stats_result_id="run-"..F.sequence,started=true,ended=false,pvp_race=race==true,
      mode=bot and"pve"or"pvp",owner_user_id=bot and"a"or nil,bot_user_id=bot and peer or nil,
      players_sorted={"a",peer},invited={a=true,b=true},stats={a={hp=150,max_hp=150},[peer]={hp=150,max_hp=150}},
      presences={a={user_id="a",session_id="a1"},[peer]={user_id=peer,session_id="b1"}},reconnect_deadlines={},
      turn_deadline_paused=false,turn_deadline_tick=99999,active_user_id="a",board={1},join_deadline_tick=120}
    if bot then s.presences[peer]=nil end
    if race then
      for _,u in ipairs(s.players_sorted)do if u:sub(1,7)~="zz-bot-"then assert(E.pay(u,{{resource="matter",amount=2}}))end end
      assert(E.claim(s.players_sorted,s.stats_result_id,s.match_id))
    end
    active=s;return s
  end
  local function rpc(u,s,p)local x={match_id=s.match_id,session_epoch=7};for k,v in pairs(p or {})do x[k]=v end;return F.nk.json_decode(L.rpc({user_id=u},F.nk.json_encode(x)))end
  local ok,err=pcall(fn,F,R,LM,E,D,L,state,rpc,leave,loop,sent,function()return arena end,function()active=nil end)
  assert(ok,name..": "..tostring(err));passed=passed+1;print("PASS "..name)
end

test("reliable Race surrender settles both entries and broadcasts one shared terminal result",function(F,R,M,E,D,L,state,rpc,leave,loop,sent)
  local s=state(true);local result=rpc("a",s)
  assert(result.ok and result.accepted and result.terminal and result.winner_user_id=="b" and s.ended)
  assert(#sent==1 and sent[1].op==11 and sent[1].targets==nil and sent[1].payload.winnerUserId=="b")
  for _,u in ipairs({"a","b"})do assert(F.get("duel_race_entry",u,"active").status=="settled")end
  assert(F.read_progress("a").matter==98 and F.read_progress("b").matter==102)
  assert(F.get("stats","a","summary").losses==1 and F.get("stats","b","summary").wins==1)
  assert(E.pay("a",{{resource="matter",amount=2}}) and E.pay("b",{{resource="matter",amount=2}}))
  assert(F.read_progress("a").matter==96 and F.read_progress("b").matter==100)
end)
for _,pro in ipairs({false,true}) do
test("acknowledged ordinary PvP surrender preserves one actual currency award and trusted result: Pro="..tostring(pro),function(F,R,M,E,D,L,state,rpc,leave,loop,sent)
  local s=state(false);s.pvp_pro=pro;assert(rpc("a",s).terminal)
  assert(F.read_progress("b").gold==175 and F.read_progress("b").xp==50)
  assert(F.get("stats","a","summary").losses==1 and F.get("stats","b","summary").wins==1)
  assert(rpc("a",s).terminal and #sent==1 and F.read_progress("b").gold==175)
end)
end
test("lost HTTP acknowledgement and duplicate opposite surrender return frozen winner without new awards",function(F,R,M,E,D,L,state,rpc,leave,loop,sent,arena,drop)
  local s=state(true);assert(rpc("a",s).terminal);drop()
  assert(rpc("a",s).winner_user_id=="b" and rpc("b",s).winner_user_id=="b")
  assert(#sent==1 and F.read_progress("b").matter==102)
end)
test("unknown participant and stale or forged RPC never signal or mutate the match",function(F,R,M,E,D,L,state,rpc)
  local s=state(true)
  assert(rpc("c",s).err=="not_participant" and not s.ended)
  assert(rpc("a",s,{session_epoch=6}).err=="session_stale" and not s.ended)
  assert(rpc("a",s,{user_id="b"}).err=="bad_payload" and not s.ended)
  assert(F.read_progress("a").matter==98)
end)
test("OP12 uses the same settlement path and HTTP retry observes its terminal receipt",function(F,R,M,E,D,L,state,rpc,leave,loop,sent)
  local s=state(true);assert(loop({},D,1,s,{{sender=s.presences.a,op_code=12}})==nil)
  assert(rpc("a",s).terminal and #sent==1 and F.read_progress("b").matter==102)
end)
test("settlement storage failure never promises terminal, then retry clears both paid reservations once",function(F,R,M,E,D,L,state,rpc,leave,loop,sent)
  local s=state(true);F.fail_before=true
  assert(rpc("a",s).err=="result_pending" and s.ended and #sent==0)
  F.fail_before=false;assert(loop({},D,6,s,{})==nil)
  assert(rpc("a",s).terminal and F.read_progress("b").matter==102 and #sent==1)
end)
test("terminal mailbox write outage after settlement delays acknowledgement without settling or paying twice",function(F,R,M,E,D,L,state,rpc,leave,loop,sent)
  local s=state(true)
  local old=M.publish;M.publish=function()return false end
  assert(rpc("a",s).err=="result_pending" and F.read_progress("b").matter==102 and #sent==0)
  M.publish=old;assert(loop({},D,6,s,{})==nil and rpc("a",s).terminal and #sent==1)
  assert(F.read_progress("b").matter==102)
end)
test("bot Race surrender refunds the actual stake and permits another entry immediately",function(F,R,M,E,D,L,state,rpc,leave,loop,sent)
  local s=state(true,true);assert(rpc("a",s).terminal)
  assert(sent[1].payload.entryRefund and sent[1].payload.rewardMatter==2 and F.read_progress("a").matter==100)
  assert(E.pay("a",{{resource="matter",amount=2}}) and F.read_progress("a").matter==98)
end)
test("unstarted public admission timeout cancels promptly without free win XP or currency",function(F,R,M,E,D,L,state,rpc,leave,loop,sent)
  local s=state(false);s.started=false;s.players_sorted={};s.presences={a=s.presences.a};s.stats={}
  assert(loop({},D,119,s,{})==s and not s.ended)
  assert(loop({},D,120,s,{})==nil and s.ended)
  assert(#sent==1 and sent[1].payload.matchCancelled and sent[1].payload.isDraw and sent[1].payload.endReason=="opponent_not_ready")
  assert(F.read_progress("a").gold==100 and F.read_progress("b").gold==100 and not F.get("stats","a","summary"))
  assert(rpc("a",s).terminal and rpc("b",s).match_cancelled)
end)
test("never-joined paid PvE cancels without phantom loss or consuming armed elixir and refunds once after match disappears",function(F,R,M,E,D,L,state,rpc,leave,loop,sent)
  local Pve=require("modules.duel_match3_pve_entry").new({read_pve_progress=F.read_progress,build_pve_progress_write=F.build_progress,
    now=function()return F.clock end,energy_max=function()return 100 end})
  local ok,_,entry=Pve.reserve("a",{{resource="energy",amount=15}},{request_key="mine:1"});assert(ok)
  local s=state(false,true);s.started=false;s.players_sorted={};s.stats={};s.presences={}
  assert(Pve.bind("a",entry.token,s.match_id,s.stats_result_id));F.matches[s.match_id]={match_id=s.match_id}
  F.put("sheet","a","v1",{consumables={armed={item_id="elixir_assault",activation_id="unused"}}})
  R.award_pve_defeat=function()error("unstarted PvE must not record a defeat")end
  assert(loop({},D,120,s,{})==nil and sent[1].payload.matchCancelled)
  assert(not F.get("stats","a","summary") and F.read_progress("a").energy==85)
  assert(Pve.recover("a") and F.read_progress("a").energy==85) -- Alive proof still forbids a refund.
  F.matches[s.match_id]=nil
  assert(Pve.recover("a") and Pve.recover("a") and F.read_progress("a").energy==100)
  assert(F.get("sheet","a","v1").consumables.armed.activation_id=="unused")
  assert(Pve.reserve("a",{{resource="energy",amount=15}},{request_key="mine:1"}) and F.read_progress("a").energy==85)
end)
test("tournament bot no-show resolves at admission deadline with known full HP and one technical result",function(F,R,M,E,D,L,state,rpc,leave,loop,sent,arena)
  local s=state(false,true);s.started=false;s.arena_mirror={};s.presences={};s.stats={};s.players_sorted={}
  assert(loop({},D,120,s,{})==nil and arena()==1)
  assert(sent[1].payload.winnerUserId==s.bot_user_id and s.stats.a.hp==150 and s.stats[s.bot_user_id].max_hp==150)
end)
test("tournament reconnect deadline is twenty seconds while ordinary duel still grants three hundred",function(F,R,M,E,D,L,state,rpc,leave,loop,sent,arena)
  local s=state(false,true);s.arena_mirror={};leave({},D,1,s,{s.presences.a})
  assert(s.reconnect_deadlines.a==121 and loop({},D,120,s,{})==s)
  assert(loop({},D,121,s,{})==nil and arena()==1)
  s=state(false);leave({},D,5,s,{s.presences.a});assert(s.reconnect_deadlines.a==1805)
end)
test("missing turn-ready packet automatically resumes the authoritative clock after eight seconds",function(F,R,M,E,D,L,state,rpc,leave,loop)
  local s=state(false);s.turn_deadline_paused=true;s.turn_pause_started_tick=10
  assert(loop({},D,57,s,{})==s and s.turn_deadline_paused)
  assert(loop({},D,58,s,{})==s and not s.turn_deadline_paused and s.turn_deadline_tick==238)
end)
test("terminal acknowledgement storage is bounded to four own results and CAS rejects forged state",function(F,R,M,E,D,L,state,rpc)
  for i=1,12 do
    local s=state(false);s.match_id="bounded:"..i;s.ended=true;assert(M.publish(s,{winnerUserId="b"}))
  end
  local v=F.get(M.COLLECTION,"a",M.KEY);assert(#v.receipts==4 and v.receipts[1].match_id=="bounded:12")
  assert(not M.lookup("c","bounded:12") and not M.lookup("a","bounded:1"))
end)
print("Match lifecycle security: "..passed.." passed")
