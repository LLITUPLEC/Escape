-- Счёт побед в Nakama Leaderboards (authoritative, operator=incr).
-- Периоды привязаны к МСК через bucket-ID (день/неделя/месяц), без cron-reset.
local nk = require("nakama")

local function runtime_lua_require(name_nested, name_root)
  local ok, mod = pcall(require, name_nested)
  if ok and mod ~= nil then return mod end
  return require(name_root)
end

local CFG = runtime_lua_require("modules.duel_match3_config", "duel_match3_config")

local MSK_OFFSET = 3 * 3600
local PERIODS = { "day", "week", "month", "all" }
local PVE_MAX_LEVEL = 12

local M = {}

function M.is_human(uid)
  if uid == nil or uid == "" then return false end
  return string.sub(uid, 1, 7) ~= CFG.BOT_USER_ID_PREFIX
end

local function msk_unix(ts)
  return (tonumber(ts) or os.time()) + MSK_OFFSET
end

local function day_bucket(ts)
  return os.date("!%Y%m%d", msk_unix(ts))
end

local function month_bucket(ts)
  return os.date("!%Y%m", msk_unix(ts))
end

local function is_leap_year(y)
  return (y % 4 == 0) and (y % 100 ~= 0 or y % 400 == 0)
end

local function days_in_month(y, m)
  local days = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 }
  if m == 2 and is_leap_year(y) then return 29 end
  return days[m]
end

local function shift_ymd(y, m, d, delta)
  d = d + delta
  while d < 1 do
    m = m - 1
    if m < 1 then m, y = 12, y - 1 end
    d = d + days_in_month(y, m)
  end
  while d > days_in_month(y, m) do
    d = d - days_in_month(y, m)
    m = m + 1
    if m > 12 then m, y = 1, y + 1 end
  end
  return y, m, d
end

-- Понедельник текущей недели (МСК) — bucket для period=week (Lua 5.1 не поддерживает %G/%V).
local function week_bucket(ts)
  local t = os.date("!*t", msk_unix(ts))
  local wday = t.wday -- 1=вс
  local iso = (wday == 1) and 7 or (wday - 1) -- 1=пн … 7=вс
  local y, m, d = shift_ymd(t.year, t.month, t.day, -(iso - 1))
  return string.format("%04d%02d%02d", y, m, d)
end

function M.leaderboard_id(view_id, period, ts)
  view_id = tostring(view_id or "")
  if period == "all" then
    return "lb_" .. view_id .. "_all"
  end
  if period == "day" then
    return "lb_" .. view_id .. "_d_" .. day_bucket(ts)
  end
  if period == "week" then
    return "lb_" .. view_id .. "_w_" .. week_bucket(ts)
  end
  if period == "month" then
    return "lb_" .. view_id .. "_m_" .. month_bucket(ts)
  end
  return "lb_" .. view_id .. "_all"
end

function M.floor_from_bot_id(bot_id)
  local sid = tostring(bot_id or "")
  local n = string.match(sid, "^mine_(%d+)$")
  if n == nil then return 0 end
  return tonumber(n) or 0
end

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

local ensured = {}

function M.ensure_leaderboard(lb_id)
  if ensured[lb_id] then return end
  local ok, err = pcall(function()
    nk.leaderboard_create(lb_id, true, "desc", "incr", "", {})
  end)
  if not ok then
    local msg = tostring(err or "")
    if not string.find(msg, "already exists", 1, true)
        and not string.find(msg, "duplicate", 1, true) then
      nk.logger_warn("leaderboard_create " .. tostring(lb_id) .. ": " .. msg)
    end
  end
  ensured[lb_id] = true
end

local function unwrap_records(list_result)
  if list_result == nil then return {} end
  if type(list_result) ~= "table" then return {} end
  if list_result.records ~= nil then return list_result.records end
  if list_result[1] ~= nil and type(list_result[1]) == "table" then return list_result end
  return {}
end

local function record_to_entry(rec, fallback_rank)
  return {
    rank = tonumber(rec.rank) or fallback_rank or 0,
    rank_delta = 0,
    is_new = false,
    user_id = tostring(rec.owner_id or rec.ownerId or ""),
    nickname = (rec.username ~= nil and rec.username ~= "") and tostring(rec.username) or "Survivor",
    score = tonumber(rec.score) or 0,
    secondary_score = tonumber(rec.subscore) or 0,
  }
end

function M.resolve_view_ids(state, winner)
  local views = {}
  if state == nil or winner == nil or winner == "" then
    return views
  end
  if not M.is_human(winner) then
    return views
  end

  local am = state.arena_mirror
  if am ~= nil then
    -- Турнир арены: только финал → tournament_* (не duel_skirmish / duel_arena).
    if tostring(am.round or "") == "final" then
      local ak = string.lower(tostring(am.kind or "smith"))
      if ak == "ore" then
        views[#views + 1] = "tournament_ore"
      elseif ak == "gold" then
        views[#views + 1] = "tournament_gold"
      else
        views[#views + 1] = "tournament_smith"
      end
    end
  elseif state.mode ~= "pve" then
    -- match3ProButton → PvP Pro; match3Button → классическая 1v1 дуэль.
    if state.pvp_pro == true then
      views[#views + 1] = "duel_skirmish"
    else
      views[#views + 1] = "duel_arena"
    end
  elseif winner == state.owner_user_id then
    local floor = M.floor_from_bot_id(state.bot_id)
    if floor >= 1 and floor <= PVE_MAX_LEVEL then
      views[#views + 1] = "mine_floor_" .. tostring(floor)
    end
  end

  return views
end

function M.inc_win(user_id, view_id, username, ts)
  if user_id == nil or user_id == "" or view_id == nil or view_id == "" then
    return
  end
  local name = username
  if name == nil or name == "" then
    name = read_username(user_id)
  end
  for _, period in ipairs(PERIODS) do
    local lb_id = M.leaderboard_id(view_id, period, ts)
    M.ensure_leaderboard(lb_id)
    local ok, err = pcall(function()
      nk.leaderboard_record_write(lb_id, user_id, name, 1, 0, {}, nil)
    end)
    if not ok then
      nk.logger_error("leaderboard_record_write " .. lb_id .. ": " .. tostring(err))
    end
  end
end

function M.flush_match_wins(state, winner)
  local views = M.resolve_view_ids(state, winner)
  if #views == 0 then return end
  local ts = os.time()
  local username = read_username(winner)
  for _, view_id in ipairs(views) do
    M.inc_win(winner, view_id, username, ts)
  end
end

function M.list(period, view_id, caller_user_id, limit)
  limit = math.max(1, math.min(tonumber(limit) or 100, 100))
  local lb_id = M.leaderboard_id(view_id, period)
  M.ensure_leaderboard(lb_id)

  local list_ok, list_result = pcall(function()
    return nk.leaderboard_records_list(lb_id, nil, limit)
  end)
  if not list_ok then
    nk.logger_error("leaderboard_records_list " .. lb_id .. ": " .. tostring(list_result))
    list_result = nil
  end

  local records = unwrap_records(list_result)
  local entries = {}
  local self_in_top = nil

  for i, rec in ipairs(records) do
    local entry = record_to_entry(rec, i)
    entries[#entries + 1] = entry
    if caller_user_id ~= nil and caller_user_id ~= "" and entry.user_id == caller_user_id then
      self_in_top = entry
    end
  end

  local self_entry
  if self_in_top ~= nil then
    self_entry = self_in_top
  elseif caller_user_id ~= nil and caller_user_id ~= "" then
    local owner_ok, owner_result = pcall(function()
      return nk.leaderboard_records_list(lb_id, { caller_user_id }, 1)
    end)
    local owner_recs = {}
    if owner_ok then
      owner_recs = unwrap_records(owner_result)
    end
    if owner_recs[1] ~= nil then
      self_entry = record_to_entry(owner_recs[1], 0)
    else
      self_entry = {
        rank = 0,
        rank_delta = 0,
        is_new = false,
        user_id = caller_user_id,
        nickname = read_username(caller_user_id),
        score = 0,
        secondary_score = 0,
      }
    end
  end

  return entries, self_entry
end

return M
