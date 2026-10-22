local nk = require("nakama")
local ok, RateLimit = pcall(require, "modules.duel_api_rate_limit")
if not ok then RateLimit = require("duel_api_rate_limit") end
local entry_ok, RaceEntry = pcall(require, "modules.duel_match3_race_entry")
if not entry_ok then RaceEntry = require("duel_match3_race_entry") end

-- Relayed matches are not used by Escape. All game matches are created inside
-- authoritative RPC/matchmaker handlers, which do not run this client hook.
nk.register_rt_before(function(ctx, payload)
  error({ "client_match_creation_disabled", 7 })
end, "MatchCreate")

for _, name in ipairs({ "MatchmakerAdd", "PartyMatchmakerAdd" }) do
  nk.register_rt_before(function(ctx, payload)
    RateLimit.consume(ctx, "create")
    local body = payload and (payload.matchmaker_add or payload.party_matchmaker_add) or payload
    local properties = type(body) == "table" and (body.string_properties or body.stringProperties) or nil
    if type(properties) == "table" and string.lower(tostring(properties.rules or "")) == "race" then
      -- Party tickets require checking every member, which this callback does
      -- not expose. Ordinary solo tickets are checked again when matched.
      if name == "PartyMatchmakerAdd" then error({ "race_party_matchmaking_unsupported", 7 }) end
      local paid, err = RaceEntry.require_pending(ctx and ctx.user_id or "")
      if not paid then error({ err or "race_entry_required", 7 }) end
    end
    return payload
  end, name)
end

for _, name in ipairs({ "MatchJoin", "PartyCreate", "PartyJoin", "ChannelJoin", "ChannelMessageSend", "ChannelMessageUpdate", "StatusUpdate" }) do
  nk.register_rt_before(function(ctx, payload)
    RateLimit.consume(ctx, "mutate")
    return payload
  end, name)
end
