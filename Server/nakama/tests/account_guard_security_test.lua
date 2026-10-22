local hooks = {}
package.loaded["nakama"] = {register_req_before = function(fn, name) hooks[name] = fn end}
assert(loadfile("Server/nakama/modules/duel_account_guard.lua"))()
local before = assert(hooks.UpdateAccount)
assert(before({}, {username = "free_rename"}) == nil)
assert(before({}, {username = {value = "free_rename"}}) == nil)
local profile = {display_name = "Display name", avatar_url = "https://example.com/avatar"}
assert(before({}, profile) == profile)
assert(before({}, nil) == nil)
print("account_guard_security_test: 4 checks passed")
