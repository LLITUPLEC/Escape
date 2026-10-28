local nk = require("nakama")
local M = {}

-- A single server-only object per account, shared by every Lua VM/node/session.
-- Versioned writes prevent parallel calls from spending the same token twice.
-- The duel_ prefix is reserved by duel_storage_guard, including first creation.
local COLLECTION = "duel_api_rate_limits"
local KEY = "v1"
local POLICIES = {
  read = { capacity = 60, per_second = 4 },
  mutate = { capacity = 20, per_second = 2 },
  create = { capacity = 4, per_second = 0.1 },
  session = { capacity = 4, per_second = 0.2 },
}
local CREATE = {
  duel_match3_pve_create = true, duel_match3_race_create = true,
  duel_match3_race_enter = true, duel_friends_race_respond = true,
  duel_arena_queue_join = true,
  duel_global_boss_enter = true,
}
local READ = {
  duel_match3_stats_get = true, duel_match3_stats_record = true,
  duel_match3_achievement_sync = true, duel_match3_achievement_catalog_get = true,
  duel_match3_pve_catalog_get = true, duel_match3_race_info = true,
  duel_character_get = true, duel_player_resources_get = true,
  duel_nickname_status_get = true, duel_match3_item_catalog_get = true,
  duel_match3_server_aura_get = true, duel_arena_queue_poll = true,
  duel_leaderboard_get = true, duel_online_ping_and_count = true,
  duel_online_list = true, duel_session_epoch_get = true,
  duel_friends_resolve_username = true,
  duel_account_gender_get = true,
  duel_player_cosmetics_get = true,
  duel_public_appearance_get = true,
  duel_daily_get = true,
  duel_consumables_get = true,
  duel_player_guidance_get = true,
  duel_global_boss_get = true, duel_mail_get = true, duel_shop_get = true,
  duel_global_boss_leaderboard_get = true,
}

local function reject(retry)
  -- Nakama's Lua custom error format is positional: { message, gRPC code }.
  -- HTTP maps ResourceExhausted (8) to 429; socket RPC carries the same message.
  error({ "rate_limited: retry_after_seconds=" .. tostring(math.max(1, math.ceil(retry or 1))), 8 })
end

local function next_bucket(bucket, policy, now)
  local tokens, updated = policy.capacity, now
  if type(bucket) == "table" then
    tokens = math.max(0, math.min(policy.capacity, tonumber(bucket.tokens) or 0))
    updated = tonumber(bucket.updated_ms) or now
    tokens = math.min(policy.capacity, tokens + math.max(0, now - updated) * policy.per_second / 1000)
  end
  if tokens < 1 then return nil, (1 - tokens) / policy.per_second end
  return { tokens = tokens - 1, updated_ms = math.max(now, updated) }
end

function M.consume(ctx, group)
  local user_id = ctx and ctx.user_id
  if type(user_id) ~= "string" or user_id == "" then
    error({ "authentication_required", 16 })
  end
  local policy = assert(POLICIES[group], "Unknown rate limit policy")
  for _ = 1, 4 do
    local ok, rows = pcall(nk.storage_read, { { collection = COLLECTION, key = KEY, user_id = user_id } })
    if not ok or type(rows) ~= "table" then reject(1) end
    local row = rows[1]
    local value = row and row.value or {}
    if type(value) ~= "table" then reject(1) end
    local bucket, retry = next_bucket(value[group], policy, nk.time())
    if bucket == nil then reject(retry) end
    -- Only known, fixed-size groups are persisted. Payload fields never enter it.
    local updated = {}
    for name in pairs(POLICIES) do
      if type(value[name]) == "table" then updated[name] = value[name] end
    end
    updated[group] = bucket
    local written = pcall(nk.storage_write, { {
      collection = COLLECTION, key = KEY, user_id = user_id, value = updated,
      version = row and row.version or "*", permission_read = 0, permission_write = 0,
    } })
    if written then return end
    -- Re-read after a CAS conflict. Persistent storage failures fail closed too.
  end
  reject(1)
end

function M.policy_for_rpc(name)
  if CREATE[name] then return "create" end
  if name == "duel_session_claim" or name == "duel_account_wipe" then return "session" end
  if READ[name] then return "read" end
  return "mutate" -- New RPCs are limited even before they are classified.
end

function M.register_rpc(fn, name)
  local group = M.policy_for_rpc(name)
  nk.register_rpc(function(ctx, payload)
    M.consume(ctx, group)
    if M.maintenance then M.maintenance(ctx) end
    -- The limiter is outside handlers' pcall blocks: a limit stays a real 429.
    return fn(ctx, payload)
  end, name)
end

function M.configure_maintenance(fn)
  assert(fn == nil or type(fn) == "function", "Invalid maintenance hook")
  M.maintenance = fn
end

-- Training has no durable admission or result. Its transport budget is also
-- in memory: normal RPC maintenance could otherwise settle real pending loot.
local transient_pools={}
function M.consume_transient(ctx,group)
    local policy=assert(POLICIES[group],"Unknown transient rate limit policy")
    local uid=ctx and ctx.user_id
    if type(uid)~="string"or uid==""then error({"authentication_required",16})end
    local now=nk.time()
    local pool=transient_pools[group]
    if not pool then pool={buckets={},count=0};transient_pools[group]=pool end
    local buckets=pool.buckets
    -- Bound memory even across many accounts; inactive IDs expire after two
    -- minutes, and a full pool evicts its oldest entry before admitting a new ID.
    for key,b in pairs(buckets)do
      if now-b.updated_ms>120000 then buckets[key]=nil;pool.count=pool.count-1 end
    end
    if not buckets[uid]and pool.count>=1024 then
      local oldest,oldest_time
      for key,b in pairs(buckets)do if not oldest_time or b.updated_ms<oldest_time then oldest,oldest_time=key,b.updated_ms end end
      buckets[oldest]=nil;pool.count=pool.count-1
    end
    local bucket,retry=next_bucket(buckets[uid],policy,now)
    if not bucket then reject(retry)end
    if not buckets[uid]then pool.count=pool.count+1 end;buckets[uid]=bucket
end
function M.register_transient_rpc(fn,name)
  nk.register_rpc(function(ctx,payload)
    M.consume_transient(ctx,"mutate")
    return fn(ctx,payload)
  end,name)
end

-- A match has one serialized Lua state, shared by all its incoming sockets.
-- Filter before JSON decoding, board simulation, snapshots, or storage work;
-- never perform a database operation for individual board-selection messages.
function M.filter_match_messages(state, messages, now, on_reject)
  state.api_input_buckets = state.api_input_buckets or {}
  local accepted = {}
  local policy = { capacity = 30, per_second = 20 }
  for _, message in ipairs(messages or {}) do
    local user_id = message.sender and message.sender.user_id
    if user_id and state.presences and state.presences[user_id] then
      local previous = state.api_input_buckets[user_id]
      local bucket = next_bucket(previous, policy, now)
      if bucket and (type(message.data) ~= "string" or #message.data <= 4096) then
        bucket.notify_after_ms = previous and previous.notify_after_ms or 0
        state.api_input_buckets[user_id] = bucket
        accepted[#accepted + 1] = message
      elseif on_reject and (not previous or now >= (previous.notify_after_ms or 0)) then
        -- At most one rejection per user/second: don't amplify a flood.
        previous = previous or { tokens = 0, updated_ms = now }
        previous.notify_after_ms = now + 1000
        state.api_input_buckets[user_id] = previous
        on_reject(message.sender, "rate_limited")
      end
    end
  end
  return accepted
end

return M
