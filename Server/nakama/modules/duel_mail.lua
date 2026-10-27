-- Durable server-created mail. Claim cannot create a missing entitlement.
local nk=require('nakama')
local function load(n)local ok,m=pcall(require,'modules.'..n);if ok then return m end;return require(n)end
local Inventory=load('duel_match3_inventory')
local M={COLLECTION='duel_mail',INDEX_KEY='inbox',HISTORY=4}
local deps={}
local function copy(v)if type(v)~='table'then return v end;local o={};for k,x in pairs(v)do o[k]=copy(x)end;return o end
local function read(uid,key)
 local rows=nk.storage_read({{collection=M.COLLECTION,key=key,user_id=uid}});assert(type(rows)=='table','mail_storage_unavailable')
 local r=rows[1];local v=r and r.value;if type(v)=='string'then v=nk.json_decode(v)end;return v,r and r.version
end
local function op(uid,key,value,version)return{collection=M.COLLECTION,key=key,user_id=uid,value=value,version=version or '*',permission_read=0,permission_write=0}end
local function index(uid)local v,ver=read(uid,M.INDEX_KEY);return v or{schema=1,unclaimed={},claimed={}},ver end
function M.configure(d)deps=d end
-- Build an inbox/message batch for another authoritative transaction. The caller
-- commits these CAS writes together with its permanent delivery receipt.
function M.plan_delivery_writes(uid,messages)
 if not messages or #messages==0 then return{}end
 local i,iv=index(uid);local writes,seen={},{}
 for _,message in ipairs(messages)do
  assert(type(message.id)=='string'and message.id~=''and message.id~=M.INDEX_KEY and not seen[message.id],'mail_invalid_server_id')
  seen[message.id]=true
  if not read(uid,message.id)then
   local m=copy(message);m.claimed=false;m.schema=1
   i.unclaimed[message.id]=message.created_at
   writes[#writes+1]=op(uid,message.id,m)
  end
 end
 if #writes>0 then writes[#writes+1]=op(uid,M.INDEX_KEY,i,iv)end
 return writes
end
-- Internal only. IDs are bound to a server event, never supplied by creation RPC.
function M.deliver(uid,message,proof_write)
 for _=1,6 do
  local existing=read(uid,message.id);if existing then return true end
  local i,iv=index(uid);i.unclaimed[message.id]=message.created_at
  local m=copy(message);m.claimed=false;m.schema=1
  local writes={op(uid,message.id,m),op(uid,M.INDEX_KEY,i,iv)}
  if proof_write then writes[#writes+1]=proof_write end
  local ok=pcall(nk.storage_write,writes)
  if ok or read(uid,message.id)then return true end
 end
 return false,'mail_delivery_deferred'
end
function M.get(uid)
 local i=index(uid);local ids={};for id in pairs(i.unclaimed)do ids[#ids+1]=id end
 table.sort(ids,function(a,b)return(i.unclaimed[a]or 0)>(i.unclaimed[b]or 0)end)
 local messages={};for _,id in ipairs(ids)do local m=read(uid,id);if m and not m.claimed then messages[#messages+1]=m end end
 for j=#i.claimed,1,-1 do local m=read(uid,i.claimed[j]);if m then messages[#messages+1]=m end end
 return{ok=true,unread_count=#ids,messages=#messages>0 and messages or nil}
end
function M.claim(uid,id)
 for _=1,6 do
  local m,mv=read(uid,id);if not m or id==M.INDEX_KEY then return{ok=false,err='mail_not_found'}end
  if m.claimed then local r=copy(m.receipt);r.ok=true;r.claimed=true;r.already_claimed=true;return r end
  local i,iv=index(uid);local p,pv,sheet,defs
  local attachments=m.attachments or{}
  if #attachments>0 then p,pv=deps.read_progress(uid);sheet=deps.read_sheet(uid);defs=deps.get_defs()end
  local receipt={claimed=true,informational=#attachments==0,delivered_items={},pending_items={},resources={}}
  for _,a in ipairs(attachments)do
   if a.def_id then
    assert(defs[a.def_id],'mail_item_missing');local result=Inventory.add_or_queue(sheet,a.def_id,a.amount,defs)
    if result.delivered>0 then receipt.delivered_items[#receipt.delivered_items+1]={def_id=a.def_id,amount=result.delivered}end
    if result.pending>0 then receipt.pending_items[#receipt.pending_items+1]={def_id=a.def_id,amount=result.pending}end
   elseif a.resource=='gold'or a.resource=='ore'or a.resource=='matter'then
    assert((p[a.resource]or 0)+a.amount<=2000000000,'mail_wallet_overflow')
    p[a.resource]=(p[a.resource]or 0)+a.amount;receipt.resources[a.resource]=(receipt.resources[a.resource]or 0)+a.amount
   else error('mail_invalid_attachment')end
  end
  if sheet then Inventory.sync_resource_counters(sheet,p,defs)end
  i.unclaimed[id]=nil;i.claimed[#i.claimed+1]=id
  local old={};while #i.claimed>M.HISTORY do old[#old+1]=table.remove(i.claimed,1)end
  local count=0;for _ in pairs(i.unclaimed)do count=count+1 end;receipt.unread_count=count
  if #receipt.delivered_items==0 then receipt.delivered_items=nil end
  if #receipt.pending_items==0 then receipt.pending_items=nil end
  m.claimed=true;m.claimed_at=os.time();m.receipt=receipt
  local writes={op(uid,id,m,mv),op(uid,M.INDEX_KEY,i,iv)}
  if sheet then writes[#writes+1]=deps.build_sheet_write(uid,sheet);writes[#writes+1]=deps.build_progress_write(uid,p,pv)end
  local ok=pcall(nk.storage_write,writes)
  if ok then
   -- Claimed rows no longer present in the index are optional history only.
   for _,key in ipairs(old)do pcall(nk.storage_delete,{{collection=M.COLLECTION,key=key,user_id=uid}})end
   receipt.ok=true;return receipt
  end
 end
 return{ok=false,err='mail_claim_deferred'}
end
local function rpc(ctx,payload,claim)
 local uid=ctx and ctx.user_id;if type(uid)~='string'or uid==''then return nk.json_encode({ok=false,err='unauthorized'})end
 local p=payload or{};if type(p)=='string'then if#p>512 then return nk.json_encode({ok=false,err='bad_payload'})end;local ok,v=pcall(nk.json_decode,p);p=ok and v or nil end
 if type(p)~='table'then return nk.json_encode({ok=false,err='bad_payload'})end
 for k in pairs(p)do if k~='session_epoch'and not(claim and k=='message_id')then return nk.json_encode({ok=false,err='bad_payload'})end end
 if claim then
  local yes,err=deps.guard_epoch(uid,payload);if not yes then return nk.json_encode({ok=false,err=err})end
  if type(p.message_id)~='string'or#p.message_id>128 or p.message_id==M.INDEX_KEY then return nk.json_encode({ok=false,err='bad_message_id'})end
 end
 local ok,r=pcall(function()return claim and M.claim(uid,p.message_id)or M.get(uid)end)
 if not ok then nk.logger_error('mail deferred: '..tostring(r));r={ok=false,err='mail_unavailable'}end
 return nk.json_encode(r)
end
function M.rpc_get(c,p)return rpc(c,p,false)end
function M.rpc_claim(c,p)return rpc(c,p,true)end
return M
