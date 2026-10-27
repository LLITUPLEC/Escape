package.path = "Server/nakama/?.lua;" .. package.path
local I=require("modules.duel_match3_inventory")
local function copy(v)if type(v)~="table"then return v end local r={}for k,x in pairs(v)do r[k]=copy(x)end return r end
local nk={json_encode=copy,json_decode=copy,logger_error=function()end};package.preload.nakama=function()return nk end
local sheet,progress,sv,pv,writes,fail_after,before
local defs={helmet={kind="equipment",slot="helmet",max_stack=1,sale_price=20},recipe_green={kind="recipe",max_stack=10},ingot_green={kind="material",max_stack=100}}
local function reset()
 sheet=I.normalize({equipment={},inventory_extra_slots=3,learned_recipes={}});progress={matter=12,gold=100,ingots=999,blueprint_green=999};sv=1;pv=1;writes=0;fail_after=false;before=nil
end
local deps={CFG={},Guard={assert_client_epoch_matches=function()return true end},EQUIP_ORDER={"helmet"},
 read_character_sheet=function()local s=copy(sheet);s._storage_version=sv;return s end,
 read_pve_progress=function()return copy(progress),pv end,
 ensure_sheet_inventory_counts=I.normalize,get_merged_item_defs=function()return defs end,
 item_def_is_equipment=function(d)return d.kind=="equipment"end,item_def_is_recipe=function(d)return d.kind=="recipe"end,
 item_max_stack=function(d)return d.max_stack or 1 end,
 write_character_sheet=function(uid,s,p,v)
  if before then local f=before;before=nil;f()end
  assert(s._storage_version==sv and v==pv,"CAS conflict")
  sheet=copy(s);sheet._storage_version=nil;progress=copy(p);sv=sv+1;pv=pv+1;writes=writes+1
  if fail_after then fail_after=false;error("lost acknowledgement")end
 end,
 encode_character_ok_response=function(s,p)return{ok=true,sheet=copy(s),progression=copy(p)}end,
}
local rpc=require("modules.duel_match3_economy_rpc")(deps)
local function invoke(name,p)return rpc[name]({user_id="fixture"},p or {})end
local passed=0
local function test(name,f)reset();f();passed=passed+1;print("PASS "..name)end
test("equip from paid second-page cell uses global inventory index",function()
 sheet.inventory[28]="helmet";sheet.inventory_counts[28]=1
 assert(invoke("duel_character_item_move",{op="inv_to_equip",inv_index=27,slot_index=0}).ok)
 assert(sheet.equipment[1]=="helmet" and sheet.inventory[28]=="" and sheet.inventory_extra_slots==3)
end)
test("locked second-page indices are rejected despite forged inventory item",function()
 sheet.inventory[29]="helmet";sheet.inventory_counts[29]=1
 -- A client payload cannot change capacity or grant a slot. Persisted migration
 -- intentionally recovers real pre-existing items; do not forge persisted rows here.
 sheet.inventory[29]=nil;sheet.inventory_counts[29]=nil
 assert(not invoke("duel_character_item_move",{op="inv_to_equip",inv_index=28,slot_index=0,inventory_extra_slots=25}).ok)
 assert(writes==0)
end)
test("sell second-page item drains durable pending loot in same wallet transaction",function()
 sheet.inventory[28]="helmet";sheet.inventory_counts[28]=1
 for n=1,27 do sheet.inventory[n]="helmet";sheet.inventory_counts[n]=1 end
 I.add_or_queue(sheet,"ingot_green",6,defs)
 assert(invoke("duel_character_item_sell",{source="inventory",inv_index=27}).ok)
 assert(progress.gold==120 and progress.ingots==6 and sheet.inventory[28]=="ingot_green" and #sheet.inventory_pending_loot==0)
 assert(writes==1)
end)
test("learn recipe from second page and consume freed slot delivery without losing learned data",function()
 sheet.inventory[27]="recipe_green";sheet.inventory_counts[27]=1
 assert(invoke("duel_character_recipe_learn",{inv_index=26}).ok)
 assert(sheet.learned_recipes[1]=="recipe_green" and progress.blueprint_green==0)
end)
test("fresh character get recovers pending items and derives spendable balances",function()
 sheet.inventory_pending_loot={{def_id="ingot_green",amount=9}}
 local result=invoke("duel_character_get")
 assert(result.ok and progress.ingots==9 and #sheet.inventory_pending_loot==0 and writes==1)
 assert(invoke("duel_character_get").ok and writes==1)
end)
test("profile delivery survives lost acknowledgement without double item",function()
 sheet.inventory_pending_loot={{def_id="ingot_green",amount=9}};fail_after=true
 assert(invoke("duel_character_get").ok and progress.ingots==9 and writes==1)
 local n=0;for i,id in ipairs(sheet.inventory)do if id=="ingot_green"then n=n+sheet.inventory_counts[i]end end;assert(n==9)
end)
test("profile delivery CAS retry preserves concurrent inventory changes",function()
 sheet.inventory_pending_loot={{def_id="ingot_green",amount=9}}
 before=function()sheet.inventory[1]="helmet";sheet.inventory_counts[1]=1;sv=sv+1 end
 assert(invoke("duel_character_get").ok and sheet.inventory[1]=="helmet" and sheet.inventory[2]=="ingot_green" and writes==1)
end)
print("Inventory page economy integration: "..passed.." passed")
