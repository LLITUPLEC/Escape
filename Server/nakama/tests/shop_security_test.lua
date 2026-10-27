-- Real shop + inventory with atomic Nakama storage, CAS races and lost acknowledgements.
package.path="Server/nakama/?.lua;"..package.path
local function copy(v) if type(v)~="table" then return v end local o={} for k,x in pairs(v) do o[k]=copy(x) end return o end
local db,revision,before_write,fail_write,lost_ack,writes
local function key(c,k,u) return c.."/"..k.."/"..u end
local function put(c,k,u,v) revision=revision+1;db[key(c,k,u)]={collection=c,key=k,user_id=u,value=copy(v),version=tostring(revision)} end
local nk={json_encode=copy,json_decode=function(v) assert(type(v)=="table","invalidjson");return copy(v) end,logger_error=function() end}
nk.storage_read=function(keys) local out={} for _,k in ipairs(keys) do local r=db[key(k.collection,k.key,k.user_id)];if r then out[#out+1]=copy(r) end end return out end
nk.storage_write=function(batch)
  if before_write then local f=before_write;before_write=nil;f() end
  if fail_write then error("storage unavailable") end
  local seen={}
  for _,w in ipairs(batch) do
    local k=key(w.collection,w.key,w.user_id);assert(not seen[k],"duplicate write");seen[k]=true
    local old=db[k];assert(w.version=="*" and not old or old and w.version==old.version,"version conflict")
    assert(w.permission_write==0,"protected writes only")
  end
  for _,w in ipairs(batch) do put(w.collection,w.key,w.user_id,w.value) end
  writes=writes+1
  if lost_ack then lost_ack=false;error("connection lost after commit") end
end
package.loaded.nakama=nk
local Shop=require("modules.duel_shop")
local Inventory=require("modules.duel_match3_inventory")
local defs={ingot_green={kind="material",max_stack=200},ingot_blue={kind="material",max_stack=200},ingot_purple={kind="material",max_stack=200},
  tesseract={kind="tesseract",max_stack=5},elixir_assault={kind="consumable",max_stack=20},elixir_stone={kind="consumable",max_stack=20},
  elixir_gambit={kind="consumable",max_stack=20},scroll_pve_bounty={kind="consumable",max_stack=20},filler={kind="material",max_stack=1}}
Shop.merge_defs(defs)
local function read(c,k,u,default) local r=db[key(c,k,u)];return r and copy(r.value) or copy(default),r and r.version or "*" end
local function wallet(u) return read("wallet","v1",u,{gold=100000,ore=0,matter=1000,energy=0}) end
local function sheet(u) local v,ver=read("sheet","v1",u,{});v._storage_version=ver;return v end
Shop.configure({read_progress=wallet,read_sheet=sheet,get_defs=function() return defs end,energy_max=function() return 20000 end,
  guard_epoch=function(u,p) return p.session_epoch==7,"session_stale" end,
  build_progress_write=function(u,v,ver)return {collection="wallet",key="v1",user_id=u,value=v,version=ver,permission_read=1,permission_write=0}end,
  build_sheet_write=function(u,v)local ver=v._storage_version;v._storage_version=nil;return {collection="sheet",key="v1",user_id=u,value=v,version=ver,permission_read=1,permission_write=0}end})
local function get(u) return Shop.rpc_get({user_id=u or "alice"},{}) end
local function request(offer,seq,id,version) return {offer_id=offer or "resources_2500",purchase_seq=seq or 1,request_id=id or "request_shop_0001",catalog_version=version or get().catalog_version,session_epoch=7} end
local function buy(p,u) return Shop.rpc_buy({user_id=u or "alice"},p or request()) end
local function total(id,u) local s=sheet(u or "alice");local n=0;for i,x in pairs(s.inventory or {}) do if x==id then n=n+(s.inventory_counts[i] or 1) end end;for _,x in ipairs(s.inventory_pending_loot or {}) do if x.def_id==id then n=n+x.amount end end;return n end
local function edit(fn) local c=copy(Shop.DEFAULT_CATALOG);fn(c);put(Shop.CATALOG_COLLECTION,Shop.CATALOG_KEY,Shop.SYSTEM_USER_ID,c) end
local passed=0
local function test(name,fn) db={};revision=0;before_write=nil;fail_write=false;lost_ack=false;writes=0;fn();passed=passed+1;print("PASS "..name) end
test("read only catalog exposes22 offers and next sequence without any writes",function() local r=get();assert(r.ok and #r.offers==22 and r.next_sequence==1 and r.wallet.matter==1000 and writes==0) end)
test("authentication and arbitrary client prices/rewards are rejected",function()
  assert(Shop.rpc_get({},{}).err=="unauthorized")
  for _,field in ipairs({"user_id","price","currency","gold","items","reward"}) do local p=request();p[field]=999;assert(buy(p).err=="bad_payload") end
  local p=request();p.session_epoch=6;assert(buy(p).err=="session_stale");assert(writes==0)
end)
test("strict sequence request id and quote payload types",function()
  for _,n in ipairs({0,-1,1.5,math.huge,"1"}) do local p=request();p.purchase_seq=n;assert(buy(p).err=="bad_payload") end
  for _,id in ipairs({"short","bad/id","with spaces"}) do local p=request();p.request_id=id;assert(buy(p).err=="bad_payload") end
  local p=request();p.catalog_version=false;assert(buy(p).err=="bad_payload" and writes==0)
end)
test("purchase atomic receipt debit and two resource grants",function()
  local r=buy();assert(r.ok and r.receipt.price==10 and r.next_sequence==2)
  local p=wallet("alice");assert(p.gold==102500 and p.ore==2500 and p.matter==990 and writes==1)
  assert(db[key(Shop.COLLECTION,Shop.KEY,"alice")].value.last_seq==1)
end)
test("same request exact once even after admin catalog update",function()
  local p=request();local first=buy(p);edit(function(c)c.offers[4].price=100 end)
  local r=buy(p);assert(r.ok and r.already_purchased and r.receipt.price==first.receipt.price and wallet("alice").matter==990 and writes==1)
end)
test("retention four rejects compacted exact payload after six purchases",function()
  local first=request(nil,1,"purchase_retention_1")
  for n=1,6 do assert(buy(request(nil,n,"purchase_retention_"..n)).ok) end
  local before=wallet("alice").matter;local r=buy(first)
  assert(r.err=="request_compacted" and wallet("alice").matter==before and writes==6)
  local l=db[key(Shop.COLLECTION,Shop.KEY,"alice")].value;assert(l.last_seq==6 and #l.receipts==4)
end)
test("request fingerprint and sequence conflict never charge",function()
  local p=request();assert(buy(p).ok)
  p.offer_id="boss_teleport";assert(buy(p).err=="request_id_reused")
  assert(buy(request(nil,1,"different_request")).err=="request_id_reused")
  assert(buy(request(nil,4,"future_request_04")).err=="sequence_changed" and writes==1)
end)
test("changed price requires new quote and explicit user intent",function()
  local p=request();edit(function(c)c.offers[4].price=20 end)
  assert(buy(p).err=="catalog_changed" and writes==0)
  local r=buy(request());assert(r.ok and r.receipt.price==20 and wallet("alice").matter==980)
end)
test("every rub offer remains payment stub without ledger or debit",function()
  for _,o in ipairs(get().offers) do if o.currency=="rub" then assert(buy(request(o.id)).err=="payment_not_connected") end end
  assert(writes==0 and not next(db))
end)
test("insufficient currency and full energy do not advance ledger",function()
  put("wallet","v1","alice",{gold=1,ore=0,matter=0,energy=20000})
  assert(buy().err=="not_enough_matter" and buy(request("boss_teleport")).err=="not_enough_gold")
  put("wallet","v1","alice",{gold=100000,ore=0,matter=1000,energy=19950})
  assert(buy(request("energy_100")).err=="energy_full" and get().next_sequence==1 and writes==0)
end)
test("full bag defers purchased items and keeps exact token gold price",function()
  local s={inventory={},inventory_counts={}};for i=1,25 do s.inventory[i]="filler";s.inventory_counts[i]=1 end;put("sheet","v1","alice",s)
  local r=buy(request("boss_teleport"));assert(r.ok and r.receipt.currency=="gold" and r.receipt.price==30000 and r.receipt.pending_items[1].def_id==Shop.TOKEN_ID and total(Shop.TOKEN_ID)==1)
  assert(wallet("alice").gold==70000 and wallet("alice").matter==1000 and total("filler")==25)
end)
test("successful item pack synchronizes resource counters in the same batch",function()
  local r=buy(request("craft_pack"));assert(r.ok and #r.receipt.delivered_items==3 and total("ingot_green")==20 and total("ingot_blue")==20 and total("ingot_purple")==20)
  assert(wallet("alice").ingots==60 and wallet("alice").matter==970 and writes==1)
end)
test("failed atomic storage never partially debits or delivers",function()
  fail_write=true;local r=buy(request("boss_teleport"));assert(r.err=="shop_unavailable" and not next(db) and writes==0)
end)
test("lost acknowledgement resolves committed receipt without second charge",function()
  lost_ack=true;local p=request("boss_teleport");local r=buy(p)
  assert(r.ok and r.already_purchased and total(Shop.TOKEN_ID)==1 and wallet("alice").gold==70000 and writes==1)
  assert(buy(p).ok and writes==1 and total(Shop.TOKEN_ID)==1)
end)
test("racing different offers cannot overspend or bypass sequence admission",function()
  before_write=function() assert(buy(request("resources_2500",1,"concurrent_winner")).ok) end
  local r=buy(request("boss_teleport",1,"concurrent_loser1"));assert(r.err=="request_id_reused")
  assert(wallet("alice").gold==102500 and wallet("alice").matter==990 and total(Shop.TOKEN_ID)==0 and writes==1)
end)
test("racing same request returns one grant to both callers",function()
  local p=request("ingots_green");before_write=function() assert(buy(p).ok) end
  local r=buy(p);assert(r.ok and r.already_purchased and total("ingot_green")==20 and wallet("alice").matter==997 and writes==1)
end)
test("unknown disabled and recipe offers cannot bypass mine progression",function()
  assert(buy(request("unknown")).err=="offer_unavailable")
  edit(function(c)c.offers[4].enabled=false end);assert(buy(request()).err=="offer_unavailable")
  edit(function(c)c.offers[4].items={{def_id="recipe_drop_t11_epic_weapon",amount=1}} end);assert(not get().ok and writes==0)
end)
test("catalog validates trusted item kind before debit",function()
  local old=defs.ingot_green.kind;defs.ingot_green.kind="recipe"
  assert(buy(request("ingots_green")).err=="shop_unavailable" and writes==0 and total("ingot_green")==0);defs.ingot_green.kind=old
end)
test("missing-only token fallback preserves existing definitions",function()
  local before={x={kind="recipe",max_stack=1}};local merged=Shop.merge_defs(before);assert(merged.x.kind=="recipe" and merged[Shop.TOKEN_ID].max_stack==99)
  merged[Shop.TOKEN_ID].sale_price=77;Shop.merge_defs(merged);assert(merged[Shop.TOKEN_ID].sale_price==77)
end)
print("Shop security tests passed: "..passed)
