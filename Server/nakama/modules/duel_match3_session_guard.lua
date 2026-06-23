-- Проверка session_epoch (вынесено из duel_match3.lua — лимит local variables).
return function(deps)
  local nk = deps.nk
  local CFG = deps.CFG

  local G = {}

  function G.read_metadata_epoch(user_id)
    if user_id == nil or user_id == "" then
      return 0
    end
    local ok, account = pcall(function()
      return nk.account_get_id(user_id)
    end)
    if not ok or account == nil or account.user == nil or account.user.metadata == nil then
      return 0
    end
    local v = account.user.metadata[CFG.SESSION_EPOCH_ACCOUNT_META]
    if v == nil then
      return 0
    end
    return tonumber(v) or 0
  end

  local function parse_client_epoch_from_payload(payload)
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

  function G.assert_client_epoch_matches(user_id, payload)
    local server_e = G.read_metadata_epoch(user_id)
    local client_e = parse_client_epoch_from_payload(payload)
    if client_e == nil then
      return false, "session_epoch_required"
    end
    if client_e ~= server_e then
      return false, "session_stale"
    end
    return true, nil
  end

  function G.is_epoch_stale_for_match(user_id, match_snapshot_epoch)
    local snap = tonumber(match_snapshot_epoch) or 0
    return G.read_metadata_epoch(user_id) > snap
  end

  return G
end
