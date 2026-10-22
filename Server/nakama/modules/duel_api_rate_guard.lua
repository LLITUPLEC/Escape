local nk = require("nakama")
local ok, RateLimit = pcall(require, "modules.duel_api_rate_limit")
if not ok then RateLimit = require("duel_api_rate_limit") end

-- Relayed matches are not used by Escape. All game matches are created inside
-- authoritative RPC/matchmaker handlers, which do not run this client hook.
nk.register_rt_before(function(ctx, payload)
  error({ "client_match_creation_disabled", 7 })
end, "MatchCreate")

for _, name in ipairs({ "MatchmakerAdd", "PartyMatchmakerAdd" }) do
  nk.register_rt_before(function(ctx, payload)
    RateLimit.consume(ctx, "create")
    return payload
  end, name)
end

for _, name in ipairs({ "MatchJoin", "PartyCreate", "PartyJoin", "ChannelJoin", "ChannelMessageSend", "ChannelMessageUpdate", "StatusUpdate" }) do
  nk.register_rt_before(function(ctx, payload)
    RateLimit.consume(ctx, "mutate")
    return payload
  end, name)
end
