--- Общие проверки session_epoch (метаданные аккаунта, ключ session_epoch из duel_session.lua).
local SESSION_EPOCH_META_KEY = "session_epoch"

local M = {}

function M.read_metadata_epoch(nk_module, user_id)
  if nk_module == nil or user_id == nil or user_id == "" then
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

function M.parse_client_epoch_from_payload(payload)
  if payload == nil or payload == "" then
    return nil
  end
  local ok, p = pcall(nk.json_decode, payload)
  if not ok or type(p) ~= "table" then
    return nil
  end
  if p.session_epoch == nil then
    return nil
  end
  return tonumber(p.session_epoch)
end

--- Мутации: клиент обязан передать session_epoch; значение должно совпадать с метаданными на сервере.
function M.assert_client_epoch_matches(nk_module, user_id, payload)
  local server_e = M.read_metadata_epoch(nk_module, user_id)
  local client_e = M.parse_client_epoch_from_payload(payload)
  if client_e == nil then
    return false, "session_epoch_required"
  end
  if client_e ~= server_e then
    return false, "session_stale"
  end
  return true, nil
end

--- PVE: эпоха выросла после старта матча — старая клайентская сессия не должна получать награды / ходы.
function M.is_epoch_stale_for_match(nk_module, user_id, match_snapshot_epoch)
  local snap = tonumber(match_snapshot_epoch) or 0
  local cur = M.read_metadata_epoch(nk_module, user_id)
  return cur > snap
end

return M
