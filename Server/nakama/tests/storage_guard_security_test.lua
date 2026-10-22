-- Run from the repository root: lua Server/nakama/tests/storage_guard_security_test.lua
package.path = "Server/nakama/?.lua;" .. package.path
local hooks = {}
package.preload.nakama = function()
  return { register_req_before = function(fn, name) assert(not hooks[name]); hooks[name] = fn end }
end
require("modules.duel_storage_guard")
local protected = {
  "duel_match3_stats", "duel_match3_progress", "duel_match3_character",
  "duel_match3_cheat_whitelist", "duel_match3_item_defs", "duel_match3_achievement_defs",
  "duel_match3_server_aura", "duel_match3_contest_goals", "duel_match3_bot_defs",
  "duel_api_rate_limits",
  "duel_friends_race", "arena_tournament", "arena_user_tid", "arena_bot_cooldowns",
  "duel_future_server_state", "arena_future_server_state",
}
local count = 0
for _, collection in ipairs(protected) do
  for _, version in ipairs({ "", "*", "known-version" }) do
    local obj = { collection = collection, key = "profile", value = '{"gold":999999}', version = version, permission_write = 1 }
    assert(hooks.WriteStorageObjects({ user_id = "player" }, { objects = { obj } }) == nil)
    assert(hooks.DeleteStorageObjects({ user_id = "player" }, { object_ids = { obj } }) == nil)
    count = count + 2
  end
end
-- Entire mixed batch is rejected, not partially passed to the engine.
assert(hooks.WriteStorageObjects({}, { objects = {
  { collection = "client_settings", key = "audio" },
  { collection = "duel_match3_progress", key = "profile" },
} }) == nil)
assert(hooks.DeleteStorageObjects({}, { object_ids = {
  { collection = "client_settings", key = "audio" },
  { collection = "arena_user_tid", key = "tid" },
} }) == nil)
for _, entry in ipairs({ { "WriteStorageObjects", "objects" }, { "DeleteStorageObjects", "object_ids" } }) do
  local hook, field = hooks[entry[1]], entry[2]
  local allowed = { [field] = { { collection = "client_settings", key = "audio" } } }
  assert(hook({}, allowed) == allowed)
  assert(hook({}, nil) == nil and hook({}, {}) == nil)
  assert(hook({}, { [field] = { { key = "missing_collection" } } }) == nil)
end
print("storage guard security: " .. count .. " protected create/update/delete cases and mixed/malformed/allowed batches passed")
