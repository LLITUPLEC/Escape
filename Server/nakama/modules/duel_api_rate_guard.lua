local nk = require("nakama")
local ok, RateLimit = pcall(require, "modules.duel_api_rate_limit")
if not ok then RateLimit = require("duel_api_rate_limit") end
local entry_ok, RaceEntry = pcall(require, "modules.duel_match3_race_entry")
if not entry_ok then RaceEntry = require("duel_match3_race_entry") end
local practice_ok, Practice = pcall(require,"modules.duel_global_boss_practice")
if not practice_ok then Practice=require("duel_global_boss_practice")end

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

nk.register_rt_before(function(ctx,payload)
  local body=type(payload)=="table"and(payload.match_join or payload)or{}
  local mid=type(body)=="table"and(body.match_id or body.matchId)
  if type(mid)=="string"and#mid>0 and#mid<=160 then
    RateLimit.consume_transient(ctx,"mutate")
    local ok,match=pcall(nk.match_get,mid)
    if not ok or type(match)~="table"then error({"match_probe_unavailable",14})end
    -- Only server-created, authoritative, owner-bound practice labels qualify.
    -- Client metadata/tokens and relayed labels can never remove the normal
    -- durable budget; the match's join_attempt still authorizes its owner.
    if type(match.label)=="string"and match.label:sub(1,#Practice.label(""))==Practice.label("")then
      if match.authoritative==true and match.handler_name=="duel_match3"
          and match.label==Practice.label(ctx.user_id)then return payload end
      error({"practice_join_not_authorized",7})
    end
  end
  RateLimit.consume(ctx,"mutate");return payload
end,"MatchJoin")

for _, name in ipairs({ "PartyCreate", "PartyJoin", "ChannelJoin", "ChannelMessageSend", "ChannelMessageUpdate", "StatusUpdate" }) do
  nk.register_rt_before(function(ctx, payload)
    RateLimit.consume(ctx, "mutate")
    return payload
  end, name)
end
