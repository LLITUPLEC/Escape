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

local PVE_PROGRESS_COLLECTION = "duel_match3_progress"
local PVE_PROGRESS_KEY = "profile"
local PVE_MAX_LEVEL = 12

local function read_username(user_id)
  if user_id == nil or user_id == "" then
    return "Survivor"
  end
  local ok, acc = pcall(function()
    return nk.account_get_id(user_id)
  end)
  if ok and acc ~= nil and acc.user ~= nil and acc.user.username ~= nil and acc.user.username ~= "" then
    return tostring(acc.user.username)
  end
  return "Survivor"
end

local function read_player_level(user_id)
  if user_id == nil or user_id == "" then
    return 1
  end
  local ok, rows = pcall(function()
    return nk.storage_read({
      {
        collection = PVE_PROGRESS_COLLECTION,
        key = PVE_PROGRESS_KEY,
        user_id = user_id,
      },
    })
  end)
  if not ok or rows == nil or #rows == 0 then
    return 1
  end
  local row = rows[1]
  local val = row.value
  if type(val) == "string" then
    local dec_ok, decoded = pcall(nk.json_decode, val)
    if dec_ok then
      val = decoded
    else
      val = nil
    end
  end
  if type(val) ~= "table" then
    return 1
  end
  local level = math.floor(tonumber(val.level) or 1)
  if level < 1 then level = 1 end
  if level > PVE_MAX_LEVEL then level = PVE_MAX_LEVEL end
  return level
end

-- Список онлайн: total + до 50 игроков (username + level) + все online_ids (для статуса друзей).
local function duel_online_list(ctx, payload)
  local ok, result = pcall(function()
    local caller_id = ctx and ctx.user_id or ""
    if caller_id == nil or caller_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local limit = 50
    if payload ~= nil and payload ~= "" then
      local parsed_ok, parsed = pcall(nk.json_decode, payload)
      if parsed_ok and type(parsed) == "table" and parsed.limit ~= nil then
        local n = tonumber(parsed.limit)
        if n ~= nil then
          limit = math.max(1, math.min(50, math.floor(n)))
        end
      end
    end

    local ts = now_unix()
    cleanup_expired(ts)

    local ids = {}
    for user_id, _ in pairs(online_expire_at) do
      ids[#ids + 1] = user_id
    end
    table.sort(ids)

    local total = #ids
    local batch = {}
    for i = 1, #ids do
      local uid = ids[i]
      if uid ~= caller_id then
        batch[#batch + 1] = uid
        if #batch >= limit then
          break
        end
      end
    end

    local players = {}
    for _, uid in ipairs(batch) do
      players[#players + 1] = {
        user_id = uid,
        username = read_username(uid),
        level = read_player_level(uid),
      }
    end

    return nk.json_encode({
      ok = true,
      total = total,
      shown = #players,
      players = players,
      online_ids = ids,
    })
  end)

  if not ok then
    nk.logger_error("duel_online_list: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

nk.register_rpc(duel_online_ping_and_count, "duel_online_ping_and_count")
nk.register_rpc(duel_online_leave, "duel_online_leave")
nk.register_rpc(duel_online_list, "duel_online_list")
