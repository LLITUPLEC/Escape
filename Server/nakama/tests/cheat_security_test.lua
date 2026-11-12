-- Exercise the actual allowlist implementation without loading the match runtime.
local file = assert(io.open("Server/nakama/modules/duel_match3.lua", "r"))
local source = file:read("*a")
file:close()
local block = assert(source:match("(local function cheat_whitelist_storage_id.-)function normalize_mine_unlocked"))
local system = "00000000-0000-0000-0000-000000000000"
local player = "11111111-1111-1111-1111-111111111111"
local other = "22222222-2222-2222-2222-222222222222"
local reads, data, accounts, storage_error, writes = {}, {}, {}, false, 0
local env = {
  CFG = { CHEAT_WHITELIST_COLLECTION = "duel_match3_cheat_whitelist", CHEAT_WHITELIST_KEY = "emails",
    CHEAT_WHITELIST_USER_ID = system },
  Runtime = {},
  nk = { storage_read = function(ids)
    if storage_error then error("offline") end
    reads[#reads + 1] = ids[1].user_id
    if data[ids[1].user_id] then return {{value = data[ids[1].user_id]}} end
    return {}
  end, storage_write = function(rows)
    local row = rows[1]
    assert(row.user_id == system and row.version == "*" and row.permission_read == 0 and row.permission_write == 0)
    assert(not data[system], "bootstrap must be create-only")
    writes = writes + 1
    data[system] = row.value
  end, account_get_id = function(uid) return assert(accounts[uid], "missing account") end,
    logger_warn = function(message) error(message) end },
  decode_storage_value = function(row) return row.value end,
}
setmetatable(env, {__index = _G})
local chunk = assert(loadstring(block .. "\nreturn build_cheat_whitelist_set, is_user_allowed_for_cheat_rows"))
setfenv(chunk, env)
local build, allowed = chunk()
assert(not allowed(player), "missing allowlist must deny")
data[player] = {user_ids = {player}, emails = {"attacker@example.com"}}
reads = {}
assert(not allowed(player, build({player})), "player-owned allowlist must not grant privileges")
assert(#reads == 1 and reads[1] == system, "only system-owned storage may be read")
env.Runtime.seed_cheat_whitelist(" TESTER@example.com, tester@example.com, invalid,second@example.com")
assert(writes == 1 and #data[system].emails == 2 and data[system].emails[1] == "tester@example.com"
  and data[system].emails[2] == "second@example.com", "missing list must receive the normalized private defaults")
env.Runtime.seed_cheat_whitelist()
assert(writes == 1, "existing list must be preserved")
accounts[player] = { email = "Player@Example.com" }
accounts[other] = { user = { metadata = { email = "player@example.com" } } }
data[system] = {emails = {"  PLAYER@example.com  "}}
assert(allowed(player), "case-insensitive system-owned email should work")
assert(not allowed(other), "user metadata is not a linked address")
accounts[player].disable_time = 1
assert(not allowed(player), "disabled account must not gain email access")
accounts[player] = nil
assert(not allowed(player), "unavailable account must not gain email access")
data[system] = { emails = {} }
env.Runtime.seed_cheat_whitelist()
assert(writes == 1 and #data[system].emails == 0, "empty administrator list must be preserved")
data[system] = nil
env.Runtime.seed_cheat_whitelist()
assert(writes == 2 and #data[system].emails == 0, "a fresh deployment without private defaults must deny by default")
data[system] = {user_ids = {player}}
assert(allowed(player), "explicit system-owned user ID should work")
assert(not allowed(other), "other user must remain denied")
storage_error = true
assert(not allowed(player), "storage failure must fail closed")
env.CFG.CHEAT_WHITELIST_USER_ID = player
assert(not allowed(player), "non-system configuration must fail closed")
print("cheat_security_test: server-owned email, UUID and seed checks passed")
