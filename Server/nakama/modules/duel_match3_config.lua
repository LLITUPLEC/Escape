-- Константы для duel_match3.lua: require("modules.duel_match3_config") или require("duel_match3_config")
-- в зависимости от runtime.path (см. комментарий в duel_match3.lua).
local CFG = {  SIZE = 6,
  HEIGHT = 8,
  ACTIVE_Y_MIN = 2,
  ACTIVE_ROWS = 6,
  MAX_HP = 150,
  MAX_MANA = 100,
  TURN_SECONDS = 30,
  TICK_RATE = 6,
  BOT_THINK_SECONDS = 5.0,
  BOT_THINK_TICKS = math.max(1, math.floor(5.0 * 6 + 0.5)),
  -- Задержка между ходами бота в одном «сегменте» (доп. ход, петарда и т.д.), после анимаций (OP 17).
  BOT_THINK_SECONDS_FAST = 2.5,
  BOT_THINK_TICKS_FAST = math.max(1, math.floor(2.5 * 6 + 0.5)),
  --- «Человекоподобные» боты из ONLINE_POOL_NAMES: долгий think случайный в [min..max] сек.
  BOT_THINK_SECONDS_HUMAN_LIKE_MIN = 7,
  BOT_THINK_SECONDS_HUMAN_LIKE_MAX = 22,
  --- С какой server-y строки бот учитывает поле при симуляции "качества" хода.
  --  0 = как раньше (качество по всей 6x8, с preview-строками),
  --  1 = учитывать 6x7 (y=1..7),
  --  2 = только активная зона 6x6 (y=2..7).
  BOT_SIM_QUALITY_Y_MIN = 2,
  --- ONLINE_POOL-бот в финале арены: чуть шире «зрение» симуляции (6x7).
  BOT_SIM_QUALITY_Y_MIN_HUMAN_LIKE_FINAL = 1,
  --- Бот: режим выживания (щит + мана/хил), пока HP не восстановится.
  BOT_SURVIVAL_HP_ENTER = 30,
  BOT_SURVIVAL_HP_EXIT = 75,
  --- В выживании: сколько маны можно «пожертвовать» ради лечения способностью vs свап без хила.
  BOT_SURVIVAL_HEAL_MANA_SLACK = 5,
  --- Минимальная мана, с которой бот оценивает комбо ярость→петарда→линия бомб.
  BOT_FURY_FINISH_MIN_MANA = 60,
  CROSS_ABILITY_COST = 20,
  SQUARE_ABILITY_COST = 20,
  PETARD_ABILITY_COST = 30,
  SHIELD_ABILITY_COST = 40,
  FURY_ABILITY_COST = 30,
  CROSS_ABILITY_COOLDOWN = 2,
  SQUARE_ABILITY_COOLDOWN = 2,
  PETARD_ABILITY_COOLDOWN = 1,
  SHIELD_ABILITY_COOLDOWN = 1,
  FURY_ABILITY_COOLDOWN = 2,
  ABILITY_BASE_DAMAGE = 3,
  PETARD_DAMAGE = 15,
  SKULL_DAMAGE = 5,
  ANKH_HEAL = 1,
  FURY_CRIT_CHANCE = 0.18,
  SHIELD_DURATION_TURNS = 3,
  SHIELD_MAX_STACKS = 3,
  SHIELD_ARMOR_PER_STACK = 4,
  SHIELD_HEAL_PER_STACK = 3,
  GEM_MANA = { [1] = 5, [2] = 3, [3] = 1 },
  SPAWN_POOL = { 1, 2, 3, 4, 5 },
  FROZEN_ABILITY_COST_BONUS = 10,
  ACID_HP_LOSS_PCT = 0.03,
  REGEN_HP_PCT = 0.03,
  CHEAT_ROWS_COUNT = 2,
  CHEAT_ROWS_TOTAL = 6 * 2,
  CHEAT_WHITELIST_COLLECTION = "duel_match3_cheat_whitelist",
  CHEAT_WHITELIST_KEY = "emails",
  CHEAT_WHITELIST_USER_ID = "global",
  DEFAULT_CHEAT_EMAILS = { "tminn91@mail.ru", "tttt@mail.ru" },
  OP_BOARD_SYNC = 10,
  OP_GAME_OVER = 11,
  OP_PLAYER_LEFT = 12,
  OP_ACTION_REQUEST = 13,
  OP_ACTION_REJECT = 14,
  OP_SELECTION_SYNC = 15,
  OP_SNAPSHOT_REQUEST = 16,
  --- Уведомление: соперник временно потерял связь (клиент показывает ожидание; матч не завершается сразу).
  OP_PEER_DISCONNECT = 18,
  --- Сколько секунд ждать переподключения ушедшего игрока в PvP, прежде чем засчитать поражение.
  RECONNECT_GRACE_SECONDS = 300,
  STATS_COLLECTION = "duel_match3_stats",
  STATS_KEY = "summary",
  PVE_PROGRESS_COLLECTION = "duel_match3_progress",
  PVE_PROGRESS_KEY = "profile",
  CHARACTER_SHEET_COLLECTION = "duel_match3_character",
  CHARACTER_SHEET_KEY = "sheet",
  ITEM_DEFS_COLLECTION = "duel_match3_item_defs",
  ITEM_DEFS_KEY = "catalog",
  ITEM_DEFS_STORAGE_USER_ID = "0777075f-a8ec-4912-a5d5-bd9729d6a917",
  ACHIEVEMENT_DEFS_COLLECTION = "duel_match3_achievement_defs",
  ACHIEVEMENT_DEFS_KEY = "catalog",
  ACHIEVEMENT_DEFS_STORAGE_USER_ID = "0777075f-a8ec-4912-a5d5-bd9729d6a917",
  ACHIEVEMENT_CATALOG_CACHE_TTL_SEC = 30,
  --- Глобальные «аномалии» для PvE match3: Storage с user_id = SERVER_AURA_STORAGE_USER_ID.
  SERVER_AURA_COLLECTION = "duel_match3_server_aura",
  SERVER_AURA_KEY = "active",
  SERVER_AURA_STORAGE_USER_ID = "0777075f-a8ec-4912-a5d5-bd9729d6a917",
  --- Кэш чтения ауры (секунды); снижает нагрузку на storage.
  SERVER_AURA_CACHE_TTL_SECONDS = 30,
  BOTS_COLLECTION = "duel_match3_bot_defs",
  BOTS_KEY = "catalog",
  --- Каталоги ботов по сложности шахты: перекрывают id из BOTS_KEY.
  BOTS_KEYS_BY_DIFFICULTY = {
    easy = "catalog_easy",
    medium = "catalog_medium",
    hard = "catalog_hard",
  },
  BOTS_STORAGE_USER_ID = "0777075f-a8ec-4912-a5d5-bd9729d6a917",
  BOT_USER_ID_PREFIX = "zz-bot-",
  LEVEL_XP = { 0, 100, 320, 804, 1869, 4212, 9367, 20708, 45658, 100548, 221306, 486974 },
  PVE_MAX_LEVEL = 12,
  --- Максимальный запас энергии (покупки, «потолок» для HUD). Реген по времени — только до PVE_ENERGY_REGEN_CAP.
  PVE_ENERGY_MAX_BASE = 20000,
  PVE_ENERGY_REGEN_CAP = 100,
  PVE_ENERGY_REGEN_SECONDS = 60,
  PVE_ENERGY_BUY_MATTER_COST = 1,
  PVE_ENERGY_BUY_MATTER_GRANT = 100,
  PVE_ENERGY_BUY_GOLD_COST = 1000,
  PVE_ENERGY_BUY_GOLD_GRANT = 100,
  --- Смена никнейма (Nakama Username): первая бесплатно, дальше за золото.
  NICKNAME_CHANGE_GOLD_COST = 20000,
  NICKNAME_MIN_LEN = 3,
  NICKNAME_MAX_LEN = 17,
  WORKSHOP_CRAFT_RUSH_GOLD = 500,
  WORKSHOP_CRAFT_RUSH_SECONDS = 20 * 60,
  PVE_ENTRY_ENERGY_COST = 15,
  MINE_SUMMON_ENERGY_COST = 5,
  MINE_SUMMON_GOLD_COST = 50,
  MINE_DIFFICULTY_DEFAULT = "easy",
  MINE_RESPAWN_NORMAL_SECONDS = 10 * 60,
  MINE_RESPAWN_BOSS_SECONDS = 4 * 60 * 60,
  --- Модуль duel_match3_metrics: вклад брони в EHP (чем выше, тем «толще» бот при той же броне).
  METRICS_ARMOR_TO_EHP_K = 0.05,
  --- Сравнение с суммами §3.2 (HP/броня в «тысячах»): EHP_игрока ≈ HP×(1 + armor/REF).
  METRICS_PLAYER_ARMOR_REF = 1000,
  MINE_AFFIX_POOL = {
    "acid",
    "energy_block",
    "regeneration",
    "fragility",
    "stone_skin",
    "mana_vampire",
    "frozen",
    "monster_rage",
    "instability",
    "overload",
    "bare_current",
    "scree",
  },
  --- Базовые значения барьеров шахты (solo.md × ~1,4).
  MINE_BARRIER_REQUIREMENTS = {
    [2] = { ore = 140 },
    [3] = { ore = 490 },
    [4] = { ore = 1120 },
    [5] = { ore = 2100, gold = 2800 },
    [6] = { ore = 3500 },
    [7] = { ore = 5320, gold = 6500 },
    [8] = { ore = 7700 },
    [9] = { ore = 10500, gold = 14000 },
    [10] = { ore = 14000, gold = 17000 },
    [11] = { ore = 18200, gold = 22000 },
    [12] = { ore = 23800, matter = 300, gold = 35000 },
  },
  SESSION_EPOCH_ACCOUNT_META = "session_epoch",
  --- Зелёный normal T1: синхронно с gen_item_catalog.js (упор на накопление ресурсов).
  -- Fallback без craft_* в каталоге: как зелёный шлем T1 (этаж рецепта 1); по слотам см. duel_match3_item_catalog.
  WORKSHOP_T1_NORMAL_COST = { ore = 350, gold = 300, ingot_def = "ingot_green", ingot_n = 4 },
  WORKSHOP_T2_NORMAL_COST = { ore = 350, gold = 300, ingot_def = "ingot_green", ingot_n = 4 },
  WORKSHOP_T3_NORMAL_COST = { ore = 350, gold = 300, ingot_def = "ingot_green", ingot_n = 4 },
  --- Длительность крафта (секунды); тиры 2–3 не используются в экипе, оставлены для совместимости.
  WORKSHOP_CRAFT_DURATION_SEC_BY_TIER = { [1] = 60 * 60, [2] = 120 * 60, [3] = 240 * 60 },
  --- Награда за победу в обычном PvP (не арена-турнир, не PvE).
  PVP_WIN_XP = 50,
  PVP_WIN_GOLD = 75,
}

function CFG.clamp_int(v, lo, hi)
  local n = tonumber(v) or 0
  n = math.floor(n)
  if lo ~= nil and n < lo then n = lo end
  if hi ~= nil and n > hi then n = hi end
  return n
end

function CFG.character_stats_base_for_level(level)
  local lvl = CFG.clamp_int(level, 1, CFG.PVE_MAX_LEVEL)
  local bonus_levels = math.max(0, lvl - 1)
  local hp = 150 + bonus_levels * 30
  local damage = bonus_levels
  local armor = bonus_levels
  local crit = 0.005 + bonus_levels * 0.005
  local healing = bonus_levels
  return {
    hp = hp,
    damage = damage,
    armor = armor,
    crit_chance = crit,
    healing = healing,
  }
end

return CFG