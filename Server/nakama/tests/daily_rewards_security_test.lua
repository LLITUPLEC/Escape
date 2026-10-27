-- Real daily module/inventory, atomic Nakama storage simulation, server clock.
package.path="Server/nakama/?.lua;"..package.path
local function copy(v) if type(v)~="table" then return v end local out={} for k,x in pairs(v) do out[k]=copy(x) end return out end
local db,revision,now,before_write,fail_write,ambiguous
local real_time=os.time
os.time=function() return now end
local function key(c,k,u) return c.."/"..k.."/"..u end
local function put(c,k,u,v) revision=revision+1;db[key(c,k,u)]={collection=c,key=k,user_id=u,value=copy(v),version=tostring(revision)} end
local nk={json_encode=copy,json_decode=function(v) if type(v)=="table" then return copy(v) end error("bad_json") end,logger_error=function() end}
nk.storage_read=function(keys) local out={} for _,k in ipairs(keys) do local r=db[key(k.collection,k.key,k.user_id)];if r then out[#out+1]=copy(r) end end return out end
nk.storage_write=function(writes)
  if before_write then local f=before_write;before_write=nil;f() end
  if fail_write then error("storage unavailable") end
  local seen={}
  for _,w in ipairs(writes) do
    local k=key(w.collection,w.key,w.user_id);assert(not seen[k],"duplicate batch key");seen[k]=true
    local old=db[k];assert(w.version=="*" and not old or old and w.version==old.version,"version conflict")
    assert(w.permission_write==0 and w.user_id~="")
  end
  for _,w in ipairs(writes) do put(w.collection,w.key,w.user_id,w.value) end
  if ambiguous then ambiguous=false;error("connection lost after commit") end
end
package.loaded.nakama=nk
local CFG=require("modules.duel_match3_config")
local Daily=require("modules.duel_daily")
local Inventory=require("modules.duel_match3_inventory")
local defs={ingot_green={max_stack=999,kind="material"},ingot_blue={max_stack=999,kind="material"},ingot_purple={max_stack=999,kind="material"},
  elixir_assault={max_stack=99,kind="consumable"},elixir_stone={max_stack=99,kind="consumable"},elixir_gambit={max_stack=99,kind="consumable"},
  scroll_pve_bounty={max_stack=99,kind="consumable"},global_boss_teleport={max_stack=99,kind="material"},filler={max_stack=1}}
local function read(c,k,u,default) local r=db[key(c,k,u)];return r and copy(r.value) or copy(default),r and r.version or "*" end
local function wallet(u) return read(CFG.PVE_PROGRESS_COLLECTION,CFG.PVE_PROGRESS_KEY,u,{gold=0,ore=0,matter=0}) end
local function sheet(u) local v,ver=read(CFG.CHARACTER_SHEET_COLLECTION,CFG.CHARACTER_SHEET_KEY,u,{});v._storage_version=ver;return v end
Daily.configure({read_progress=wallet,read_sheet=sheet,get_defs=function() return defs end,
  guard_epoch=function(u,p) return p.session_epoch==7,"session_stale" end,
  build_progress_write=function(u,v,ver)return {collection=CFG.PVE_PROGRESS_COLLECTION,key=CFG.PVE_PROGRESS_KEY,user_id=u,value=v,version=ver,permission_read=1,permission_write=0}end,
  build_sheet_write=function(u,v)local ver=v._storage_version;v._storage_version=nil;return {collection=CFG.CHARACTER_SHEET_COLLECTION,key=CFG.CHARACTER_SHEET_KEY,user_id=u,value=v,version=ver,permission_read=1,permission_write=0}end})
local function init() db={};revision=0;now=1792886400;before_write=nil;fail_write=false;ambiguous=false end
local function get(u) return Daily.rpc_get({user_id=u or "alice"},{}) end
local function claim(kind,id,u,day) return Daily.rpc_claim({user_id=u or "alice"},{kind=kind,quest_id=id,day=day or Daily.day(),session_epoch=7}) end
local function seed_done(u,day) put(CFG.STATS_COLLECTION,CFG.STATS_KEY,u or "alice",{daily={day=day or Daily.day(),mine_wins=50,ability_uses=50,pvp_played=50}}) end
local function total(u,id) local v=sheet(u);local n=0;for i,x in pairs(v.inventory or {}) do if x==id then n=n+(v.inventory_counts[i] or 1) end end;for _,r in ipairs(v.inventory_pending_loot or {}) do if r.def_id==id then n=n+r.amount end end;return n end
local passed=0
local function test(name,fn)init();fn();passed=passed+1;print("PASS "..name)end
test("authentication strict payload and server day only",function()
  assert(not Daily.rpc_get({},{}).ok)
  for _,p in ipairs({{user_id="bob"},{now=0},{progress=999},{reward={gold=999}}}) do assert(get().ok and Daily.rpc_get({user_id="alice"},p).err=="bad_payload") end
  assert(claim("login",nil,nil,Daily.day()+1).err=="day_changed")
  assert(Daily.rpc_claim({user_id="alice"},{kind="login",day=Daily.day(),session_epoch=6}).err=="session_stale")
  assert(not next(db))
end)
test("snapshot defines seven days and three zero-progress quests",function()
  local s=get();assert(s.ok and #s.login.rewards==7 and #s.quests==3 and s.login.can_claim and s.login.next_day==1)
  for _,q in ipairs(s.quests)do assert(q.progress==0 and not q.ready and not q.claimed)end
  assert(s.reset_at==((s.day+1)*86400-10800))
end)
test("same-day replay exact once and skipped days preserve cycle",function()
  assert(claim("login").ok);assert(claim("login").already_claimed);local p=wallet("alice");assert(p.gold==150 and p.ore==100)
  now=now+86400*9;assert(get().login.next_day==2);assert(claim("login").ok);assert(total("alice","ingot_green")==3)
end)
test("seven successful claims wrap cycle independent of elapsed days",function()
  for i=1,7 do assert(get().login.next_day==i);assert(claim("login").ok);now=now+86400*(i+1)end
  assert(get().login.next_day==1 and get().login.total_claimed==7)
  local p=wallet("alice");assert(p.gold==450 and p.ore==300 and p.matter==2)
  assert(total("alice","ingot_blue")==3 and total("alice","ingot_purple")==3 and total("alice","elixir_assault")==1 and total("alice","scroll_pve_bounty")==1)
  assert(total("alice","elixir_stone")==1 and total("alice","elixir_gambit")==1 and total("alice","global_boss_teleport")==1)
end)
test("Moscow midnight boundary and stale-window claim",function()
  now=Daily.day()*86400-10800+86399;local day=Daily.day();assert(claim("login").ok)
  now=now+1;assert(Daily.day()==day+1 and claim("login",nil,nil,day).err=="day_changed");assert(claim("login").ok)
end)
test("full inventory queues login loot without loss",function()
  claim("login");now=now+86400;local s={inventory={},inventory_counts={}}
  for i=1,25 do s.inventory[i]="filler";s.inventory_counts[i]=1 end
  put(CFG.CHARACTER_SHEET_COLLECTION,CFG.CHARACTER_SHEET_KEY,"alice",s)
  local r=claim("login");assert(r.ok and r.receipt.pending_items[1].amount==3 and total("alice","ingot_green")==3)
end)
test("progress before opening is trusted and quest claims exact once",function()
  seed_done();local s=get();for _,q in ipairs(s.quests)do assert(q.ready);assert(claim("quest",q.id).ok);assert(claim("quest",q.id).already_claimed)end
  assert(get().completion_ready);assert(claim("completion").ok);assert(claim("completion").already_claimed)
  local p=wallet("alice");assert(p.gold==250 and p.ore==125);assert(total("alice","ingot_green")==2 and total("alice","scroll_pve_bounty")==1)
end)
test("day seven keeps existing three prizes and adds one durable boss teleport with a full inventory",function()
  for i=1,6 do assert(claim("login").ok);now=now+86400 end
  local s={inventory={},inventory_counts={}};for i=1,25 do s.inventory[i]="filler";s.inventory_counts[i]=1 end
  put(CFG.CHARACTER_SHEET_COLLECTION,CFG.CHARACTER_SHEET_KEY,"alice",s)
  local r=claim("login");assert(r.ok and #r.receipt.pending_items==4 and r.receipt.delivered_items==nil)
  assert(total("alice","scroll_pve_bounty")==1 and total("alice","ingot_purple")==3 and total("alice","elixir_gambit")==1)
  assert(total("alice","global_boss_teleport")==1 and claim("login").already_claimed and total("alice","elixir_gambit")==1 and total("alice","global_boss_teleport")==1)
end)
test("incomplete quests and unclaimed completion cannot issue prizes",function()
  assert(claim("quest","mine").err=="quest_incomplete");seed_done();assert(claim("completion").err=="quests_unclaimed")
  assert(claim("quest","forged").err=="unknown_quest");assert(not db[key(Daily.COLLECTION,Daily.KEY,"alice")])
end)
test("new day resets quests and cannot reuse yesterday counters",function()
  seed_done();claim("quest","mine");now=now+86400;local s=get();assert(not s.quests[1].claimed and s.quests[1].progress==0)
  assert(claim("quest","mine").err=="quest_incomplete")
end)
test("parallel login requests award one receipt",function()
  before_write=function()assert(claim("login").ok)end
  assert(claim("login").ok);assert(wallet("alice").gold==150 and get().login.total_claimed==1)
end)
test("parallel quest requests award one receipt",function()
  seed_done();before_write=function()assert(claim("quest","mine").ok)end
  assert(claim("quest","mine").ok);assert(wallet("alice").gold==100)
end)
test("wallet CAS conflict rereads and preserves unrelated gold",function()
  before_write=function()put(CFG.PVE_PROGRESS_COLLECTION,CFG.PVE_PROGRESS_KEY,"alice",{gold=10,ore=0,matter=0})end
  assert(claim("login").ok);assert(wallet("alice").gold==160)
end)
test("storage outage cannot partially commit claim or wallet",function()
  fail_write=true;assert(claim("login").err=="daily_unavailable");assert(not next(db));fail_write=false;assert(claim("login").ok)
end)
test("ambiguous committed response remains exactly once on retry",function()
  ambiguous=true;assert(claim("login").err=="daily_unavailable");assert(claim("login").already_claimed)
  assert(wallet("alice").gold==150 and get().login.total_claimed==1)
end)
test("account isolation keeps progress and receipts separate",function()
  seed_done("alice");assert(claim("quest","mine","bob").err=="quest_incomplete");assert(claim("login",nil,"bob").ok)
  assert(wallet("alice").gold==0 and wallet("bob").gold==150 and get("alice").login.can_claim)
end)
test("server clock regression never permits an earlier-day duplicate",function()
  assert(claim("login").ok);now=now-86400;assert(claim("login").err=="clock_regression");assert(wallet("alice").gold==150)
end)
os.time=real_time
print("daily rewards security: "..passed.." passed")
