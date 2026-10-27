-- Real pure inventory + RPC transaction tests; no Nakama/network/user writes.
package.path = "Server/nakama/?.lua;" .. package.path
local Inventory = require("modules.duel_match3_inventory")
local function copy(x) if type(x) ~= "table" then return x end local r={} for k,v in pairs(x) do r[k]=copy(v) end return r end
local defs = { sword={max_stack=1}, ore={max_stack=100}, recipe={max_stack=1} }
local function fresh(extra)
  return Inventory.normalize({inventory_extra_slots=extra or 0, equipment={}, learned_recipes={}, workshop_slots={}})
end
local function fill(sheet)
  for i=1,Inventory.capacity(sheet) do sheet.inventory[i]="sword"; sheet.inventory_counts[i]=1 end
end
local function count(sheet,id) local n=0 for i=1,Inventory.capacity(sheet) do if sheet.inventory[i]==id then n=n+sheet.inventory_counts[i] end end return n end
local passed=0
local function test(name,fn) fn();passed=passed+1;print("PASS "..name) end

test("legacy base cells and paid extra cells survive normalization",function()
  local s=fresh(7); s.inventory[32]="sword";s.inventory_counts[32]=1
  s.inventory_slot_receipts["31"]={request_id="purchase-123"}
  s=Inventory.normalize(copy(s))
  assert(Inventory.capacity(s)==32 and s.inventory[32]=="sword" and s.inventory_slot_receipts["31"].request_id=="purchase-123")
  s.inventory_extra_slots=nil;assert(Inventory.capacity(Inventory.normalize(s))==32)
end)
test("legacy items beyond maximum become pending instead of being truncated",function()
  local s=fresh(); s.inventory[52]="recipe";s.inventory_counts[52]=2
  Inventory.normalize(s);assert(s.inventory[52]==nil and s.inventory_pending_loot[1].amount==2)
  Inventory.normalize(s);assert(s.inventory_pending_loot[1].amount==2)
end)
test("all-or-nothing TryAdd never leaves a partially filled stack",function()
  local s=fresh();fill(s);s.inventory[1]="ore";s.inventory_counts[1]=99
  assert(not Inventory.try_add(s,"ore",4,defs));assert(s.inventory_counts[1]==99)
end)
test("full chest receipt precisely distinguishes delivered and pending loot",function()
  local s=fresh();fill(s);s.inventory[1]="ore";s.inventory_counts[1]=99
  local r=Inventory.add_or_queue(s,"ore",4,defs)
  assert(r.delivered==1 and r.pending==3 and s.inventory_counts[1]==100)
  assert(s.inventory_pending_loot[1].amount==3)
end)
test("full second page retains every reward and preserves unknown catalog items",function()
  local s=fresh(25);fill(s)
  assert(Inventory.add_or_queue(s,"recipe",8,defs).pending==8)
  assert(Inventory.add_or_queue(s,"future-item",2,defs).pending==2)
  assert(Inventory.drain(s,defs).delivered==0)
  s.inventory[50]="";s.inventory_counts[50]=0
  assert(Inventory.drain(s,defs).delivered==1 and count(s,"recipe")==1)
  assert(Inventory.drain(s,defs).delivered==0)
  local rows,total=Inventory.pending_payload(s);assert(total==9 and #rows==2)
end)
test("pending items deliver exactly once after a slot is released",function()
  local s=fresh();fill(s);Inventory.add_or_queue(s,"ore",30,defs)
  s.inventory[2]="";s.inventory_counts[2]=0
  assert(Inventory.drain(s,defs).delivered==30 and count(s,"ore")==30)
  assert(Inventory.drain(s,defs).delivered==0 and #s.inventory_pending_loot==0)
end)
test("purchase price is fixed at three matter and fifty is the hard maximum",function()
  local s,p=fresh(),{matter=75}
  for i=25,49 do assert(Inventory.unlock_slot(s,p,i,"request-"..i)) end
  assert(p.matter==0 and Inventory.capacity(s)==50)
  assert(not Inventory.unlock_slot(s,p,50,"request-50"))
end)
test("purchase rejects insufficient matter, skipped cells, fractional indices and receipt reuse",function()
  local s,p=fresh(),{matter=2}
  assert(not Inventory.unlock_slot(s,p,25,"request-25") and p.matter==2 and Inventory.capacity(s)==25)
  p.matter=9
  assert(not Inventory.unlock_slot(s,p,26,"request-26"));assert(not Inventory.unlock_slot(s,p,25.5,"request-25"))
  assert(Inventory.unlock_slot(s,p,25,"request-25") and p.matter==6)
  assert(not Inventory.unlock_slot(s,p,26,"request-25") and p.matter==6)
  assert(Inventory.unlock_slot(s,p,25,"different-request") and p.matter==6)
end)

test("pending and consumed loot never appear as spendable shadow resources",function()
  local s=fresh(1);fill(s);s.inventory[26]="ingot_blue";s.inventory_counts[26]=4
  Inventory.add_or_queue(s,"tesseract",2,{tesseract={max_stack=1}})
  local p={ingots=900,tesseracts=800,blueprint_green=20}
  Inventory.sync_resource_counters(s,p,defs)
  assert(p.ingots==4 and p.tesseracts==0 and p.blueprint_green==0)
  s.inventory[2]="recipe_drop_t1_green_Head";s.inventory_counts[2]=3
  s.inventory[26]="";s.inventory_counts[26]=0
  Inventory.sync_resource_counters(s,p,defs)
  assert(p.ingots==0 and p.blueprint_green==3 and p.tesseracts==0)
  Inventory.drain(s,{tesseract={max_stack=99}});Inventory.sync_resource_counters(s,p,defs)
  assert(p.tesseracts==2 and #s.inventory_pending_loot==0)
end)

local db,batches,before_write,fail_before,fail_after
local nk={json_encode=copy,json_decode=copy,logger_error=function() end}
package.preload.nakama=function()return nk end
local function reset(balance)
  db={sheet=fresh(),progress={matter=balance or 9},sv=1,pv=1};batches=0;before_write=nil;fail_before=false;fail_after=false
end
local deps={
  Guard={assert_client_epoch_matches=function(uid,p) return p and p.session_epoch==7,"session_replaced" end},
  read_character_sheet=function() local s=copy(db.sheet);s._storage_version=db.sv;return s end,
  read_pve_progress=function()return copy(db.progress),db.pv end,
  get_merged_item_defs=function()return defs end,
  write_character_sheet=function(uid,s,p,pv)
    batches=batches+1
    if before_write then local fn=before_write;before_write=nil;fn() end
    if fail_before then error("database unavailable") end
    assert(s._storage_version==db.sv,"sheet CAS conflict")
    assert(not p or pv==db.pv,"wallet CAS conflict")
    db.sheet=copy(s);db.sheet._storage_version=nil;db.sv=db.sv+1
    if p then db.progress=copy(p);db.pv=db.pv+1 end
    if fail_after then fail_after=false;error("ack lost after commit") end
  end,
  encode_character_ok_response=function(s,p)return {ok=true,sheet=copy(s),progression=copy(p)}end,
}
local rpc=require("modules.duel_match3_inventory_rpc")(deps)
local ctx={user_id="fixture"}
local function buy(index,id)
  return rpc.duel_character_inventory_slot_buy(ctx,{session_epoch=7,slot_index=index or 25,request_id=id or "request-25",price=0})
end
local function deliver()return rpc.duel_character_loot_deliver(ctx,{session_epoch=7})end

test("RPC rejects anonymous, stale epoch and client price cannot influence exact charge",function()
  reset();assert(not rpc.duel_character_inventory_slot_buy({},{}).ok)
  assert(not rpc.duel_character_inventory_slot_buy(ctx,{session_epoch=6}).ok)
  assert(buy().ok and db.progress.matter==6 and Inventory.capacity(db.sheet)==26)
end)
test("retry after commit and lost acknowledgement charges once",function()
  reset();fail_after=true;assert(buy().ok);assert(db.progress.matter==6 and Inventory.capacity(db.sheet)==26)
  assert(buy().ok and db.progress.matter==6)
end)
test("concurrent requests for the same absolute slot charge once",function()
  reset();local other
  before_write=function()other=buy(25,"other-request")end
  assert(buy().ok and other.ok and db.progress.matter==6 and Inventory.capacity(db.sheet)==26)
end)
test("concurrent wallet spend is reread and cannot overspend",function()
  reset(3);before_write=function()db.progress.matter=1;db.pv=db.pv+1 end
  assert(not buy().ok and db.progress.matter==1 and Inventory.capacity(db.sheet)==25)
end)
test("storage failure leaves wallet, slot and pending loot unchanged",function()
  reset();fill(db.sheet);Inventory.add_or_queue(db.sheet,"recipe",1,defs);fail_before=true
  assert(not buy().ok and db.progress.matter==9 and Inventory.capacity(db.sheet)==25 and db.sheet.inventory_pending_loot[1].amount==1)
end)
test("buying one cell atomically delivers one pending nonstackable item",function()
  reset();fill(db.sheet);Inventory.add_or_queue(db.sheet,"recipe",3,defs)
  assert(buy().ok and db.progress.matter==6 and count(db.sheet,"recipe")==1)
  assert(db.sheet.inventory_pending_loot[1].amount==2)
end)
test("pending delivery CAS retry cannot duplicate loot across concurrent calls",function()
  reset();fill(db.sheet);Inventory.add_or_queue(db.sheet,"ore",18,defs);db.sheet.inventory[25]="";db.sheet.inventory_counts[25]=0
  local other;before_write=function()other=deliver()end
  assert(deliver().ok and other.ok and count(db.sheet,"ore")==18 and #db.sheet.inventory_pending_loot==0)
end)
test("pending delivery retry after lost acknowledgement is exactly once",function()
  reset();Inventory.add_or_queue(db.sheet,"unknown",9,defs)
  db.sheet.inventory_pending_loot={{def_id="recipe",amount=1}};fail_after=true
  assert(deliver().ok and count(db.sheet,"recipe")==1 and #db.sheet.inventory_pending_loot==0)
end)
test("pending delivery storage fault retains durable queue for later retry",function()
  reset();db.sheet.inventory_pending_loot={{def_id="recipe",amount=1}};fail_before=true
  assert(not deliver().ok and count(db.sheet,"recipe")==0 and db.sheet.inventory_pending_loot[1].amount==1)
  fail_before=false;assert(deliver().ok and count(db.sheet,"recipe")==1)
end)
test("new purchase and delivery RPCs inherit mutate rate limiting",function()
  local limiter=require("modules.duel_api_rate_limit")
  assert(limiter.policy_for_rpc("duel_character_inventory_slot_buy")=="mutate")
  assert(limiter.policy_for_rpc("duel_character_loot_deliver")=="mutate")
end)
print("Inventory delivery security: "..passed.." passed")
