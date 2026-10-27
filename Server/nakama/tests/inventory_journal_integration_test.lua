-- Real finalization journal + actual core sheet batch builder + inventory queue.
-- In-memory Nakama CAS store only; no network or real account mutation.
package.path="Server/nakama/?.lua;"..package.path
local I=require("modules.duel_match3_inventory")
local function copy(x)if type(x)~="table"then return x end local r={}for k,v in pairs(x)do r[k]=copy(v)end return r end
local db,before,fail_before,fail_after
local function key(op)return op.user_id.."/"..op.collection.."/"..op.key end
local function put(op)local k=key(op);local old=db[k];db[k]={value=copy(op.value),version=tostring((old and tonumber(old.version)or 0)+1)}end
local nk={json_encode=copy,json_decode=copy,logger_error=function()end,
 storage_read=function(ids)local out={}for _,op in ipairs(ids)do local row=db[key(op)]if row then local c=copy(row);c.collection=op.collection;c.key=op.key;out[#out+1]=c end end return out end,
 storage_write=function(ops)
  if before then local fn=before;before=nil;fn()end
  if fail_before then error("database unavailable")end
  for _,op in ipairs(ops)do local row=db[key(op)];assert(op.permission_write==0)
   assert((op.version=="*" and not row)or(row and row.version==op.version),"version check failed")end
  local acks={}for _,op in ipairs(ops)do put(op);acks[#acks+1]={collection=op.collection,key=op.key,version=db[key(op)].version}end
  if fail_after then fail_after=false;error("lost acknowledgement")end
  return acks
 end}
package.preload.nakama=function()return nk end
local cfg={CHARACTER_SHEET_COLLECTION="character",CHARACTER_SHEET_KEY="sheet",PVE_PROGRESS_COLLECTION="progress",PVE_PROGRESS_KEY="profile"}
local defs={helmet={max_stack=1},recipe_green={max_stack=1}}
local uid="fixture"
local f=assert(io.open(os.getenv("INVENTORY_CORE_SOURCE")or"Server/nakama/modules/duel_match3.lua","r"));local source=f:read("*a");f:close()
local a=assert(source:find("local function normalize_character_sheet",1,true));local b=assert(source:find("local SET_BONUS_FULL_EQUIP_STATS",a,true))
local runtime={Inventory=I}
local env=setmetatable({Runtime=runtime,CFG=cfg,nk=nk,get_merged_item_defs=function()return defs end,decode_storage_value=function(row)return row.value end},{__index=_G})
local chunk=assert(loadstring(source:sub(a,b-1).."\nreturn read_character_sheet,write_character_sheet"));setfenv(chunk,env)
local read_sheet,write_sheet=chunk()
local Journal=require("modules.duel_match3_journal")
local J=require("tests.journal_fixture_admission").wrap(Journal.new())
local so={user_id=uid,collection=cfg.CHARACTER_SHEET_COLLECTION,key=cfg.CHARACTER_SHEET_KEY}
local po={user_id=uid,collection=cfg.PVE_PROGRESS_COLLECTION,key=cfg.PVE_PROGRESS_KEY}
local function wallet()return copy(db[key(po)].value),db[key(po)].version end
local function current()return db[key(so)].value end
local function reset()
 db={};before=nil;fail_before=false;fail_after=false
 local s=I.normalize({inventory_extra_slots=25})for i=1,50 do s.inventory[i]="helmet";s.inventory_counts[i]=1 end
 so.value=s;po.value={gold=10,matter=9};put(so);put(po);J=require("tests.journal_fixture_admission").wrap(Journal.new())
end
local function plan(n)return{items={{def_id="recipe_green",amount=n}},gold=5}end
local function builder(p)
 local s=read_sheet(uid);local progress,pv=wallet();local delivered,pending={},{};local total=0
 for _,item in ipairs(p.items)do local r=I.add_or_queue(s,item.def_id,item.amount,defs)
  if r.delivered>0 then delivered[#delivered+1]={def_id=item.def_id,amount=r.delivered}end
  if r.pending>0 then pending[#pending+1]={def_id=item.def_id,amount=r.pending};total=total+r.pending end
 end
 progress.gold=progress.gold+p.gold
 local writes=runtime.build_character_sheet_writes(uid,s,progress,pv)
 return writes,{deliveredItems=delivered,pendingItems=pending,itemLootPendingCount=total}
end
local passed=0
local function test(name,run)reset();run();passed=passed+1;print("PASS "..name)end
test("full bag pending loot and result receipt commit with wallet and journal atomically",function()
 assert(J.prepare(uid,"result-a","pve",plan(3)))
 local ok,err,r=J.commit(uid,"result-a",builder);assert(ok and not err and #r.deliveredItems==0 and r.itemLootPendingCount==3)
 assert(current().inventory_pending_loot[1].amount==3 and db[key(po)].value.gold==15 and db[key(po)].value.blueprint_green==0)
end)
test("database failure before outcome commit loses neither frozen loot nor wallet",function()
 assert(J.prepare(uid,"result-a","pve",plan(3)));fail_before=true;assert(not J.commit(uid,"result-a",builder))
 assert(#current().inventory_pending_loot==0 and db[key(po)].value.gold==10)
 fail_before=false;J=require("tests.journal_fixture_admission").wrap(Journal.new());assert(J.commit(uid,"result-a",builder));assert(current().inventory_pending_loot[1].amount==3 and db[key(po)].value.gold==15)
end)
test("lost acknowledgement plus process restart cannot double-grant full-bag loot",function()
 assert(J.prepare(uid,"result-a","pve",plan(3)));fail_after=true;assert(not J.commit(uid,"result-a",builder))
 J=require("tests.journal_fixture_admission").wrap(Journal.new());assert(J.commit(uid,"result-a",builder));assert(current().inventory_pending_loot[1].amount==3 and db[key(po)].value.gold==15)
end)
test("parallel recovery of one outcome queues items and awards wallet once",function()
 assert(J.prepare(uid,"result-a","pve",plan(3)));local other
 before=function()other=J.commit(uid,"result-a",builder)end
 assert(J.commit(uid,"result-a",builder)and other);assert(current().inventory_pending_loot[1].amount==3 and db[key(po)].value.gold==15)
end)
test("different concurrent outcomes retain every item after sheet CAS retry",function()
 assert(J.prepare(uid,"result-a","pve",plan(3)));assert(J.prepare(uid,"result-b","pve",plan(2)))
 before=function()assert(J.commit(uid,"result-b",builder))end
 assert(J.commit(uid,"result-a",builder));assert(current().inventory_pending_loot[1].amount==5 and db[key(po)].value.gold==20)
end)
test("recovery then freeing space delivers once and leaves remaining queue durable",function()
 assert(J.prepare(uid,"result-a","pve",plan(3)));assert(J.commit(uid,"result-a",builder))
 local s=read_sheet(uid);s.inventory[50]="";s.inventory_counts[50]=0;local p,pv=wallet()
 assert(I.drain(s,defs).delivered==1);write_sheet(uid,s,p,pv)
 assert(current().inventory[50]=="recipe_green" and current().inventory_pending_loot[1].amount==2 and db[key(po)].value.blueprint_green==1)
 J=require("tests.journal_fixture_admission").wrap(Journal.new());assert(J.commit(uid,"result-a",builder));assert(current().inventory_pending_loot[1].amount==2 and db[key(po)].value.gold==15)
end)
print("Inventory journal/core fault integration: "..passed.." passed")
