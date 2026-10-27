-- Actual aura module: start/end boundaries, cached scheduled documents and disable.
local make = dofile("Server/nakama/modules/duel_match3_server_aura.lua")
local now, reads, row, fail = 1000, 0, nil, false
local original_time = os.time
os.time = function(...) if select("#",...)>0 then return original_time(...) end;return now end
local nk = { storage_read=function()
  reads=reads+1;if fail then error("storage offline")end;return row and {row} or {}
end,json_encode=function(v)return v end }
local function fresh(value)
  now,reads,fail=1000,0,false;row=value and {value=value,update_time=1000} or nil
  return make({nk=nk,CFG={SERVER_AURA_CACHE_TTL_SECONDS=30},decode_storage_value=function(r)return r.value end})
end
local count=0
local function test(name,fn)fn();count=count+1;print("PASS "..name)end
test("future start does not apply and cached aura activates exactly at start",function()
 local a=fresh({active=true,started_at_unix=1010,ends_at_unix=1020,xp_bonus_pct=50})
 assert(a.get_active()==nil and reads==1);now=1009;assert(a.get_active()==nil and reads==1)
 now=1010;assert(a.xp_multiplier(a.get_active())==1.5 and reads==1)
end)
test("cached aura expires at exact end second",function()
 local a=fresh({active=true,started_at_unix=990,ends_at_unix=1010})
 assert(a.get_active());now=1009;assert(a.get_active());now=1010;assert(a.get_active()==nil and reads==1)
end)
test("duration is calculated from explicit future start",function()
 local a=fresh({active=true,started_at_unix=1010,duration_hours=1/60})
 assert(a.get_active()==nil);now=1010;assert(a.get_active());now=1069;assert(a.get_active());now=1070;assert(a.get_active()==nil)
end)
test("anchor alias honors future start",function()
 local a=fresh({active=true,anchor_unix=1010,ends_at_unix=1020})
 assert(a.get_active()==nil);now=1010;assert(a.get_active())
end)
test("disable is observed after bounded cache refresh",function()
 local a=fresh({active=true,ends_at_unix=2000});assert(a.get_active())
 row={value={active=false,ends_at_unix=2000}};now=1029;assert(a.get_active());now=1030;assert(a.get_active()==nil)
 now=1031;assert(a.get_active()==nil and reads==2)
end)
test("enabled false and absent aura give no bonuses",function()
 local a=fresh({active=true,enabled=false});assert(a.get_active()==nil)
 a=fresh(nil);assert(a.get_active()==nil);now=1020;assert(a.get_active()==nil and reads==1)
end)
test("invalid inverted interval fails closed",function()
 local a=fresh({active=true,started_at_unix=1020,ends_at_unix=1010});assert(a.get_active()==nil)
end)
test("legacy active aura without explicit start remains compatible",function()
 local a=fresh({active=true,ends_at_unix=1020});assert(a.get_active())
 a=fresh({active=true,duration_hours=1});assert(a.get_active())
 a=fresh({active=true});assert(a.get_active())
end)
test("read failures do not return stale aura or remain negatively cached",function()
 local a=fresh({active=true});assert(a.get_active());now=1030;fail=true;assert(a.get_active()==nil)
 fail=false;assert(a.get_active()and reads==3)
end)
test("public read hides scheduled aura before start and at end",function()
 local a=fresh({active=true,started_at_unix=1010,ends_at_unix=1020,title="scheduled"})
 assert(a.rpc_get({},{}) .active==false);now=1010;assert(a.rpc_get({},{}).active==true)
 now=1020;assert(a.rpc_get({},{}).active==false)
end)
os.time=original_time
print("Server aura schedule: "..count.." passed")
