-- Executes the real account-delete RPC against a mock account API only.
-- No account/network access. Storage APIs throw if the handler attempts a reset.
package.path="Server/nakama/?.lua;"..package.path
local function copy(v)if type(v)~="table"then return v end local r={}for k,x in pairs(v)do r[k]=copy(x)end return r end
local function equal(a,b)if type(a)~=type(b)then return false end if type(a)~="table"then return a==b end for k,v in pairs(a)do if not equal(v,b[k])then return false end end for k in pairs(b)do if a[k]==nil then return false end end return true end
local rpc,calls,db,storage_calls,registered
local nk={json_encode=copy,logger_error=function()end,storage_delete=function()storage_calls=storage_calls+1;error("partial delete forbidden")end,
 storage_list=function()error("listing for partial deletion forbidden")end,storage_write=function()error("partial reset forbidden")end}
package.loaded.nakama=nk
package.loaded["modules.duel_api_rate_limit"]={register_rpc=function(fn,name)rpc=fn;registered=name end}
assert(loadfile("Server/nakama/modules/duel_account_wipe.lua"))()
assert(registered=="duel_account_wipe")
local passed=0
local function reset()
 calls={};storage_calls=0
 db={self={character={inventory_extra_slots=4,items={"item"}},progress={gold=10},journal={status="prepared"},race={state="pending"},inventory_pending={{def_id="loot",amount=2}}},other={character={items={"other"}},progress={gold=90},journal={status="prepared"}}}
 nk.account_delete_id=function(uid)calls[#calls+1]=uid;db[uid]=nil end
end
local function test(name,fn)reset();fn();assert(storage_calls==0,"handler called storage_delete");passed=passed+1;print("PASS "..name)end
test("missing hard-delete API fails closed and retains all account records",function()
 local before=copy(db);nk.account_delete_id=nil
 local r=rpc({user_id="self"},{user_id="other"})
 assert(not r.ok and r.account_deleted==false and r.err=="account_delete_unavailable" and equal(db,before) and #calls==0)
end)
test("database exception leaves profile paid slots journals and pending loot intact",function()
 local before=copy(db);nk.account_delete_id=function(uid)calls[#calls+1]=uid;error("database unavailable")end
 local r=rpc({user_id="self"},{user_id="other"})
 assert(not r.ok and r.account_deleted==false and r.err=="account_delete_failed" and equal(db,before) and calls[1]=="self")
end)
test("explicit API failure is not reported as account deletion",function()
 local before=copy(db);nk.account_delete_id=function(uid)calls[#calls+1]=uid;return false end
 local r=rpc({user_id="self"},"ignored payload")
 assert(not r.ok and r.account_deleted==false and equal(db,before))
end)
test("success deletes only authenticated UID and ignores a forged payload UID",function()
 local other=copy(db.other)
 local r=rpc({user_id="self"},{user_id="other",account_id="other",reset_only=true})
 assert(r.ok and r.account_deleted and #calls==1 and calls[1]=="self" and db.self==nil and equal(db.other,other))
end)
test("payload cannot authorize deletion without an authenticated UID",function()
 local before=copy(db)
 for _,ctx in ipairs({{}, {user_id=""}, {user_id=42}})do local r=rpc(ctx,{user_id="self"});assert(not r.ok and r.account_deleted==false and r.err=="unauthorized")end
 local r=rpc(nil,{user_id="self"});assert(not r.ok and r.account_deleted==false)
 assert(#calls==0 and equal(db,before))
end)
test("failure then retry delegates the complete deletion once and leaves no surviving journal",function()
 local delete=nk.account_delete_id;nk.account_delete_id=function()error("transient failure")end
 assert(not rpc({user_id="self"},{}).ok and db.self.journal.status=="prepared")
 nk.account_delete_id=delete;assert(rpc({user_id="self"},{}).ok and db.self==nil and db.other.journal.status=="prepared")
end)
print("Account hard-delete security: "..passed.." passed; zero storage_delete calls")
