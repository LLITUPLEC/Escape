local nk = require("nakama")

-- Renaming must pass the game's validation and price checks in duel_nickname_change.
-- Runtime account_update_id does not invoke this client request hook.
nk.register_req_before(function(ctx, payload)
  if type(payload) ~= "table" then return nil end
  if payload.username ~= nil and payload.username ~= "" then return nil end
  return payload
end, "UpdateAccount")
