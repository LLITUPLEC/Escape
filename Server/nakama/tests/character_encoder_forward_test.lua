-- The REAL early core encoder + REAL character-stats calculation. Reader is
-- intentionally declared afterwards, exactly as in the runtime module.
package.path="Server/nakama/?.lua;"..package.path
local function copy(v)if type(v)~="table"then return v end local r={}for k,x in pairs(v)do r[k]=copy(x)end return r end
local nk={json_encode=copy,json_decode=copy,logger_error=function()end,logger_warn=function()end,
 storage_read=function()return{{value={schema_version=1,chains={{id="fixture",steps={}}}}}}end}
package.preload.nakama=function()return nk end
local Inventory=require("modules.duel_match3_inventory")
local Consumables=require("modules.duel_consumables")
local Stats=require("modules.duel_match3_character_stats")
local wallet={level=1,matter=0,gold=3};local reconciled=false
local sheet=Inventory.normalize({consumables={schema=1,armed={item_id="elixir_assault",activation_id="fixture-activation",armed_at=1000}}})
local config={CFG={MAX_HP=150,PVE_MAX_LEVEL=12,clamp_int=function(n,lo,hi)return math.max(lo,math.min(hi,n))end,
 character_stats_base_for_level=function()return{hp=1000,damage=10,armor=5,healing=2,crit_chance=.1}end},
 read_pve_progress=function()return copy(wallet)end,read_character_sheet=function()return copy(sheet)end,
 storage_read_match3_summary_val=function()return{achievement_claimed={}}end,
 cosmetics_hp_bonus=function()if not reconciled then wallet.matter=45;reconciled=true end;return .045 end}
Stats.configure(config)
local file=assert(io.open("Server/nakama/modules/duel_match3.lua"));local source=file:read("*a");file:close()
local a=assert(source:find("local function encode_character_ok_response",1,true));local b=assert(source:find("local function sheet_has_learned",a,true))
local runtime={Inventory=Inventory,ConsumableModule=Consumables}
local env=setmetatable({nk=nk,Runtime=runtime,CharStats=Stats,
 read_pve_progress=function()error("The early encoder must never capture a global reader")end,
 ensure_sheet_inventory_counts=Inventory.normalize,
 character_sheet_payload_arrays=function(s)return s.equipment or{},s.inventory,s.inventory_counts end,
 workshop_payload_arrays=function()return{},{}end,learned_recipes_payload=function()return{}end,
 build_progression_payload=function(p)return copy(p)end,
 late_read=function()return copy(wallet)end},{__index=_G})
local chunk=assert(loadstring(source:sub(a,b-1)..[[
 local function read_pve_progress(uid)return late_read(uid)end
 Runtime.readProgressSnapshot=read_pve_progress
 return encode_character_ok_response
]]));setfenv(chunk,env)
local encode=chunk();local response=encode(copy(sheet),copy(wallet),"fixture")
assert(response.ok and response.stats.hp==1045,"Real stats failed to apply unlocked-cosmetic HP")
assert(response.progression.matter==45,"First character response contains the wallet snapshot from before reconciliation")
assert(response.consumables.armed_item_id=="elixir_assault","Real encoder omitted the consumable state")
assert(encode(copy(sheet),copy(wallet),"fixture").progression.matter==45,"Repeated encode credited cosmetic matter again")
print("character encoder: real stats + late reader + fresh first wallet + consumable payload + repeated snapshot PASS")
local migrated,refreshes=false,0
config.cosmetics_bonuses=function()
 local changed=not migrated
 if changed then
  wallet.matter=wallet.matter+620
  Inventory.add_or_queue(sheet,"tesseract",1,{tesseract={kind="tesseract",max_stack=99}})
  Inventory.sync_resource_counters(sheet,wallet,{})
  migrated=true
 end
 return{hp=.12,armor=.09,damage=.06,healing=.07,healing_flat=300,crit_chance=.01},changed
end
runtime.readCharacterSnapshot=function()refreshes=refreshes+1;return copy(sheet)end
response=encode(copy(sheet),copy(wallet),"fixture")
assert(response.stats.hp==1120 and response.stats.healing==302 and math.abs(response.stats.crit_chance-.11)<1e-10)
assert(response.progression.matter==665 and response.progression.tesseracts==1,"First migrated response returned the pre-grant resource snapshot")
assert(response.inventory_def_ids[1]=="tesseract"and response.inventory_counts[1]==1 and refreshes==1,"First response hid the atomically granted tesseract")
assert(response.consumables.armed_item_id=="elixir_assault","Fresh sheet lost unrelated consumable metadata")
response=encode(copy(sheet),copy(wallet),"fixture")
assert(response.progression.matter==665 and response.inventory_counts[1]==1 and refreshes==1,"Repeat reply regranted item or discarded accepted-sheet snapshot")
print("PASS actual encoder refreshes inventory only for a newly granted cosmetic tesseract and returns all new bonuses and wallet")
