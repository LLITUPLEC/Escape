-- Exercises the real shared friend-invitation RPCs; no live users or balances.
package.path = "Server/nakama/modules/?.lua;" .. package.path
local function copy(v)
  if type(v) ~= "table" then return v end
  local out = {}; for k, c in pairs(v) do out[k] = copy(c) end; return out
end
local store, notifications, payments, refunds, setup, serial = {}, {}, {}, {}, nil, 0
local can_pay, friends, epoch, create_ok = true, true, true, true
local function key(o) return o.collection .. "/" .. o.key end
local nk = {
  json_encode = copy, json_decode = copy,
  uuid_v4 = function() serial = serial + 1 return "invite-" .. serial end,
  account_get_id = function(uid) return {user={username=uid .. "-name"}} end,
  friends_list = function(a) return friends and {{user={id=a == "alice" and "bob" or "alice"}}} or {} end,
  storage_list = function() return {}, "" end,
  storage_read = function(ids)
    local out = {}; for _, id in ipairs(ids) do if store[key(id)] then out[#out+1] = {value=copy(store[key(id)]),version="v1"} end end
    return out
  end,
  storage_write = function(rows) for _, row in ipairs(rows) do store[key(row)] = copy(row.value) end end,
  storage_delete = function(rows) for _, row in ipairs(rows) do store[key(row)] = nil end end,
  notification_send = function(uid, subject, content, code) notifications[#notifications+1]={uid=uid,subject=subject,content=copy(content),code=code} end,
  logger_error = function(err) error(err) end,
}
package.loaded.nakama = nk
local api = require("duel_friends_race")({
  CFG={RACE_ENTRY_MATTER=2},
  ContestGoals={race_entry_costs=function() return {{resource="matter",amount=2}} end},
  Guard={assert_client_epoch_matches=function() return epoch, "stale_session" end},
  decode_storage_value=function(row) return row.value end,
  try_match_create=function(params) setup=copy(params); return create_ok and "match-1" or nil end,
  read_pve_progress=function() return {matter=10} end,
  race_progress_balance=function(p, resource) return p[resource] end,
  race_normalize_entry_costs=copy,
  Entry={require_pending=function() return false end,
    pay=function(uid) payments[#payments+1]=uid; return can_pay,can_pay and nil or "not_enough_matter",nil,{newly_paid=can_pay} end,
    cancel=function(uid) refunds[#refunds+1]=uid; return true end},
})
local function reset()
  store, notifications, payments, refunds, setup = {}, {}, {}, {}, nil
  can_pay, friends, epoch, create_ok = true, true, true, true
end
local function invite(kind)
  return api.duel_friends_race_invite({user_id="alice"},{target_user_id="bob",kind=kind})
end
local function respond(id, accept, who, forged_kind)
  return api.duel_friends_race_respond({user_id=who or "bob"},{invite_id=id,accept=accept,kind=forged_kind})
end
local count = 0
local function pass(label) count=count+1;print("PASS "..label) end

reset()
local r=invite("duel")
assert(r.ok and notifications[1].uid=="bob" and notifications[1].code==10020)
assert(notifications[1].content.kind=="duel" and notifications[1].content.from_username=="alice-name")
local ready=respond(r.invite_id,true,nil,"race")
assert(ready.ok and ready.kind=="duel" and ready.opponent_user_id=="alice")
assert(setup.mode=="pvp" and setup.pvp_race==false and #setup.invited==2 and #payments==0)
assert(#notifications==3 and notifications[2].content.kind=="duel" and notifications[3].content.match_id=="match-1")
assert(not respond(r.invite_id,true).ok)
pass("duel sends incoming popup, creates classic PvP for both participants without race fees, ignores forged response kind and rejects replay")

reset()
r=invite(nil)
assert(r.ok and notifications[1].content.kind=="race")
ready=respond(r.invite_id,true,nil,"duel")
assert(ready.ok and ready.kind=="race" and setup.pvp_race==true and #payments==2)
pass("legacy no-kind invite remains paid Race and response cannot bypass entry charges")

reset()
r=invite("duel")
assert(not respond(r.invite_id,true,"mallory").ok and not setup and #payments==0)
assert(respond(r.invite_id,false).ok and notifications[2].content.kind=="duel" and notifications[2].content.status=="declined")
assert(not respond(r.invite_id,true).ok)
pass("only invitee may accept; decline keeps duel title and consumes invitation")

reset()
assert(not invite("pro").ok and not next(store) and #notifications==0)
friends=false
assert(not invite("duel").ok and not next(store))
friends=true;epoch=false
assert(not invite("duel").ok and not next(store))
pass("unknown modes, non-friends and stale sessions cannot create invitations")

reset()
r=invite("duel")
assert(not invite("race").ok and #notifications==1)
local cleared=api.duel_friends_race_clear({user_id="alice"},{reason="match_enter"})
assert(cleared.ok and cleared.cleared==1 and not next(store))
assert(notifications[2].content.kind=="duel" and notifications[2].content.status=="cancelled")
pass("shared inbox prevents duplicate modes and match entry clears duel with correct cancellation kind")

reset()
r=invite("race");create_ok=false
assert(not respond(r.invite_id,true).ok and #payments==2 and #refunds==2)
pass("failed Race creation refunds both newly paid entries")

reset()
r=invite("duel");create_ok=false;can_pay=false
assert(not respond(r.invite_id,true).ok and #payments==0 and #refunds==0)
pass("failed duel creation never touches Race entry state")
print("friends_invitation_kind_test: "..count.." invariants passed")
