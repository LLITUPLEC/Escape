-- Hard-delete the authenticated account. Nakama cascades all owned storage,
-- including pending reward journals; a failed deletion must not partially reset it.
local nk = require("nakama")
local rate_ok, RateLimit = pcall(require, "modules.duel_api_rate_limit")
if not rate_ok then RateLimit = require("duel_api_rate_limit") end

local function duel_account_wipe(ctx, payload)
  local user_id = ctx and ctx.user_id
  if type(user_id) ~= "string" or user_id == "" then
    return nk.json_encode({ ok = false, account_deleted = false, err = "unauthorized" })
  end
  if type(nk.account_delete_id) ~= "function" then
    return nk.json_encode({ ok = false, account_deleted = false, err = "account_delete_unavailable" })
  end
  -- Never use payload user IDs and never fall back to storage_delete. The
  -- account deletion and its storage cascade belong to the server API.
  local ok, result = pcall(nk.account_delete_id, user_id)
  if not ok or result == false then
    nk.logger_error("duel_account_wipe account deletion failed: " .. tostring(result))
    return nk.json_encode({ ok = false, account_deleted = false, err = "account_delete_failed" })
  end
  return nk.json_encode({ ok = true, account_deleted = true })
end

RateLimit.register_rpc(duel_account_wipe, "duel_account_wipe")
