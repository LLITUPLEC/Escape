local nk = require("nakama")

-- In-memory online map for this Nakama node.
-- Key: user_id, Value: unix timestamp when presence expires.
local online_expire_at = {}
local PRESENCE_TTL_SEC = 20

local SESSION_EPOCH_META_KEY = "session_epoch"

local function read_session_epoch(user_id)
  if user_id == nil or user_id == "" then
    return 0
  end
  local ok, account = pcall(function()
    return nk.account_get_id(user_id)
  end)
  if not ok or account == nil or account.user == nil or account.user.metadata == nil then
    return 0
  end
  local v = account.user.metadata[SESSION_EPOCH_META_KEY]
  if v == nil then
    return 0
  end
  return tonumber(v) or 0
end

local function now_unix()
  return os.time()
end

local function cleanup_expired(now_ts)
  for user_id, expire_at in pairs(online_expire_at) do
    if expire_at == nil or expire_at <= now_ts then
      online_expire_at[user_id] = nil
    end
  end
end

local function duel_online_ping_and_count(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local ts = now_unix()
    online_expire_at[user_id] = ts + PRESENCE_TTL_SEC
    cleanup_expired(ts)

    local count = 0
    for _, _ in pairs(online_expire_at) do
      count = count + 1
    end

    local epoch = read_session_epoch(user_id)
    return nk.json_encode({ ok = true, count = count, session_epoch = epoch })
  end)

  if not ok then
    nk.logger_error("duel_online_ping_and_count: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

local function duel_online_leave(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end
    online_expire_at[user_id] = nil
    return nk.json_encode({ ok = true })
  end)

  if not ok then
    nk.logger_error("duel_online_leave: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

nk.register_rpc(duel_online_ping_and_count, "duel_online_ping_and_count")
nk.register_rpc(duel_online_leave, "duel_online_leave")
