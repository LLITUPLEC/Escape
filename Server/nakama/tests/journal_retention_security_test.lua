-- Actual strict journal/retention with transactional CAS and crash faults.
package.path="Server/nakama/?.lua;"..package.path
local Fixture=require("tests.storage_fault_fixture")
local original_time=os.time
local F,J,T,passed
passed=0
local function reset()
 F=Fixture.new();F.install();os.time=function()return F.clock end
 J=require("modules.duel_match3_journal").new();T=require("modules.duel_journal_retention")
end
local function test(name,fn)reset();fn();passed=passed+1;print("PASS "..name)end
local function ticket(id,kind,uid)
 local ok,err,rows=J.issue_many({{uid=uid or"u",id=id,kind=kind or"effects"}});assert(ok,err);return rows[1]
end
local function finish(id,t,uid)
 uid=uid or"u";assert(J.prepare(uid,id,"effects",{match_stats={result_id=id,mode="mine",won=true}},t))
 assert(J.deliver_effects(uid,id))
end
local function count(uid)
 local n=0;for _,row in pairs(F.db)do if row.collection=="duel_match3_journal"and row.user_id==(uid or"u")and row.key:sub(1,7)=="result:"and(row.value.status=="complete"or row.value.status=="aborted")then n=n+1 end end;return n
end
test("missing admission never runs economic builder",function()
 local builds=0;local ok,err=J.atomic("u","old-id","pve",{},function()builds=builds+1;return{},{}end)
 assert(not ok and err=="result_ticket_required"and builds==0 and count()==0)
end)
test("eight completed results retain exactly four rows and four stats IDs",function()
 for i=1,8 do local id="r"..i;finish(id,ticket(id))end
 local summary=F.get("duel_match3_stats","u","summary");local c=F.get("duel_match3_journal","u","retention_v2")
 assert(count()==4 and summary.played==8 and #summary.match_result_ids==4 and summary.match_result_ids[1]=="r5")
 assert(c.retired_through==8 and next(c.open)==nil and #c.closed_ranges==0 and #c.recent==4)
end)
test("compacted original ticket and missing-ticket replay cannot re-award",function()
 local old=ticket("r1");finish("r1",old)
 for i=2,7 do local id="r"..i;finish(id,ticket(id))end
 assert(not F.get("duel_match3_journal","u","result:r1"))
 local ok,err=J.prepare("u","r1","effects",{match_stats={result_id="r1",mode="mine",won=true}},old)
 assert(not ok and err=="result_compacted")
 ok,err=J.prepare("u","r1","effects",{})
 assert(not ok and err=="result_ticket_required"and F.get("duel_match3_stats","u","summary").played==7)
end)
test("out-of-order pending holes survive retirement and recency follows actual completion",function()
 local ts={};for i=1,6 do ts[i]=ticket("r"..i)end
 assert(J.prepare("u","r1","effects",{match_stats={result_id="r1",mode="mine",won=true}},ts[1]))
 for i=6,2,-1 do finish("r"..i,ts[i])end
 local c=F.get("duel_match3_journal","u","retention_v2")
 assert(c.retired_through==0 and c.open["1"]and #c.closed_ranges==1 and c.closed_ranges[1][1]==2 and c.closed_ranges[1][2]==6)
 assert(J.deliver_effects("u","r1"));c=F.get("duel_match3_journal","u","retention_v2")
 assert(c.retired_through==6 and #c.recent==4 and c.recent[4].id=="r1"and count()==4)
 assert(F.get("duel_match3_stats","u","summary").played==6)
end)
test("forged future, wrong owner and wrong nonce tickets fail",function()
 local t=ticket("r");local forged=Fixture.copy(t);forged.nonce="forged"
 assert(not J.prepare("u","r","effects",{},forged));assert(not J.prepare("v","r","effects",{},t))
 assert(not J.prepare("u","other","effects",{},t));assert(not J.prepare("u","r","effects",{},{sequence=999,nonce=t.nonce}))
end)
test("unused ticket cancellation retires without creating a result",function()
 local t=ticket("r");assert(J.cancel_unused_many({{uid="u",id="r",kind="effects",ticket=t}}))
 local ok,err=J.prepare("u","r","effects",{},t);assert(not ok and err=="result_compacted"and count()==0)
end)
test("unused cancellation cannot remove a prepared earned intent",function()
 local t=ticket("r");assert(J.prepare("u","r","pve",{},t)==false) -- kind mismatch
 t=ticket("reward","pve");assert(J.prepare("u","reward","pve",{},t))
 assert(J.cancel_unused_many({{uid="u",id="reward",kind="pve",ticket=t}}))
 assert(F.get("duel_match3_journal","u","retention_v2").open[tostring(t.sequence)])
end)
test("atomic grant and closed watermark share the same transaction",function()
 F.seed("u");local t=ticket("reward","pve");F.fail_before=true
 local function builder()local p,v=F.read_progress("u");p.gold=p.gold+7;return{F.build_progress("u",p,v)},{gold=7}end
 assert(not J.atomic("u","reward","pve",{},builder,t));assert(F.read_progress("u").gold==100)
 assert(F.get("duel_match3_journal","u","retention_v2").open[tostring(t.sequence)])
 F.fail_before=false;assert(J.atomic("u","reward","pve",{},builder,t));assert(F.read_progress("u").gold==107)
end)
test("commit-then-lost-ack atomic replay returns one immutable grant",function()
 F.seed("u");local t=ticket("reward","pve");local calls=0
 local function builder()calls=calls+1;local p,v=F.read_progress("u");p.gold=p.gold+7;return{F.build_progress("u",p,v)},{gold=7}end
 F.fail_after=true;assert(not J.atomic("u","reward","pve",{},builder,t))
 assert(J.atomic("u","reward","pve",{},builder,t));assert(calls==1 and F.read_progress("u").gold==107)
end)
test("deletion failure never loses the award and later maintenance can prune",function()
 F.delete_failure=true;for i=1,7 do finish("r"..i,ticket("r"..i))end
 assert(count()==7 and F.get("duel_match3_stats","u","summary").played==7)
 F.delete_failure=false
 for _,row in pairs(Fixture.copy(F.db))do if T.can_prune(row)then F.nk.storage_delete({row})end end
 assert(count()==4)
end)
test("legacy completed rows compact by timestamp without admitting old UUID retries",function()
 for i=8,1,-1 do F.put("duel_match3_journal","u","result:legacy"..i,{result_id="legacy"..i,kind="effects",status="complete",completed_at=i})end
 for _=1,2 do for _,row in pairs(Fixture.copy(F.db))do if T.can_prune(row)then F.nk.storage_delete({row})end end end
 assert(count()==4)
 for i=5,8 do assert(F.get("duel_match3_journal","u","result:legacy"..i))end
 assert(not J.prepare("u","legacy1","effects",{}))
end)
test("genuine unfinished legacy result adopts only with its pending proof",function()
 F.put("duel_match3_journal","u","result:old",{result_id="old",kind="effects",status="committed",plan={achievement_deltas={x=2}}})
 assert(not J.deliver_effects("u","old"))
 F.put("duel_match3_journal","u","pending",{entries={old={kind="effects"}}})
 assert(J.deliver_effects("u","old"));assert(F.get("duel_match3_stats","u","summary").achievement_stats.x==2)
end)
test("two-owner atomic admission and settlement cannot partially commit",function()
 local ok,_,ts=J.issue_many({{uid="u",id="bind",kind="consumable_bind"},{uid="v",id="bind",kind="consumable_bind"}});assert(ok)
 local specs={};for i,uid in ipairs({"u","v"})do specs[i]={uid=uid,id="bind",kind="consumable_bind",ticket=ts[i],plan={},build=function()return{},{}end}end
 F.fail_before=true;assert(not J.atomic_many(specs));assert(count("u")==0 and count("v")==0)
 F.fail_before=false;assert(J.atomic_many(specs));assert(count("u")==1 and count("v")==1)
end)
test("open admissions are bounded and never silently truncate unfinished work",function()
 for i=1,128 do ticket("r"..i)end
 local ok,err=J.issue_many({{uid="u",id="overflow",kind="effects"}})
 assert(not ok and err=="recovery_backlog_full"and F.get("duel_match3_journal","u","retention_v2").next_seq==128)
end)
test("full pending quota cannot prevent a proved orphan-entry refund abort",function()
 for i=1,128 do ticket("open"..i)end
 local writes,err=J.abort_writes("u","cancelled:pve","create_failed");assert(writes,err);F.nk.storage_write(writes)
 local c=F.get("duel_match3_journal","u","retention_v2");local n=0;for _ in pairs(c.open)do n=n+1 end
 assert(n==128 and J.read("u","cancelled:pve").status=="aborted")
 assert(not J.prepare("u","cancelled:pve","pve",{}))
end)
test("orphan sweep preserves frozen Race result and live authoritative actors",function()
 local ok,_,ts=J.issue_many({{uid="u",id="race:stats",kind="effects",origin={match_id="race-match",root_result="race"}},
  {uid="u",id="live:stats",kind="effects",origin={match_id="live-match",root_result="live"}},
  {uid="u",id="abandoned:stats",kind="effects",origin={match_id="absent",root_result="abandoned"}}});assert(ok)
 F.put("duel_race_entry","u","run:race",{status="bound",finish_intent={winner="u"}});F.matches["live-match"]={match_id="live-match"}
 F.clock=F.clock+121;assert(J.recover("u",{}))
 local c=F.get("duel_match3_journal","u","retention_v2")
 assert(c.open[tostring(ts[1].sequence)]and c.open[tostring(ts[2].sequence)]and not c.open[tostring(ts[3].sequence)])
end)
test("different-mode concurrent finalizations preserve both counters and tickets",function()
 local a,b=ticket("mine"),ticket("duel")
 assert(J.prepare("u","mine","effects",{match_stats={mode="mine",won=true,result_id="mine"}},a))
 assert(J.prepare("u","duel","effects",{match_stats={mode="duel",won=false,result_id="duel"}},b))
 F.before_write=function()assert(J.deliver_effects("u","duel"))end
 assert(J.deliver_effects("u","mine"))
 local s=F.get("duel_match3_stats","u","summary");assert(s.played==2 and s.modes.mine.wins==1 and s.modes.duel.losses==1)
 assert(F.get("duel_match3_journal","u","retention_v2").retired_through==2)
end)
test("existing sixty-four-ID summaries normalize to four without changing totals",function()
 local ids={};for i=1,64 do ids[i]="old"..i end
 F.put("duel_match3_stats","u","summary",{played=123,wins=100,match_result_ids=ids,achievement_stats={kept=7}})
 package.loaded["modules.duel_match3_achievements"]=nil
 local Ach=require("modules.duel_match3_achievements");Ach.configure({decode_storage_value=function(row)return row.value end})
 F.fail_after=true;local s,version=Ach.storage_read_match3_summary_val("u")
 assert(version and s.played==123 and s.wins==100 and s.achievement_stats.kept==7 and #s.match_result_ids==4 and s.match_result_ids[1]=="old61")
 assert(#F.get("duel_match3_stats","u","summary").match_result_ids==4)
end)
os.time=original_time
print("Strict journal retention: "..passed.." passed")
