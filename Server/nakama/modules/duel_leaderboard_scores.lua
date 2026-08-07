-- Счёт побед в Nakama Leaderboards (authoritative, operator=incr).
-- Периоды привязаны к МСК через bucket-ID (день/неделя/месяц), без cron-reset.
-- Устаревшие lb_*_d/w/m удаляются автоматически; *_all сохраняются.
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
--- Не чаще раза в час гоняем SQL-чистку устаревших периодных LB.
local PURGE_INTERVAL_SEC = 3600

local M = {}
local ensured = {}
local last_purge_ts = 0

function M.is_human(uid)
  if uid == nil or uid == "" then return false end
  return string.sub(uid, 1, 7) ~= CFG.BOT_USER_ID_PREFIX
end

--- Unix-время, сдвинутое так, что os.date("!...") даёт календарь МСК.
local function msk_unix(ts)
  return (tonumber(ts) or os.time()) + MSK_OFFSET
end

local function day_bucket(ts)
  return os.date("!%Y%m%d", msk_unix(ts))
end

local function month_bucket(ts)
  return os.date("!%Y%m", msk_unix(ts))
end

-- Понедельник текущей недели (МСК) — bucket для period=week.
-- Считаем через day-index, без os.date().wday: в части runtime wday ≠-- оказывается «пн=1», из‑за чего неделя ошибочно стартовала со вторника.
local function week_bucket(ts)
  local day_index = math.floor(msk_unix(ts) / 86400)
  -- 1970-01-01 = четверг; (day_index + 3) % 7 == 0 → понедельник.
  local days_since_monday = (day_index + 3) % 7
  local monday_index = day_index - days_since_monday
  return os.date("!%Y%m%d", monday_index * 86400)
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

local function delete_leaderboard_silent(lb_id)
  if lb_id == nil or lb_id == "" then return false end
  local ok, err = pcall(function()
    nk.leaderboard_delete(lb_id)
  end)
  if ok then
    ensured[lb_id] = nil
    return true
  end
  local msg = tostring(err or "")
  if not string.find(msg, "not found", 1, true)
      and not string.find(msg, "NotFound", 1, true) then
    nk.logger_warn("leaderboard_delete " .. tostring(lb_id) .. ": " .. msg)
  end
  return false
end

local function row_id(row)
  if row == nil then return nil end
  return row.id or row[1] or row["id"]
end

--- Удаляет lb_*_d/w/m с bucket старше текущего (МСК). *_all не трогает.
function M.purge_stale_leaderboards(ts)
  local now = tonumber(ts) or os.time()
  local cur_d = day_bucket(now)
  local cur_w = week_bucket(now)
  local cur_m = month_bucket(now)

  local ok, rows = pcall(function()
    return nk.sql_query([[SELECT id FROM leaderboard WHERE id LIKE 'lb\_%' ESCAPE '\']])
  end)
  if not ok or rows == nil then
    nk.logger_warn("leaderboard_purge sql_query: " .. tostring(rows))
    return 0
  end

  local deleted = 0
  for _, row in ipairs(rows) do
    local id = tostring(row_id(row) or "")
    if id ~= "" and not string.match(id, "_all$") then
      local kind, bucket = string.match(id, "_([dwm])_(%d+)$")
      local stale = false
      if kind == "d" and bucket ~= nil and bucket < cur_d then
        stale = true
      elseif kind == "w" and bucket ~= nil and bucket < cur_w then
        stale = true
      elseif kind == "m" and bucket ~= nil and bucket < cur_m then
        stale = true
      end
      if stale and delete_leaderboard_silent(id) then
        deleted = deleted + 1
      end
    end
  end
  if deleted > 0 then
    nk.logger_info("leaderboard_purge stale deleted=" .. tostring(deleted)
      .. " cur_d=" .. cur_d .. " cur_w=" .. cur_w .. " cur_m=" .. cur_m)
  end
  return deleted
end

local function maybe_purge_stale(ts)
  local now = tonumber(ts) or os.time()
  if (now - last_purge_ts) < PURGE_INTERVAL_SEC then
    return
  end
  last_purge_ts = now
  pcall(function()
    M.purge_stale_leaderboards(now)
  end)
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
  maybe_purge_stale(ts)
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
  maybe_purge_stale(os.time())
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
    -- nk.leaderboard_records_list → records, owner_records, ...
    -- При owners[] нужен именно 2-й возврат: 1-й — топ таблицы (limit),
    -- иначе sticky получает чужой #1, когда у игрока ещё нет записи.
    local owner_ok, owner_result = pcall(function()
      local _top, owner_records = nk.leaderboard_records_list(lb_id, { caller_user_id }, 1)
      return owner_records
    end)
    local owner_recs = {}
    if owner_ok then
      owner_recs = unwrap_records(owner_result)
    end
    local own = owner_recs[1]
    if own ~= nil then
      local own_uid = tostring(own.owner_id or own.ownerId or "")
      if own_uid == caller_user_id then
        self_entry = record_to_entry(own, 0)
      end
    end
    if self_entry == nil then
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
