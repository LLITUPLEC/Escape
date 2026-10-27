package.path="Server/nakama/?.lua;"..package.path
local I=require("modules.duel_match3_inventory")
local function copy(x)if type(x)~="table"then return x end local r={}for k,v in pairs(x)do r[k]=copy(v)end return r end
local sheet,progress,sv,pv,before,fail_before,fail_after,writes
local nk={json_encode=copy,json_decode=copy,logger_error=function()end};package.preload.nakama=function()return nk end
local defs={helmet={kind="equipment",slot="helmet",max_stack=1,sale_price=20},ore={kind="material",max_stack=100,sale_price=5}}
local function reset()
 sheet=I.normalize({equipment={}});progress={gold=100,matter=9};sv=1;pv=1;before=nil;fail_before=false;fail_after=false;writes=0
 for i=1,25 do sheet.inventory[i]="helmet";sheet.inventory_counts[i]=1 end
end
local deps={CFG={},Guard={assert_client_epoch_matches=function()return true end},
 read_character_sheet=function()local s=copy(sheet);s._storage_version=sv;return s end,
 read_pve_progress=function()return copy(progress),pv end,
 ensure_sheet_inventory_counts=I.normalize,get_merged_item_defs=function()return defs end,
 write_character_sheet=function(uid,s,p,v)
  if before then local f=before;before=nil;f()end
  if fail_before then error("database unavailable")end
  assert(s._storage_version==sv and v==pv,"version check failed")
  sheet=copy(s);sheet._storage_version=nil;progress=copy(p);sv=sv+1;pv=pv+1;writes=writes+1
  if fail_after then fail_after=false;error("ack lost after commit")end
 end,
 encode_character_ok_response=function(s,p)return{ok=true,sheet=copy(s),progression=copy(p)}end,
}
local rpc=require("modules.duel_match3_economy_rpc")(deps)
local function sell(id,expected,index,source)
 source=source or "inventory"
 return rpc.duel_character_item_sell({user_id="fixture"},{source=source,inv_index=index or 0,slot_index=index or 0,request_id=id,expected_def_id=expected})
end
local passed=0
local function test(name,fn)reset();fn();passed=passed+1;print("PASS "..name)end
test("replay after automatic refill cannot sell newly delivered loot",function()
 I.add_or_queue(sheet,"ore",6,defs)
 local a=sell("request-one","helmet");assert(a.ok and progress.gold==120 and sheet.inventory[1]=="ore" and sheet.inventory_counts[1]==6)
 local b=sell("request-one","helmet");assert(b.ok and b.sale_replayed and b.sold_item_id=="helmet" and b.gold_gained==20 and progress.gold==120)
 assert(sheet.inventory[1]=="ore" and sheet.inventory_counts[1]==6 and writes==1)
end)
test("receipt protects same-definition refill too",function()
 I.add_or_queue(sheet,"helmet",2,defs)
 assert(sell("request-one","helmet").ok and sheet.inventory[1]=="helmet" and sheet.inventory_pending_loot[1].amount==1)
 assert(sell("request-one","helmet").sale_replayed and progress.gold==120 and sheet.inventory_pending_loot[1].amount==1 and writes==1)
end)
test("lost commit acknowledgement replays receipt instead of reselling refill",function()
 I.add_or_queue(sheet,"ore",6,defs);fail_after=true
 local r=sell("request-one","helmet")
 assert(r.ok and r.sale_replayed and progress.gold==120 and sheet.inventory[1]=="ore" and writes==1)
end)
test("concurrent identical requests commit exactly one sale and queue delivery",function()
 I.add_or_queue(sheet,"helmet",2,defs);local other
 before=function()other=sell("request-one","helmet")end
 local r=sell("request-one","helmet")
 assert(r.ok and other.ok and progress.gold==120 and writes==1 and sheet.inventory_pending_loot[1].amount==1)
end)
test("wrong expected item and missing request half fail without mutation",function()
 assert(sell("request-one","ore").err=="item_changed")
 assert(sell("request-one",nil).err=="bad_sale_request")
 assert(sell(nil,"helmet").err=="bad_sale_request")
 assert(progress.gold==100 and writes==0 and sheet.inventory[1]=="helmet")
end)
test("new intent with stale expected definition cannot sell auto-refilled loot",function()
 I.add_or_queue(sheet,"ore",6,defs);assert(sell("request-one","helmet").ok)
 assert(sell("request-two","helmet").err=="item_changed" and progress.gold==120 and sheet.inventory[1]=="ore" and writes==1)
end)
test("request reuse for another source index or item is rejected",function()
 assert(sell("request-one","helmet").ok)
 assert(sell("request-one","helmet",1).err=="request_id_reused")
 assert(sell("request-one","ore",0).err=="request_id_reused")
 assert(sell("request-one","helmet",0,"equipment").err=="request_id_reused" and progress.gold==120)
end)
test("failed transaction retains item pending loot wallet and no receipt",function()
 I.add_or_queue(sheet,"ore",6,defs);fail_before=true
 assert(not sell("request-one","helmet").ok)
 assert(progress.gold==100 and sheet.inventory[1]=="helmet" and sheet.inventory_pending_loot[1].amount==6 and #sheet.inventory_sale_receipts==0)
 fail_before=false;assert(sell("request-one","helmet").ok and progress.gold==120 and writes==1)
end)
test("equipment confirmation can replay after its source becomes empty",function()
 sheet.equipment[1]="helmet"
 assert(sell("request-one","helmet",0,"equipment").ok and sheet.equipment[1]=="")
 assert(sell("request-one","helmet",0,"equipment").sale_replayed and progress.gold==120 and writes==1)
end)
test("legacy client without protection fields keeps its existing contract",function()
 assert(sell(nil,nil).ok and progress.gold==120 and #sheet.inventory_sale_receipts==0)
end)
test("recent receipt history is bounded and latest confirmation remains replayable",function()
 for n=1,70 do sheet.inventory[1]="helmet";sheet.inventory_counts[1]=1;assert(sell("request-"..n,"helmet").ok)end
 assert(#sheet.inventory_sale_receipts==64 and sheet.inventory_sale_receipts[1].request_id=="request-7")
 assert(sell("request-70","helmet").sale_replayed and progress.gold==1500 and writes==70)
end)
print("Protected inventory sale security: "..passed.." passed")
