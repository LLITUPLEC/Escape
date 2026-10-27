-- Server-priced shop. Ledger sequence, four retained receipts, wallet and
-- inventory delivery are one CAS batch. Compacted requests can never recharge.
local nk = require("nakama")
local function load(n) local ok,m=pcall(require,"modules."..n);if ok then return m end;return require(n) end
local Inventory=load("duel_match3_inventory")
local M={CATALOG_COLLECTION="duel_shop_catalog",CATALOG_KEY="v1",COLLECTION="duel_shop_receipts",KEY="v1",RECEIPT_LIMIT=4,
  SYSTEM_USER_ID="00000000-0000-0000-0000-000000000000",TOKEN_ID="global_boss_teleport"}
M.TOKEN_DEF={kind="material",tier=1,quality="legendary",max_stack=99,sale_price=0}
local deps={}
local function copy(v) if type(v)~="table" then return v end local o={} for k,x in pairs(v) do o[k]=copy(x) end return o end
local function int(n,max) return type(n)=="number" and n==n and n>=0 and n<=max and n==math.floor(n) end
local function price(id,tab,title,currency,cost,gold,ore,energy,items,description,badge,featured,matter)
  return {id=id,tab=tab,title=title,currency=currency,price=cost,gold=gold or 0,ore=ore or 0,matter=matter or 0,energy=energy or 0,
    items=items,description=description or "",badge=badge or "",featured=featured==true,enabled=true}
end
local function item(id,n) return {def_id=id,amount=n} end
M.DEFAULT_CATALOG={schema=1,offers={
  price("expedition_pack","bundles","Набор выжившего","matter",16,2500,2500,200,{item("elixir_assault",1),item("elixir_stone",1)},"Ресурсы, энергия и два эликсира для новых вылазок.","КОМПЛЕКТ",true),
  price("craft_pack","bundles","Запас мастера","matter",30,5000,5000,0,{item("ingot_green",20),item("ingot_blue",20),item("ingot_purple",20)},"По 20 слитков каждого вида и ресурсы для изученных рецептов.","60 СЛИТКОВ"),
  price("supply_pack","bundles","Долгая экспедиция","matter",14,0,0,500,{item("scroll_pve_bounty",1),item("elixir_gambit",1)},"Энергия, свиток добычи и эликсир азарта.","ПРИПАСЫ"),
  price("resources_2500","resources","Малый тайник","matter",10,2500,2500,0,nil,"Золото и руда в равных количествах."),
  price("resources_10000","resources","Большой тайник","matter",40,10000,10000,0,nil,"По 10 000 золота и руды.","ДВОЙНОЙ ЗАПАС",true),
  price("resources_25000","resources","Склад убежища","matter",100,25000,25000,0,nil,"По 25 000 золота и руды."),
  price("ingots_green","resources","Зелёные слитки","matter",3,0,0,0,{item("ingot_green",20)},"20 слитков для ремесла."),
  price("ingots_blue","resources","Синие слитки","matter",4,0,0,0,{item("ingot_blue",20)},"20 слитков для редкой экипировки."),
  price("ingots_purple","resources","Фиолетовые слитки","matter",5,0,0,0,{item("ingot_purple",20)},"20 слитков для эпической экипировки."),
  price("energy_100","supplies","Второе дыхание","matter",1,0,0,100,nil,"100 энергии. Покупка доступна при наличии места в запасе."),
  price("energy_500","supplies","Энергия экспедиции","matter",5,0,0,500,nil,"500 энергии для следующих спусков."),
  price("elixir_assault","supplies","Эликсир натиска","matter",3,0,0,0,{item("elixir_assault",1)},"Урон +20%, броня −15%. Один бой PvE или Pro."),
  price("elixir_stone","supplies","Каменная настойка","matter",3,0,0,0,{item("elixir_stone",1)},"Броня +20%, урон −10%. Один бой PvE или Pro."),
  price("elixir_gambit","supplies","Эликсир азарта","matter",4,0,0,0,{item("elixir_gambit",1)},"Крит +10 п.п., лечение −50%. Один бой PvE или Pro."),
  price("scroll_bounty","supplies","Свиток щедрой добычи","matter",7,0,0,0,{item("scroll_pve_bounty",1)},"Золото и руда PvE ×1,5 на один час."),
  price("tesseract","supplies","Тессеракт","matter",150,0,0,0,{item("tesseract",1)},"Ценный материал для легендарного ремесла.","ЛЕГЕНДАРНЫЙ"),
  price("boss_teleport","supplies","Телепорт к боссу","gold",30000,0,0,0,{item(M.TOKEN_ID,1)},"Одна попытка боя с глобальным боссом. Подготовьте снаряжение.","ГЛОБАЛЬНЫЙ БОСС",true),
  price("rub_resources_99","rub","Припасы убежища","rub",99,10000,10000,0,nil,"10 000 золота + 10 000 руды.","НАБОР",true),
  price("rub_matter_40","rub","Горсть материи","rub",99,0,0,0,nil,"40 материи на выбранные припасы.","",false,40),
  price("rub_matter_125","rub","Сундук материи","rub",299,0,0,0,nil,"125 материи на выбранные припасы.","125 МАТЕРИИ",false,125),
  price("rub_matter_430","rub","Хранилище материи","rub",999,0,0,0,nil,"430 материи на выбранные припасы.","430 МАТЕРИИ",false,430),
  price("rub_matter_1350","rub","Ядро материи","rub",2990,0,0,0,nil,"1 350 материи на выбранные припасы.","1 350 МАТЕРИИ",false,1350),
}}
function M.configure(d)
  for _,k in ipairs({"read_progress","read_sheet","get_defs","build_sheet_write","build_progress_write","guard_epoch"}) do assert(type(d[k])=="function","shop_missing_dependency:"..k) end
  deps=d
end
function M.merge_defs(defs) if defs[M.TOKEN_ID]==nil then defs[M.TOKEN_ID]=copy(M.TOKEN_DEF) end return defs end
local ITEMS={ingot_green=true,ingot_blue=true,ingot_purple=true,tesseract=true,global_boss_teleport=true,
  elixir_assault=true,elixir_stone=true,elixir_gambit=true,scroll_pve_bounty=true}
local OFFER_FIELDS={id=true,tab=true,title=true,description=true,badge=true,featured=true,enabled=true,currency=true,price=true,gold=true,ore=true,matter=true,energy=true,items=true}
local TABS={bundles=true,resources=true,supplies=true,rub=true}
local function text(s,n) return type(s)=="string" and #s<=n end
function M.validate_catalog(v)
  assert(type(v)=="table" and v.schema==1 and type(v.offers)=="table" and #v.offers>0 and #v.offers<=64,"shop_invalid_catalog")
  local seen={}
  for _,o in ipairs(v.offers) do
    assert(type(o)=="table" and text(o.id,64) and o.id:match("^[%w_%-]+$") and not seen[o.id],"shop_invalid_offer_id");seen[o.id]=true
    for k in pairs(o) do assert(OFFER_FIELDS[k],"shop_unknown_offer_field") end
    assert(TABS[o.tab] and text(o.title,160) and #o.title>0 and text(o.description or "",512) and text(o.badge or "",80),"shop_invalid_offer_text")
    assert((o.currency=="matter" or o.currency=="gold" or o.currency=="rub") and (o.tab=="rub")== (o.currency=="rub") and int(o.price,1000000000) and o.price>0,"shop_invalid_price")
    assert(o.enabled==nil or type(o.enabled)=="boolean","shop_invalid_enabled")
    assert(o.featured==nil or type(o.featured)=="boolean","shop_invalid_featured")
    local amount=0
    for _,k in ipairs({"gold","ore","matter","energy"}) do assert(int(o[k] or 0,1000000000),"shop_invalid_grant");amount=amount+(o[k] or 0) end
    assert(o.currency=="rub" or (o.matter or 0)==0,"shop_matter_grant_requires_payment")
    if o.items~=nil then
      assert(type(o.items)=="table" and #o.items<=8,"shop_invalid_items");local ids={}
      for _,i in ipairs(o.items) do
        assert(type(i)=="table" and ITEMS[i.def_id] and not ids[i.def_id] and int(i.amount,1000000) and i.amount>0,"shop_item_not_allowed");ids[i.def_id]=true;amount=amount+i.amount
        for k in pairs(i) do assert(k=="def_id" or k=="amount","shop_invalid_item_field") end
        if i.def_id==M.TOKEN_ID then assert(o.currency=="gold" and #o.items==1 and i.amount==1 and (o.gold or 0)+(o.ore or 0)+(o.matter or 0)+(o.energy or 0)==0,"shop_teleport_requires_gold") end
      end
    end
    assert(amount>0,"shop_empty_offer")
  end
  return v
end
local function catalog()
  local rows=nk.storage_read({{collection=M.CATALOG_COLLECTION,key=M.CATALOG_KEY,user_id=M.SYSTEM_USER_ID}})
  assert(type(rows)=="table","shop_catalog_unavailable")
  local r=rows[1];local v=r and r.value or copy(M.DEFAULT_CATALOG)
  if type(v)=="string" then v=nk.json_decode(v) end
  return M.validate_catalog(v),r and r.version or "builtin-v1"
end
local function ledger(uid)
  local rows=nk.storage_read({{collection=M.COLLECTION,key=M.KEY,user_id=uid}});assert(type(rows)=="table","shop_ledger_unavailable")
  local r=rows[1];local v=r and r.value or {schema=1,last_seq=0,receipts={}}
  assert(type(v)=="table" and v.schema==1 and int(v.last_seq,1000000000000) and type(v.receipts)=="table" and #v.receipts<=M.RECEIPT_LIMIT,"shop_invalid_ledger")
  for _,x in ipairs(v.receipts) do assert(type(x)=="table" and type(x.request_id)=="string" and int(x.purchase_seq,v.last_seq),"shop_invalid_receipt") end
  return v,r and r.version or "*"
end
local function wallet(uid)
  local p,version=deps.read_progress(uid)
  assert(type(p)=="table","shop_wallet_unavailable")
  for _,k in ipairs({"gold","ore","matter","energy"}) do assert(int(p[k] or 0,2000000000),"shop_invalid_wallet") end
  return p,version
end
local function snapshot(uid,v,cv,l,p)
  local rows={} for _,o in ipairs(v.offers) do if o.enabled~=false then rows[#rows+1]=copy(o) end end
  return {ok=true,schema=1,catalog_version=cv,next_sequence=l.last_seq+1,offers=rows,
    wallet={gold=p.gold or 0,ore=p.ore or 0,matter=p.matter or 0,energy=p.energy or 0,energy_max=deps.energy_max and deps.energy_max(uid) or 20000},last_receipt=l.receipts[#l.receipts]}
end
local function parsed(payload,buy)
  if payload==nil or payload=="" then payload={} end
  if type(payload)=="string" then if #payload>640 then return nil end local ok,p=pcall(nk.json_decode,payload);if not ok then return nil end payload=p end
  if type(payload)~="table" then return nil end
  local fields=buy and {session_epoch=true,offer_id=true,request_id=true,purchase_seq=true,catalog_version=true} or {session_epoch=true}
  for k in pairs(payload) do if not fields[k] then return nil end end
  if payload.session_epoch~=nil and not int(payload.session_epoch,2147483647) then return nil end
  if buy and (not text(payload.offer_id,64) or not text(payload.request_id,80) or #payload.request_id<8 or not payload.request_id:match("^[%w_%-]+$")
      or not int(payload.purchase_seq,1000000000000) or payload.purchase_seq<1 or not text(payload.catalog_version,128) or #payload.catalog_version<1) then return nil end
  return payload
end
local function same(r,p) return r.request_id==p.request_id and r.purchase_seq==p.purchase_seq and r.offer_id==p.offer_id and r.catalog_version==p.catalog_version end
local function handle(ctx,payload,buy)
  local uid=ctx and ctx.user_id;if type(uid)~="string" or uid=="" then return nk.json_encode({ok=false,err="unauthorized"}) end
  local ok,result=pcall(function()
    local p=parsed(payload,buy);if not p then return {ok=false,err="bad_payload"} end
    if buy and deps.guard_epoch then local allowed,err=deps.guard_epoch(uid,payload);if not allowed then return {ok=false,err=err or "session_stale"} end end
    for attempt=1,6 do
      local l,lv=ledger(uid);local c,cv=catalog()
      if not buy then return snapshot(uid,c,cv,l,wallet(uid)) end
      for _,r in ipairs(l.receipts) do
        if r.request_id==p.request_id or r.purchase_seq==p.purchase_seq then
          if not same(r,p) then return {ok=false,err="request_id_reused"} end
          local reply=snapshot(uid,c,cv,l,wallet(uid));reply.receipt=copy(r);reply.already_purchased=true;return reply
        end
      end
      if p.purchase_seq<=l.last_seq then return {ok=false,err="request_compacted",next_sequence=l.last_seq+1} end
      if p.purchase_seq~=l.last_seq+1 then return {ok=false,err="sequence_changed",next_sequence=l.last_seq+1} end
      if p.catalog_version~=cv then return {ok=false,err="catalog_changed"} end
      local o for _,x in ipairs(c.offers) do if x.id==p.offer_id and x.enabled~=false then o=x end end
      if not o then return {ok=false,err="offer_unavailable"} end
      if o.currency=="rub" then return {ok=false,err="payment_not_connected"} end
      local progress,pv=wallet(uid)
      if (progress[o.currency] or 0)<o.price then return {ok=false,err="not_enough_"..o.currency} end
      local cap=deps.energy_max and deps.energy_max(uid) or 20000
      if (o.energy or 0)>0 and (progress.energy or 0)+o.energy>cap then return {ok=false,err="energy_full"} end
      progress[o.currency]=(progress[o.currency] or 0)-o.price
      for _,k in ipairs({"gold","ore","matter","energy"}) do
        assert((progress[k] or 0)<=2000000000-(o[k] or 0),"shop_wallet_overflow");progress[k]=(progress[k] or 0)+(o[k] or 0)
      end
      if (o.energy or 0)>0 then progress.energy_updated_at=os.time() end
      local receipt={request_id=p.request_id,purchase_seq=p.purchase_seq,catalog_version=cv,offer_id=o.id,currency=o.currency,price=o.price,
        gold=o.gold or 0,ore=o.ore or 0,matter=o.matter or 0,energy=o.energy or 0,bought_at=os.time()}
      local writes={}
      if o.items and #o.items>0 then
        local sheet=deps.read_sheet(uid);local defs=deps.get_defs();local delivered,pending={},{}
        for _,i in ipairs(o.items) do
          local def=defs[i.def_id];assert(type(def)=="table" and (def.kind=="material" or def.kind=="consumable" or def.kind=="tesseract"),"shop_item_catalog_unavailable")
          local grant=Inventory.add_or_queue(sheet,i.def_id,i.amount,defs);assert(grant.delivered+grant.pending==i.amount,"shop_delivery_mismatch")
          if grant.delivered>0 then delivered[#delivered+1]={def_id=i.def_id,amount=grant.delivered} end
          if grant.pending>0 then pending[#pending+1]={def_id=i.def_id,amount=grant.pending} end
        end
        receipt.delivered_items=#delivered>0 and delivered or nil;receipt.pending_items=#pending>0 and pending or nil
        Inventory.sync_resource_counters(sheet,progress,defs);writes[#writes+1]=deps.build_sheet_write(uid,sheet)
      end
      l.last_seq=p.purchase_seq;l.receipts[#l.receipts+1]=receipt
      while #l.receipts>M.RECEIPT_LIMIT do table.remove(l.receipts,1) end
      writes[#writes+1]=deps.build_progress_write(uid,progress,pv)
      writes[#writes+1]={collection=M.COLLECTION,key=M.KEY,user_id=uid,value=l,version=lv,permission_read=0,permission_write=0}
      local committed,err=pcall(nk.storage_write,writes)
      if committed then local reply=snapshot(uid,c,cv,l,progress);reply.receipt=copy(receipt);return reply end
      -- Always re-read after an ambiguous acknowledgement. A committed receipt
      -- wins even when the transport error did not contain "version".
      local after=ledger(uid)
      for _,r in ipairs(after.receipts) do
        if same(r,p) then local reply=snapshot(uid,c,cv,after,wallet(uid));reply.receipt=copy(r);reply.already_purchased=true;return reply end
      end
      if not tostring(err):find("version",1,true) then error(err) end
    end
    return {ok=false,err="retry_exhausted"}
  end)
  if not ok then nk.logger_error("duel_shop: "..tostring(result));result={ok=false,err="shop_unavailable"} end
  return nk.json_encode(result)
end
function M.rpc_get(ctx,payload) return handle(ctx,payload,false) end
function M.rpc_buy(ctx,payload) return handle(ctx,payload,true) end
return M
