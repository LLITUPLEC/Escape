-- Real gender module, mocked Nakama storage only. No network or player data.
package.path = "Server/nakama/?.lua;" .. package.path
local function copy(v) if type(v) ~= "table" then return v end local r = {} for k,x in pairs(v) do r[k] = copy(x) end return r end
local before, after, rpcs, db, accounts, versions, fault, race = {}, {}, {}, {}, {}, 0, false, nil
local nk = {
  json_encode = copy, json_decode = function(v) if type(v) == "table" then return v end error("bad json") end,
  logger_error = function() end,
  register_req_before = function(fn, name) assert(not before[name], "duplicate before " .. name); before[name] = fn end,
  register_req_after = function(fn, name) assert(not after[name], "duplicate after " .. name); after[name] = fn end,
  account_get_id = function(uid) assert(accounts[uid], "unknown account"); return copy(accounts[uid]) end,
  storage_read = function(keys)
    if fault then error("storage unavailable") end
    local r = {} for _,key in ipairs(keys) do if db[key.user_id] then r[#r+1] = copy(db[key.user_id]) end end return r
  end,
  storage_write = function(writes)
    if fault then error("storage unavailable") end
    for _,w in ipairs(writes) do
      assert(w.collection == "duel_account_identity" and w.key == "gender_v1")
      assert(w.permission_read == 0 and w.permission_write == 0)
      if race then local competing = race; race = nil; competing(w) end
      local old = db[w.user_id]
      assert(w.version == "*" and not old or old and w.version == old.version, "CAS conflict")
      versions = versions + 1; db[w.user_id] = { version = tostring(versions), value = copy(w.value) }
    end
  end,
}
package.loaded.nakama = nk
-- This suite isolates immutable gender. The real two-record auth initialization
-- and Guidance CAS/fault behavior are covered by player_guidance_security_test.
package.loaded["modules.duel_player_guidance"] = { new_account_write = function() return nil end }
package.loaded["modules.duel_api_rate_limit"] = { register_rpc = function(fn, name) assert(not rpcs[name]); rpcs[name] = fn end }
local Gender = assert(loadfile("Server/nakama/modules/duel_account_gender.lua"))()
local get = rpcs.duel_account_gender_get
local choose = rpcs.duel_account_gender_register
local function account(uid, email) accounts[uid] = { email = email or "", user = { metadata = {} } } end
local function fresh(uid) account(uid); after.AuthenticateDevice({user_id=uid}, {created=true}, {}) end
local passed = 0
local function test(name, fn) db={};accounts={};versions=0;fault=false;race=nil;fn();passed=passed+1;print("PASS " .. name) end

test("legacy account missing identity is permanently male", function()
  account("old", "old@example.test")
  local r = get({user_id="old"}, {gender="female"})
  assert(r.ok and r.gender == "male" and r.can_choose == false)
  local r2 = choose({user_id="old"}, {gender="female"})
  assert(not r2.ok and r2.err == "account_gender_immutable" and db.old.value.gender == "male")
end)
test("legacy guest cannot opt into the new registration choice", function()
  account("old")
  local r = choose({user_id="old"}, {gender="female"})
  assert(not r.ok and r.gender == "male" and db.old.value.finalized)
end)
test("new device choice finalizes on successful linking and retries idempotently", function()
  fresh("new")
  assert(get({user_id="new"}, {}).can_choose)
  local r = choose({user_id="new"}, {gender="female"})
  assert(r.ok and r.gender == "female" and r.can_choose == true)
  accounts.new.email = "new@example.test"; after.LinkEmail({user_id="new"})
  local version = db.new.version
  assert(choose({user_id="new"}, {gender="female"}).ok and db.new.version == version)
  assert(not choose({user_id="new"}, {gender="male"}).ok and db.new.version == version)
  after.AuthenticateDevice({user_id="new"}, {created=true}, {})
  assert(db.new.value.gender == "female" and db.new.value.finalized)
end)
test("gender choice ignores forged target user IDs", function()
  fresh("self"); fresh("other")
  assert(choose({user_id="self"}, {gender="female",user_id="other",account_id="other"}).ok)
  assert(db.self.value.gender == "female" and not db.other.value.finalized)
end)
test("untrusted metadata and authentication vars cannot change an existing identity", function()
  fresh("self"); assert(choose({user_id="self"}, {gender="male"}).ok)
  accounts.self.email = "self@example.test"; after.LinkEmail({user_id="self"})
  accounts.self.user.metadata.gender = "female"
  Gender.after_email({user_id="self"}, {created=false}, {account={vars={gender="female"}}})
  assert(get({user_id="self"}, {gender="female"}).gender == "male")
end)
test("direct newly created email account accepts gender only during creation", function()
  account("new", "new@example.test")
  Gender.after_email({user_id="new"}, {created=true}, {account={vars={gender="female"}}})
  assert(get({user_id="new"}, {}).gender == "female")
  account("existing", "old@example.test")
  Gender.after_email({user_id="existing"}, {created=false}, {account={vars={gender="female"}}})
  assert(get({user_id="existing"}, {}).gender == "male")
end)
test("old-client email linking finalizes pending male permanently", function()
  fresh("new"); accounts.new.email = "new@example.test"
  after.LinkEmail({user_id="new"})
  assert(db.new.value.gender == "male" and db.new.value.finalized)
  assert(not choose({user_id="new"}, {gender="female"}).ok)
end)
test("choice made before linking remains unchanged after successful link", function()
  fresh("new"); assert(choose({user_id="new"}, {gender="female"}).ok)
  accounts.new.email="new@example.test"; after.LinkEmail({user_id="new"})
  assert(db.new.value.gender == "female" and db.new.value.finalized)
end)
test("failed registration can revise its pending choice but successful registration cannot", function()
  fresh("new")
  assert(choose({user_id="new"}, {gender="female"}).can_choose)
  -- Email is still absent after an invalid email/password or network failure.
  assert(choose({user_id="new"}, {gender="male"}).can_choose)
  assert(db.new.value.gender == "male" and not db.new.value.finalized)
  accounts.new.email = "new@example.test"; after.LinkEmail({user_id="new"})
  assert(not choose({user_id="new"}, {gender="female"}).ok and db.new.value.gender == "male")
end)
test("invalid and unauthenticated requests never persist gender", function()
  fresh("new"); local version = db.new.version
  for _,gender in ipairs({"Female", "", "other", 1, true, {}}) do
    assert(not choose({user_id="new"}, {gender=gender}).ok)
  end
  assert(not choose(nil, {gender="female",user_id="new"}).ok)
  assert(not get({}, {}).ok and not choose({user_id=12}, {gender="male"}).ok)
  assert(not choose({user_id="new"}, "bad json").ok)
  assert(not choose({user_id="new"}, string.rep("x",257)).ok)
  assert(db.new.version == version and not db.new.value.finalized)
end)
test("invalid auth gender rejected before account creation", function()
  local payload={account={vars={gender="female"}}}
  assert(before.AuthenticateEmail({},payload) == payload)
  assert(not pcall(before.AuthenticateEmail, {}, {account={vars={gender="other"}}}))
end)
test("concurrent choices use CAS and cannot replace the first committed value", function()
  fresh("new")
  race = function(w) versions=versions+1;db[w.user_id]={version=tostring(versions),value={version=1,gender="male",finalized=true}} end
  local r = choose({user_id="new"}, {gender="female"})
  assert(not r.ok and r.err == "account_gender_immutable" and db.new.value.gender == "male")
end)
test("storage failure fails closed without guessing or unlocking identity", function()
  fresh("new"); fault=true
  assert(not get({user_id="new"}, {}).ok and not choose({user_id="new"}, {gender="female"}).ok)
  fault=false; assert(not db.new.value.finalized)
  db.new.value.gender="corrupted"
  assert(not get({user_id="new"}, {}).ok and db.new.value.gender == "corrupted")
end)
test("real client-storage guard forbids initial creation replacement and deletion", function()
  assert(loadfile("Server/nakama/modules/duel_storage_guard.lua"))()
  assert(before.WriteStorageObjects({}, {objects={{collection="duel_account_identity",key="gender_v1",value={gender="female"}}}}) == nil)
  assert(before.DeleteStorageObjects({}, {object_ids={{collection="duel_account_identity",key="gender_v1"}}}) == nil)
end)
test("gender storage failure does not bypass existing email session replacement", function()
  account("self", "self@example.test")
  accounts.self.user.metadata.session_epoch = "3"
  local notifications = 0
  nk.account_update_id = function(uid, metadata) accounts[uid].user.metadata = metadata end
  nk.notification_send = function(uid, subject, data, code) assert(uid == "self" and code == 10001 and data.session_epoch == 4); notifications=notifications+1 end
  package.loaded["modules.duel_account_gender"] = { after_email = function() error("storage failure") end }
  assert(loadfile("Server/nakama/modules/duel_session.lua"))()
  after.AuthenticateEmail({user_id="self"}, {created=false}, {})
  assert(accounts.self.user.metadata.session_epoch == "4" and notifications == 1)
end)
print("Account gender security: " .. passed .. " passed")
