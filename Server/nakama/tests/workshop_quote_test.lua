-- Real craft/claim/rush RPC handlers + real catalog-cost function and quote guard.
-- Only the auth/Storage boundary is simulated; no network or player mutations.
package.path="Server/nakama/?.lua;"..package.path
local function copy(v)if type(v)~="table"then return v end;local r={}for k,x in pairs(v)do r[k]=copy(x)end;return r end
local function same(a,b)
 if type(a)~=type(b)then return false end;if type(a)~="table"then return a==b end
 for k,v in pairs(a)do if not same(v,b[k])then return false end end;for k in pairs(b)do if a[k]==nil then return false end end;return true
end
local nk={json_encode=copy,json_decode=copy,logger_error=function()end}
package.preload.nakama=function()return nk end
local Inventory=require("modules.duel_match3_inventory")
local Quote=require("modules.duel_workshop_quote")
local CFG=require("modules.duel_match3_config")
local f=assert(io.open("Server/nakama/modules/duel_match3.lua"));local source=f:read("*a");f:close()
local a=assert(source:find("local function workshop_craft_duration_seconds",1,true));local b=assert(source:find("local function inventory_count_def",a,true))
local compile=assert(loadstring(source:sub(a,b-1).."\nreturn workshop_craft_cost_from_def,workshop_craft_duration_seconds"))
setfenv(compile,setmetatable({CFG=CFG},{__index=_G}));local cost,duration=compile()
local slots={"Helmet","Shoulders","Chest","Gloves","Legs","Feet","WeaponLeft","WeaponRight"}
local sheet,progress,defs,writes,inits,reads,fail_write
local function count(s,id)local n=0;for i,x in ipairs(s.inventory)do if x==id then n=n+(s.inventory_counts[i]or 0)end end;return n end
local function remove(s,id,n)
 if count(s,id)<n then return false end
 for i,x in ipairs(s.inventory)do if x==id then local take=math.min(n,s.inventory_counts[i]);s.inventory_counts[i]=s.inventory_counts[i]-take;n=n-take;if s.inventory_counts[i]==0 then s.inventory[i]=""end;if n==0 then return true end end end
 return false
end
local Rpc=require("modules.duel_match3_economy_rpc")({CFG=CFG,EQUIP_ORDER=slots,
 Guard={assert_client_epoch_matches=function(_,p)return p.session_epoch==7,"session_stale"end},
 ensure_character_sheet_initialized=function()inits=inits+1 end,
 read_character_sheet=function()reads=reads+1;return copy(sheet)end,ensure_sheet_inventory_counts=Inventory.normalize,
 ensure_sheet_workshop=function(s)s.workshop_slots=s.workshop_slots or{};for i=1,8 do s.workshop_slots[i]=s.workshop_slots[i]or{}end end,
 get_merged_item_defs=function()return defs end,item_def_is_equipment=function(d)return d.kind=="equipment"end,
 sheet_has_learned_for_craft=function(s,id)for _,x in ipairs(s.learned_recipes)do if x==id then return true end end;return false end,
 workshop_craft_cost_from_def=cost,workshop_craft_duration_seconds=duration,
 inventory_count_def=count,inventory_remove_def_total=remove,
 workshop_has_exact_item_fodder=function(s,id)return count(s,id)>0 end,
 workshop_consume_exact_item_fodder=function(s,id)return remove(s,id,1)end,
 inventory_try_add=function(s,id,n)return Inventory.try_add(s,id,n,defs)end,
 read_pve_progress=function()return copy(progress),"exact"end,
 write_character_sheet=function(_,s,p)if fail_write then error("storage_unavailable")end;writes=writes+1;sheet=copy(s);progress=copy(p)end,
 encode_character_ok_response=function(s,p)return{ok=true,sheet=copy(s),progression=copy(p)}end})
local current_id,slot_index
local function reset(id,ingots,quality,slot)
 current_id=id or"eq_t1_epic_legs";local slotName=slot or"Legs";slot_index=1
 for i,x in ipairs(slots)do if x==slotName then slot_index=i end end
 defs={ingot_purple={kind="material",max_stack=200},fodder={kind="equipment",max_stack=1},recipe={kind="recipe",max_stack=1}}
 defs[current_id]={kind="equipment",max_stack=1,slot=slotName,tier=1,quality=quality or"epic",craft_recipe_id="recipe",
  craft_item_id="fodder",craft_minutes=180,craft_ore=70,craft_gold=50,craft_ingot_def="ingot_purple",craft_ingot_n=ingots or 560,craft_tesseract_n=0}
 sheet={equipment={},inventory={"ingot_purple","fodder"},inventory_counts={1000,1},learned_recipes={"recipe"},first_recipe_received=true,workshop_slots={}}
 Inventory.normalize(sheet);for i=1,8 do sheet.workshop_slots[i]={output_def_id="",ends_at=0}end
 progress={gold=100000,ore=100000,energy=0};writes,inits,reads,fail_write=0,0,0,false
end
local function request(expected)
 return Rpc.duel_workshop_craft_start({user_id="temporary"},{session_epoch=7,slot_index=slot_index-1,output_def_id=current_id,expected_ingot_n=expected})
end
local passed=0
local function check(label,fn)reset();fn();passed=passed+1;print("PASS "..label)end
check("all eight changed epic prices reject legacy with readable text and no debit or job",function()
 for id,old in pairs(Quote.LEGACY_INGOTS)do
  reset(id,old+100);local before, wallet=copy(sheet),copy(progress)
  local result=request(nil);assert(not result.ok and result.err==Quote.LEGACY_UPDATED)
  assert(same(before,sheet)and same(wallet,progress)and writes==0)
 end
end)
check("known old epic price remains available to the published unquoted client before migration",function()
 reset("eq_t1_epic_legs",180);assert(request(nil).ok)
 assert(count(sheet,"ingot_purple")==820 and progress.gold==99950 and writes==1)
end)
check("unchanged green blue legendary and administrative IDs keep legacy behavior",function()
 for _,q in ipairs({"normal","rare","legendary","epic"})do reset("unchanged_"..q,20,q,"Helmet");assert(request(nil).ok and count(sheet,"ingot_purple")==980)end
end)
check("matching displayed quote allows actual new560 price with one atomic debit and job",function()
 assert(request(560).ok and writes==1 and count(sheet,"ingot_purple")==440 and count(sheet,"fodder")==0)
 assert(progress.ore==99930 and progress.gold==99950 and sheet.workshop_slots[slot_index].output_def_id==current_id)
 assert(request(560).err=="workshop_busy" and writes==1 and count(sheet,"ingot_purple")==440)
end)
check("wrong quote returns actual replacement quote without touching resources or job",function()
 local before,wallet=copy(sheet),copy(progress);local result=request(180)
 assert(result.err==Quote.CHANGED and result.craft_quote.schema==1 and result.craft_quote.output_def_id==current_id and result.craft_quote.ingot_n==560)
 assert(same(sheet,before)and same(progress,wallet)and writes==0)
end)
check("invalid numeric and typed quotes fail before access initialization or sheet reads",function()
 for _,bad in ipairs({-1,1.5,"560",false,{},1000000001,math.huge,-math.huge,0/0})do
  reset();local before,wallet=copy(sheet),copy(progress);local result=request(bad)
  assert(result.err==Quote.INVALID and writes==0 and inits==0 and reads==0)
  assert(same(sheet,before)and same(progress,wallet))
 end
end)
check("authentication and epoch precede all quote preflight processing",function()
 assert(Rpc.duel_workshop_craft_start({},{expected_ingot_n=-1}).err=="unauthorized")
 assert(Rpc.duel_workshop_craft_start({user_id="temporary"},{session_epoch=6,expected_ingot_n=-1}).err=="session_stale")
 assert(writes==0 and reads==0 and inits==0)
end)
check("zero ingot quote is valid for a recipe with a different material budget",function()
 reset("unchanged_legendary",0,"legendary","Helmet");assert(request(0).ok and count(sheet,"ingot_purple")==1000)
end)
check("failed quoted write persists no wallet material or job mutation",function()
 local before,wallet=copy(sheet),copy(progress);fail_write=true
 assert(not request(560).ok and same(sheet,before)and same(progress,wallet)and writes==0)
end)
check("legacy existing changed-price job can still be claimed without a quote",function()
 sheet.workshop_slots[slot_index]={output_def_id=current_id,ends_at=os.time()-1}
 local result=Rpc.duel_workshop_craft_claim({user_id="temporary"},{session_epoch=7,slot_index=slot_index-1})
 assert(result.ok and count(sheet,current_id)==1 and sheet.workshop_slots[slot_index].output_def_id=="" and count(sheet,"ingot_purple")==1000)
end)
check("legacy existing changed-price job can still be rushed without an ingot quote",function()
 local ending=os.time()+10000;sheet.workshop_slots[slot_index]={output_def_id=current_id,ends_at=ending}
 local result=Rpc.duel_workshop_craft_rush({user_id="temporary"},{session_epoch=7,slot_index=slot_index-1})
 assert(result.ok and sheet.workshop_slots[slot_index].ends_at==ending-CFG.WORKSHOP_CRAFT_RUSH_SECONDS and count(sheet,"ingot_purple")==1000)
end)
print("Workshop quote safety: "..passed.." actual RPC/cost cases passed")
