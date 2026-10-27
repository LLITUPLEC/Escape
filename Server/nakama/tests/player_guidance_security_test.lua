-- Real Guidance + inventory + auth identity + session guard + extracted core sheet
-- helpers. Only Nakama storage/auth boundaries are simulated; every batch is CAS-atomic.
package.path="Server/nakama/?.lua;"..package.path
local Fixture=assert(loadfile("Server/nakama/tests/storage_fault_fixture.lua"))()
local F=Fixture.new();F.install()
local copy=Fixture.copy
local before,after,rpcs,accounts,json_payloads={},{},{},{},{}
F.nk.register_req_before=function(fn,name)before[name]=fn end
F.nk.register_req_after=function(fn,name)after[name]=fn end
F.nk.account_get_id=function(uid)return copy(accounts[uid] or {user={metadata={session_epoch="7"}}})end
F.nk.json_decode=function(p)if type(p)=="table"then return copy(p)end;if json_payloads[p]then return copy(json_payloads[p])end;error("invalid_json")end
package.loaded["modules.duel_api_rate_limit"]={register_rpc=function(fn,name)rpcs[name]=fn end}
local CFG=require("modules.duel_match3_config")
local Inventory=require("modules.duel_match3_inventory")
local Guidance=require("modules.duel_player_guidance")
local Guard=require("modules.duel_match3_session_guard")({nk=F.nk,CFG=CFG})
local defs={recipe={kind="recipe",max_stack=1},filler={kind="equipment",max_stack=1},crafted={kind="equipment",max_stack=1}}
Guidance.configure({get_defs=function()return defs end,guard_epoch=Guard.assert_client_epoch_matches})
local Gender=require("modules.duel_account_gender")
local function account(uid,email)accounts[uid]={email=email or "",user={metadata={[CFG.SESSION_EPOCH_ACCOUNT_META]="7"}}}end
local function put(c,k,u,v)F.put(c,u,k,v)end
local function value(c,k,u)return F.get(c,u,k)end
local function get(u,p)return Guidance.rpc_get({user_id=u or "alice"},p or {})end
local function set(u,result,epoch)return Guidance.rpc_set({user_id=u or "alice"},{result=result,session_epoch=epoch or 7})end
local function seed_sheet(u,s)put(CFG.CHARACTER_SHEET_COLLECTION,CFG.CHARACTER_SHEET_KEY,u or "alice",s)end
local function seed_stats(n,u)put(CFG.STATS_COLLECTION,CFG.STATS_KEY,u or "alice",{modes={mine={wins=n}}})end
local function seed_progress(p,u)put(CFG.PVE_PROGRESS_COLLECTION,CFG.PVE_PROGRESS_KEY,u or "alice",p)end
local function marker(u)return value(Guidance.COLLECTION,Guidance.KEY,u or "alice")end
local function fresh(u)account(u);after.AuthenticateDevice({user_id=u},{created=true},{})end
local f=assert(io.open("Server/nakama/modules/duel_match3.lua","r"));local source=f:read("*a");f:close()
local a=assert(source:find("local function normalize_character_sheet",1,true));local b=assert(source:find("local SET_BONUS_FULL_EQUIP_STATS",a,true))
local core_env=setmetatable({CFG=CFG,Runtime={Inventory=Inventory},get_merged_item_defs=function()return defs end,
  decode_storage_value=function(row)return row.value end,nk=F.nk},{__index=_G})
local chunk=assert(loadstring(source:sub(a,b-1).."\nreturn normalize_character_sheet,read_character_sheet,write_character_sheet"));setfenv(chunk,core_env)
local normalize,read_sheet,write_sheet=chunk()
defs.crafted.slot="Helmet";defs.crafted.tier=1;defs.crafted.quality="normal";defs.crafted.craft_recipe_id="recipe"
local Economy=require("modules.duel_match3_economy_rpc")({CFG=CFG,Guard=Guard,EQUIP_ORDER={"Helmet"},
 read_character_sheet=read_sheet,write_character_sheet=write_sheet,ensure_sheet_inventory_counts=Inventory.normalize,
 ensure_character_sheet_initialized=function()end,get_merged_item_defs=function()return defs end,
 item_def_is_equipment=function(d)return d.kind=="equipment"end,
 ensure_sheet_workshop=function(s)s.workshop_slots=s.workshop_slots or {};for i=1,8 do s.workshop_slots[i]=s.workshop_slots[i] or {}end end,
 sheet_has_learned_for_craft=function(s,id)for _,x in ipairs(s.learned_recipes or {})do if x==id then return true end end;return false end,
 workshop_craft_cost_from_def=function()return{ore=100,gold=75,ingot_def="",ingot_n=0,tesseract_n=0}end,
 workshop_craft_duration_seconds=function()return 60 end,
 read_pve_progress=function(u)return F.get(CFG.PVE_PROGRESS_COLLECTION,u,CFG.PVE_PROGRESS_KEY)end,
 encode_character_ok_response=function(s,p)return{ok=true,sheet=copy(s),progression=copy(p)}end})
local function craft(payload,ctx)return Economy.duel_workshop_craft_start(ctx or {user_id="alice"},payload or {session_epoch=7,slot_index=0,output_def_id="crafted"})end
local passed=0
local function test(name,fn)
 F.db={};F.writes={};F.read_failure=false;F.fail_before=false;F.fail_after=false;F.before_write=nil;accounts={};json_payloads={}
 account("alice");account("bob");fn();passed=passed+1;print("PASS "..name)
end

test("0 and9 wins lock tournaments; exact10 unlocks",function()
 assert(get().mine_wins==0 and not get().tournaments_unlocked)
 seed_stats(9);assert(get().mine_wins==9 and not get().tournaments_unlocked)
 seed_stats(10);assert(get().mine_wins==10 and get().tournaments_unlocked)
 assert(#F.writes==0)
end)
test("three legacy views use max, never sum or ignore stronger durable view",function()
 seed_stats(3);seed_progress({mine={floor_states={a={wins=2},b={wins=1}}},defeated={mine_1=3,not_a_mine=999}})
 assert(get().mine_wins==3 and not get().tournaments_unlocked)
 seed_stats(8);seed_progress({mine={floor_states={a={wins=4},b={wins=6}}},defeated={mine_1=9}})
 assert(get().mine_wins==10 and get().tournaments_unlocked)
 seed_progress({defeated={mine_1=11,mine_2=1,arena_1=10000}});assert(get().mine_wins==12)
end)
test("invalid counters fail closed",function()
 for _,n in ipairs({-1,0/0,math.huge,-math.huge,"not-a-number"})do seed_stats(n);assert(get().mine_wins==0)end
end)
test("workshop does not unlock without a real recipe",function()
 seed_sheet(nil,{inventory={"filler"},inventory_counts={1}});assert(not get().workshop_unlocked)
 seed_sheet(nil,{first_recipe_received="true"});assert(not get().workshop_unlocked)
end)
test("first recipe full bag queues item and permanently marks receipt",function()
 local s={inventory={},inventory_counts={}};for i=1,25 do s.inventory[i]="filler";s.inventory_counts[i]=1 end
 local r=Inventory.add_or_queue(s,"recipe",1,defs);assert(r.delivered==0 and r.pending==1 and s.first_recipe_received)
 seed_sheet(nil,s);assert(get().workshop_unlocked)
 local loaded=read_sheet("alice");loaded.inventory_pending_loot={};write_sheet("alice",loaded)
 assert(value(CFG.CHARACTER_SHEET_COLLECTION,CFG.CHARACTER_SHEET_KEY,"alice").first_recipe_received==true and get().workshop_unlocked)
end)
test("legacy bag learned pending sold and craft proofs are recognized",function()
 for _,s in ipairs({{inventory={"recipe"},inventory_counts={1}},{learned_recipes={"recipe"}},{inventory_pending_loot={{def_id="recipe",amount=1}}},
  {inventory_sale_receipts={{request_id="sold",source="inventory",index=0,expected_def_id="recipe",sold_item_id="recipe"}}},{workshop_slots={{output_def_id="crafted",ends_at=1}}}})do
  seed_sheet(nil,s);assert(get().workshop_unlocked)
 end
end)
test("actual core read write roundtrip retains first recipe after sale or learning",function()
 seed_sheet(nil,{inventory={"recipe"},inventory_counts={1}})
 local s=read_sheet("alice");assert(s.first_recipe_received);s.inventory={};s.inventory_counts={};s.learned_recipes={};write_sheet("alice",s)
 local persisted=value(CFG.CHARACTER_SHEET_COLLECTION,CFG.CHARACTER_SHEET_KEY,"alice")
 assert(normalize(persisted).first_recipe_received and get().workshop_unlocked)
end)
test("stale inventory CAS cannot erase a concurrently received recipe marker",function()
 seed_sheet(nil,{})
 local stale=read_sheet("alice");local current=read_sheet("alice");assert(Inventory.try_add(current,"recipe",1,defs));write_sheet("alice",current)
 assert(not pcall(write_sheet,"alice",stale));assert(get().workshop_unlocked and read_sheet("alice").first_recipe_received)
end)
test("new device and new direct email seed pending in same identity batch",function()
 fresh("device");assert(marker("device").tutorial=="pending")
 local writes=F.writes[1];assert(#writes==2 and writes[1].collection=="duel_account_identity" and writes[2].collection==Guidance.COLLECTION)
 assert(writes[1].version=="*" and writes[2].version=="*" and writes[2].permission_write==0 and writes[2].permission_read==0)
 account("email","new@example.test");Gender.after_email({user_id="email"},{created=true},{account={vars={gender="female"}}})
 assert(marker("email").tutorial=="pending" and get("email").tutorial_status=="pending")
end)
test("existing accounts never gain pending from auth request spoofing or identity get",function()
 after.AuthenticateDevice({user_id="alice"},{created=false},{created=true,account={vars={created=true}}})
 assert(get().tutorial_status=="legacy" and not marker())
 Gender.after_email({user_id="bob"},{created=false},{account={vars={created=true}}});assert(get("bob").tutorial_status=="legacy" and not marker("bob"))
end)
test("new-account replay does not reset completed tutorial",function()
 fresh("alice");assert(set(nil,"completed").tutorial_status=="completed")
 after.AuthenticateDevice({user_id="alice"},{created=true},{});assert(marker().tutorial=="completed")
end)
test("identity and tutorial initialization reject atomically on storage failure",function()
 F.fail_before=true;assert(not pcall(fresh,"alice"));assert(not marker() and not value("duel_account_identity","gender_v1","alice"))
end)
test("initial identity lost acknowledgement leaves both rows committed once",function()
 F.fail_after=true;fresh("alice");assert(marker().tutorial=="pending" and value("duel_account_identity","gender_v1","alice"))
 assert(#F.writes==1 and #F.writes[1]==2)
end)
test("competing tutorial create cannot be overwritten by initial auth CAS",function()
 F.before_write=function()put(Guidance.COLLECTION,Guidance.KEY,"alice",{schema=1,tutorial="completed"})end
 fresh("alice");assert(marker().tutorial=="completed" and value("duel_account_identity","gender_v1","alice"))
end)
test("unauthorized strict payload and forged eligibility never write",function()
 assert(Guidance.rpc_get({},{}).err=="unauthorized" and Guidance.rpc_set({},{result="completed"}).err=="unauthorized")
 for _,p in ipairs({{mine_wins=10},{workshop_unlocked=true},{user_id="bob"},{session_epoch=7}})do assert(get(nil,p).err=="bad_payload")end
 for _,p in ipairs({{result="pending",session_epoch=7},{result="completed",session_epoch=7,user_id="bob"},{result="completed",session_epoch=7,first_recipe_received=true},{result="completed",session_epoch=7,reward=999}})do
  assert(Guidance.rpc_set({user_id="alice"},p).err=="bad_payload")
 end
 assert(Guidance.rpc_set({user_id="alice"},string.rep("x",513)).err=="bad_payload" and #F.writes==0)
end)
test("real session epoch guard rejects missing and stale epochs",function()
 assert(Guidance.rpc_set({user_id="alice"},{result="completed"}).err=="session_epoch_required")
 assert(set(nil,"completed",6).err=="session_stale" and not marker())
 json_payloads['{"result":"completed","session_epoch":7}']={result="completed",session_epoch=7}
 assert(Guidance.rpc_set({user_id="alice"},'{"result":"completed","session_epoch":7}').tutorial_status=="completed")
end)
test("completion skip replay and account isolation are monotonic",function()
 fresh("alice");fresh("bob");assert(set(nil,"skipped").tutorial_status=="skipped")
 assert(set(nil,"completed").tutorial_status=="completed");local _,version=F.get(Guidance.COLLECTION,"alice",Guidance.KEY)
 assert(set(nil,"skipped").tutorial_status=="completed" and set(nil,"completed").tutorial_status=="completed")
 local _,after_version=F.get(Guidance.COLLECTION,"alice",Guidance.KEY);assert(version==after_version and marker("bob").tutorial=="pending")
end)
test("parallel completion commits once",function()
 F.before_write=function()assert(set(nil,"completed").ok)end
 assert(set(nil,"completed").tutorial_status=="completed")
 local v=marker();assert(v.tutorial=="completed")
end)
test("completed wins against a concurrent skipped write",function()
 fresh("alice");F.before_write=function()assert(set(nil,"completed").ok)end
 assert(set(nil,"skipped").tutorial_status=="completed" and marker().tutorial=="completed")
end)
test("ambiguous completion acknowledgement rereads receipt without reset",function()
 fresh("alice");F.fail_after=true;assert(set(nil,"completed").tutorial_status=="completed" and marker().tutorial=="completed")
end)
test("read and write outages cannot unlock content or forge tutorial result",function()
 F.read_failure=true;assert(get().err=="guidance_unavailable");F.read_failure=false
 F.fail_before=true;assert(not set(nil,"completed").ok and not marker())
end)
test("actual craft gate rejects spoofed recipe receipt before wallet or inventory changes",function()
 seed_progress({gold=1000,ore=1000});seed_sheet(nil,{})
 local p={session_epoch=7,slot_index=0,output_def_id="crafted",first_recipe_received=true,workshop_unlocked=true}
 assert(craft(p).err=="workshop_locked" and #F.writes==0)
 local wallet=value(CFG.PVE_PROGRESS_COLLECTION,CFG.PVE_PROGRESS_KEY,"alice");assert(wallet.gold==1000 and wallet.ore==1000)
 p.session_epoch=6;assert(craft(p).err=="session_stale" and #F.writes==0)
end)
test("first pending recipe opens workshop but does not bypass recipe learning",function()
 seed_progress({gold=1000,ore=1000});seed_sheet(nil,{inventory_pending_loot={{def_id="recipe",amount=1}}})
 assert(craft().err=="recipe_not_learned" and #F.writes==0)
end)
test("learned legacy recipe starts real craft and atomically preserves permanent unlock",function()
 seed_progress({gold=1000,ore=1000});seed_sheet(nil,{learned_recipes={"recipe"}})
 local r=craft();assert(r.ok and r.progression.gold==925 and r.progression.ore==900)
 local saved=value(CFG.CHARACTER_SHEET_COLLECTION,CFG.CHARACTER_SHEET_KEY,"alice")
 assert(saved.first_recipe_received and saved.workshop_slots[1].output_def_id=="crafted" and #F.writes==1 and #F.writes[1]==2)
 assert(craft().err=="workshop_busy" and #F.writes==1)
end)
test("real storage guard blocks tutorial and first-recipe state spoofing",function()
 assert(loadfile("Server/nakama/modules/duel_storage_guard.lua"))()
 assert(before.WriteStorageObjects({user_id="alice"},{objects={{collection=Guidance.COLLECTION,key=Guidance.KEY,value={schema=1,tutorial="completed"}}}})==nil)
 assert(before.DeleteStorageObjects({user_id="alice"},{object_ids={{collection=Guidance.COLLECTION,key=Guidance.KEY}}})==nil)
 assert(before.WriteStorageObjects({user_id="alice"},{objects={{collection=CFG.CHARACTER_SHEET_COLLECTION,key=CFG.CHARACTER_SHEET_KEY,value={first_recipe_received=true}}}})==nil)
 assert(not marker() and #F.writes==0)
end)
print("Player guidance security: "..passed.." passed")
