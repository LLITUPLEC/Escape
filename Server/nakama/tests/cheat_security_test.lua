-- Exercise the actual allowlist implementation without loading the match runtime.
local file = assert(io.open("Server/nakama/modules/duel_match3.lua", "r"))
local source = file:read("*a")
file:close()
local block = assert(source:match("(local function read_cheat_whitelist_emails_for_user_id.-)function normalize_mine_unlocked"))
local system = "00000000-0000-0000-0000-000000000000"
local player = "11111111-1111-1111-1111-111111111111"
local reads, data, storage_error = {}, {}, false
local env = {
  CFG = { CHEAT_WHITELIST_COLLECTION = "duel_match3_cheat_whitelist", CHEAT_WHITELIST_KEY = "emails",
    CHEAT_WHITELIST_USER_ID = system, DEFAULT_CHEAT_EMAILS = {"attacker@example.com"} },
  nk = { storage_read = function(ids)
    if storage_error then error("offline") end
    reads[#reads + 1] = ids[1].user_id
    if data[ids[1].user_id] then return {{value = data[ids[1].user_id]}} end
    return {}
  end },
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
data[system] = {emails = {"attacker@example.com"}}
assert(not allowed(player), "legacy email allowlist must not grant privileges")
data[system] = {user_ids = {player}}
assert(allowed(player), "explicit system-owned user ID should work")
assert(not allowed("22222222-2222-2222-2222-222222222222"), "other user must remain denied")
storage_error = true
assert(not allowed(player), "storage failure must fail closed")
env.CFG.CHEAT_WHITELIST_USER_ID = player
assert(not allowed(player), "non-system configuration must fail closed")
print("cheat_security_test: 8 checks passed")
