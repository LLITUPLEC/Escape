-- lua Server/nakama/tests/api_rate_limit_security_test.lua (Lua 5.1)
package.path = "Server/nakama/?.lua;" .. package.path
local now, store, writes, reads, revision = 100000, {}, 0, 0, 0
local fail_read, fail_write, competing_write
local hooks, rpc = {}, {}
local function clone(value)
  if type(value) ~= "table" then return value end
  local copy = {}
  for k, v in pairs(value) do copy[k] = clone(v) end
  return copy
end
local nk = {
  time = function() return now end,
  storage_read = function(ids)
    reads = reads + 1
    if fail_read then error("database unavailable") end
    local id = ids[1]
    assert(id.collection == "duel_api_rate_limits" and id.key == "v1")
    local row = store[id.user_id]
    return row and { clone(row) } or {}
  end,
  storage_write = function(objects)
    writes = writes + 1
    if fail_write then error("database unavailable") end
    local obj = objects[1]
    assert(#objects == 1 and obj.permission_read == 0 and obj.permission_write == 0)
    if competing_write then
      local f = competing_write
      competing_write = nil
      f()
    end
    local row = store[obj.user_id]
    if (row and obj.version ~= row.version) or (not row and obj.version ~= "*") then
      error("Storage write rejected - version check failed")
    end
    revision = revision + 1
    store[obj.user_id] = { value = clone(obj.value), version = tostring(revision) }
    return { { version = tostring(revision) } }
  end,
  register_rpc = function(fn, name) assert(not rpc[name]); rpc[name] = fn end,
  register_rt_before = function(fn, name) assert(not hooks[name]); hooks[name] = fn end,
}
package.preload.nakama = function() return nk end
local limit = require("modules.duel_api_rate_limit")
local function denied(fn, expected_code)
  local ok, err = pcall(fn)
  assert(not ok and type(err) == "table", "Expected a typed runtime error")
  assert(err[2] == (expected_code or 8), "Wrong gRPC error code")
  if expected_code == nil then assert(err[1]:find("retry_after_seconds=", 1, true)) end
  return err
end

-- Shared budgets across endpoints and fresh runtimes: no per-VM allowance.
local user = { user_id = "alice" }
for group, capacity in pairs({ read = 60, mutate = 20, create = 4, session = 4 }) do
  for _ = 1, capacity do limit.consume(user, group) end
  for _ = 1, 20 do denied(function() limit.consume(user, group) end) end
  local other_vm = assert(loadfile("Server/nakama/modules/duel_api_rate_limit.lua"))()
  denied(function() other_vm.consume(user, group) end)
end
assert(store.alice.value.read.tokens == 0 and store.alice.value.mutate.tokens == 0)
local row_count = 0
for _ in pairs(store) do row_count = row_count + 1 end
assert(row_count == 1, "One account must use exactly one fixed-size limiter object")

-- Refill, user isolation, and everyday polling/UI burst allowance.
limit.consume({ user_id = "bob" }, "create")
now = now + 10000
limit.consume(user, "create")
denied(function() limit.consume(user, "create") end)
local poller = { user_id = "poller" }
for _ = 1, 60 do limit.consume(poller, "read") end -- initial load burst
for _ = 1, 60 do
  now = now + 5000
  for _ = 1, 15 do limit.consume(poller, "read") end -- many panels every 5 sec
end

-- Rolling back the wall clock never adds tokens or resets the allowance.
local rollback = { user_id = "rollback" }
for _ = 1, 4 do limit.consume(rollback, "create") end
now = now - 1000
denied(function() limit.consume(rollback, "create") end)
local partial_rollback = { user_id = "partial_rollback" }
limit.consume(partial_rollback, "create")
now = now - 5000
limit.consume(partial_rollback, "create")
now = now + 5000
limit.consume(partial_rollback, "create")
limit.consume(partial_rollback, "create")
denied(function() limit.consume(partial_rollback, "create") end)
now = now + 1000
denied(function() limit.consume(rollback, "create") end)

-- Deterministic concurrent interleaving: outer call loses its CAS and retries
-- using the winner's new balance, rather than overwriting the consumed token.
local concurrent = { user_id = "concurrent" }
for _ = 1, 3 do limit.consume(concurrent, "create") end
competing_write = function() limit.consume(concurrent, "create") end
denied(function() limit.consume(concurrent, "create") end)
assert(store.concurrent.value.create.tokens == 0)
local first_create = { user_id = "first_create" }
competing_write = function() limit.consume(first_create, "create") end
limit.consume(first_create, "create")
assert(store.first_create.value.create.tokens == 2, "Concurrent first creation must spend both tokens")

-- Storage errors and repeated CAS failures are bounded and fail closed.
fail_read = true
denied(function() limit.consume(user, "read") end)
fail_read = false
fail_write = true
local before = writes
denied(function() limit.consume({ user_id = "no_write" }, "read") end)
assert(writes - before == 4)
fail_write = false
before = reads
denied(function() limit.consume({}, "read") end, 16)
assert(reads == before, "Unauthenticated calls must never create rate state")

-- Limit outside the handler: no side effect after rejection; all names in a
-- category share one budget and future unknown RPCs default to mutation policy.
local handled = 0
limit.register_rpc(function(ctx, payload) handled = handled + 1; return payload end, "duel_character_item_sell")
for _ = 1, 20 do assert(rpc.duel_character_item_sell({ user_id = "wrapper" }, "unchanged") == "unchanged") end
denied(function() rpc.duel_character_item_sell({ user_id = "wrapper" }, "untrusted") end)
assert(handled == 20)
assert(limit.policy_for_rpc("duel_match3_race_create") == "create")
assert(limit.policy_for_rpc("duel_match3_pve_create") == "create")
assert(limit.policy_for_rpc("future_new_rpc") == "mutate")

require("modules.duel_api_rate_guard")
denied(function() hooks.MatchCreate(user, {}) end, 7)
local socket_user, envelope = { user_id = "socket-user" }, { cid = "ticket" }
for _ = 1, 4 do assert(hooks.MatchmakerAdd(socket_user, envelope) == envelope) end
denied(function() hooks.PartyMatchmakerAdd(socket_user, envelope) end)
for _ = 1, 20 do assert(hooks.MatchJoin(socket_user, envelope) == envelope) end
denied(function() hooks.ChannelMessageSend(socket_user, envelope) end)

-- Authoritative input filtering is per match/user, independent of socket,
-- never touches storage, and rejects before processing large payloads.
local state = { presences = { alice = {}, bob = {} } }
local messages = {}
for _ = 1, 100 do
  for _, uid in ipairs({ "alice", "bob" }) do
    messages[#messages + 1] = { sender = { user_id = uid }, data = "{}" }
  end
end
local notifications = 0
before = writes
local function notice(p, reason) assert(reason == "rate_limited"); notifications = notifications + 1 end
assert(#limit.filter_match_messages(state, messages, now, notice) == 60)
assert(notifications == 2 and writes == before)
assert(#limit.filter_match_messages(state, messages, now, notice) == 0)
assert(notifications == 2)
now = now + 1000
assert(#limit.filter_match_messages(state, messages, now, notice) == 40)
assert(notifications == 4)
assert(#limit.filter_match_messages(state, { { sender = { user_id = "outsider" }, data = "{}" } }, now, notice) == 0)
assert(state.api_input_buckets.outsider == nil)
assert(#limit.filter_match_messages({ presences = { alice = {} } }, {
  { sender = { user_id = "alice" }, data = string.rep("x", 4097) },
}, now, notice) == 0)

-- All externally registered game RPCs must use the common wrapper.
for _, filename in ipairs({ "duel_match3", "duel_session", "duel_online", "duel_friends", "duel_leaderboard", "duel_account_wipe" }) do
  local file = assert(io.open("Server/nakama/modules/" .. filename .. ".lua", "r"))
  local source = file:read("*a"); file:close()
  assert(not source:find("nk.register_rpc(", 1, true), "Unprotected RPC in " .. filename)
  assert(source:find("RateLimit.register_rpc(", 1, true))
end
print("API rate limit security: shared budgets, flood/refill, 5-second polling, CAS interleavings, fail-closed storage, RPC/RT guards and match input filtering passed")
