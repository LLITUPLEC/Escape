-- Exercise real public resolver and RPC. Only Nakama I/O is mocked.
local rpcs, records, reads, fail, writes = {}, {}, 0, false, 0
local function clone(v) if type(v)~="table" then return v end local t={} for k,x in pairs(v)do t[k]=clone(x)end return t end
local target="11111111-2222-3333-4444-555555555555"
package.loaded.nakama={
 json_encode=clone,
 json_decode=function(v) if v=="valid"then return {user_id=target,gender="male",selected_theme="phoenix"}end error("invalid json")end,
 storage_read=function(keys)
  reads=reads+1;if fail then error("offline")end
  local result={}for _,key in ipairs(keys)do local row=records[key.collection];if row then result[#result+1]={collection=key.collection,key=key.key,user_id=key.user_id,value=clone(row)}end end
  return result
 end,
 storage_write=function()writes=writes+1;error("public reads must not write")end,
}
package.loaded["modules.duel_api_rate_limit"]={register_rpc=function(fn,name)rpcs[name]=fn end}
local M=assert(loadfile("Server/nakama/modules/duel_public_appearance.lua"))()
local rpc=rpcs.duel_public_appearance_get
local passed=0
local function test(name,fn)records={};reads=0;fail=false;fn();assert(writes==0);passed=passed+1;print("PASS "..name)end
test("legacy male keeps exact default fallback",function()
 local a=M.resolve(target);assert(a.avatar_id==""and a.body_id=="")
end)
test("female default uses correct gender without mutating identity",function()
 records.duel_account_identity={gender="female",finalized=false}
 assert(M.resolve(target).body_id=="dawnwarden-female")
end)
test("only unlocked server outfit can be rendered",function()
 records.duel_account_identity={gender="female"}
 records.duel_player_cosmetics={selected_theme="phoenix",unlocked={phoenix=true}}
 local a=M.resolve(target);assert(a.avatar_id=="phoenix-female"and a.body_id==a.avatar_id)
 records.duel_player_cosmetics.unlocked.phoenix=false
 assert(M.resolve(target).avatar_id=="dawnwarden-female")
 records.duel_player_cosmetics={selected_theme="../../private",unlocked={["../../private"]=true}}
 assert(M.resolve(target).avatar_id=="dawnwarden-female")
end)
test("NPCs never address player storage",function()
 assert(M.resolve("zz-bot-mine_4").body_id==""and reads==0)
end)
test("RPC rejects anonymous malformed oversized and invalid targets",function()
 assert(rpc({},"valid").err=="unauthorized")
 for _,payload in ipairs({"bad",string.rep("x",257)})do assert(rpc({user_id="caller"},payload).err=="bad_payload")end
 assert(reads==0)
end)
test("public RPC output contains only render IDs and target",function()
 records.duel_account_identity={gender="female",secret="private"}
 records.duel_player_cosmetics={selected_theme="voidranger",unlocked={voidranger=true},unlock_reward_receipts={private=true}}
 local a=rpc({user_id="caller"},"valid");assert(a.ok and a.user_id==target and a.avatar_id=="voidranger-female")
 local count=0 for k in pairs(a)do assert(k=="ok"or k=="user_id"or k=="avatar_id"or k=="body_id");count=count+1 end assert(count==4)
end)
test("storage error is unavailable and cannot create reward or identity",function()
 fail=true;assert(rpc({user_id="caller"},"valid").err=="public_appearance_unavailable")
end)
print("public_appearance_test: "..passed.." PASS; zero writes")
