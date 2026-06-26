local nk = require("nakama")

local function runtime_lua_require(name_nested, name_root)
  local ok, mod = pcall(require, name_nested)
  if ok and mod ~= nil then return mod end
  return require(name_root)
end

local LB = runtime_lua_require("modules.duel_leaderboard_scores", "duel_leaderboard_scores")

local PVE_MAX_LEVEL = 12

local VALID_TYPES = {
  tournament = true,
  duel = true,
  mine = true,
}

local VALID_VIEWS = {
  tournament_ore = true,
  tournament_gold = true,
  tournament_smith = true,
  duel_skirmish = true,
  duel_arena = true,
}

for floor = 1, PVE_MAX_LEVEL do
  VALID_VIEWS["mine_floor_" .. tostring(floor)] = true
end

local VALID_PERIODS = {
  day = true,
  week = true,
  month = true,
  all = true,
}

local function default_view_for_type(typ)
  if typ == "duel" then return "duel_skirmish" end
  if typ == "mine" then return "mine_floor_1" end
  return "tournament_ore"
end

local function validate_request(body)
  local period = "week"
  local typ = "tournament"
  local view_id = "tournament_ore"

  if body ~= nil and body ~= "" then
    local ok, decoded = pcall(nk.json_decode, body)
    if ok and decoded ~= nil then
      if decoded.period ~= nil then period = tostring(decoded.period) end
      if decoded.type ~= nil then typ = tostring(decoded.type) end
      if decoded.view_id ~= nil then view_id = tostring(decoded.view_id) end
    end
  end

  if not VALID_PERIODS[period] then period = "week" end
  if not VALID_TYPES[typ] then typ = "tournament" end
  if not VALID_VIEWS[view_id] then view_id = default_view_for_type(typ) end

  return period, typ, view_id
end

local function rewards_for(period, typ, view_id)
  local mult = 1
  if period == "day" then mult = 0.25
  elseif period == "month" then mult = 2.5
  elseif period == "all" then mult = 5 end

  if typ == "mine" then mult = mult * 0.8 end

  local base_gold = 1000
  if view_id == "tournament_gold" then base_gold = 1500
  elseif view_id == "tournament_smith" then base_gold = 1200 end

  local g = math.floor(base_gold * mult + 0.5)
  return {
    { place = 1, items = {
      { icon_id = "gold", amount = g },
      { icon_id = "matter", amount = math.floor(g * 0.5 + 0.5) },
      { icon_id = "diamond", amount = math.max(1, math.floor(g * 0.05 + 0.5)) },
    }},
    { place = 2, items = {
      { icon_id = "gold", amount = math.floor(g * 0.5 + 0.5) },
      { icon_id = "matter", amount = math.floor(g * 0.25 + 0.5) },
      { icon_id = "ore", amount = math.max(1, math.floor(g * 0.1 + 0.5)) },
    }},
    { place = 3, items = {
      { icon_id = "gold", amount = math.floor(g * 0.25 + 0.5) },
      { icon_id = "ore", amount = math.max(1, math.floor(g * 0.08 + 0.5)) },
      { icon_id = "energy", amount = math.max(5, math.floor(g * 0.03 + 0.5)) },
    }},
  }
end

local function duel_leaderboard_get(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local period, typ, view_id = validate_request(payload)
    local entries, self_entry = LB.list(period, view_id, user_id, 100)
    local rewards = rewards_for(period, typ, view_id)

    return nk.json_encode({
      ok = true,
      entries = entries,
      self_entry = self_entry,
      rewards = rewards,
    })
  end)

  if not ok then
    nk.logger_error("duel_leaderboard_get: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

nk.register_rpc(duel_leaderboard_get, "duel_leaderboard_get")
