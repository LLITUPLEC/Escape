local nk = require("nakama")

local SESSION_EPOCH_META_KEY = "session_epoch"
local NOTIFY_CODE_SESSION_REPLACED = 10001

local function read_session_epoch_from_account(nk_module, user_id)
  if user_id == nil or user_id == "" then
    return 0
  end
  local ok, account = pcall(function()
    return nk_module.account_get_id(user_id)
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

local function merge_metadata_and_set_epoch(nk_module, user_id, new_epoch)
  local account = nk_module.account_get_id(user_id)
  local md = {}
  local u = account.user
  if u ~= nil and u.metadata ~= nil then
    for k, v in pairs(u.metadata) do
      md[k] = v
    end
  end
  md[SESSION_EPOCH_META_KEY] = tostring(new_epoch)
  nk_module.account_update_id(user_id, md, nil, nil, nil, nil, nil, nil)
end

--- После каждого входа по e-mail инкрементируем эпоху и шлём in-app уведомление всем сокетам этого пользователя.
local function after_authenticate_email(ctx, logger, nkrt, session, request)
  local user_id = ctx.user_id
  if user_id == nil or user_id == "" then
    return
  end

  local ok, err = pcall(function()
    local epoch = read_session_epoch_from_account(nkrt, user_id) + 1
    merge_metadata_and_set_epoch(nkrt, user_id, epoch)
    nkrt.notification_send(
      user_id,
      "session_replaced",
      { session_epoch = epoch },
      NOTIFY_CODE_SESSION_REPLACED,
      "",
      false
    )
  end)

  if not ok then
    nk.logger_error("duel_session after_authenticate_email: " .. tostring(err))
  end
end

nk.register_req_after(after_authenticate_email, "AuthenticateEmail")

local function duel_session_epoch_get(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end
    local epoch = read_session_epoch_from_account(nk, user_id)
    return nk.json_encode({ ok = true, session_epoch = epoch })
  end)

  if not ok then
    nk.logger_error("duel_session_epoch_get: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

nk.register_rpc(duel_session_epoch_get, "duel_session_epoch_get")
