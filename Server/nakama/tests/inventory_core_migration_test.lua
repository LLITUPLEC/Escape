-- Executes the actual core normalize/serialization helpers (or a deferred copy).
package.path = "Server/nakama/?.lua;" .. package.path
local Inventory = require("modules.duel_match3_inventory")
local path = os.getenv("INVENTORY_CORE_SOURCE") or "Server/nakama/modules/duel_match3.lua"
local f=assert(io.open(path,"r"));local source=f:read("*a");f:close()
local a=assert(source:find("local function normalize_character_sheet",1,true))
local b=assert(source:find("local SET_BONUS_FULL_EQUIP_STATS",a,true))
local calls={}
local cfg={CHARACTER_SHEET_COLLECTION="sheet",CHARACTER_SHEET_KEY="v1",PVE_PROGRESS_COLLECTION="progress",PVE_PROGRESS_KEY="v1"}
local runtime={Inventory=Inventory}
local env=setmetatable({CFG=cfg,Runtime=runtime,get_merged_item_defs=function()return {}end,
  decode_storage_value=function(row)return row.value end,
  nk={storage_write=function(rows)calls[#calls+1]=rows;return {}end}}, {__index=_G})
local chunk=assert(loadstring(source:sub(a,b-1).."\nreturn normalize_character_sheet,write_character_sheet"));setfenv(chunk,env)
local normalize,write=chunk()
local old={first_recipe_received=true,equipment={"helm"},inventory={[1]="old",[30]="paid"},inventory_counts={[1]=3,[30]=2},
  inventory_extra_slots=9,inventory_pending_loot={{def_id="recipe",amount=7}},inventory_slot_receipts={["25"]={request_id="purchase-a"}},
  inventory_sale_receipts={{request_id="sale-one",source="inventory",index=0,expected_def_id="old",sold_item_id="old",sold_count=1,gold_gained=20}},
  learned_recipes={"learned"},workshop_slots={{output_def_id="crafted",ends_at=12345}}}
local sheet=normalize(old);sheet._storage_version="s7"
local p={matter=6,ingots=999,tesseracts=999,blueprint_green=999}
write("fixture",sheet,p,"p8")
local rows=calls[1];assert(#rows==2 and rows[1].version=="s7" and rows[2].version=="p8")
local persisted=rows[1].value
assert(persisted.inventory_sale_receipts[1].request_id=="sale-one", "sale receipt omitted from actual core CAS write")
assert(persisted.inventory_extra_slots==9 and persisted.inventory[1]=="old" and persisted.inventory[30]=="paid")
assert(persisted.inventory_counts[1]==3 and persisted.inventory_counts[30]==2 and persisted.inventory_pending_loot[1].amount==7)
assert(persisted.inventory_slot_receipts["25"].request_id=="purchase-a" and persisted.learned_recipes[1]=="learned")
assert(persisted.workshop_slots[1].output_def_id=="crafted" and persisted.equipment[1]=="helm" and persisted._storage_version==nil)
assert(persisted.first_recipe_received==true,"first recipe receipt omitted by actual core sheet writer")
assert(p.ingots==0 and p.tesseracts==0 and p.blueprint_green==0)
local roundtrip=normalize(persisted)
assert(roundtrip.first_recipe_received==true,"first recipe receipt lost in actual core normalization")
assert(roundtrip.inventory_sale_receipts[1].request_id=="sale-one", "sale receipt omitted from actual core normalization")
assert(Inventory.capacity(roundtrip)==34 and roundtrip.inventory_pending_loot[1].amount==7 and roundtrip.inventory[30]=="paid")
local plain=runtime.build_character_sheet_writes("fixture",roundtrip,p,"p8")
assert(#calls==1 and #plain==2,"pure builder must not write before outcome journal CAS batch")
print("Inventory core migration: actual normalize/write/roundtrip/pure batch passed")
