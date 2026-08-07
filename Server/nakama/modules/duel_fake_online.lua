--- Общая логика фиктивного онлайна (badge / список) и допустимых ников ботов арены.
--- Пул «человекоподобных» ников показывается онлайн пачками 1..4 / 30 мин;
--- «сказочные» + запасные всегда можно ставить в турнир.
--- Из пула в турнир попадают только те, кто сейчас в фиктивном онлайне
--- и не на кулдауне (не более 1 турнира в час на ник из ONLINE_POOL_NAMES).

local nk = require("nakama")

local M = {}

M.HALF_HOUR_SEC = 30 * 60
M.MSK_OFFSET_SEC = 3 * 3600
M.QUIET_START_BASE_SEC = 22 * 3600 + 42 * 60 -- 22:42 МСК
M.QUIET_END_BASE_SEC = 8 * 3600 + 17 * 60   -- 08:17 МСК
M.QUIET_JITTER_MIN = 10
--- Лимит: ник из ONLINE_POOL_NAMES — не чаще 1 турнира в час (любой kind).
M.POOL_TOURNAMENT_COOLDOWN_SEC = 60 * 60

local STORAGE_SYSTEM_USER_ID = "00000000-0000-0000-0000-000000000000"
local STORAGE_COLL = "arena_bot_cooldowns"
local STORAGE_KEY = "online_pool_v1"

--- Ники, которые могут отображаться в фиктивном онлайне.
M.ONLINE_POOL_NAMES = {
  "Player_eUIbGX83r3",
  "_eby_kak_xo4y_",
  "Player_qKwBIlSUfZ",
  "Vanya_22",
  "Player_4e66fa56-",
}

local ONLINE_POOL_LOOKUP = {}
for i = 1, #M.ONLINE_POOL_NAMES do
  ONLINE_POOL_LOOKUP[string.lower(M.ONLINE_POOL_NAMES[i])] = true
end

function M.is_online_pool_name(name)
  if name == nil or name == "" then return false end
  return ONLINE_POOL_LOOKUP[string.lower(tostring(name))] == true
end

--- Всегда доступны для пар турнира (не зависят от фиктивного онлайна).
M.ARENA_ALWAYS_NAMES = {
  "Морозко",
  "Шипогрыз",
  "Туманный",
  "Жнец",
  "Шутиха",
  "Надзиратель", -- запас, когда из пула онлайн только 1 ник
  "Осколок",
  "Дуболом",
}

--- lower(name) -> unix timestamp последнего входа в турнир.
local pool_last_tournament_at = {}
local pool_cd_loaded = false

local function now_unix()
  return os.time()
end

local function load_pool_cooldowns()
  if pool_cd_loaded then
    return
  end
  pool_cd_loaded = true
  local ok, rows = pcall(function()
    return nk.storage_read({
      {
        user_id = STORAGE_SYSTEM_USER_ID,
        collection = STORAGE_COLL,
        key = STORAGE_KEY,
      },
    })
  end)
  if not ok or rows == nil or #rows == 0 then
    return
  end
  local val = rows[1].value
  if type(val) == "string" then
    local dec_ok, decoded = pcall(nk.json_decode, val)
    if dec_ok then
      val = decoded
    else
      val = nil
    end
  end
  if type(val) ~= "table" or type(val.at) ~= "table" then
    return
  end
  for k, v in pairs(val.at) do
    local ts = tonumber(v)
    if type(k) == "string" and k ~= "" and ts ~= nil and ts > 0 then
      pool_last_tournament_at[string.lower(k)] = ts
    end
  end
end

local function save_pool_cooldowns()
  local at = {}
  local now = now_unix()
  local ttl = M.POOL_TOURNAMENT_COOLDOWN_SEC
  for k, v in pairs(pool_last_tournament_at) do
    local ts = tonumber(v) or 0
    -- не храним совсем протухшие
    if ts > 0 and (now - ts) < ttl * 2 then
      at[k] = ts
    end
  end
  pool_last_tournament_at = at
  pcall(function()
    nk.storage_write({
      {
        user_id = STORAGE_SYSTEM_USER_ID,
        collection = STORAGE_COLL,
        key = STORAGE_KEY,
        value = { at = at },
      },
    })
  end)
end

function M.canonical_pool_name(username)
  local name = tostring(username or "")
  if name == "" then
    return nil
  end
  local key = string.lower(name)
  for i = 1, #M.ONLINE_POOL_NAMES do
    if string.lower(M.ONLINE_POOL_NAMES[i]) == key then
      return M.ONLINE_POOL_NAMES[i]
    end
  end
  return nil
end

function M.is_online_pool_name(username)
  return M.canonical_pool_name(username) ~= nil
end

--- true, если ник из пула уже сыграл турнир менее часа назад.
function M.is_pool_on_tournament_cooldown(username, ts)
  local canon = M.canonical_pool_name(username)
  if canon == nil then
    return false
  end
  load_pool_cooldowns()
  local t = tonumber(ts) or now_unix()
  local last = tonumber(pool_last_tournament_at[string.lower(canon)]) or 0
  if last <= 0 then
    return false
  end
  return (t - last) < M.POOL_TOURNAMENT_COOLDOWN_SEC
end

--- Зафиксировать участие ников из ONLINE_POOL в турнире (вызывать после назначения display).
function M.mark_pool_tournament_played(displays, ts)
  local t = tonumber(ts) or now_unix()
  load_pool_cooldowns()
  local changed = false
  for _, d in ipairs(displays or {}) do
    local canon = M.canonical_pool_name(d)
    if canon ~= nil then
      pool_last_tournament_at[string.lower(canon)] = t
      changed = true
    end
  end
  if changed then
    save_pool_cooldowns()
  end
end

function M.slot(ts)
  return math.floor((tonumber(ts) or now_unix()) / M.HALF_HOUR_SEC)
end

function M.pad_for_slot(slot)
  local s = tonumber(slot) or 0
  local x = (s * 2654435761) % 4294967296
  if x < 0 then x = -x end
  return (x % 4) + 1
end

local function msk_day_index(ts)
  return math.floor(((tonumber(ts) or now_unix()) + M.MSK_OFFSET_SEC) / 86400)
end

local function msk_seconds_since_midnight(ts)
  return ((tonumber(ts) or now_unix()) + M.MSK_OFFSET_SEC) % 86400
end

local function deterministic_jitter(seed, amp)
  local a = math.max(0, math.floor(tonumber(amp) or 0))
  if a <= 0 then
    return 0
  end
  local x = (tonumber(seed) or 0) * 2654435761
  x = x % 4294967296
  if x < 0 then x = -x end
  return (x % (2 * a + 1)) - a
end

local function quiet_bounds_for_night(day_index)
  local d = tonumber(day_index) or 0
  local j_start = deterministic_jitter(d * 17 + 11, M.QUIET_JITTER_MIN)
  local j_end = deterministic_jitter(d * 31 + 7, M.QUIET_JITTER_MIN)
  local start_sec = M.QUIET_START_BASE_SEC + j_start * 60
  local end_sec = M.QUIET_END_BASE_SEC + j_end * 60
  if start_sec < 0 then start_sec = 0 end
  if start_sec > 86399 then start_sec = 86399 end
  if end_sec < 0 then end_sec = 0 end
  if end_sec > 86399 then end_sec = 86399 end
  return start_sec, end_sec
end

function M.is_quiet(ts)
  local t = tonumber(ts) or now_unix()
  local sod = msk_seconds_since_midnight(t)
  local day = msk_day_index(t)
  local start_today = quiet_bounds_for_night(day)
  local _, end_from_yesterday = quiet_bounds_for_night(day - 1)
  return sod >= start_today or sod < end_from_yesterday
end

function M.effective_pad(ts)
  local t = tonumber(ts) or now_unix()
  if M.is_quiet(t) then
    return 0
  end
  return M.pad_for_slot(M.slot(t))
end

function M.user_id_for(username)
  return "zz-decor-online-" .. tostring(username or "")
end

--- Перемешанный порядок пула для слота (тот же PRNG, что у списка онлайн).
local function shuffled_pool(slot)
  local names = {}
  for i = 1, #M.ONLINE_POOL_NAMES do
    names[i] = M.ONLINE_POOL_NAMES[i]
  end
  local state = (tonumber(slot) or 0) * 1103515245 + 12345
  local function rnd(n)
    state = (state * 1103515245 + 12345) % 2147483648
    return (state % n) + 1
  end
  for i = #names, 2, -1 do
    local j = rnd(i)
    names[i], names[j] = names[j], names[i]
  end
  return names
end

--- Игроки фиктивного онлайна (как в duel_online_list).
--- exclude_lower: set lower(username) реальных онлайн, чтобы не дублировать ник.
function M.build_players(slot, pad, exclude_lower)
  exclude_lower = exclude_lower or {}
  local names = shuffled_pool(slot)
  local out = {}
  for i = 1, #names do
    if #out >= pad then
      break
    end
    local name = names[i]
    local key = string.lower(name)
    if exclude_lower[key] ~= true then
      out[#out + 1] = {
        user_id = M.user_id_for(name),
        username = name,
        level = ((slot + i - 1) % 12) + 1,
        decor = true,
      }
      exclude_lower[key] = true
    end
  end
  return out
end

--- Ники из пула, которые сейчас считаются «онлайн» (без exclude реальных).
function M.visible_pool_names(ts)
  local t = tonumber(ts) or now_unix()
  local pad = M.effective_pad(t)
  if pad <= 0 then
    return {}
  end
  local players = M.build_players(M.slot(t), pad, {})
  local out = {}
  for i = 1, #players do
    out[#out + 1] = players[i].username
  end
  return out
end

--- Каталог имён, из которых можно набрать ботов арены прямо сейчас.
function M.arena_eligible_bot_names(ts)
  local t = tonumber(ts) or now_unix()
  local out = {}
  local seen = {}
  for i = 1, #M.ARENA_ALWAYS_NAMES do
    local n = M.ARENA_ALWAYS_NAMES[i]
    if seen[n] ~= true then
      seen[n] = true
      out[#out + 1] = n
    end
  end
  local visible = M.visible_pool_names(t)
  for i = 1, #visible do
    local n = visible[i]
    if seen[n] ~= true and not M.is_pool_on_tournament_cooldown(n, t) then
      seen[n] = true
      out[#out + 1] = n
    end
  end
  return out
end

return M
