-- Actual consumable module + actual immutable journal + transactional CAS store.
-- No network, real account or production data operation.
package.path="Server/nakama/?.lua;"..package.path
local Fixture=require("tests.storage_fault_fixture")
local Effects=require("modules.duel_consumable_effects")
local Inventory=require("modules.duel_match3_inventory")
local original_time=os.time
local F,C,J,Module
local defs={block={max_stack=1}}
local core_file=assert(io.open("Server/nakama/modules/duel_match3.lua"));local core_source=core_file:read("*a");core_file:close()
local normalizer_start=assert(core_source:find("local function normalize_stored_item_def",1,true))
local normalizer_end=assert(core_source:find("local function item_def_is_equipment",normalizer_start,true))
local normalizer_chunk=assert(loadstring(core_source:sub(normalizer_start,normalizer_end-1).."\nreturn normalize_stored_item_def"))
setfenv(normalizer_chunk,setmetatable({CFG=require("modules.duel_match3_config")},{__index=_G}))
local normalize_def=normalizer_chunk()
for id in pairs(Effects.DEFS)do defs[id]=assert(normalize_def({kind="consumable",max_stack=20,sale_price=0,effect_id=id}),"Real catalog normalizer discarded consumable "..id)end
local function sheet(uid)
 local value,version=F.get("sheet",uid,"character")
 value=Inventory.normalize(value or{});value._storage_version=version or"*";return value
end
local function write(uid,s)
 local value=Fixture.copy(s);value._storage_version=nil
 return{collection="sheet",key="character",user_id=uid,value=value,version=s._storage_version or"*",permission_read=1,permission_write=0}
end
local function put(uid,items)
 local s=Inventory.normalize({});for i,row in ipairs(items or{})do s.inventory[i]=row[1];s.inventory_counts[i]=row[2]end
 F.put("sheet",uid,"character",s);F.seed(uid)
end
local function reset()
 F=Fixture.new();F.install();os.time=function()return F.clock end
 package.loaded["modules.duel_consumables"]=nil;package.loaded["duel_consumables"]=nil
 Module=require("modules.duel_consumables");J=require("tests.journal_fixture_admission").wrap(require("modules.duel_match3_journal").new(),F)
 C=Module.new({Guard={assert_client_epoch_matches=function(uid,p)return type(p)=="table"and p.session_epoch==7,"session_epoch_mismatch"end},
  read_sheet=sheet,read_progress=F.read_progress,get_defs=function()return defs end,build_sheet_write=write,
  journal=function()return J end,is_human=function(uid)return uid=="u"or uid=="v"end,
  encode_character=function(s)return{ok=true,inventory=s.inventory,counts=s.inventory_counts,consumables=Module.status(s)}end,
  bind_extra_writes=function(state,uid)
   if state.mode~="pve"then return{}end
   local entry,version=F.get("entry",uid,state.match_id)
   if not entry then return nil,"pve_entry_required"end
   return{{collection="entry",key=state.match_id,user_id=uid,value={consumed=true},version=version,permission_read=0,permission_write=0}}
  end})
 put("u",{{"elixir_assault",2},{"elixir_stone",2},{"elixir_gambit",2},{"scroll_pve_bounty",3}})
 put("v",{{"elixir_gambit",2}})
end
local serial=0
local function use(uid,index,id,request,extra)
 serial=serial+1;local p={session_epoch=7,inv_index=index,expected_def_id=id,request_id=request or("request-"..serial)}
 F.use_requests=F.use_requests or {};local key=uid..":"..p.request_id
 p.use_seq=F.use_requests[key]or Module.status(sheet(uid)).next_use_seq;F.use_requests[key]=p.use_seq
 for k,v in pairs(extra or{})do p[k]=v end
 return C.rpc_use({user_id=uid},p)
end
local function battle(mode,options)
 local s={mode=mode,match_id="match-"..serial,stats_result_id="result-"..serial,stats={u={},v={}},pve_run={}}
 for k,v in pairs(options or{})do s[k]=v end
 if mode=="pve"then F.put("entry","u",s.match_id,{consumed=false})end
 return s
end
local count=0
local function test(name,run)reset();run();count=count+1;print("PASS "..name)end
test("real core catalog normalization retains consumable kind, stack, fixed effect ID and zero gear stats",function()
 for id in pairs(Effects.DEFS)do local d=defs[id];assert(d and d.kind=="consumable"and d.max_stack==20 and d.sale_price==0 and d.effect_id==id and d.slot==""and d.damage==0)end
end)
test("activation consumes one stack unit and durably arms the fixed server effect",function()
 local r=use("u",0,"elixir_assault");assert(r.ok and r.counts[1]==1 and r.consumables.armed_item_id=="elixir_assault")
 assert(sheet("u").consumables.armed.activation_id~="")
end)
test("same request replay consumes nothing; same nonce cannot name another item",function()
 assert(use("u",0,"elixir_assault","same-request").ok)
 assert(use("u",0,"elixir_assault","same-request").ok and sheet("u").inventory_counts[1]==1)
 local r=use("u",1,"elixir_stone","same-request");assert(not r.ok and r.err=="request_id_reused"and sheet("u").inventory_counts[2]==2)
end)
test("armed replacement rejects before consuming another item",function()
 assert(use("u",0,"elixir_assault").ok)
 local r=use("u",1,"elixir_stone");assert(not r.ok and r.err=="consumable_already_armed"and sheet("u").inventory_counts[2]==2)
end)
test("commit-then-lost-ack activation retry returns its immutable receipt",function()
 F.fail_after=true;local first=use("u",0,"elixir_assault","lost-ack-request");assert(not first.ok)
 assert(use("u",0,"elixir_assault","lost-ack-request").ok and sheet("u").inventory_counts[1]==1)
end)
test("precommit failure leaves both inventory and armed state unchanged",function()
 F.fail_before=true;assert(not use("u",0,"elixir_assault").ok);F.fail_before=false
 assert(sheet("u").inventory_counts[1]==2 and not sheet("u").consumables)
end)
test("epoch, slot, expected ID and fixed allowlist prevent forged activation",function()
 assert(not use("u",0,"elixir_assault",nil,{session_epoch=6}).ok)
 assert(not use("u",49,"elixir_assault").ok)
 assert(not use("u",0,"elixir_gambit").ok)
 assert(not use("u",0,"attacker_item").ok)
 assert(sheet("u").inventory_counts[1]==2)
 local r=use("u",0,"elixir_assault",nil,{user_id="v",amount=999,damage_multiplier=999})
 assert(r.ok and sheet("v").inventory_counts[1]==2 and sheet("u").inventory_counts[1]==1)
end)
test("scroll starts one real server hour and rejects stack/extension",function()
 assert(use("u",3,"scroll_pve_bounty","scroll-request").ok)
 local ends=sheet("u").consumables.bounty.ends_at;assert(ends==F.clock+3600)
 F.clock=F.clock+300
 assert(use("u",3,"scroll_pve_bounty","scroll-request").ok and sheet("u").consumables.bounty.ends_at==ends)
 local r=use("u",3,"scroll_pve_bounty");assert(not r.ok and r.err=="bounty_already_active"and sheet("u").inventory_counts[4]==2)
 F.clock=ends;assert(use("u",3,"scroll_pve_bounty").ok and sheet("u").consumables.bounty.ends_at==ends+3600)
end)
test("normal duel, arena, suppressed PvE and Race leave the arm untouched",function()
 assert(use("u",0,"elixir_assault").ok)
 for _,s in ipairs({battle("pvp"),battle("pvp",{pvp_pro=true,arena_mirror={}}),battle("pve",{pve_run={arena_suppress_all=true}}),battle("pve",{arena_pvp_style=true}),battle("pvp",{pvp_pro=true,pvp_race=true})})do
  assert(C.bind_match(s,{"u"})and not s._consumable_bindings)
 end
 assert(sheet("u").consumables.armed.item_id=="elixir_assault")
end)
test("PvE entry confirmation and elixir match binding commit in one batch",function()
 assert(use("u",0,"elixir_assault").ok);local s=battle("pve")
 assert(C.bind_match(s,{"u"}));assert(not sheet("u").consumables.armed)
 assert(F.get("entry","u",s.match_id).consumed)
 local batch=F.writes[#F.writes];local entry,sheet_write,receipt=false,false,false
 for _,op in ipairs(batch)do entry=entry or op.collection=="entry";sheet_write=sheet_write or op.collection=="sheet";receipt=receipt or op.collection=="duel_match3_journal"end
 assert(entry and sheet_write and receipt);C.apply_match(s);assert(s.stats.u.consumable_id=="elixir_assault")
end)
test("missing PvE entry and unstarted/cancelled creation cannot consume the arm",function()
 assert(use("u",0,"elixir_assault").ok)
 local s=battle("pve");F.db[F.id("entry","u",s.match_id)]=nil
 assert(not C.bind_match(s,{"u"})and sheet("u").consumables.armed.item_id=="elixir_assault")
 local file=assert(io.open("Server/nakama/modules/duel_match3.lua"));local source=file:read("*a");file:close()
 local a=assert(source:find("local function match_init",1,true));local b=assert(source:find("local function match_join_attempt",a,true))
 assert(not source:sub(a,b-1):find("Consumables.bind_match",1,true))
end)
test("Pro claims both humans all-or-nothing, including storage faults",function()
 assert(use("u",0,"elixir_assault").ok and use("v",0,"elixir_gambit").ok)
 local s=battle("pvp",{pvp_pro=true});F.fail_before=true
 assert(not C.bind_match(s,{"u","v"}));F.fail_before=false
 assert(sheet("u").consumables.armed and sheet("v").consumables.armed)
 assert(C.bind_match(s,{"u","v"}));C.apply_match(s)
 assert(s.stats.u.consumable_damage_mult==1.2 and s.stats.v.consumable_crit_add==.1)
end)
test("reconnect/retry reads frozen match receipt and cannot steal the next armed elixir",function()
 assert(use("u",0,"elixir_assault").ok)
 local s=battle("pvp",{pvp_pro=true});assert(C.bind_match(s,{"u"}))
 assert(use("u",1,"elixir_stone").ok)
 local rejoin={mode="pvp",pvp_pro=true,match_id=s.match_id,stats={u={}}}
 assert(C.bind_match(rejoin,{"u"}));C.apply_match(rejoin)
 assert(rejoin.stats.u.consumable_id=="elixir_assault"and sheet("u").consumables.armed.item_id=="elixir_stone")
end)
test("lost internal binding acknowledgement resolves before the match start callback returns",function()
 assert(use("u",0,"elixir_assault").ok and use("v",0,"elixir_gambit").ok)
 local s=battle("pvp",{pvp_pro=true});F.fail_after=true
 assert(C.bind_match(s,{"u","v"}));C.apply_match(s)
 assert(s.stats.u.consumable_id=="elixir_assault"and s.stats.v.consumable_id=="elixir_gambit")
 assert(not sheet("u").consumables.armed and not sheet("v").consumables.armed)
end)
test("activation racing an empty initial checkpoint is serialized by sheet CAS",function()
 local s=battle("pvp",{pvp_pro=true})
 F.before_write=function()assert(use("u",0,"elixir_assault").ok)end
 assert(C.bind_match(s,{"u"}));C.apply_match(s)
 assert(s.stats.u.consumable_id=="elixir_assault"and not sheet("u").consumables.armed)
end)
test("same started match without elixir never acquires a later activation",function()
 local s=battle("pvp",{pvp_pro=true});assert(C.bind_match(s,{"u"}))
 assert(use("u",0,"elixir_assault").ok)
 s._consumable_bindings=nil;assert(C.bind_match(s,{"u"}));C.apply_match(s)
 assert(s.stats.u.consumable_id==""and sheet("u").consumables.armed.item_id=="elixir_assault")
end)
test("full inventory queues consumables and delivers via the existing inventory mechanism",function()
 local s=Inventory.normalize({});for i=1,25 do s.inventory[i]="block";s.inventory_counts[i]=1 end
 local r=Inventory.add_or_queue(s,"elixir_gambit",2,defs);assert(r.delivered==0 and r.pending==2)
 s.inventory[1]="";s.inventory_counts[1]=0;assert(Inventory.drain(s,defs).delivered==2 and s.inventory[1]=="elixir_gambit")
end)
test("all combat bonuses/debuffs affect final damage, total armor, healing and capped crit",function()
 local a={};Effects.apply(a,{item_id="elixir_assault"});assert(Effects.damage(50,a)==60 and Effects.armor(100,a)==85)
 Effects.apply(a,{item_id="elixir_stone"});assert(Effects.damage(50,a)==45 and Effects.armor(100,a)==120)
 Effects.apply(a,{item_id="elixir_gambit"});assert(Effects.healing(11,a)==5 and math.abs(Effects.crit(.15,a)-.25)<.00001 and Effects.crit(.99,a)==1)
end)
test("bounty has exact expiry and combines with aura by max",function()
 local s={started_at=1000,ends_at=4600}
 assert(Effects.bounty_multiplier(s,999)==1 and Effects.bounty_multiplier(s,1000)==1.5)
 assert(Effects.bounty_multiplier(s,4599)==1.5 and Effects.bounty_multiplier(s,4600)==1)
 assert(Effects.currency_multiplier(s,1200,1.2)==1.5 and Effects.currency_multiplier(s,1200,2)==2)
end)
test("frozen reward boost touches only PvE gold/ore, never xp/matter/items",function()
 local Rewards=require("modules.duel_match3_pve_rewards").new({normalize_difficulty=function()return"easy"end,get_bot_profile=function()return{reward_xp=20,reward_gold=100,reward_ore=80,reward_matter_min=4,reward_matter_max=4}end,
  clamp_floor=function()return 1 end,is_boss_floor=function()return false end,reward_multiplier=function()return 1 end,stat_multiplier=function()return 1 end,
  apply_reward_xp=function(x)return x end,respawn_seconds=function()return 10 end,next_affix=function()return""end,uses_recipe_drop=function()return false end,ingot_def=function()return"ingot_green"end})
 local p=Rewards.freeze("mine_1",{},"result","u",{currency_gold_multiplier=1.5,currency_ore_multiplier=2})
 assert(p.reward_gold==150 and p.reward_ore==160 and p.reward_xp==20 and p.reward_matter==4 and #p.items==0)
end)
os.time=original_time
test("four activation receipts retain an irreversible sequence watermark",function()
 put("u",{{"elixir_assault",6}})
 for i=1,6 do
  assert(use("u",0,"elixir_assault","bounded-use-"..i).ok)
  assert(C.bind_match(battle("pvp",{pvp_pro=true}),{"u"}))
 end
 local ledger=sheet("u").consumables.use_ledger
 assert(ledger.last_seq==6 and #ledger.receipts==4 and ledger.receipts[1].use_seq==6 and ledger.receipts[4].use_seq==3)
 local stale=use("u",0,"elixir_assault","bounded-use-1")
 assert(not stale.ok and stale.err=="request_compacted"and sheet("u").inventory_counts[1]==0)
end)
test("missing fractional and future activation sequences cannot consume inventory",function()
 local base={session_epoch=7,inv_index=0,expected_def_id="elixir_assault",request_id="sequence-request"}
 assert(C.rpc_use({user_id="u"},base).err=="bad_use_sequence")
 for _,value in ipairs({0,-1,1.5,9007199254740992})do base.use_seq=value;assert(C.rpc_use({user_id="u"},base).err=="bad_use_sequence")end
 base.use_seq=9;assert(C.rpc_use({user_id="u"},base).err=="sequence_changed")
 assert(sheet("u").inventory_counts[1]==2 and Module.status(sheet("u")).next_use_seq==1)
end)
print("Consumables: "..count.." meaningful CAS/security/combat/reward tests passed")
