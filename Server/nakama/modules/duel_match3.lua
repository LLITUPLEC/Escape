local nk = require("nakama")
-- Nakama кладёт модули в package.preload по имени: относительный путь от runtime.path, «/» → «.».
-- Файл modules/duel_match3_config.lua → require("modules.duel_match3_config").
-- Если runtime.path указывает уже на внутреннюю папку modules/, подойдёт короткое имя — см. runtime_lua_require.
local function runtime_lua_require(name_nested, name_root)
  local ok, mod = pcall(require, name_nested)
  if ok and mod ~= nil then return mod end
  return require(name_root)
end

local CFG = runtime_lua_require("modules.duel_match3_config", "duel_match3_config")
local Metrics = runtime_lua_require("modules.duel_match3_metrics", "duel_match3_metrics")
local Ach = runtime_lua_require("modules.duel_match3_achievements", "duel_match3_achievements")

-- Board dimensions:
-- We keep a real 6x8 board on server.
-- Active area (seen/played by everyone) is bottom 6 rows (client y=0..5).
-- Preview area (seen only by whitelisted users) is top 2 rows (server y=0..1),
-- and is NOT interactable and NOT affected by abilities/match scoring until pieces fall into active area.

-- ─────────────────────────────────────────────────────────────────────────────
-- Character progression (server-authoritative).
--
-- We keep stats extensible by returning a string-keyed map in RPC.
-- For now the game uses: hp, damage, armor, crit_chance (0..1).
-- ─────────────────────────────────────────────────────────────────────────────
local function clamp_int(v, lo, hi)
  local n = tonumber(v) or 0
  n = math.floor(n)
  if lo ~= nil and n < lo then n = lo end
  if hi ~= nil and n > hi then n = hi end
  return n
end

local function character_stats_base_for_level(level)
  local lvl = clamp_int(level, 1, CFG.PVE_MAX_LEVEL)
  local bonus_levels = math.max(0, lvl - 1)

  -- Базовые параметры на 1-м уровне, рост — согласно solo.md.
  local hp = 150 + bonus_levels * 30
  -- В бою есть базовый урон черепа (SKULL_DAMAGE), поэтому level-бонус идёт отдельной прибавкой.
  local damage = bonus_levels
  local armor = bonus_levels
  -- Крит: 0.5% на 1-м уровне, +0.5% за уровень → на 12-м = 6% (eqip_stats §3.2 / фаза 0).
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

local function guard_read_metadata_epoch(user_id)
  if user_id == nil or user_id == "" then
    return 0
  end
  local ok, account = pcall(function()
    return nk.account_get_id(user_id)
  end)
  if not ok or account == nil or account.user == nil or account.user.metadata == nil then
    return 0
  end
  local v = account.user.metadata[CFG.SESSION_EPOCH_ACCOUNT_META]
  if v == nil then
    return 0
  end
  return tonumber(v) or 0
end

local function guard_parse_client_epoch_from_payload(payload)
  if payload == nil or payload == "" then
    return nil
  end
  local ok, p = pcall(nk.json_decode, payload)
  if not ok or type(p) ~= "table" then
    return nil
  end
  if p.session_epoch == nil then
    return nil
  end
  return tonumber(p.session_epoch)
end

local function guard_assert_client_epoch_matches(user_id, payload)
  local server_e = guard_read_metadata_epoch(user_id)
  local client_e = guard_parse_client_epoch_from_payload(payload)
  if client_e == nil then
    return false, "session_epoch_required"
  end
  if client_e ~= server_e then
    return false, "session_stale"
  end
  return true, nil
end

local function guard_is_epoch_stale_for_match(user_id, match_snapshot_epoch)
  local snap = tonumber(match_snapshot_epoch) or 0
  return guard_read_metadata_epoch(user_id) > snap
end

local award_pve_victory
local award_pve_defeat

function is_boss_floor(floor)
  return floor == 4 or floor == 8 or floor == 12
end

function mine_bot_id_for_floor(floor)
  return "mine_" .. tostring(clamp_int(floor, 1, CFG.PVE_MAX_LEVEL))
end

--- Базовые значения из solo.md × ~1,4 (руда/золото/материя); ключи в барьерах не используются.
local MINE_BARRIER_REQUIREMENTS = {
  [2] = { ore = 140 },
  [3] = { ore = 490 },
  [4] = { ore = 1120 },
  [5] = { ore = 2100, gold = 2800 },
  [6] = { ore = 3500 },
  [7] = { ore = 5320 },
  [8] = { ore = 7700 },
  [9] = { ore = 10500, gold = 14000 },
  [10] = { ore = 14000 },
  [11] = { ore = 18200 },
  [12] = { ore = 23800, matter = 700, gold = 35000 },
}

-- Стоимость боя / «Прогнать» (duel_match3_pve_mine_cost.lua).
local function pve_energy_max_for_user(user_id)
  return CFG.PVE_ENERGY_MAX_BASE
end

function empty_key_items()
  return { miner_key = 0, dark_key = 0 }
end

local build_pve_mine_cost = runtime_lua_require("modules.duel_match3_pve_mine_cost", "duel_match3_pve_mine_cost")
local PveMineCost = build_pve_mine_cost({
  clamp_int = clamp_int,
  pve_energy_max_for_user = pve_energy_max_for_user,
  empty_key_items = empty_key_items,
})

function build_floor_bot_entry(floor)
  local f = clamp_int(floor, 1, CFG.PVE_MAX_LEVEL)
  local boss = is_boss_floor(f)
  local hp = 80 + f * 20
  local damage = 3 + f
  local armor = math.floor(f / 2)
  local crit = 0.01 * math.floor(f / 2)
  local start_mana = (f - 1) * 3
  local reward_xp = 40 + f * 10
  local reward_gold = 20 + f * 5
  local reward_ore = 15 + f * 8
  local reward_matter_min = 0
  local reward_matter_max = 0
  local reward_blueprint = ""
  local reward_ingots = 0
  local reward_tesseract_chance = 0
  local reward_key_id = ""
  local reward_key_amount = 0

  if boss then
    hp = hp * 2
    damage = damage * 2
    armor = armor * 2
    crit = crit * 2
    start_mana = start_mana * 2
    reward_matter_min = 5
    reward_matter_max = 10
    if f == 4 then
      reward_xp = 200
      reward_gold = 150
      reward_ore = 200
      reward_blueprint = "green"
      reward_key_id = "miner_key"
      reward_key_amount = 1
      reward_ingots = 1
    elseif f == 8 then
      reward_xp = 350
      reward_gold = 250
      reward_ore = 350
      reward_blueprint = "blue"
      reward_key_id = "dark_key"
      reward_key_amount = 1
      reward_ingots = 2
    else
      reward_xp = 500
      reward_gold = 350
      reward_ore = 500
      reward_blueprint = "purple"
      reward_ingots = 3
      reward_tesseract_chance = 0.10
    end
  else
    reward_matter_min = 1
    reward_matter_max = 3
  end

  local hp_bonus = math.max(0, hp - CFG.MAX_HP)
  return {
    id = mine_bot_id_for_floor(f),
    name = boss and ("Страж шахты " .. tostring(f)) or ("Шахтный монстр " .. tostring(f)),
    difficulty = f,
    floor = f,
    is_boss = boss,
    hp_bonus = hp_bonus,
    start_mana = start_mana,
    ai_ability_chance = math.min(0.70, 0.12 + f * 0.03),
    petard_bias = 0.34,
    cross_bias = 0.33,
    square_bias = 0.33,
    reward_xp = reward_xp,
    reward_gold = reward_gold,
    reward_ore = reward_ore,
    reward_matter_min = reward_matter_min,
    reward_matter_max = reward_matter_max,
    reward_blueprint = reward_blueprint,
    reward_ingots = reward_ingots,
    reward_tesseract_chance = reward_tesseract_chance,
    reward_key_id = reward_key_id,
    reward_key_amount = reward_key_amount,
    base_damage = damage,
    base_armor = armor,
    base_crit = crit,
    base_heal = 0,
    cost_attack = PveMineCost.clone_list(PveMineCost.DEFAULT_ATTACK),
    cost_banish = PveMineCost.clone_list(PveMineCost.DEFAULT_BANISH),
  }
end

local BOTS_FALLBACK = {}
for floor = 1, CFG.PVE_MAX_LEVEL do
  local bot = build_floor_bot_entry(floor)
  BOTS_FALLBACK[bot.id] = bot
end

math.randomseed(os.time())

--- First-move fairness: avoid math.random(0,1) here — multiple Lua modules call
--- math.randomseed(os.time()); combined with Nakama's RNG this skewed toward one side.
local function pick_first_actor(id_a, id_b)
  local s = nk.uuid_v4()
  local sum = 0
  for i = 1, #s do
    sum = sum + string.byte(s, i)
  end
  if (sum % 2) == 0 then
    return id_a
  end
  return id_b
end

local function idx(x, y)
  return y * CFG.SIZE + x + 1
end

local function in_bounds(x, y)
  return x >= 0 and x < CFG.SIZE and y >= 0 and y < CFG.HEIGHT
end

local function in_active_client(x, y)
  return x >= 0 and x < CFG.SIZE and y >= 0 and y < CFG.ACTIVE_ROWS
end

local function client_to_server_y(y)
  return (tonumber(y) or -999) + CFG.ACTIVE_Y_MIN
end

local function in_active_server(x, y)
  return x >= 0 and x < CFG.SIZE and y >= CFG.ACTIVE_Y_MIN and y < CFG.HEIGHT
end

local function bget(board, x, y)
  return board[idx(x, y)]
end

local function bset(board, x, y, v)
  board[idx(x, y)] = v
end

local function clone_board(board)
  local out = {}
  for i = 1, #board do out[i] = board[i] end
  return out
end

local function sorted_two_players(presences_map)
  local ids = {}
  for uid, _ in pairs(presences_map) do
    ids[#ids + 1] = uid
  end
  table.sort(ids)
  if #ids > 2 then
    ids = { ids[1], ids[2] }
  end
  return ids
end

local function make_bot_user_id(bot_id)
  return CFG.BOT_USER_ID_PREFIX .. tostring(bot_id or mine_bot_id_for_floor(1))
end

local function current_level_from_xp(xp)
  local level = 1
  local safe_xp = math.max(0, tonumber(xp) or 0)
  for i = 2, #CFG.LEVEL_XP do
    if safe_xp >= CFG.LEVEL_XP[i] then
      level = i
    else
      break
    end
  end
  if level > CFG.PVE_MAX_LEVEL then level = CFG.PVE_MAX_LEVEL end
  return level
end

function normalize_mine_difficulty(v)
  local s = tostring(v or CFG.MINE_DIFFICULTY_DEFAULT)
  if s == "medium" or s == "hard" or s == "easy" then
    return s
  end
  return CFG.MINE_DIFFICULTY_DEFAULT
end

function mine_stat_multiplier(diff)
  -- Каталоги ботов разделены по сложности (catalog_easy / medium / hard); статы в JSON уже «готовые».
  -- Не умножаем повторно — иначе двойной скейл (см. eqip_stats.MD §3.6 вариант A).
  return 1.0
end

function mine_reward_multiplier(diff)
  local d = normalize_mine_difficulty(diff)
  if d == "medium" then return 1.5 end
  if d == "hard" then return 2.0 end
  return 1.0
end

function make_floor_state_key(diff, floor)
  return normalize_mine_difficulty(diff) .. ":" .. tostring(clamp_int(floor, 1, CFG.PVE_MAX_LEVEL))
end

local function truthy_match_param(v)
  if v == true then return true end
  if v == 1 then return true end
  if type(v) == "string" and string.lower(v) == "true" then return true end
  return false
end

local function new_stats()
  return {
    hp = CFG.MAX_HP,
    mana = 0,
    cross_cd = 0,
    square_cd = 0,
    petard_cd = 0,
    shield_cd = 0,
    fury_cd = 0,
    shield_t1 = 0,
    shield_t2 = 0,
    shield_t3 = 0,
    fury_active = false,
    max_hp = CFG.MAX_HP
  }
end

local function tick_cooldowns(stats)
  if stats.cross_cd > 0 then stats.cross_cd = stats.cross_cd - 1 end
  if stats.square_cd > 0 then stats.square_cd = stats.square_cd - 1 end
  if stats.petard_cd > 0 then stats.petard_cd = stats.petard_cd - 1 end
  if stats.shield_cd > 0 then stats.shield_cd = stats.shield_cd - 1 end
  if stats.fury_cd > 0 then stats.fury_cd = stats.fury_cd - 1 end
end

local function get_shield_stacks(st)
  local c = 0
  if (st.shield_t1 or 0) > 0 then c = c + 1 end
  if (st.shield_t2 or 0) > 0 then c = c + 1 end
  if (st.shield_t3 or 0) > 0 then c = c + 1 end
  return c
end

local function get_armor(st)
  local base = tonumber(st and st.base_armor) or 0
  return math.max(0, base) + get_shield_stacks(st) * CFG.SHIELD_ARMOR_PER_STACK
end

local function get_heal_bonus(st)
  local base = tonumber(st and st.base_heal) or 0
  return math.max(0, base) + get_shield_stacks(st) * CFG.SHIELD_HEAL_PER_STACK
end

local function count_skulls(board)
  local n = 0
  for y = CFG.ACTIVE_Y_MIN, CFG.HEIGHT - 1 do
    for x = 0, CFG.SIZE - 1 do
      if bget(board, x, y) == 4 then n = n + 1 end
    end
  end
  return n
end

local function roll_outgoing_damage(state, board, attacker, base_damage)
  local dmg = math.max(0, tonumber(base_damage) or 0)

  -- База урона с экипа/уровня: PvE и PvP Pro; в классическом PvP base_damage не задаётся.
  if attacker ~= nil then
    dmg = dmg + math.max(0, tonumber(attacker.base_damage) or 0)
  end

  -- Fury: add skulls as bonus damage.
  if attacker ~= nil and attacker.fury_active == true then
    dmg = dmg + count_skulls(board)
  end

  -- Crit chance: base crit (PVE) + fury crit (existing mechanic).
  local crit = false
  local baseCrit = attacker ~= nil and (tonumber(attacker.base_crit) or 0) or 0
  local furyCrit = attacker ~= nil and attacker.fury_active == true and CFG.FURY_CRIT_CHANCE or 0
  local critChance = math.max(0, baseCrit + furyCrit)
  if critChance > 0 and math.random() < critChance then
    dmg = dmg * 2
    crit = true
  end

  local affix = tostring((((state or {}).pve_run or {}).affix) or "")
  if affix == "bare_current" then
    dmg = dmg * 2
  end

  return dmg, crit
end

local function deal_damage(state, board, attacker, target, base_damage)
  local raw, crit = roll_outgoing_damage(state, board, attacker, base_damage)
  local reduced = math.max(0, raw - get_armor(target))
  target.hp = math.max(0, (target.hp or CFG.MAX_HP) - reduced)
  return crit
end

local function apply_shield_stack(st)
  st.shield_t1 = tonumber(st.shield_t1) or 0
  st.shield_t2 = tonumber(st.shield_t2) or 0
  st.shield_t3 = tonumber(st.shield_t3) or 0

  -- Add a stack up to 3.
  if st.shield_t1 <= 0 then st.shield_t1 = CFG.SHIELD_DURATION_TURNS
  elseif st.shield_t2 <= 0 then st.shield_t2 = CFG.SHIELD_DURATION_TURNS
  elseif st.shield_t3 <= 0 then st.shield_t3 = CFG.SHIELD_DURATION_TURNS
  end

  -- Refresh duration for ALL existing stacks on every cast.
  if st.shield_t1 > 0 then st.shield_t1 = CFG.SHIELD_DURATION_TURNS end
  if st.shield_t2 > 0 then st.shield_t2 = CFG.SHIELD_DURATION_TURNS end
  if st.shield_t3 > 0 then st.shield_t3 = CFG.SHIELD_DURATION_TURNS end
end

local function tick_buffs_end_turn(st)
  if (st.shield_t1 or 0) > 0 then st.shield_t1 = st.shield_t1 - 1 end
  if (st.shield_t2 or 0) > 0 then st.shield_t2 = st.shield_t2 - 1 end
  if (st.shield_t3 or 0) > 0 then st.shield_t3 = st.shield_t3 - 1 end
  st.fury_active = false
end

local function current_affix_id(state)
  local pve_run = state and state.pve_run or nil
  return tostring((pve_run and pve_run.affix) or "")
end

local function has_affix(state, affix_id)
  return current_affix_id(state) == tostring(affix_id or "")
end

local function base_action_mana_cost(action_type)
  if action_type == 2 then return CFG.CROSS_ABILITY_COST end
  if action_type == 3 then return CFG.SQUARE_ABILITY_COST end
  if action_type == 4 then return CFG.PETARD_ABILITY_COST end
  if action_type == 5 then return CFG.SHIELD_ABILITY_COST end
  if action_type == 6 then return CFG.FURY_ABILITY_COST end
  return 0
end

local function action_mana_cost(state, action_type)
  local base = base_action_mana_cost(action_type)
  if base <= 0 then return 0 end
  if has_affix(state, "frozen") then
    return base + CFG.FROZEN_ABILITY_COST_BONUS
  end
  return base
end

function mana_gain_per_object(state, base_gain)
  local g = math.max(0, tonumber(base_gain) or 0)
  if g <= 0 then return 0 end
  if has_affix(state, "bare_current") then return 0 end
  if has_affix(state, "overload") then return 1 end
  return g
end

function turn_seconds_for_state(state)
  if has_affix(state, "scree") then
    return math.max(1, math.floor(CFG.TURN_SECONDS / 3))
  end
  return CFG.TURN_SECONDS
end

-- extra_turn: true — после хода игрок получил доп. ход за 5+ в ряд (не считается отдельным «ходом» для stone_skin).
local function apply_turn_end_affix_effects(state, actor_id, opponent_id, extra_turn)
  if state == nil or state.mode ~= "pve" then return end
  if actor_id == nil or actor_id == "" then return end
  local actor = state.stats and state.stats[actor_id] or nil
  local opponent = state.stats and state.stats[opponent_id] or nil
  if actor == nil then return end
  extra_turn = extra_turn == true

  local affix = current_affix_id(state)

  if affix == "acid" then
    local init_hp = math.max(1, tonumber(actor.initial_hp) or tonumber(actor.max_hp) or CFG.MAX_HP)
    local loss = math.max(1, math.floor(init_hp * CFG.ACID_HP_LOSS_PCT + 0.5))
    actor.hp = math.max(0, (actor.hp or CFG.MAX_HP) - loss)
  end

  if affix == "regeneration" and actor_id == state.bot_user_id then
    local max_hp = math.max(1, tonumber(actor.max_hp) or CFG.MAX_HP)
    local gain = math.max(1, math.floor(max_hp * CFG.REGEN_HP_PCT + 0.5))
    actor.hp = math.min(max_hp, (actor.hp or max_hp) + gain)
  end

  -- Каменная кожа: +15 брони каждый 3-й полноценный ход бота (без учёта доп. хода за 5+ камней).
  if affix == "stone_skin" and actor_id == state.bot_user_id then
    if not extra_turn then
      state.affix_bot_turns = (tonumber(state.affix_bot_turns) or 0) + 1
      if (state.affix_bot_turns % 3) == 0 then
        actor.base_armor = math.max(0, tonumber(actor.base_armor) or 0) + 15
      end
    end
  end

end

local function would_create_match(board, x, y, t)
  -- Only enforce "no initial matches" inside active area.
  if not in_active_server(x, y) then return false end
  if x >= 2 and bget(board, x - 1, y) == t and bget(board, x - 2, y) == t then return true end
  if y >= (CFG.ACTIVE_Y_MIN + 2) and bget(board, x, y - 1) == t and bget(board, x, y - 2) == t then return true end
  return false
end

local function init_board()
  local board = {}
  for y = 0, CFG.HEIGHT - 1 do
    for x = 0, CFG.SIZE - 1 do
      local t = 1
      local tries = 0
      repeat
        t = CFG.SPAWN_POOL[math.random(1, #CFG.SPAWN_POOL)]
        tries = tries + 1
      until tries >= 20 or not would_create_match(board, x, y, t)
      bset(board, x, y, t)
    end
  end
  return board
end

local function init_cheat_rows()
  local out = {}
  -- Order for clients: yGhost=0 (top) => logical y=-2, then yGhost=1 => logical y=-1.
  for yGhost = 0, CFG.CHEAT_ROWS_COUNT - 1 do
    for x = 0, CFG.SIZE - 1 do
      out[#out + 1] = CFG.SPAWN_POOL[math.random(1, #CFG.SPAWN_POOL)]
    end
  end
  return out
end

-- Spawn preview implementation:
-- We maintain a per-column queue of upcoming pieces. Refill consumes from this queue,
-- and cheat_rows always mirrors the next 2 pieces for each column.
local function spawn_queue_min_len()
  -- A single refill can spawn up to CFG.SIZE new pieces in a column, so keep some buffer.
  return CFG.SIZE + CFG.CHEAT_ROWS_COUNT + 2
end

local function ensure_spawn_queue(state, x)
  if state.spawn_queues == nil then state.spawn_queues = {} end
  if state.spawn_queues[x] == nil then state.spawn_queues[x] = {} end
  local q = state.spawn_queues[x]
  local need = spawn_queue_min_len()
  while #q < need do
    q[#q + 1] = CFG.SPAWN_POOL[math.random(1, #CFG.SPAWN_POOL)]
  end
  return q
end

local function spawn_next_from_queue(state, x)
  local q = ensure_spawn_queue(state, x)
  local v = q[1]
  table.remove(q, 1)
  q[#q + 1] = CFG.SPAWN_POOL[math.random(1, #CFG.SPAWN_POOL)]
  return v
end

local function update_cheat_rows_from_queues(state)
  if state == nil then return end
  if state.cheat_rows == nil then state.cheat_rows = {} end
  local out = {}
  -- yGhost=0 => logical y=-2 (farthest), yGhost=1 => logical y=-1 (nearest to board)
  for yGhost = 0, CFG.CHEAT_ROWS_COUNT - 1 do
    for x = 0, CFG.SIZE - 1 do
      local q = ensure_spawn_queue(state, x)
      local idx1 = yGhost + 1
      out[#out + 1] = q[idx1] or CFG.SPAWN_POOL[math.random(1, #CFG.SPAWN_POOL)]
    end
  end
  state.cheat_rows = out
end

local function update_cheat_rows_from_board(state)
  if state == nil or state.board == nil then return end
  local out = {}
  -- Server top rows are y=0..1 (2 rows). We expose them as cheatRows in the same order:
  -- yGhost=0 corresponds to server y=0, yGhost=1 corresponds to server y=1.
  for y = 0, CFG.CHEAT_ROWS_COUNT - 1 do
    for x = 0, CFG.SIZE - 1 do
      out[#out + 1] = bget(state.board, x, y)
    end
  end
  state.cheat_rows = out
end

local function regenerate_active_board_without_matches(state)
  if state == nil or state.board == nil then return end
  for y = CFG.ACTIVE_Y_MIN, CFG.HEIGHT - 1 do
    for x = 0, CFG.SIZE - 1 do
      local t = 1
      local tries = 0
      repeat
        t = CFG.SPAWN_POOL[math.random(1, #CFG.SPAWN_POOL)]
        tries = tries + 1
      until tries >= 20 or not would_create_match(state.board, x, y, t)
      bset(state.board, x, y, t)
    end
  end
  update_cheat_rows_from_board(state)
end

local function do_swap(board, x1, y1, x2, y2)
  local t = bget(board, x1, y1)
  bset(board, x1, y1, bget(board, x2, y2))
  bset(board, x2, y2, t)
end

local function find_matches(board)
  local results = {}

  for y = CFG.ACTIVE_Y_MIN, CFG.HEIGHT - 1 do
    local x = 0
    while x < CFG.SIZE do
      local t = bget(board, x, y)
      if t == 0 then
        x = x + 1
      else
        local len = 1
        while x + len < CFG.SIZE and bget(board, x + len, y) == t do
          len = len + 1
        end
        if len >= 3 then
          local cells = {}
          for i = 0, len - 1 do cells[#cells + 1] = { x = x + i, y = y } end
          results[#results + 1] = { type = t, count = len, cells = cells }
        end
        x = x + len
      end
    end
  end

  for x = 0, CFG.SIZE - 1 do
    local y = CFG.ACTIVE_Y_MIN
    while y < CFG.HEIGHT do
      local t = bget(board, x, y)
      if t == 0 then
        y = y + 1
      else
        local len = 1
        while y + len < CFG.HEIGHT and bget(board, x, y + len) == t do
          len = len + 1
        end
        if len >= 3 then
          local cells = {}
          for i = 0, len - 1 do cells[#cells + 1] = { x = x, y = y + i } end
          results[#results + 1] = { type = t, count = len, cells = cells }
        end
        y = y + len
      end
    end
  end

  return results
end

local function clear_matches(board, matches)
  for _, m in ipairs(matches) do
    for _, c in ipairs(m.cells) do
      if in_active_server(c.x, c.y) then
        bset(board, c.x, c.y, 0)
      end
    end
  end
end

local function apply_gravity_and_refill(state)
  local board = state.board
  for x = 0, CFG.SIZE - 1 do
    local write_y = CFG.HEIGHT - 1
    for y = CFG.HEIGHT - 1, 0, -1 do
      local t = bget(board, x, y)
      if t ~= 0 then
        bset(board, x, write_y, t)
        if write_y ~= y then bset(board, x, y, 0) end
        write_y = write_y - 1
      end
    end
    for y = write_y, 0, -1 do
      bset(board, x, y, 0)
    end
  end

  for y = 0, CFG.HEIGHT - 1 do
    for x = 0, CFG.SIZE - 1 do
      if bget(board, x, y) == 0 then
        bset(board, x, y, spawn_next_from_queue(state, x))
      end
    end
  end

  -- cheatRows must always reflect the real top 2 rows of the 6x8 board.
  update_cheat_rows_from_board(state)

  return find_matches(board)
end

-- Simulation-only gravity/refill:
-- Make bot's "quality" simulation configurable by y_min.
-- Also avoid consuming spawn queues (otherwise bot "knows" upcoming pieces),
-- so refill uses RNG from SPAWN_POOL.
local function apply_gravity_and_refill_sim_ymin(state, y_min)
  local board = state.board
  local ymin = tonumber(y_min)
  if ymin == nil then ymin = CFG.ACTIVE_Y_MIN end
  if ymin < 0 then ymin = 0 end
  if ymin > CFG.HEIGHT - 1 then ymin = CFG.HEIGHT - 1 end

  for x = 0, CFG.SIZE - 1 do
    local write_y = CFG.HEIGHT - 1
    for y = CFG.HEIGHT - 1, ymin, -1 do
      local t = bget(board, x, y)
      if t ~= 0 then
        bset(board, x, write_y, t)
        if write_y ~= y then bset(board, x, y, 0) end
        write_y = write_y - 1
      end
    end
    for y = write_y, ymin, -1 do
      bset(board, x, y, 0)
    end
  end

  for y = ymin, CFG.HEIGHT - 1 do
    for x = 0, CFG.SIZE - 1 do
      if bget(board, x, y) == 0 then
        bset(board, x, y, CFG.SPAWN_POOL[math.random(1, #CFG.SPAWN_POOL)])
      end
    end
  end

  return find_matches(board)
end

local function try_swap(board, x1, y1, x2, y2)
  if not in_active_server(x1, y1) or not in_active_server(x2, y2) then return false, nil end
  if math.abs(x1 - x2) + math.abs(y1 - y2) ~= 1 then return false, nil end

  do_swap(board, x1, y1, x2, y2)
  local matches = find_matches(board)
  if #matches > 0 then return true, matches end

  do_swap(board, x1, y1, x2, y2)
  return false, nil
end

local function apply_ability(board, action_type, cx, cy)
  if action_type == 2 then
    for dx = -2, 2 do
      local nx = cx + dx
      if in_active_server(nx, cy) then bset(board, nx, cy, 0) end
    end
    for dy = -2, 2 do
      if dy ~= 0 then
        local ny = cy + dy
        if in_active_server(cx, ny) then bset(board, cx, ny, 0) end
      end
    end
  elseif action_type == 3 then
    for dy = -1, 1 do
      for dx = -1, 1 do
        local nx, ny = cx + dx, cy + dy
        if in_active_server(nx, ny) then bset(board, nx, ny, 0) end
      end
    end
  elseif action_type == 4 then
    -- Petard does not affect board cells.
  end
end

local function apply_match_effects(state, actor_id, opponent_id, matches, extra_turn)
  local actor = state.stats[actor_id]
  local opp = state.stats[opponent_id]
  local sim = state._sim_metrics
  local affix = current_affix_id(state)

  local healed = false
  local pending_heal = 0

  for _, m in ipairs(matches) do
    if m.count >= 5 then extra_turn = true end
    if sim ~= nil and m.count >= 5 then sim.extra_turn = true end
    if m.type == 1 or m.type == 2 or m.type == 3 then
      local gain = mana_gain_per_object(state, CFG.GEM_MANA[m.type] or 0) * m.count
      actor.mana = math.min(CFG.MAX_MANA, actor.mana + gain)
      if sim ~= nil then
        if m.type == 1 then sim.red = sim.red + m.count
        elseif m.type == 2 then sim.yellow = sim.yellow + m.count
        elseif m.type == 3 then sim.green = sim.green + m.count end
      end
    elseif m.type == 4 then
      if affix == "energy_block" then
        actor.base_damage = math.max(0, tonumber(actor.base_damage) or 0) + 5 * m.count
      else
        -- Весь урон от черепов за ход (все каскады) + база персонажа — один бросок в конце resolve_action.
        state._action_damage_flat = (state._action_damage_flat or 0) + CFG.SKULL_DAMAGE * m.count
      end
      if affix == "monster_rage" and actor_id == state.bot_user_id then
        state._monster_rage_bombs_action = (state._monster_rage_bombs_action or 0) + m.count
      end
    elseif m.type == 5 then
      healed = true
      pending_heal = pending_heal + CFG.ANKH_HEAL * m.count
      if affix == "mana_vampire" and opp ~= nil then
        local vamp_gain = mana_gain_per_object(state, 2) * m.count
        opp.mana = math.min(CFG.MAX_MANA, (tonumber(opp.mana) or 0) + vamp_gain)
      end
    end
  end

  if healed then
    pending_heal = pending_heal + get_heal_bonus(actor)
    actor.hp = math.min(actor.max_hp or CFG.MAX_HP, (actor.hp or CFG.MAX_HP) + pending_heal)
  end

  return extra_turn
end

local function other_player_id(state, uid)
  if #state.players_sorted < 2 then return nil end
  if state.players_sorted[1] == uid then return state.players_sorted[2] end
  return state.players_sorted[1]
end

local function make_sync_msg(state, action, extra_turn, anim_steps, cheatRowsForUser, tick)
  local a_id = state.players_sorted[1]
  local b_id = state.players_sorted[2]
  local a = state.stats[a_id]
  local b = state.stats[b_id]

  local function export_active_board()
    local out = {}
    for y = CFG.ACTIVE_Y_MIN, CFG.HEIGHT - 1 do
      for x = 0, CFG.SIZE - 1 do
        out[#out + 1] = bget(state.board, x, y)
      end
    end
    return out
  end

  local server_now_unix_ms = math.floor(os.time() * 1000)
  local turn_ends_at_unix_ms = 0
  if state.turn_deadline_paused then
    turn_ends_at_unix_ms = 0
  elseif state.started and not state.ended and tick ~= nil and state.turn_deadline_tick ~= nil then
    local rem_ticks = math.max(0, (state.turn_deadline_tick or 0) - tick)
    local rem_ms = math.floor((rem_ticks * 1000) / CFG.TICK_RATE)
    turn_ends_at_unix_ms = server_now_unix_ms + rem_ms
  end

  return {
    board = export_active_board(),
    cheatRows = cheatRowsForUser or {},
    aHp = a.hp,
    aMana = a.mana,
    aCrossCd = a.cross_cd,
    aSquareCd = a.square_cd,
    aPetardCd = a.petard_cd,
    aShieldCd = a.shield_cd or 0,
    aFuryCd = a.fury_cd or 0,
    aMaxHp = a.max_hp or CFG.MAX_HP,
    aBaseDamage = tonumber(a.base_damage) or 0,
    aBaseArmor = tonumber(a.base_armor) or 0,
    aBaseCrit = tonumber(a.base_crit) or 0,
    aBaseHeal = tonumber(a.base_heal) or 0,
    aShieldT1 = a.shield_t1 or 0,
    aShieldT2 = a.shield_t2 or 0,
    aShieldT3 = a.shield_t3 or 0,
    aFuryTurns = a.fury_active == true and 1 or 0,
    aFuryBonus = 0,
    bHp = b.hp,
    bMana = b.mana,
    bCrossCd = b.cross_cd,
    bSquareCd = b.square_cd,
    bPetardCd = b.petard_cd,
    bShieldCd = b.shield_cd or 0,
    bFuryCd = b.fury_cd or 0,
    bMaxHp = b.max_hp or CFG.MAX_HP,
    bBaseDamage = tonumber(b.base_damage) or 0,
    bBaseArmor = tonumber(b.base_armor) or 0,
    bBaseCrit = tonumber(b.base_crit) or 0,
    bBaseHeal = tonumber(b.base_heal) or 0,
    bShieldT1 = b.shield_t1 or 0,
    bShieldT2 = b.shield_t2 or 0,
    bShieldT3 = b.shield_t3 or 0,
    bFuryTurns = b.fury_active == true and 1 or 0,
    bFuryBonus = 0,
    extraTurn = extra_turn or false,
    activeUserId = state.active_user_id,
    serverNowUnixMs = server_now_unix_ms,
    turnEndsAtUnixMs = turn_ends_at_unix_ms,
    actionType = action and action.actionType or 0,
    fromX = action and action.fromX or -1,
    fromY = action and action.fromY or -1,
    toX = action and action.toX or -1,
    toY = action and action.toY or -1,
    abilityX = action and action.cx or -1,
    abilityY = action and action.cy or -1,
    critTriggered = state.last_crit == true,
    pveAffix = current_affix_id(state),
    animSteps = anim_steps or {},
  }
end

--- Арена (турнир 8 игроков): forward refs — реализации ниже после инвентаря.
local arena_mirror_commit
local arena_on_match_finished

local function broadcast_sync(dispatcher, state, action, extra_turn, anim_steps, tick)
  -- Синки без действия (старт матча, таймаут хода) не должны тащить critTriggered с прошлого хода.
  if action == nil then
    state.last_crit = false
  end

  arena_mirror_commit(state)

  local allowed_presences = {}
  local other_presences = {}
  if state.cheat_rows_allowed ~= nil then
    for uid, p in pairs(state.presences) do
      if state.cheat_rows_allowed[uid] == true then
        allowed_presences[#allowed_presences + 1] = p
      else
        other_presences[#other_presences + 1] = p
      end
    end
  end

  -- If we don't have any permission map (edge cases), fallback to "send to all".
  if state.cheat_rows_allowed == nil then
    local msg = make_sync_msg(state, action, extra_turn, anim_steps, {}, tick)
    dispatcher.broadcast_message(CFG.OP_BOARD_SYNC, nk.json_encode(msg), nil, nil)
    return
  end

  local msg_allowed = make_sync_msg(state, action, extra_turn, anim_steps, state.cheat_rows or {}, tick)
  local msg_other = make_sync_msg(state, action, extra_turn, anim_steps, {}, tick)

  if #allowed_presences > 0 then
    dispatcher.broadcast_message(CFG.OP_BOARD_SYNC, nk.json_encode(msg_allowed), allowed_presences, nil)
  end
  if #other_presences > 0 then
    dispatcher.broadcast_message(CFG.OP_BOARD_SYNC, nk.json_encode(msg_other), other_presences, nil)
  end
end

local function send_reject(dispatcher, presence, reason)
  local payload = nk.json_encode({ reason = reason or "invalid_action" })
  dispatcher.broadcast_message(CFG.OP_ACTION_REJECT, payload, { presence }, nil)
end

local function finish_turn_and_broadcast(dispatcher, state, action, extra_turn, keep_turn, tick, tick_rate, anim_steps)
  local actor = state.active_user_id
  local opponent = other_player_id(state, actor)
  local prev_active_user_id = state.active_user_id
  local action_type = action and tonumber(action.actionType) or 0

  if actor ~= nil and action ~= nil then
    local ask = Ach.map_action_to_stat(action.actionType)
    if ask ~= nil then
      Ach.inc_session(state, actor, ask, 1)
    end
  end

  Ach.snapshot_hp_was_exactly_one(state)

  if not keep_turn and actor ~= nil and opponent ~= nil then
    apply_turn_end_affix_effects(state, actor, opponent, extra_turn == true)
  end

  Ach.snapshot_hp_was_exactly_one(state)

  if state.stats[actor].hp <= 0 or state.stats[opponent].hp <= 0 then
    local winner = state.stats[actor].hp > 0 and actor or opponent
    state.ended = true
    broadcast_sync(dispatcher, state, action, extra_turn, anim_steps, tick)

    local game_over_payload = { winnerUserId = winner }
    -- Arena tournament meta for client UX (no history bracket, show modal on final win / any loss).
    if state.arena_mirror ~= nil then
      game_over_payload.arenaTournamentId = state.arena_mirror.tournament_id or ""
      game_over_payload.arenaRound = state.arena_mirror.round or ""
      game_over_payload.arenaBetTier = state.arena_mirror.bet_tier or ""
    end
    if state.mode == "pve" and winner == state.owner_user_id then
      if state.pve_run ~= nil and state.pve_run.arena_suppress_all == true then
        local progress = { level = 1, xp = 0 }
        if type(read_pve_progress) == "function" then
          progress = read_pve_progress(state.owner_user_id) or progress
        end
        state.last_reward = {
          reward_xp = 0,
          reward_gold = 0,
          reward_ore = 0,
          reward_matter = 0,
          reward_ingots = 0,
          level = progress.level or 1,
          xp = progress.xp or 0,
        }
      else
        state.last_reward = award_pve_victory(state.owner_user_id, state.bot_id, state.owner_session_epoch, state.pve_run)
      end
      game_over_payload.rewardXp = state.last_reward.reward_xp or 0
      game_over_payload.rewardGold = state.last_reward.reward_gold or 0
      game_over_payload.rewardOre = state.last_reward.reward_ore or 0
      game_over_payload.rewardMatter = state.last_reward.reward_matter or 0
      game_over_payload.rewardIngots = state.last_reward.reward_ingots or 0
      game_over_payload.rewardKeyId = state.last_reward.reward_key_id or ""
      game_over_payload.rewardKeyAmount = state.last_reward.reward_key_amount or 0
      game_over_payload.rewardBlueprint = state.last_reward.reward_blueprint or ""
      game_over_payload.rewardRecipeItemId = state.last_reward.reward_recipe_item_id or ""
      game_over_payload.rewardTesseract = state.last_reward.reward_tesseract or 0
      game_over_payload.newLevel = state.last_reward.level or 1
    elseif state.mode == "pve" and winner == state.bot_user_id then
      if state.pve_run ~= nil and state.pve_run.arena_suppress_all == true then
        local progress = { level = 1, xp = 0 }
        if type(read_pve_progress) == "function" then
          progress = read_pve_progress(state.owner_user_id) or progress
        end
        state.last_reward = {
          reward_xp = 0,
          reward_gold = 0,
          reward_ore = 0,
          reward_matter = 0,
          level = progress.level or 1,
          xp = progress.xp or 0,
        }
      else
        state.last_reward = award_pve_defeat(state.owner_user_id, state.owner_session_epoch)
      end
      game_over_payload.rewardXp = state.last_reward.reward_xp or 0
      game_over_payload.rewardGold = 0
      game_over_payload.rewardOre = 0
      game_over_payload.rewardMatter = 0
      game_over_payload.newLevel = state.last_reward.level or 1
    end

    -- Arena финал: показываем финальную награду в UI (сама выдача — в arena_tournament.lua).
    if game_over_payload.arenaRound == "final"
        and state.owner_user_id ~= nil and winner == state.owner_user_id then
      local bt = string.lower(tostring(game_over_payload.arenaBetTier or "green"))
      if bt == "blue" then
        game_over_payload.rewardGold = 1200
        game_over_payload.rewardOre = 1200
      elseif bt == "purple" then
        game_over_payload.rewardGold = 2400
        game_over_payload.rewardOre = 2400
      else
        game_over_payload.rewardGold = 600
        game_over_payload.rewardOre = 600
      end
    end
    -- Arena tournament hook must never break match loop.
    local ok_arena, err_arena = pcall(function()
      if type(arena_on_match_finished) == "function" then
        arena_on_match_finished(state, winner)
      end
    end)
    if not ok_arena then
      nk.logger_error("arena_on_match_finished failed: " .. tostring(err_arena))
    end
    local ok_achi, err_achi = pcall(function()
      Ach.flush_match_finish(state, winner, actor, opponent, action_type)
    end)
    if not ok_achi then
      nk.logger_error("achievement_flush_match_finish failed: " .. tostring(err_achi))
    end
    dispatcher.broadcast_message(CFG.OP_GAME_OVER, nk.json_encode(game_over_payload), nil, nil)
    return
  end

  if keep_turn then
    state.active_user_id = actor
    -- Ярость / Петарда / Щит: ход сохраняется, таймер хода не перезапускается.
    if action_type ~= 4 and action_type ~= 5 and action_type ~= 6 then
      state.turn_deadline_tick = tick + turn_seconds_for_state(state) * tick_rate
    end
  elseif extra_turn then
    state.active_user_id = actor
  else
    tick_buffs_end_turn(state.stats[actor])
    state.active_user_id = opponent
    tick_cooldowns(state.stats[opponent])
    if state.mode == "pve" and opponent == state.owner_user_id then
      state.bot_fury_open_mana = nil
    end
  end

  if not keep_turn then
    if extra_turn then
      -- Доп. ход (5+ в линии): полный таймер после анимаций, как при смене хода — не «съедаем» время каскадом.
      local need_ack = state.active_user_id ~= nil
      if need_ack then
        state.turn_deadline_paused = true
        state.turn_pause_started_tick = tick
        state.turn_deadline_tick = tick
      else
        state.turn_deadline_paused = false
        state.turn_deadline_tick = tick + turn_seconds_for_state(state) * tick_rate
      end
    else
      -- PVP и PVE: дедлайн стартует после OP 17 от клиента, отыгравшего анимации (активный игрок; в PVE при ходе бота — owner).
      local need_ack = state.active_user_id ~= nil
      if need_ack then
        state.turn_deadline_paused = true
        state.turn_pause_started_tick = tick
        state.turn_deadline_tick = tick
      else
        state.turn_deadline_paused = false
        state.turn_deadline_tick = tick + turn_seconds_for_state(state) * tick_rate
      end
    end
  end

  if state.mode == "pve" and state.active_user_id == state.bot_user_id and prev_active_user_id ~= nil then
    state.bot_long_think_next = (prev_active_user_id == state.owner_user_id)
  elseif state.mode == "pve" and state.active_user_id == state.owner_user_id then
    state.bot_long_think_next = false
  end

  if state.mode == "pve" and actor == state.bot_user_id and action ~= nil then
    if action.actionType == 6 and state._bot_pre_mana ~= nil and state._bot_pre_mana >= 80 then
      state.bot_fury_open_mana = state._bot_pre_mana
    elseif action.actionType == 4 and state.bot_fury_open_mana ~= nil and has_affix(state, "frozen") then
      state.bot_fury_open_mana = nil
    elseif (action.actionType == 2 or action.actionType == 3) and state.bot_fury_open_mana ~= nil then
      state.bot_fury_open_mana = nil
    end
  end

  if state.mode == "pve" then
    if state.active_user_id == state.bot_user_id then
      state.bot_turn_pending = true
      if state.turn_deadline_paused then
        state.bot_turn_ready_tick = 0
      else
        state.bot_turn_ready_tick = tick + (state.bot_long_think_next and CFG.BOT_THINK_TICKS or CFG.BOT_THINK_TICKS_FAST)
      end
    else
      state.bot_turn_pending = false
      state.bot_turn_ready_tick = 0
    end

    -- Instability: regenerate board only when player's new turn starts.
    if has_affix(state, "instability")
      and state.active_user_id == state.owner_user_id
      and prev_active_user_id ~= state.owner_user_id then
      regenerate_active_board_without_matches(state)
    end
  end
  broadcast_sync(dispatcher, state, action, extra_turn, anim_steps, tick)
end

local function clone_step(board, phase)
  -- Anim steps are sent in client 6x6 coordinates (active area only).
  local out = {}
  for y = CFG.ACTIVE_Y_MIN, CFG.HEIGHT - 1 do
    for x = 0, CFG.SIZE - 1 do
      out[#out + 1] = bget(board, x, y)
    end
  end
  return { phase = phase, board = out }
end

local function collect_ability_cells(action_type, cx, cy)
  local cells = {}
  local used = {}
  local function add_cell(x, y)
    if not in_active_server(x, y) then return end
    local k = tostring(x) .. ":" .. tostring(y)
    if used[k] then return end
    used[k] = true
    cells[#cells + 1] = { x = x, y = y }
  end

  if action_type == 2 then
    for dx = -2, 2 do add_cell(cx + dx, cy) end
    for dy = -2, 2 do add_cell(cx, cy + dy) end
  elseif action_type == 3 then
    for dy = -1, 1 do
      for dx = -1, 1 do add_cell(cx + dx, cy + dy) end
    end
  elseif action_type == 4 then
    add_cell(cx, cy)
  end
  return cells
end

local function apply_ability_rewards(state, actor_id, opponent_id, action_type, cx, cy)
  local actor = state.stats[actor_id]
  local opp = state.stats[opponent_id]
  local sim = state._sim_metrics
  local affix = current_affix_id(state)
  if action_type == 4 then
    local crit = deal_damage(state, state.board, actor, opp, CFG.PETARD_DAMAGE)
    return crit
  end

  local cells = collect_ability_cells(action_type, cx, cy)
  local skulls = 0
  local monster_rage_bombs = 0
  local healed = false
  local pending_heal = 0

  for _, c in ipairs(cells) do
    local t = bget(state.board, c.x, c.y)
    if t == 1 or t == 2 or t == 3 then
      actor.mana = math.min(CFG.MAX_MANA, actor.mana + mana_gain_per_object(state, CFG.GEM_MANA[t] or 0))
      if sim ~= nil then
        if t == 1 then sim.red = sim.red + 1
        elseif t == 2 then sim.yellow = sim.yellow + 1
        elseif t == 3 then sim.green = sim.green + 1 end
      end
    elseif t == 5 then
      healed = true
      pending_heal = pending_heal + CFG.ANKH_HEAL
      if affix == "mana_vampire" and opp ~= nil then
        local vamp_gain = mana_gain_per_object(state, 2)
        opp.mana = math.min(CFG.MAX_MANA, (tonumber(opp.mana) or 0) + vamp_gain)
      end
    elseif t == 4 then
      if affix == "energy_block" then
        actor.base_damage = math.max(0, tonumber(actor.base_damage) or 0) + 5
      else
        skulls = skulls + 1
      end
      if affix == "monster_rage" and actor_id == state.bot_user_id then
        monster_rage_bombs = monster_rage_bombs + 1
      end
    end
  end

  if healed then
    pending_heal = pending_heal + get_heal_bonus(actor)
    actor.hp = math.min(actor.max_hp or CFG.MAX_HP, (actor.hp or CFG.MAX_HP) + pending_heal)
  end

  -- Урон от способности + бомбы в зоне — в общий пул на ход; один deal_damage в конце resolve_action.
  state._action_damage_flat = (state._action_damage_flat or 0) + CFG.ABILITY_BASE_DAMAGE + CFG.SKULL_DAMAGE * skulls
  if affix == "monster_rage" and actor_id == state.bot_user_id and monster_rage_bombs > 0 then
    state._monster_rage_bombs_action = (state._monster_rage_bombs_action or 0) + monster_rage_bombs
  end
  return false
end

local function resolve_action(state, action, actor_id, opponent_id)
  state.last_crit = false
  state._action_damage_flat = 0
  state._monster_rage_bombs_action = 0
  local initial_matches = {}
  local anim_steps = {}
  local keep_turn = false
  local crit_triggered = false
  local five_plus_segments = 0
  local function tally_five_plus(matches)
    if matches == nil then return end
    for _, m in ipairs(matches) do
      local c = tonumber(m.count)
      if c ~= nil and c >= 5 then five_plus_segments = five_plus_segments + 1 end
    end
  end
  if action.actionType == 1 then
    local fy = client_to_server_y(action.fromY)
    local ty = client_to_server_y(action.toY)
    local ok, matches = try_swap(state.board, action.fromX, fy, action.toX, ty)
    if not ok then return false, "invalid_swap", false, false, anim_steps end
    initial_matches = matches or {}
  elseif action.actionType == 4 then
    if apply_ability_rewards(state, actor_id, opponent_id, action.actionType, -1, -1) then crit_triggered = true end
    keep_turn = true
    state.last_crit = crit_triggered
    return true, nil, false, true, anim_steps
  elseif action.actionType == 5 then
    apply_shield_stack(state.stats[actor_id])
    keep_turn = true
    state.last_crit = false
    return true, nil, false, true, anim_steps
  elseif action.actionType == 6 then
    state.stats[actor_id].fury_active = true
    keep_turn = true
    state.last_crit = false
    return true, nil, false, true, anim_steps
  else
    local sy = client_to_server_y(action.cy)
    if apply_ability_rewards(state, actor_id, opponent_id, action.actionType, action.cx, sy) then crit_triggered = true end
    apply_ability(state.board, action.actionType, action.cx, sy)
    anim_steps[#anim_steps + 1] = clone_step(state.board, 1)
    initial_matches = {}
  end

  local extra_turn = false

  if #initial_matches > 0 then
    tally_five_plus(initial_matches)
    extra_turn = apply_match_effects(state, actor_id, opponent_id, initial_matches, extra_turn)
    clear_matches(state.board, initial_matches)
    anim_steps[#anim_steps + 1] = clone_step(state.board, 1)
  end

  while true do
    local cascade = nil
    if state._sim_quality_y_min ~= nil then
      local ymin = tonumber(state._sim_quality_y_min)
      if ymin ~= nil and ymin > 0 then
        cascade = apply_gravity_and_refill_sim_ymin(state, ymin)
      else
        cascade = apply_gravity_and_refill(state)
      end
    else
      cascade = apply_gravity_and_refill(state)
    end
    anim_steps[#anim_steps + 1] = clone_step(state.board, 2)
    if #cascade == 0 then break end
    tally_five_plus(cascade)
    extra_turn = apply_match_effects(state, actor_id, opponent_id, cascade, extra_turn)
    clear_matches(state.board, cascade)
    anim_steps[#anim_steps + 1] = clone_step(state.board, 1)
  end

  -- Один расчёт урона за ход: сумма бомб (все каскады) + урон способности (если был); base_damage персонажа и крит — в roll_outgoing_damage.
  if (state._action_damage_flat or 0) > 0 then
    if deal_damage(state, state.board, state.stats[actor_id], state.stats[opponent_id], state._action_damage_flat) then
      crit_triggered = true
    end
  end
  state._action_damage_flat = 0
  local mrb = state._monster_rage_bombs_action or 0
  state._monster_rage_bombs_action = 0
  if has_affix(state, "monster_rage") and actor_id == state.bot_user_id and mrb > 0 then
    local actor = state.stats[actor_id]
    actor.base_damage = math.max(0, tonumber(actor.base_damage) or 0) + 3 * mrb
  end

  if five_plus_segments >= 2 then
    Ach.inc_session(state, actor_id, Ach.stat_key_dnn(), 1)
  end

  state.last_crit = crit_triggered
  return true, nil, extra_turn, keep_turn, anim_steps
end

local function count_present_players(state)
  local n = 0
  for _, _ in pairs(state.presences) do n = n + 1 end
  return n
end

--- Владелец PvE отключился: убрать зависание на turn_deadline_paused (ожидание OP 17 с клиента).
local function on_pve_owner_socket_gone(state, tick)
  if state.mode ~= "pve" or state.owner_user_id == nil or state.owner_user_id == "" then return end
  if state.turn_deadline_paused == true then
    state.turn_deadline_paused = false
    state.turn_deadline_tick = tick + turn_seconds_for_state(state) * CFG.TICK_RATE
    state.turn_pause_started_tick = tick
    if state.active_user_id == state.bot_user_id then
      state.bot_turn_pending = true
      state.bot_long_think_next = false
      state.bot_turn_ready_tick = tick + CFG.BOT_THINK_TICKS_FAST
    end
  end
end

local function parse_action(data)
  if not data or data == "" then return nil end
  local ok, action = pcall(nk.json_decode, data)
  if not ok or type(action) ~= "table" then return nil end
  action.actionType = tonumber(action.actionType) or 0
  action.fromX = tonumber(action.fromX) or -1
  action.fromY = tonumber(action.fromY) or -1
  action.toX = tonumber(action.toX) or -1
  action.toY = tonumber(action.toY) or -1
  action.cx = tonumber(action.cx) or -1
  action.cy = tonumber(action.cy) or -1
  return action
end

local function parse_selection(data)
  if not data or data == "" then return nil end
  local ok, msg = pcall(nk.json_decode, data)
  if not ok or type(msg) ~= "table" then return nil end
  local x = tonumber(msg.x)
  local y = tonumber(msg.y)
  local selected = msg.selected == true
  if x == nil or y == nil then return nil end
  return { x = x, y = y, selected = selected }
end

local function decode_storage_value(obj)
  if obj == nil then return nil end
  local v = obj.value
  if v == nil then v = obj.Value end
  if v == nil then return nil end
  if type(v) == "table" then return v end
  if type(v) == "string" then
    local ok, decoded = pcall(nk.json_decode, v)
    if not ok or decoded == nil then
      nk.logger_warn("decode_storage_value: invalid json in storage value")
      return nil
    end
    return decoded
  end
  return nil
end

-- ═══ Глобальная аномалия сервера (Storage → бафы PvE match3) ═══

local function storage_row_time_unix(row)
  if row == nil then return nil end
  local u = row.update_time or row.UpdateTime or row.create_time or row.CreateTime
  if u == nil then return nil end
  if type(u) == "number" then
    if u > 20000000000 then return math.floor(u / 1000) end
    if u > 1000000000000 then return math.floor(u / 1000000) end
    return math.floor(u)
  end
  if type(u) == "string" then
    local y, mo, d, h, mi, se = u:match("^(%d%d%d%d)%-(%d%d)%-(%d%d)[T ](%d%d):(%d%d):(%d%d)")
    if y then
      return os.time({
        year = tonumber(y), month = tonumber(mo), day = tonumber(d),
        hour = tonumber(h), min = tonumber(mi), sec = tonumber(se),
        isdst = false
      })
    end
  end
  return nil
end

local function compute_aura_expiry_unix(doc, row)
  if doc == nil then return nil end
  local e = tonumber(doc.ends_at_unix) or tonumber(doc.ends_at)
  if e ~= nil and e > 0 then return math.floor(e) end
  local dur_h = tonumber(doc.duration_hours)
  if dur_h == nil or dur_h <= 0 then return nil end
  local start_u = tonumber(doc.started_at_unix) or tonumber(doc.anchor_unix) or storage_row_time_unix(row) or os.time()
  return math.floor(start_u + dur_h * 3600 + 0.5)
end

local _server_aura_cache_doc = nil
local _server_aura_cache_expiry = nil
local _server_aura_cache_t = 0

local function get_active_server_aura()
  local now = os.time()
  local ttl = math.max(5, tonumber(CFG.SERVER_AURA_CACHE_TTL_SECONDS) or 30)
  if _server_aura_cache_doc ~= nil and (now - _server_aura_cache_t) < ttl then
    if _server_aura_cache_expiry ~= nil and now > _server_aura_cache_expiry then
      return nil
    end
    return _server_aura_cache_doc
  end

  local ok_read, rows = pcall(function()
    return nk.storage_read({
      {
        collection = CFG.SERVER_AURA_COLLECTION,
        key = CFG.SERVER_AURA_KEY,
        user_id = CFG.SERVER_AURA_STORAGE_USER_ID,
      },
    })
  end)
  _server_aura_cache_t = now
  _server_aura_cache_doc = nil
  _server_aura_cache_expiry = nil

  if not ok_read or rows == nil or #rows == 0 then
    return nil
  end

  local row = rows[1]
  local doc = decode_storage_value(row) or {}
  if doc.active == false or doc.enabled == false then
    return nil
  end

  local exp = compute_aura_expiry_unix(doc, row)
  if exp ~= nil and now > exp then
    return nil
  end

  _server_aura_cache_expiry = exp
  _server_aura_cache_doc = doc
  return doc
end

local function aura_xp_multiplier(aura)
  if aura == nil then return 1 end
  local p = tonumber(aura.xp_bonus_pct) or tonumber(aura.xp_pct) or 0
  return math.max(0, 1 + p / 100)
end

local function aura_apply_to_pve_reward_xp(reward_xp, aura)
  local m = aura_xp_multiplier(aura)
  if m == 1 then return reward_xp end
  return math.max(0, math.ceil((tonumber(reward_xp) or 0) * m))
end

--- mine_respawn_wait_pct: +50 → таймер короче в 2 раза (10→5 мин); −50 → длиннее (10→15 мин).
local function aura_mine_respawn_duration_seconds(base_seconds, aura)
  local b = math.max(1, math.floor(tonumber(base_seconds) or 600))
  if aura == nil then return b end
  local p = tonumber(aura.mine_respawn_wait_pct) or tonumber(aura.mine_respawn_pct) or 0
  if p == 0 then return b end
  local mult = 1 - p / 100
  mult = math.max(0.05, math.min(20, mult))
  return math.max(10, math.floor(b * mult + 0.5))
end

local function aura_apply_to_pve_player_stats(stats, aura)
  if stats == nil or aura == nil then return end
  local function pct_mul(p)
    p = tonumber(p) or 0
    return math.max(-0.95, 1 + p / 100)
  end
  local all_ex_crit = pct_mul(aura.all_stats_pct or aura.stats_bonus_pct)
  local m_hp = all_ex_crit * pct_mul(aura.hp_pct)
  local m_dmg = all_ex_crit * pct_mul(aura.damage_pct)
  local m_arm = all_ex_crit * pct_mul(aura.armor_pct)
  local m_heal = all_ex_crit * pct_mul(aura.healing_pct)
  local m_crit = pct_mul(aura.crit_pct)

  local max_hp = math.max(1, math.floor((tonumber(stats.max_hp) or CFG.MAX_HP) * m_hp + 0.5))
  local hp = math.floor((tonumber(stats.hp) or max_hp) * m_hp + 0.5)
  stats.max_hp = max_hp
  stats.hp = math.min(max_hp, math.max(1, hp))
  stats.base_damage = math.max(0, math.floor((tonumber(stats.base_damage) or 0) * m_dmg + 0.5))
  stats.base_armor = math.max(0, math.floor((tonumber(stats.base_armor) or 0) * m_arm + 0.5))
  stats.base_heal = math.floor((tonumber(stats.base_heal) or 0) * m_heal + 0.5)
  stats.base_crit = math.max(0, (tonumber(stats.base_crit) or 0) * m_crit)
  stats.initial_hp = stats.max_hp
end

local function duel_match3_server_aura_get(ctx, payload)
  local aura = get_active_server_aura()
  if aura == nil then
    return nk.json_encode({ ok = true, active = false })
  end
  local exp = _server_aura_cache_expiry
  return nk.json_encode({
    ok = true,
    active = true,
    title = tostring(aura.title or ""),
    description = tostring(aura.description or ""),
    endsAtUnix = exp or 0,
    allStatsPct = tonumber(aura.all_stats_pct) or tonumber(aura.stats_bonus_pct) or 0,
    critPct = tonumber(aura.crit_pct) or 0,
    hpPct = tonumber(aura.hp_pct) or 0,
    damagePct = tonumber(aura.damage_pct) or 0,
    armorPct = tonumber(aura.armor_pct) or 0,
    healingPct = tonumber(aura.healing_pct) or 0,
    xpBonusPct = tonumber(aura.xp_bonus_pct) or tonumber(aura.xp_pct) or 0,
    mineRespawnWaitPct = tonumber(aura.mine_respawn_wait_pct) or tonumber(aura.mine_respawn_pct) or 0,
    durationHours = tonumber(aura.duration_hours) or 0,
  })
end

-- Equipment slot order must match client enum EquipmentSlotId (0..7).
local EQUIP_ORDER = {
  "Helmet", "Shoulders", "Chest", "Gloves", "Legs", "Feet", "WeaponLeft", "WeaponRight",
}

-- Fallback, если в Storage нет записи или CFG.ITEM_DEFS_STORAGE_USER_ID пустой. Storage перекрывает эти id.
-- Полный образец — Server/nakama/data/duel_match3_item_catalog.example.json (kind / max_stack / tier / quality).
local ITEM_DEFS_FALLBACK = {
  helm_rusty = { kind = "equipment", slot = "Helmet", tier = 1, quality = "normal", hp = 30, armor = 10, healing = 10, crit_chance = 0.2 },
  sword_basic = { kind = "equipment", slot = "WeaponRight", tier = 1, quality = "normal", damage = 35 },
  boots_basic = { kind = "equipment", slot = "Feet", tier = 1, quality = "normal", armor = 2 },
  gloves_basic = { kind = "equipment", slot = "Gloves", tier = 1, quality = "normal", healing = 3, crit_chance = 0.3 },
  -- T1-only: craft_recipe_id = recipe_drop_{цвет}_{Slot} / recipe_gold_{Slot} (каталог v3).
  eq_t1_normal_helmet = { kind = "equipment", slot = "Helmet", tier = 1, quality = "normal", craft_recipe_id = "recipe_drop_green_Helmet", hp = 673, armor = 31, damage = 47, craft_ore = 350, craft_gold = 300, craft_ingot_def = "ingot_green", craft_ingot_n = 4, craft_tesseract_n = 0 },
  eq_t1_normal_shoulders = { kind = "equipment", slot = "Shoulders", tier = 1, quality = "normal", craft_recipe_id = "recipe_drop_green_Shoulders", hp = 368, armor = 69, craft_ore = 654, craft_gold = 492, craft_ingot_def = "ingot_green", craft_ingot_n = 25, craft_tesseract_n = 0 },
  eq_t1_normal_chest = { kind = "equipment", slot = "Chest", tier = 1, quality = "normal", craft_recipe_id = "recipe_drop_green_Chest", hp = 841, armor = 31, damage = 38, craft_ore = 388, craft_gold = 324, craft_ingot_def = "ingot_green", craft_ingot_n = 10, craft_tesseract_n = 0 },
  eq_t1_normal_gloves = { kind = "equipment", slot = "Gloves", tier = 1, quality = "normal", craft_recipe_id = "recipe_drop_green_Gloves", hp = 347, damage = 57, healing = 210, crit_chance = 0.006, craft_ore = 426, craft_gold = 348, craft_ingot_def = "ingot_green", craft_ingot_n = 13, craft_tesseract_n = 0 },
  eq_t1_normal_legs = { kind = "equipment", slot = "Legs", tier = 1, quality = "normal", craft_recipe_id = "recipe_drop_green_Legs", hp = 768, armor = 37, damage = 11, craft_ore = 578, craft_gold = 444, craft_ingot_def = "ingot_green", craft_ingot_n = 21, craft_tesseract_n = 0 },
  eq_t1_normal_feet = { kind = "equipment", slot = "Feet", tier = 1, quality = "normal", craft_recipe_id = "recipe_drop_green_Feet", hp = 368, armor = 69, craft_ore = 730, craft_gold = 540, craft_ingot_def = "ingot_green", craft_ingot_n = 28, craft_tesseract_n = 0 },
  eq_t1_normal_weapon_l = { kind = "equipment", slot = "WeaponLeft", tier = 1, quality = "normal", craft_recipe_id = "recipe_drop_green_WeaponLeft", hp = 400, armor = 40, damage = 88, craft_ore = 502, craft_gold = 396, craft_ingot_def = "ingot_green", craft_ingot_n = 17, craft_tesseract_n = 0 },
  eq_t1_normal_weapon_r = { kind = "equipment", slot = "WeaponRight", tier = 1, quality = "normal", craft_recipe_id = "recipe_drop_green_WeaponRight", hp = 368, armor = 25, damage = 189, craft_ore = 540, craft_gold = 420, craft_ingot_def = "ingot_green", craft_ingot_n = 19, craft_tesseract_n = 0 },
  eq_t1_rare_helmet = { kind = "equipment", slot = "Helmet", tier = 1, quality = "rare", craft_recipe_id = "recipe_drop_blue_Helmet", hp = 1556, armor = 69, damage = 105, craft_ore = 531, craft_gold = 455, craft_ingot_def = "ingot_blue", craft_ingot_n = 6, craft_tesseract_n = 0 },
  eq_t1_rare_shoulders = { kind = "equipment", slot = "Shoulders", tier = 1, quality = "rare", craft_recipe_id = "recipe_drop_blue_Shoulders", hp = 851, armor = 155, craft_ore = 992, craft_gold = 746, craft_ingot_def = "ingot_blue", craft_ingot_n = 38, craft_tesseract_n = 0 },
  eq_t1_rare_chest = { kind = "equipment", slot = "Chest", tier = 1, quality = "rare", craft_recipe_id = "recipe_drop_blue_Chest", hp = 1944, armor = 69, damage = 85, craft_ore = 588, craft_gold = 491, craft_ingot_def = "ingot_blue", craft_ingot_n = 15, craft_tesseract_n = 0 },
  eq_t1_rare_gloves = { kind = "equipment", slot = "Gloves", tier = 1, quality = "rare", craft_recipe_id = "recipe_drop_blue_Gloves", hp = 802, damage = 125, healing = 474, crit_chance = 0.006, craft_ore = 646, craft_gold = 528, craft_ingot_def = "ingot_blue", craft_ingot_n = 20, craft_tesseract_n = 0 },
  eq_t1_rare_legs = { kind = "equipment", slot = "Legs", tier = 1, quality = "rare", craft_recipe_id = "recipe_drop_blue_Legs", hp = 1774, armor = 81, damage = 25, craft_ore = 876, craft_gold = 673, craft_ingot_def = "ingot_blue", craft_ingot_n = 32, craft_tesseract_n = 0 },
  eq_t1_rare_feet = { kind = "equipment", slot = "Feet", tier = 1, quality = "rare", craft_recipe_id = "recipe_drop_blue_Feet", hp = 851, armor = 155, craft_ore = 1107, craft_gold = 819, craft_ingot_def = "ingot_blue", craft_ingot_n = 42, craft_tesseract_n = 0 },
  eq_t1_rare_weapon_l = { kind = "equipment", slot = "WeaponLeft", tier = 1, quality = "rare", craft_recipe_id = "recipe_drop_blue_WeaponLeft", hp = 924, armor = 90, damage = 195, craft_ore = 761, craft_gold = 600, craft_ingot_def = "ingot_blue", craft_ingot_n = 26, craft_tesseract_n = 0 },
  eq_t1_rare_weapon_r = { kind = "equipment", slot = "WeaponRight", tier = 1, quality = "rare", craft_recipe_id = "recipe_drop_blue_WeaponRight", hp = 851, armor = 56, damage = 421, craft_ore = 819, craft_gold = 637, craft_ingot_def = "ingot_blue", craft_ingot_n = 29, craft_tesseract_n = 0 },
  eq_t1_epic_helmet = { kind = "equipment", slot = "Helmet", tier = 1, quality = "epic", craft_recipe_id = "recipe_drop_purple_Helmet", hp = 2438, armor = 107, damage = 163, craft_ore = 739, craft_gold = 634, craft_ingot_def = "ingot_purple", craft_ingot_n = 8, craft_tesseract_n = 0 },
  eq_t1_epic_shoulders = { kind = "equipment", slot = "Shoulders", tier = 1, quality = "epic", craft_recipe_id = "recipe_drop_purple_Shoulders", hp = 1334, armor = 240, craft_ore = 1382, craft_gold = 1039, craft_ingot_def = "ingot_purple", craft_ingot_n = 53, craft_tesseract_n = 0 },
  eq_t1_epic_chest = { kind = "equipment", slot = "Chest", tier = 1, quality = "epic", craft_recipe_id = "recipe_drop_purple_Chest", hp = 3048, armor = 107, damage = 132, craft_ore = 820, craft_gold = 685, craft_ingot_def = "ingot_purple", craft_ingot_n = 21, craft_tesseract_n = 0 },
  eq_t1_epic_gloves = { kind = "equipment", slot = "Gloves", tier = 1, quality = "epic", craft_recipe_id = "recipe_drop_purple_Gloves", hp = 1257, damage = 194, healing = 737, crit_chance = 0.006, craft_ore = 900, craft_gold = 735, craft_ingot_def = "ingot_purple", craft_ingot_n = 27, craft_tesseract_n = 0 },
  eq_t1_epic_legs = { kind = "equipment", slot = "Legs", tier = 1, quality = "epic", craft_recipe_id = "recipe_drop_purple_Legs", hp = 2781, armor = 127, damage = 39, craft_ore = 1221, craft_gold = 938, craft_ingot_def = "ingot_purple", craft_ingot_n = 44, craft_tesseract_n = 0 },
  eq_t1_epic_feet = { kind = "equipment", slot = "Feet", tier = 1, quality = "epic", craft_recipe_id = "recipe_drop_purple_Feet", hp = 1333, armor = 240, craft_ore = 1542, craft_gold = 1141, craft_ingot_def = "ingot_purple", craft_ingot_n = 59, craft_tesseract_n = 0 },
  eq_t1_epic_weapon_l = { kind = "equipment", slot = "WeaponLeft", tier = 1, quality = "epic", craft_recipe_id = "recipe_drop_purple_WeaponLeft", hp = 1448, armor = 140, damage = 303, craft_ore = 1061, craft_gold = 837, craft_ingot_def = "ingot_purple", craft_ingot_n = 36, craft_tesseract_n = 0 },
  eq_t1_epic_weapon_r = { kind = "equipment", slot = "WeaponRight", tier = 1, quality = "epic", craft_recipe_id = "recipe_drop_purple_WeaponRight", hp = 1333, armor = 87, damage = 653, craft_ore = 1141, craft_gold = 887, craft_ingot_def = "ingot_purple", craft_ingot_n = 40, craft_tesseract_n = 0 },
  eq_t1_legendary_helmet = { kind = "equipment", slot = "Helmet", tier = 1, quality = "legendary", craft_recipe_id = "recipe_gold_Helmet", hp = 4970, armor = 215, damage = 327, craft_ore = 1225, craft_gold = 1050, craft_ingot_def = "", craft_ingot_n = 0, craft_tesseract_n = 1 },
  eq_t1_legendary_shoulders = { kind = "equipment", slot = "Shoulders", tier = 1, quality = "legendary", craft_recipe_id = "recipe_gold_Shoulders", hp = 2718, armor = 483, craft_ore = 2288, craft_gold = 1722, craft_ingot_def = "", craft_ingot_n = 0, craft_tesseract_n = 1 },
  eq_t1_legendary_chest = { kind = "equipment", slot = "Chest", tier = 1, quality = "legendary", craft_recipe_id = "recipe_gold_Chest", hp = 6213, armor = 214, damage = 265, craft_ore = 1357, craft_gold = 1134, craft_ingot_def = "", craft_ingot_n = 0, craft_tesseract_n = 1 },
  eq_t1_legendary_gloves = { kind = "equipment", slot = "Gloves", tier = 1, quality = "legendary", craft_recipe_id = "recipe_gold_Gloves", hp = 2563, damage = 389, healing = 1484, crit_chance = 0.006, craft_ore = 1490, craft_gold = 1218, craft_ingot_def = "", craft_ingot_n = 0, craft_tesseract_n = 1 },
  eq_t1_legendary_legs = { kind = "equipment", slot = "Legs", tier = 1, quality = "legendary", craft_recipe_id = "recipe_gold_Legs", hp = 5669, armor = 255, damage = 78, craft_ore = 2022, craft_gold = 1554, craft_ingot_def = "", craft_ingot_n = 0, craft_tesseract_n = 1 },
  eq_t1_legendary_feet = { kind = "equipment", slot = "Feet", tier = 1, quality = "legendary", craft_recipe_id = "recipe_gold_Feet", hp = 2718, armor = 483, craft_ore = 2555, craft_gold = 1890, craft_ingot_def = "", craft_ingot_n = 0, craft_tesseract_n = 1 },
  eq_t1_legendary_weapon_l = { kind = "equipment", slot = "WeaponLeft", tier = 1, quality = "legendary", craft_recipe_id = "recipe_gold_WeaponLeft", hp = 2951, armor = 282, damage = 608, craft_ore = 1757, craft_gold = 1386, craft_ingot_def = "", craft_ingot_n = 0, craft_tesseract_n = 1 },
  eq_t1_legendary_weapon_r = { kind = "equipment", slot = "WeaponRight", tier = 1, quality = "legendary", craft_recipe_id = "recipe_gold_WeaponRight", hp = 2718, armor = 174, damage = 1309, craft_ore = 1890, craft_gold = 1470, craft_ingot_def = "", craft_ingot_n = 0, craft_tesseract_n = 1 },
  eq_t2_normal_helmet = { kind = "equipment", slot = "Helmet", tier = 2, quality = "normal", craft_recipe_id = "recipe_drop_t2_green_Helmet", hp = 800, armor = 65 },
  eq_t3_normal_helmet = { kind = "equipment", slot = "Helmet", tier = 3, quality = "normal", craft_recipe_id = "recipe_drop_t3_green_Helmet", hp = 1120, armor = 92 },
  eq_t2_legendary_helmet = { kind = "equipment", slot = "Helmet", tier = 2, quality = "legendary", hp = 2600, armor = 280 },
  -- Ресурсы / рецепты (не экипируются)
  ingot_green = { kind = "material", tier = 1, quality = "normal", max_stack = 100 },
  ingot_blue = { kind = "material", tier = 1, quality = "rare", max_stack = 100 },
  ingot_purple = { kind = "material", tier = 1, quality = "epic", max_stack = 100 },
  tesseract = { kind = "tesseract", tier = 1, quality = "legendary", max_stack = 5 },
  recipe_t2_green_Helmet = { kind = "recipe", tier = 2, quality = "normal", max_stack = 1, recipe_slot = "Helmet", recipe_target_slot = "Helmet" },
  recipe_t3_green_Helmet = { kind = "recipe", tier = 3, quality = "normal", max_stack = 1, recipe_slot = "Helmet", recipe_target_slot = "Helmet" },
  recipe_green = { kind = "recipe", tier = 1, quality = "normal", max_stack = 1, recipe_slot = "Helmet" },
  recipe_blue = { kind = "recipe", tier = 1, quality = "rare", max_stack = 1, recipe_slot = "Helmet" },
  recipe_purple = { kind = "recipe", tier = 1, quality = "epic", max_stack = 1, recipe_slot = "Helmet" },
  recipe_gold = { kind = "recipe", tier = 1, quality = "legendary", max_stack = 1, recipe_slot = "Helmet" },
}

-- §4.3: по одному def_id на (цвет × слот); пулы A/B различаются набором слотов (без пересечения id).
do
  local slots_a = { "Helmet", "Chest", "Gloves", "WeaponLeft" }
  local slots_b = { "WeaponRight", "Legs", "Shoulders", "Feet" }
  local colq = { green = "normal", blue = "rare", purple = "epic" }
  for cname, qual in pairs(colq) do
    for _, s in ipairs(slots_a) do
      local id = "recipe_drop_" .. cname .. "_" .. s
      ITEM_DEFS_FALLBACK[id] = { kind = "recipe", tier = 1, quality = qual, max_stack = 1, recipe_slot = s, recipe_target_slot = s }
    end
    for _, s in ipairs(slots_b) do
      local id = "recipe_drop_" .. cname .. "_" .. s
      ITEM_DEFS_FALLBACK[id] = { kind = "recipe", tier = 1, quality = qual, max_stack = 1, recipe_slot = s, recipe_target_slot = s }
    end
  end
end

-- Каталог v3: recipe_gold_{Slot} (без тира) для craft_recipe_id легендарки.
do
  local slots_gold = { "Helmet", "Chest", "Gloves", "WeaponLeft", "WeaponRight", "Legs", "Shoulders", "Feet" }
  for _, s in ipairs(slots_gold) do
    local id = "recipe_gold_" .. s
    ITEM_DEFS_FALLBACK[id] = { kind = "recipe", tier = 1, quality = "legendary", max_stack = 1, recipe_slot = s, recipe_target_slot = s }
  end
end

-- §4.3: recipe_drop_t{тир_шахты}_{цвет}_{Slot} и золотые recipe_gold_t{тир}_{Slot} (совпадает с duel_match3_item_catalog.example.json).
do
  local slots_all = { "Helmet", "Chest", "Gloves", "WeaponLeft", "WeaponRight", "Legs", "Shoulders", "Feet" }
  local colq = { green = "normal", blue = "rare", purple = "epic" }
  for t = 1, 3 do
    for cname, qual in pairs(colq) do
      for _, s in ipairs(slots_all) do
        local id = "recipe_drop_t" .. t .. "_" .. cname .. "_" .. s
        ITEM_DEFS_FALLBACK[id] = { kind = "recipe", tier = t, quality = qual, max_stack = 1, recipe_slot = s }
      end
    end
  end
  for t = 1, 3 do
    for _, s in ipairs(slots_all) do
      local id = "recipe_gold_t" .. t .. "_" .. s
      ITEM_DEFS_FALLBACK[id] = { kind = "recipe", tier = t, quality = "legendary", max_stack = 1, recipe_slot = s }
    end
  end
end

local ITEM_DEFS_CACHE_TTL_SEC = 30
local _item_defs_merged_cache = nil
local _item_defs_merged_cache_at = 0

local function normalize_stored_item_def(def)
  if type(def) ~= "table" then return nil end
  local kind = tostring(def.kind or "")
  if kind == "" then kind = "equipment" end
  local tier = clamp_int(tonumber(def.tier) or 1, 1, 3)
  local quality = tostring(def.quality or "normal")
  if quality == "" then quality = "normal" end
  local recipe_slot = tostring(def.recipe_slot or def.recipe_target_slot or "")

  if kind == "recipe" or kind == "material" or kind == "tesseract" then
    local max_stack = tonumber(def.max_stack)
    if max_stack == nil then
      if kind == "material" then max_stack = 100 else max_stack = 1 end
    end
    max_stack = math.max(1, math.floor(max_stack))
    return {
      kind = kind,
      slot = "",
      max_stack = max_stack,
      tier = tier,
      quality = quality,
      recipe_slot = recipe_slot,
      craft_recipe_id = "",
      hp = 0,
      damage = 0,
      armor = 0,
      crit_chance = 0.0,
      healing = 0,
    }
  end

  local slot = tostring(def.slot or "")
  if slot == "" then return nil end
  local out = {
    kind = "equipment",
    slot = slot,
    max_stack = 1,
    tier = tier,
    quality = quality,
    recipe_slot = "",
    craft_recipe_id = tostring(def.craft_recipe_id or ""),
    hp = tonumber(def.hp) or 0,
    damage = tonumber(def.damage) or 0,
    armor = tonumber(def.armor) or 0,
    crit_chance = tonumber(def.crit_chance) or 0.0,
    healing = tonumber(def.healing) or 0,
  }
  if def.craft_ore ~= nil then out.craft_ore = math.max(0, math.floor(tonumber(def.craft_ore) or 0)) end
  if def.craft_gold ~= nil then out.craft_gold = math.max(0, math.floor(tonumber(def.craft_gold) or 0)) end
  if def.craft_ingot_n ~= nil then out.craft_ingot_n = math.max(0, math.floor(tonumber(def.craft_ingot_n) or 0)) end
  if def.craft_tesseract_n ~= nil then out.craft_tesseract_n = math.max(0, math.floor(tonumber(def.craft_tesseract_n) or 0)) end
  if def.craft_ingot_def ~= nil and tostring(def.craft_ingot_def) ~= "" then
    out.craft_ingot_def = tostring(def.craft_ingot_def)
  end
  return out
end

local function item_def_is_equipment(def)
  if type(def) ~= "table" then return false end
  if tostring(def.kind or "equipment") ~= "equipment" then return false end
  return tostring(def.slot or "") ~= ""
end

local function item_def_is_recipe(def)
  if type(def) ~= "table" then return false end
  return tostring(def.kind or "") == "recipe"
end

local function item_max_stack(def)
  if type(def) ~= "table" then return 1 end
  local ms = tonumber(def.max_stack)
  if ms == nil or ms < 1 then return 1 end
  return math.floor(ms)
end

local function ensure_sheet_inventory_counts(sheet)
  if type(sheet) ~= "table" then return end
  local inv = sheet.inventory
  if type(inv) ~= "table" then return end
  local ic = sheet.inventory_counts
  if type(ic) ~= "table" then ic = {} end
  for i = 1, 25 do
    local id = inv[i] or ""
    if type(id) ~= "string" then id = tostring(id) end
    local c = tonumber(ic[i])
    if c == nil then
      if id ~= "" then c = 1 else c = 0 end
    else
      c = math.floor(c)
    end
    if id == "" then
      ic[i] = 0
    else
      if c < 1 then c = 1 end
      ic[i] = c
    end
  end
  sheet.inventory_counts = ic
end

local function read_item_defs_from_storage()
  local uid = CFG.ITEM_DEFS_STORAGE_USER_ID
  if uid == nil or uid == "" then return nil end
  local ok, rows = pcall(function()
    return nk.storage_read({
      {
        collection = CFG.ITEM_DEFS_COLLECTION,
        key = CFG.ITEM_DEFS_KEY,
        user_id = uid,
      },
    })
  end)
  if not ok or rows == nil or #rows == 0 then return nil end
  local val = decode_storage_value(rows[1]) or {}
  local items = val.items
  if type(items) ~= "table" then return nil end
  local out = {}
  for id, def in pairs(items) do
    if type(id) == "string" then
      local n = normalize_stored_item_def(def)
      if n ~= nil then out[id] = n end
    end
  end
  if next(out) == nil then return nil end
  return out
end

-- Единый каталог для валидации экипировки и суммирования статов: fallback + Storage (Storage перекрывает совпадающие id).
local function get_merged_item_defs()
  local now = os.time()
  if _item_defs_merged_cache ~= nil and (now - _item_defs_merged_cache_at) < ITEM_DEFS_CACHE_TTL_SEC then
    return _item_defs_merged_cache
  end
  local merged = {}
  for k, v in pairs(ITEM_DEFS_FALLBACK) do
    merged[k] = v
  end
  local from_st = read_item_defs_from_storage()
  if from_st ~= nil then
    for k, v in pairs(from_st) do
      merged[k] = v
    end
  end
  _item_defs_merged_cache = merged
  _item_defs_merged_cache_at = now
  return merged
end

local BOTS_CACHE_TTL_SEC = 30
local _bots_merged_by_diff = {}
local _bots_merged_at_by_diff = {}

local function normalize_stored_bot(id_key, def)
  if type(def) ~= "table" then return nil end
  local floor = clamp_int(def.floor ~= nil and def.floor or def.difficulty, 1, CFG.PVE_MAX_LEVEL)
  local fallback = build_floor_bot_entry(floor)
  local bid = mine_bot_id_for_floor(floor)
  local name = tostring(def.name or fallback.name or "")
  if name == "" then return nil end
  local is_boss = def.is_boss == true or is_boss_floor(floor)
  local reward_ore = tonumber(def.reward_ore)
  if reward_ore == nil then reward_ore = tonumber(fallback.reward_ore) or 0 end
  local reward_matter_min = tonumber(def.reward_matter_min)
  if reward_matter_min == nil then reward_matter_min = tonumber(fallback.reward_matter_min) or 0 end
  local reward_matter_max = tonumber(def.reward_matter_max)
  if reward_matter_max == nil then reward_matter_max = tonumber(fallback.reward_matter_max) or reward_matter_min end
  local reward_blueprint = tostring(def.reward_blueprint or fallback.reward_blueprint or "")
  local reward_ingots = tonumber(def.reward_ingots)
  if reward_ingots == nil then reward_ingots = tonumber(fallback.reward_ingots) or 0 end
  local reward_tesseract_chance = tonumber(def.reward_tesseract_chance)
  if reward_tesseract_chance == nil then reward_tesseract_chance = tonumber(fallback.reward_tesseract_chance) or 0 end
  local reward_key_id = tostring(def.reward_key_id or fallback.reward_key_id or "")
  local reward_key_amount = tonumber(def.reward_key_amount)
  if reward_key_amount == nil then reward_key_amount = tonumber(fallback.reward_key_amount) or 0 end
  return {
    id = bid,
    name = name,
    difficulty = floor,
    floor = floor,
    is_boss = is_boss,
    hp_bonus = tonumber(def.hp_bonus) or tonumber(fallback.hp_bonus) or 0,
    start_mana = tonumber(def.start_mana) or tonumber(fallback.start_mana) or 0,
    ai_ability_chance = tonumber(def.ai_ability_chance) or tonumber(fallback.ai_ability_chance) or 0,
    petard_bias = tonumber(def.petard_bias) or tonumber(fallback.petard_bias) or 0,
    cross_bias = tonumber(def.cross_bias) or tonumber(fallback.cross_bias) or 0,
    square_bias = tonumber(def.square_bias) or tonumber(fallback.square_bias) or 0,
    reward_xp = tonumber(def.reward_xp) or tonumber(fallback.reward_xp) or 0,
    reward_gold = tonumber(def.reward_gold) or tonumber(fallback.reward_gold) or 0,
    reward_ore = reward_ore,
    reward_matter_min = reward_matter_min,
    reward_matter_max = reward_matter_max,
    reward_blueprint = reward_blueprint,
    reward_ingots = reward_ingots,
    reward_tesseract_chance = reward_tesseract_chance,
    reward_key_id = reward_key_id,
    reward_key_amount = reward_key_amount,
    base_damage = tonumber(def.base_damage) or tonumber(def.damage) or tonumber(fallback.base_damage) or 0,
    base_armor = tonumber(def.base_armor) or tonumber(def.armor) or tonumber(fallback.base_armor) or 0,
    base_crit = tonumber(def.base_crit) or tonumber(def.crit_chance) or tonumber(fallback.base_crit) or 0,
    base_heal = tonumber(def.base_heal) or tonumber(def.healing) or tonumber(fallback.base_heal) or 0,
    cost_attack = PveMineCost.normalize(def.cost_attack, fallback.cost_attack or PveMineCost.DEFAULT_ATTACK),
    cost_banish = PveMineCost.normalize(def.cost_banish, fallback.cost_banish or PveMineCost.DEFAULT_BANISH),
  }
end

local function read_bots_from_storage_key(storage_key)
  local uid = CFG.BOTS_STORAGE_USER_ID
  if uid == nil or uid == "" then return nil end
  if storage_key == nil or storage_key == "" then return nil end
  local ok, rows = pcall(function()
    return nk.storage_read({
      {
        collection = CFG.BOTS_COLLECTION,
        key = storage_key,
        user_id = uid,
      },
    })
  end)
  if not ok or rows == nil or #rows == 0 then return nil end
  local val = decode_storage_value(rows[1]) or {}
  local bots = val.bots
  if type(bots) ~= "table" then return nil end
  local out = {}
  for id, def in pairs(bots) do
    if type(id) == "string" then
      local n = normalize_stored_bot(id, def)
      if n ~= nil then out[n.id] = n end
    end
  end
  if next(out) == nil then return nil end
  return out
end

-- Fallback + legacy catalog (BOTS_KEY) + каталог по сложности (BOTS_KEYS_BY_DIFFICULTY): поздние слои перекрывают id.
local function get_merged_bots(difficulty)
  local diff = normalize_mine_difficulty(difficulty or CFG.MINE_DIFFICULTY_DEFAULT)
  local now = os.time()
  local cached = _bots_merged_by_diff[diff]
  local cached_at = _bots_merged_at_by_diff[diff]
  if cached ~= nil and cached_at ~= nil and (now - cached_at) < BOTS_CACHE_TTL_SEC then
    return cached
  end
  local merged = {}
  for k, v in pairs(BOTS_FALLBACK) do
    merged[k] = v
  end
  local from_legacy = read_bots_from_storage_key(CFG.BOTS_KEY)
  if from_legacy ~= nil then
    for kid, bot in pairs(from_legacy) do
      merged[kid] = bot
    end
  end
  local diff_key = CFG.BOTS_KEYS_BY_DIFFICULTY and CFG.BOTS_KEYS_BY_DIFFICULTY[diff] or nil
  if diff_key ~= nil and diff_key ~= "" then
    local from_diff = read_bots_from_storage_key(diff_key)
    if from_diff ~= nil then
      for kid, bot in pairs(from_diff) do
        merged[kid] = bot
      end
    end
  end
  _bots_merged_by_diff[diff] = merged
  _bots_merged_at_by_diff[diff] = now
  return merged
end

local function get_bot_profile(bot_id, difficulty_opt)
  local diff = normalize_mine_difficulty(difficulty_opt or CFG.MINE_DIFFICULTY_DEFAULT)
  local bots = get_merged_bots(diff)
  local fallback_id = mine_bot_id_for_floor(1)
  return bots[bot_id] or bots[fallback_id]
end

local function normalize_character_sheet(val)
  val = val or {}
  local eq = val.equipment
  if type(eq) ~= "table" then eq = {} end
  for i = 1, 8 do
    if eq[i] == nil or eq[i] == false then eq[i] = "" end
    if type(eq[i]) ~= "string" then eq[i] = tostring(eq[i]) end
  end
  local inv = val.inventory
  if type(inv) ~= "table" then inv = {} end
  for i = 1, 25 do
    if inv[i] == nil or inv[i] == false then inv[i] = "" end
    if type(inv[i]) ~= "string" then inv[i] = tostring(inv[i]) end
  end
  local ic_in = val.inventory_counts
  if type(ic_in) ~= "table" then ic_in = {} end
  local inventory_counts = {}
  for i = 1, 25 do
    local id = inv[i] or ""
    local c = tonumber(ic_in[i])
    if c == nil then
      if id ~= "" then c = 1 else c = 0 end
    else
      c = math.floor(c)
    end
    if id == "" then
      inventory_counts[i] = 0
    else
      if c < 1 then c = 1 end
      inventory_counts[i] = c
    end
  end
  local lr_in = val.learned_recipes
  if type(lr_in) ~= "table" then lr_in = {} end
  local learned_recipes = {}
  for i = 1, #lr_in do
    local id = lr_in[i]
    if type(id) == "string" and id ~= "" then
      learned_recipes[#learned_recipes + 1] = id
    end
  end
  local ws_in = val.workshop_slots
  if type(ws_in) ~= "table" then ws_in = {} end
  local workshop_slots = {}
  for i = 1, 8 do
    local wi = ws_in[i]
    if type(wi) == "table" then
      local oid = tostring(wi.output_def_id or "")
      local ea = tonumber(wi.ends_at) or 0
      if oid == "" then ea = 0 end
      workshop_slots[i] = { output_def_id = oid, ends_at = ea }
    else
      workshop_slots[i] = { output_def_id = "", ends_at = 0 }
    end
  end
  return {
    equipment = eq,
    inventory = inv,
    inventory_counts = inventory_counts,
    learned_recipes = learned_recipes,
    workshop_slots = workshop_slots,
  }
end

local function read_character_sheet(user_id)
  if user_id == nil or user_id == "" then
    return normalize_character_sheet({})
  end
  local ok, rows = pcall(function()
    return nk.storage_read({
      {
        collection = CFG.CHARACTER_SHEET_COLLECTION,
        key = CFG.CHARACTER_SHEET_KEY,
        user_id = user_id,
      },
    })
  end)
  if not ok or rows == nil or #rows == 0 then
    return normalize_character_sheet({})
  end
  local val = decode_storage_value(rows[1]) or {}
  return normalize_character_sheet(val)
end

local function write_character_sheet(user_id, sheet)
  nk.storage_write({
    {
      collection = CFG.CHARACTER_SHEET_COLLECTION,
      key = CFG.CHARACTER_SHEET_KEY,
      user_id = user_id,
      value = {
        equipment = sheet.equipment,
        inventory = sheet.inventory,
        inventory_counts = sheet.inventory_counts or {},
        learned_recipes = sheet.learned_recipes or {},
        workshop_slots = sheet.workshop_slots or {},
        updated_at = os.time(),
      },
      permission_read = 1,
      permission_write = 0,
    },
  })
end

--- Создаёт пустой лист в Storage, если записи ещё нет (как duel_match3_stats / progress при первой игре).
local function ensure_character_sheet_initialized(user_id)
  if user_id == nil or user_id == "" then
    return
  end
  local ok, rows = pcall(function()
    return nk.storage_read({
      {
        collection = CFG.CHARACTER_SHEET_COLLECTION,
        key = CFG.CHARACTER_SHEET_KEY,
        user_id = user_id,
      },
    })
  end)
  if not ok or rows == nil or #rows == 0 then
    write_character_sheet(user_id, normalize_character_sheet({}))
  end
end

--- 8/8 экипировано, один tier и одно quality: ×1.25 к hp/damage/armor/healing с вещей (см. gen_item_catalog.js SET_BONUS_EQ).
--- Крит с экипа не умножается (там же mergedFromSlotStats: crit = база + сумма с перчаток).
local SET_BONUS_FULL_EQUIP_STATS = 1.25

local function equipment_full_set_stat_multiplier(sheet)
  if sheet == nil or type(sheet.equipment) ~= "table" then
    return 1.0
  end
  local defs = get_merged_item_defs()
  local first_tier, first_q = nil, nil
  for i = 1, 8 do
    local def_id = sheet.equipment[i]
    if def_id == nil or def_id == "" then
      return 1.0
    end
    local d = defs[def_id]
    if d == nil or not item_def_is_equipment(d) then
      return 1.0
    end
    local t = clamp_int(tonumber(d.tier) or 1, 1, 99)
    local q = tostring(d.quality or "normal")
    if first_tier == nil then
      first_tier, first_q = t, q
    elseif t ~= first_tier or q ~= first_q then
      return 1.0
    end
  end
  return SET_BONUS_FULL_EQUIP_STATS
end

local function sum_equipment_bonuses(sheet)
  local defs = get_merged_item_defs()
  local hp = 0
  local damage = 0
  local armor = 0
  local crit = 0.0
  local healing = 0
  local eq = sheet.equipment
  for i = 1, 8 do
    local def_id = eq[i]
    if def_id ~= nil and def_id ~= "" then
      local d = defs[def_id]
      if d ~= nil and item_def_is_equipment(d) then
        hp = hp + (tonumber(d.hp) or 0)
        damage = damage + (tonumber(d.damage) or 0)
        armor = armor + (tonumber(d.armor) or 0)
        crit = crit + (tonumber(d.crit_chance) or 0.0)
        healing = healing + (tonumber(d.healing) or 0)
      end
    end
  end
  local m = equipment_full_set_stat_multiplier(sheet)
  if m > 1.0 then
    hp = hp * m
    damage = damage * m
    armor = armor * m
    healing = healing * m
  end
  return {
    hp = hp,
    damage = damage,
    armor = armor,
    crit_chance = crit,
    healing = healing,
  }
end

local function merge_stats_with_equipment(base_stats, bonus)
  return {
    hp = (base_stats.hp or 0) + (bonus.hp or 0),
    damage = (base_stats.damage or 0) + (bonus.damage or 0),
    armor = (base_stats.armor or 0) + (bonus.armor or 0),
    crit_chance = (base_stats.crit_chance or 0) + (bonus.crit_chance or 0),
    healing = (base_stats.healing or 0) + (bonus.healing or 0),
  }
end

local function character_sheet_payload_arrays(sheet)
  ensure_sheet_inventory_counts(sheet)
  local eq = {}
  local inv = {}
  local invc = {}
  for i = 1, 8 do eq[i] = sheet.equipment[i] or "" end
  for i = 1, 25 do
    inv[i] = sheet.inventory[i] or ""
    local c = tonumber(sheet.inventory_counts[i]) or 0
    if inv[i] == nil or inv[i] == "" then c = 0 end
    invc[i] = c
  end
  return eq, inv, invc
end

local function learned_recipes_payload(sheet)
  local lr = sheet.learned_recipes
  if type(lr) ~= "table" then return {} end
  local out = {}
  for i = 1, #lr do out[i] = lr[i] end
  return out
end

local function ensure_sheet_workshop(sheet)
  if sheet == nil then return end
  if type(sheet.workshop_slots) ~= "table" then sheet.workshop_slots = {} end
  for i = 1, 8 do
    if type(sheet.workshop_slots[i]) ~= "table" then
      sheet.workshop_slots[i] = { output_def_id = "", ends_at = 0 }
    else
      local oid = tostring(sheet.workshop_slots[i].output_def_id or "")
      local ea = tonumber(sheet.workshop_slots[i].ends_at) or 0
      if oid == "" then ea = 0 end
      sheet.workshop_slots[i].output_def_id = oid
      sheet.workshop_slots[i].ends_at = ea
    end
  end
end

local function workshop_payload_arrays(sheet)
  ensure_sheet_workshop(sheet)
  local o, e = {}, {}
  for i = 1, 8 do
    o[i] = sheet.workshop_slots[i].output_def_id or ""
    e[i] = tonumber(sheet.workshop_slots[i].ends_at) or 0
  end
  return o, e
end

--- Сумма трёх цветов слитков в сундуке (§4.4); для HUD вместо устаревшего progress.ingots.
local function sum_ingots_in_inventory_sheet(sheet)
  if sheet == nil then return 0 end
  ensure_sheet_inventory_counts(sheet)
  local inv = sheet.inventory
  local ic = sheet.inventory_counts
  local sum = 0
  for i = 1, 25 do
    local id = inv[i] or ""
    if id == "ingot_green" or id == "ingot_blue" or id == "ingot_purple" then
      sum = sum + math.max(0, math.floor(tonumber(ic[i]) or 0))
    end
  end
  return sum
end

local function build_resource_payload(progress, user_id)
  local energy_max = pve_energy_max_for_user(user_id)
  local miner_key = math.max(0, tonumber(progress.key_items and progress.key_items.miner_key) or 0)
  local dark_key = math.max(0, tonumber(progress.key_items and progress.key_items.dark_key) or 0)
  -- Итог по ключам для HUD: раньше в progress.keys писалось отдельно и не синхронизировалось с key_items.
  local keys_total = miner_key + dark_key
  return {
    energy = clamp_int(progress.energy, 0, energy_max),
    energy_max = energy_max,
    ore = math.max(0, tonumber(progress.ore) or 0),
    gold = math.max(0, tonumber(progress.gold) or 0),
    ingots = math.max(0, tonumber(progress.ingots) or 0),
    matter = math.max(0, tonumber(progress.matter) or 0),
    keys = keys_total,
    blueprint_green = math.max(0, tonumber(progress.blueprint_green) or 0),
    blueprint_blue = math.max(0, tonumber(progress.blueprint_blue) or 0),
    blueprint_purple = math.max(0, tonumber(progress.blueprint_purple) or 0),
    blueprint_gold = math.max(0, tonumber(progress.blueprint_gold) or 0),
    tesseracts = math.max(0, tonumber(progress.tesseracts) or 0),
    miner_key = miner_key,
    dark_key = dark_key,
  }
end

local function build_progression_payload(progress, user_id, sheet_opt)
  local resources = build_resource_payload(progress, user_id)
  local ingots = resources.ingots
  if sheet_opt ~= nil then
    local invSum = sum_ingots_in_inventory_sheet(sheet_opt)
    if invSum > 0 then
      ingots = invSum
    end
  end
  return {
    level = progress.level or 1,
    xp = progress.xp or 0,
    gold = resources.gold,
    max_level = CFG.PVE_MAX_LEVEL,
    energy = resources.energy,
    energy_max = resources.energy_max,
    ore = resources.ore,
    ingots = ingots,
    matter = resources.matter,
    keys = resources.keys,
    -- Клиент (Unity JsonUtility) ожидает key_items; без этого барьер показывает 0 ключей при наличии miner_key.
    key_items = {
      miner_key = resources.miner_key,
      dark_key = resources.dark_key,
    },
    mine = progress.mine,
  }
end

--- progression + слитки из сундука, если лист персонажа уже загружен.
local function build_progression_payload_auto(progress, user_id)
  if user_id == nil or user_id == "" then
    return build_progression_payload(progress, user_id, nil)
  end
  local sheet = read_character_sheet(user_id)
  return build_progression_payload(progress, user_id, sheet)
end

--- Единый JSON ответ duel_character_* (инвентарь + мастерская).
local function encode_character_ok_response(sheet, progress, user_id)
  ensure_sheet_inventory_counts(sheet)
  local level = clamp_int(progress.level or 1, 1, CFG.PVE_MAX_LEVEL)
  local base_stats = character_stats_base_for_level(level)
  local bonus = sum_equipment_bonuses(sheet)
  local stats = merge_stats_with_equipment(base_stats, bonus)
  local eq_arr, inv_arr, inv_cnt_arr = character_sheet_payload_arrays(sheet)
  local wo, we = workshop_payload_arrays(sheet)
  local function payload_with_progression(prog_payload)
    return {
      ok = true,
      progression = prog_payload,
      stats = stats,
      equipment_def_ids = eq_arr,
      inventory_def_ids = inv_arr,
      inventory_counts = inv_cnt_arr,
      learned_recipe_ids = learned_recipes_payload(sheet),
      workshop_output_def_ids = wo,
      workshop_ends_at = we,
    }
  end
  local full_prog = build_progression_payload(progress, user_id, sheet)
  local ok_enc, encoded = pcall(nk.json_encode, payload_with_progression(full_prog))
  if ok_enc then return encoded end
  nk.logger_error("encode_character_ok_response json_encode: " .. tostring(encoded))
  local slim_prog = {}
  for k, v in pairs(full_prog) do
    if k ~= "mine" then slim_prog[k] = v end
  end
  local ok2, enc2 = pcall(nk.json_encode, payload_with_progression(slim_prog))
  if ok2 then return enc2 end
  nk.logger_error("encode_character_ok_response slim json_encode: " .. tostring(enc2))
  return nk.json_encode({ ok = false, err = "encode_failed" })
end

local function sheet_has_learned(sheet, recipe_id)
  if recipe_id == nil or recipe_id == "" then return false end
  local lr = sheet.learned_recipes or {}
  for i = 1, #lr do
    if lr[i] == recipe_id then return true end
  end
  return false
end

--- learned в sheet = def_id свитка (часто recipe_drop_t1_*); в каталоге экипа T1 craft_recipe_id = recipe_drop_{цвет}_{Slot} без t1.
--- Проверяем обе формы в обе стороны.
local function sheet_has_learned_for_craft(sheet, craft_recipe_id)
  if sheet_has_learned(sheet, craft_recipe_id) then return true end
  if craft_recipe_id == nil or craft_recipe_id == "" then return false end
  local sid = tostring(craft_recipe_id)
  -- Каталог: recipe_drop_t1_* → в learned может быть recipe_drop_{цвет}_* (старая схема)
  local g = string.match(sid, "^recipe_drop_t1_green_(.+)$")
  if g ~= nil and sheet_has_learned(sheet, "recipe_drop_green_" .. g) then return true end
  local b = string.match(sid, "^recipe_drop_t1_blue_(.+)$")
  if b ~= nil and sheet_has_learned(sheet, "recipe_drop_blue_" .. b) then return true end
  local p = string.match(sid, "^recipe_drop_t1_purple_(.+)$")
  if p ~= nil and sheet_has_learned(sheet, "recipe_drop_purple_" .. p) then return true end
  -- Каталог T1: recipe_drop_green_* → learned после изучения свитка: recipe_drop_t1_green_*
  local g2 = string.match(sid, "^recipe_drop_green_(.+)$")
  if g2 ~= nil and sheet_has_learned(sheet, "recipe_drop_t1_green_" .. g2) then return true end
  local b2 = string.match(sid, "^recipe_drop_blue_(.+)$")
  if b2 ~= nil and sheet_has_learned(sheet, "recipe_drop_t1_blue_" .. b2) then return true end
  local p2 = string.match(sid, "^recipe_drop_purple_(.+)$")
  if p2 ~= nil and sheet_has_learned(sheet, "recipe_drop_t1_purple_" .. p2) then return true end
  if sid == "recipe_drop_t2_green_Helmet" and sheet_has_learned(sheet, "recipe_t2_green_Helmet") then return true end
  if sid == "recipe_drop_t3_green_Helmet" and sheet_has_learned(sheet, "recipe_t3_green_Helmet") then return true end
  return false
end

local function item_def_is_legendary_quality(def)
  return def ~= nil and tostring(def.quality or "") == "legendary"
end

--- Легенда нужного тира и слота (§4.2: поглощение для зелёного T2/T3).
local function workshop_legend_fodder_matches(def, slot_name, legend_tier)
  if def == nil or not item_def_is_equipment(def) or not item_def_is_legendary_quality(def) then
    return false
  end
  if clamp_int(tonumber(def.tier) or 0, 1, 3) ~= legend_tier then return false end
  if tostring(def.slot or "") ~= slot_name then return false end
  return true
end

local function workshop_has_legendary_fodder(sheet, defs, slot_index, legend_tier)
  local slot_name = EQUIP_ORDER[slot_index + 1]
  local eq_i = slot_index + 1
  local eq_id = sheet.equipment[eq_i] or ""
  if eq_id ~= "" and workshop_legend_fodder_matches(defs[eq_id], slot_name, legend_tier) then
    return true
  end
  for inv_i = 1, 25 do
    local id = sheet.inventory[inv_i] or ""
    if id ~= "" then
      local cnt = tonumber(sheet.inventory_counts[inv_i]) or 0
      if cnt >= 1 and workshop_legend_fodder_matches(defs[id], slot_name, legend_tier) then
        return true
      end
    end
  end
  return false
end

local function workshop_consume_legendary_fodder(sheet, defs, slot_index, legend_tier)
  local slot_name = EQUIP_ORDER[slot_index + 1]
  local eq_i = slot_index + 1
  local eq_id = sheet.equipment[eq_i] or ""
  if eq_id ~= "" and workshop_legend_fodder_matches(defs[eq_id], slot_name, legend_tier) then
    sheet.equipment[eq_i] = ""
    return true
  end
  for inv_i = 1, 25 do
    local id = sheet.inventory[inv_i] or ""
    if id ~= "" then
      local d = defs[id]
      local cnt = tonumber(sheet.inventory_counts[inv_i]) or 0
      if cnt >= 1 and workshop_legend_fodder_matches(d, slot_name, legend_tier) then
        cnt = cnt - 1
        if cnt <= 0 then
          sheet.inventory[inv_i] = ""
          sheet.inventory_counts[inv_i] = 0
        else
          sheet.inventory_counts[inv_i] = cnt
        end
        return true
      end
    end
  end
  return false
end

--- Поглощение при апгрейде качества внутри тира: rare ← normal, epic ← rare, legendary ← epic.
local function workshop_fodder_matches_tier_slot_quality(def, slot_name, tier, quality_str)
  if def == nil or not item_def_is_equipment(def) then return false end
  if tostring(def.slot or "") ~= slot_name then return false end
  if clamp_int(tonumber(def.tier) or 0, 1, 3) ~= tier then return false end
  return tostring(def.quality or "") == quality_str
end

local function workshop_has_quality_fodder(sheet, defs, slot_index, tier, quality_str)
  local slot_name = EQUIP_ORDER[slot_index + 1]
  local eq_i = slot_index + 1
  local eq_id = sheet.equipment[eq_i] or ""
  if eq_id ~= "" and workshop_fodder_matches_tier_slot_quality(defs[eq_id], slot_name, tier, quality_str) then
    return true
  end
  for inv_i = 1, 25 do
    local id = sheet.inventory[inv_i] or ""
    if id ~= "" then
      local cnt = tonumber(sheet.inventory_counts[inv_i]) or 0
      if cnt >= 1 and workshop_fodder_matches_tier_slot_quality(defs[id], slot_name, tier, quality_str) then
        return true
      end
    end
  end
  return false
end

local function workshop_consume_quality_fodder(sheet, defs, slot_index, tier, quality_str)
  local slot_name = EQUIP_ORDER[slot_index + 1]
  local eq_i = slot_index + 1
  local eq_id = sheet.equipment[eq_i] or ""
  if eq_id ~= "" and workshop_fodder_matches_tier_slot_quality(defs[eq_id], slot_name, tier, quality_str) then
    sheet.equipment[eq_i] = ""
    return true
  end
  for inv_i = 1, 25 do
    local id = sheet.inventory[inv_i] or ""
    if id ~= "" then
      local d = defs[id]
      local cnt = tonumber(sheet.inventory_counts[inv_i]) or 0
      if cnt >= 1 and workshop_fodder_matches_tier_slot_quality(d, slot_name, tier, quality_str) then
        cnt = cnt - 1
        if cnt <= 0 then
          sheet.inventory[inv_i] = ""
          sheet.inventory_counts[inv_i] = 0
        else
          sheet.inventory_counts[inv_i] = cnt
        end
        return true
      end
    end
  end
  return false
end

--- Стоимость из каталога (craft_*) или масштаб от WORKSHOP_T*_NORMAL_COST.
local function workshop_craft_cost_from_def(out_def, tier, quality)
  local ore = tonumber(out_def.craft_ore)
  local gold = tonumber(out_def.craft_gold)
  local ingot_n = tonumber(out_def.craft_ingot_n)
  local tess = tonumber(out_def.craft_tesseract_n)
  local ingot_def = tostring(out_def.craft_ingot_def or "")
  if ore ~= nil and gold ~= nil and ingot_n ~= nil then
    return {
      ore = math.max(0, math.floor(ore)),
      gold = math.max(0, math.floor(gold)),
      ingot_def = ingot_def,
      ingot_n = math.max(0, math.floor(ingot_n)),
      tesseract_n = math.max(0, math.floor(tonumber(tess) or 0)),
    }
  end
  local base = CFG.WORKSHOP_T1_NORMAL_COST
  if tier == 2 then base = CFG.WORKSHOP_T2_NORMAL_COST end
  if tier == 3 then base = CFG.WORKSHOP_T3_NORMAL_COST end
  local qm = 1.0
  if quality == "rare" then qm = 1.45 end
  if quality == "epic" then qm = 1.95 end
  if quality == "legendary" then qm = 2.6 end
  local idef = "ingot_green"
  if quality == "rare" then idef = "ingot_blue" end
  if quality == "epic" then idef = "ingot_purple" end
  if quality == "legendary" then idef = "" end
  local in_n = 0
  if quality ~= "legendary" then
    in_n = math.max(1, math.ceil((tonumber(base.ingot_n) or 0) * qm))
  end
  return {
    ore = math.max(0, math.ceil((tonumber(base.ore) or 0) * qm)),
    gold = math.max(0, math.ceil((tonumber(base.gold) or 0) * qm)),
    ingot_def = idef,
    ingot_n = in_n,
    tesseract_n = quality == "legendary" and 1 or 0,
  }
end

local function inventory_count_def(sheet, def_id)
  local n = 0
  for i = 1, 25 do
    if sheet.inventory[i] == def_id then
      n = n + (tonumber(sheet.inventory_counts[i]) or 0)
    end
  end
  return n
end

local function inventory_try_add(sheet, def_id, amount)
  amount = math.floor(tonumber(amount) or 0)
  if amount < 1 then return true end
  local defs = get_merged_item_defs()
  local def = defs[def_id]
  if def == nil then return false, "unknown_item" end
  local max_s = item_max_stack(def)
  for i = 1, 25 do
    if sheet.inventory[i] == def_id then
      local c = tonumber(sheet.inventory_counts[i]) or 0
      if c > 0 and c < max_s then
        local space = max_s - c
        local add = math.min(space, amount)
        sheet.inventory_counts[i] = c + add
        amount = amount - add
        if amount <= 0 then return true end
      end
    end
  end
  for i = 1, 25 do
    local id = sheet.inventory[i] or ""
    if id == "" then
      local add = math.min(max_s, amount)
      sheet.inventory[i] = def_id
      sheet.inventory_counts[i] = add
      amount = amount - add
      if amount <= 0 then return true end
    end
  end
  return false, "inventory_full"
end

--- Цвет рецепта с монстров шахты: по **сложности** тира (easy → green, medium → blue, hard → purple).
local function mine_recipe_color_for_difficulty(diff)
  local d = normalize_mine_difficulty(diff)
  if d == "medium" then return "blue" end
  if d == "hard" then return "purple" end
  return "green"
end

local function mine_tier_from_diff(diff)
  local d = normalize_mine_difficulty(diff)
  if d == "medium" then return 2 end
  if d == "hard" then return 3 end
  return 1
end

--- Тип слитка с награды — по **сложности** шахты, не по этажу.
local function ingot_def_for_difficulty(diff)
  local d = normalize_mine_difficulty(diff)
  if d == "medium" then return "ingot_blue" end
  if d == "hard" then return "ingot_purple" end
  return "ingot_green"
end

--- §4.4: шанс выпадения слитка с обычного монстра (босс 4/8/12 — 100%).
local function ingot_drop_chance_non_boss(floor)
  local f = clamp_int(floor, 1, CFG.PVE_MAX_LEVEL)
  local r = f % 4
  if r == 1 then return 0.25 end
  if r == 2 then return 0.5 end
  if r == 3 then return 0.75 end
  return 1.0
end

--- id предмета-рецепта: recipe_drop_t{тир шахты}_{цвет}_{Slot} — см. mine_recipe_item_id_for_floor_index.

--- Этажи с дропом рецепта (только не-боссы): 10 нет — иначе пересечение с боссом 4/8/12; боссы рецептов не дают.
local MINE_RECIPE_DROP_FLOORS = { 1, 2, 3, 5, 6, 7, 9, 11 }

local function mine_recipe_drop_chance_for_floor(floor)
  local f = clamp_int(floor, 1, CFG.PVE_MAX_LEVEL)
  for i = 1, #MINE_RECIPE_DROP_FLOORS do
    if MINE_RECIPE_DROP_FLOORS[i] == f then
      return (50 - 5 * (i - 1)) / 100
    end
  end
  return 0
end

local function mine_floor_uses_mine_item_recipe_drop(floor, is_boss)
  if is_boss then return false end
  return mine_recipe_drop_chance_for_floor(floor) > 0
end

--- Один рецепт на этаж: индексы 1..8 совпадают с MINE_RECIPE_DROP_FLOORS (порядок: pool A, затем B).
local MINE_RECIPE_FLOOR_SLOT_ORDER = {
  "Helmet", "Chest", "Gloves", "WeaponLeft", "WeaponRight", "Legs", "Shoulders", "Feet",
}

--- def_id свитка для этажа с дропом рецепта или пусто, если этаж вне списка.
local function mine_recipe_item_id_for_floor_index(floor, color, mine_tier)
  local idx = nil
  for i = 1, #MINE_RECIPE_DROP_FLOORS do
    if MINE_RECIPE_DROP_FLOORS[i] == floor then
      idx = i
      break
    end
  end
  if idx == nil or MINE_RECIPE_FLOOR_SLOT_ORDER[idx] == nil then
    return ""
  end
  local c = color
  if c ~= "green" and c ~= "blue" and c ~= "purple" then
    c = "green"
  end
  local t = clamp_int(tonumber(mine_tier) or 1, 1, 3)
  local slot = MINE_RECIPE_FLOOR_SLOT_ORDER[idx]
  return "recipe_drop_t" .. tostring(t) .. "_" .. c .. "_" .. slot
end

--- Возвращает: успех, def_id выданного рецепта (или пусто) — для клиентского game over. Золотые из шахты не выдаём.
--- Один слот/этаж: этажи 1,2,3,5,6,7,9,11 — ровно один из восьми слотов экипа на монстра.
local function grant_mine_recipe_drop_v43(sheet, floor, is_boss, diff)
  if not mine_floor_uses_mine_item_recipe_drop(floor, is_boss) then return false, "" end
  local p = mine_recipe_drop_chance_for_floor(floor)
  if p <= 0 or math.random() > p then return false, "" end
  local color = mine_recipe_color_for_difficulty(diff)
  local mine_tier = mine_tier_from_diff(diff)
  local pick = mine_recipe_item_id_for_floor_index(floor, color, mine_tier)
  if pick == nil or pick == "" then return false, "" end
  if sheet_has_learned_for_craft(sheet, pick) then return false, "" end
  if inventory_try_add(sheet, pick, 1) == true then
    return true, pick
  end
  return false, ""
end

local function inventory_remove_def_total(sheet, def_id, amount)
  amount = math.floor(tonumber(amount) or 0)
  if amount < 1 then return true end
  if inventory_count_def(sheet, def_id) < amount then
    return false, "not_enough_items"
  end
  for i = 1, 25 do
    if amount <= 0 then break end
    if sheet.inventory[i] == def_id then
      local c = tonumber(sheet.inventory_counts[i]) or 0
      if c > 0 then
        local take = math.min(c, amount)
        c = c - take
        amount = amount - take
        if c <= 0 then
          sheet.inventory[i] = ""
          sheet.inventory_counts[i] = 0
        else
          sheet.inventory_counts[i] = c
        end
      end
    end
  end
  return amount <= 0
end

local function inventory_can_fit(sheet, def_id, amount)
  amount = math.floor(tonumber(amount) or 0)
  if amount < 1 then return true end
  local defs = get_merged_item_defs()
  local def = defs[def_id]
  if def == nil then return false end
  local max_s = item_max_stack(def)
  local space = 0
  for i = 1, 25 do
    local id = sheet.inventory[i] or ""
    if id == "" then
      space = space + max_s
    elseif id == def_id then
      local c = tonumber(sheet.inventory_counts[i]) or 0
      if c < max_s then space = space + (max_s - c) end
    end
  end
  return space >= amount
end

local function read_cheat_whitelist_emails_for_user_id(storage_user_id)
  -- Fallback for local dev / empty storage.
  local emails = {}
  for i = 1, #CFG.DEFAULT_CHEAT_EMAILS do
    emails[#emails + 1] = CFG.DEFAULT_CHEAT_EMAILS[i]
  end

  if storage_user_id == nil or storage_user_id == "" then
    return emails
  end

  local ok, rows = pcall(function()
    return nk.storage_read({
      {
        collection = CFG.CHEAT_WHITELIST_COLLECTION,
        key = CFG.CHEAT_WHITELIST_KEY,
        user_id = storage_user_id,
      },
    })
  end)
  if not ok or rows == nil or #rows == 0 then
    return emails
  end

  local row = rows[1]
  local val = decode_storage_value(row) or {}
  local fromValue = nil

  if type(val) == "table" then
    -- Support both { emails = [...] } and [...] shapes.
    if type(val.emails) == "table" then
      fromValue = val.emails
    else
      fromValue = val
    end
  elseif type(val) == "string" then
    local ok2, p = pcall(nk.json_decode, val)
    if ok2 and type(p) == "table" then
      if type(p.emails) == "table" then fromValue = p.emails else fromValue = p end
    end
  end

  if type(fromValue) == "table" and #fromValue > 0 then
    local newEmails = {}
    for _, e in ipairs(fromValue) do
      if type(e) == "string" and e ~= "" then
        newEmails[#newEmails + 1] = string.lower(e)
      end
    end
    if #newEmails > 0 then return newEmails end
  end

  return emails
end

local function build_cheat_whitelist_set(candidate_user_ids)
  local set = {}
  local function absorb(list)
    if type(list) ~= "table" then return end
    for _, e in ipairs(list) do
      if type(e) == "string" and e ~= "" then
        set[string.lower(e)] = true
      end
    end
  end

  -- 1) Try global bucket first (if it exists).
  local global_list = read_cheat_whitelist_emails_for_user_id(CFG.CHEAT_WHITELIST_USER_ID)
  absorb(global_list)

  -- 2) Also try under any real player user_id (Console commonly writes under user context).
  if type(candidate_user_ids) == "table" then
    for _, uid in ipairs(candidate_user_ids) do
      local list = read_cheat_whitelist_emails_for_user_id(uid)
      absorb(list)
    end
  end

  -- 3) Always include defaults so the feature still works without storage.
  absorb(CFG.DEFAULT_CHEAT_EMAILS)

  return set
end

local function user_email_lower(user_id)
  if user_id == nil or user_id == "" then return nil end
  if string.sub(user_id, 1, #CFG.BOT_USER_ID_PREFIX) == CFG.BOT_USER_ID_PREFIX then
    return nil
  end

  local ok, account = pcall(function()
    return nk.account_get_id(user_id)
  end)
  if not ok or account == nil then
    return nil
  end

  -- In Nakama's ApiAccount, e-mail is usually a top-level field (account.email),
  -- not inside account.user.
  local email = account.email
  if email == nil and account.user ~= nil then
    -- Extra tolerance for older/modified payloads.
    email = account.user.email or account.user.email_address or account.user.emailAddress
    if email == nil and account.user.metadata ~= nil then
      email = account.user.metadata.email or account.user.metadata.Email
    end
  end
  if email == nil then return nil end
  return string.lower(tostring(email))
end

local function is_user_allowed_for_cheat_rows(user_id, whitelist_set)
  local email = user_email_lower(user_id)
  if email == nil or email == "" then return false end
  local set = whitelist_set
  if set == nil then
    set = build_cheat_whitelist_set({ user_id })
  end
  return set[email] == true
end

function normalize_mine_unlocked(raw, default_floor)
  local out = {
    easy = clamp_int(raw and raw.easy or default_floor, 1, CFG.PVE_MAX_LEVEL),
    medium = clamp_int(raw and raw.medium or 0, 0, CFG.PVE_MAX_LEVEL),
    hard = clamp_int(raw and raw.hard or 0, 0, CFG.PVE_MAX_LEVEL),
  }
  return out
end

local function read_pve_progress(user_id)
  local ok_read, rows = pcall(function()
    return nk.storage_read({
      {
        collection = CFG.PVE_PROGRESS_COLLECTION,
        key = CFG.PVE_PROGRESS_KEY,
        user_id = user_id,
      },
    })
  end)
  if not ok_read then
    nk.logger_error("read_pve_progress: storage_read failed: " .. tostring(rows))
    rows = nil
  end

  -- Регенерация по времени только до min(storage_max, PVE_ENERGY_REGEN_CAP); «скупленная» энергия не капается выше 100.
  local function apply_energy_regen(progress, storage_max, now)
    local cap = clamp_int(storage_max, 0, nil)
    if cap <= 0 then
      progress.energy = 0
      progress.energy_updated_at = now
      return
    end
    local regen_top = math.min(cap, math.max(0, tonumber(CFG.PVE_ENERGY_REGEN_CAP) or 100))
    local energy = clamp_int(progress.energy, 0, cap)
    local updated_at = math.floor(tonumber(progress.energy_updated_at) or now)
    if updated_at <= 0 then updated_at = now end
    if updated_at > now then updated_at = now end

    if energy >= regen_top then
      progress.energy = energy
      progress.energy_updated_at = now
      return
    end

    local gained = math.floor(math.max(0, now - updated_at) / CFG.PVE_ENERGY_REGEN_SECONDS)
    if gained <= 0 then
      progress.energy = energy
      progress.energy_updated_at = updated_at
      return
    end

    energy = math.min(regen_top, energy + gained)
    progress.energy = energy
    if energy >= regen_top then
      progress.energy_updated_at = now
    else
      progress.energy_updated_at = updated_at + gained * CFG.PVE_ENERGY_REGEN_SECONDS
    end
  end

  local now = os.time()
  local energy_max = pve_energy_max_for_user(user_id)
  if rows == nil or #rows == 0 then
    local base = {
      xp = 0,
      gold = 0,
      level = 1,
      defeated = {},
      ore = 0,
      ingots = 0,
      matter = 0,
      keys = 0,
      blueprint_green = 0,
      blueprint_blue = 0,
      blueprint_purple = 0,
      blueprint_gold = 0,
      tesseracts = 0,
      key_items = empty_key_items(),
      energy = math.min(energy_max, math.max(0, tonumber(CFG.PVE_ENERGY_REGEN_CAP) or 100)),
      energy_updated_at = now,
      mine = {
        current_difficulty = CFG.MINE_DIFFICULTY_DEFAULT,
        selected_floor = 1,
        unlocked = normalize_mine_unlocked(nil, 1),
        floor_states = {},
      },
    }
    return base, nil
  end

  local row = rows[1]
  local val = decode_storage_value(row) or {}
  local has_energy_field = val.energy ~= nil
  local initial_energy = has_energy_field and val.energy or energy_max
  local progress = {
    xp = math.max(0, tonumber(val.xp) or 0),
    gold = math.max(0, tonumber(val.gold) or 0),
    level = math.max(1, tonumber(val.level) or 1),
    defeated = type(val.defeated) == "table" and val.defeated or {},
    ore = math.max(0, tonumber(val.ore) or 0),
    ingots = math.max(0, tonumber(val.ingots) or 0),
    matter = math.max(0, tonumber(val.matter) or 0),
    keys = math.max(0, tonumber(val.keys) or 0),
    blueprint_green = math.max(0, tonumber(val.blueprint_green) or 0),
    blueprint_blue = math.max(0, tonumber(val.blueprint_blue) or 0),
    blueprint_purple = math.max(0, tonumber(val.blueprint_purple) or 0),
    blueprint_gold = math.max(0, tonumber(val.blueprint_gold) or 0),
    tesseracts = math.max(0, tonumber(val.tesseracts) or 0),
    key_items = type(val.key_items) == "table" and val.key_items or empty_key_items(),
    -- Backward compatibility: old rows had no energy field.
    -- If missing, start from full energy instead of 0.
    energy = clamp_int(initial_energy, 0, energy_max),
    energy_updated_at = math.floor(tonumber(val.energy_updated_at) or now),
    mine = type(val.mine) == "table" and val.mine or {},
  }
  progress.mine.current_difficulty = normalize_mine_difficulty(progress.mine.current_difficulty)
  progress.mine.selected_floor = clamp_int(progress.mine.selected_floor or 1, 1, CFG.PVE_MAX_LEVEL)
  progress.mine.unlocked = normalize_mine_unlocked(progress.mine.unlocked, 1)
  if type(progress.mine.floor_states) ~= "table" then progress.mine.floor_states = {} end
  progress.key_items.miner_key = math.max(0, tonumber(progress.key_items.miner_key) or 0)
  progress.key_items.dark_key = math.max(0, tonumber(progress.key_items.dark_key) or 0)
  progress.level = current_level_from_xp(progress.xp)
  apply_energy_regen(progress, energy_max, now)
  return progress, row.version
end

--- §4.6 / §14 фаза 5: статы как в PvE (уровень + экип), без mine_stat_multiplier.
local function apply_pvp_pro_stats_from_sheet(actor, user_id)
  if actor == nil or user_id == nil or user_id == "" then return end
  ensure_character_sheet_initialized(user_id)
  local progress, _ = read_pve_progress(user_id)
  local level = clamp_int(progress.level or 1, 1, CFG.PVE_MAX_LEVEL)
  local sheet = read_character_sheet(user_id)
  ensure_sheet_inventory_counts(sheet)
  local base = character_stats_base_for_level(level)
  local merged = merge_stats_with_equipment(base, sum_equipment_bonuses(sheet))
  actor.max_hp = math.max(1, math.floor(tonumber(merged.hp) or CFG.MAX_HP))
  actor.hp = actor.max_hp
  actor.initial_hp = actor.max_hp
  actor.base_damage = math.max(0, math.floor(tonumber(merged.damage) or 0))
  actor.base_armor = math.max(0, math.floor(tonumber(merged.armor) or 0))
  actor.base_crit = math.max(0, tonumber(merged.crit_chance) or 0)
  actor.base_heal = math.max(0, math.floor(tonumber(merged.healing) or 0))
end

local function write_pve_progress(user_id, progress, version)
  local write_obj = {
    collection = CFG.PVE_PROGRESS_COLLECTION,
    key = CFG.PVE_PROGRESS_KEY,
    user_id = user_id,
    value = {
      xp = progress.xp,
      gold = progress.gold,
      level = progress.level,
      defeated = progress.defeated,
      ore = progress.ore,
      ingots = progress.ingots,
      matter = progress.matter,
      keys = progress.keys,
      blueprint_green = progress.blueprint_green,
      blueprint_blue = progress.blueprint_blue,
      blueprint_purple = progress.blueprint_purple,
      blueprint_gold = progress.blueprint_gold,
      tesseracts = progress.tesseracts,
      key_items = progress.key_items,
      energy = progress.energy,
      energy_updated_at = progress.energy_updated_at,
      mine = progress.mine,
      updated_at = os.time(),
    },
    permission_read = 1,
    permission_write = 0,
  }
  if version ~= nil and version ~= "" then
    write_obj.version = version
  end
  nk.storage_write({ write_obj })
end

function random_affix_for_floor(floor)
  local f = clamp_int(floor, 1, CFG.PVE_MAX_LEVEL)
  local i = math.random(1, #CFG.MINE_AFFIX_POOL)
  return CFG.MINE_AFFIX_POOL[i] or ""
end

function add_blueprint(progress, rarity)
  local r = tostring(rarity or "")
  if r == "green" then
    progress.blueprint_green = math.max(0, tonumber(progress.blueprint_green) or 0) + 1
  elseif r == "blue" then
    progress.blueprint_blue = math.max(0, tonumber(progress.blueprint_blue) or 0) + 1
  elseif r == "purple" then
    progress.blueprint_purple = math.max(0, tonumber(progress.blueprint_purple) or 0) + 1
  elseif r == "gold" then
    progress.blueprint_gold = math.max(0, tonumber(progress.blueprint_gold) or 0) + 1
  end
end

award_pve_victory = function(user_id, bot_id, match_epoch_snapshot, run_meta)
  local snap = tonumber(match_epoch_snapshot) or 0
  if guard_is_epoch_stale_for_match(user_id, snap) then
    nk.logger_info("award_pve_victory skipped: session_stale")
    local progress = read_pve_progress(user_id)
    return {
      reward_xp = 0,
      reward_gold = 0,
      reward_ore = 0,
      reward_matter = 0,
      level = progress.level or 1,
      xp = progress.xp or 0,
      gold = progress.gold or 0,
      ore = progress.ore or 0,
      matter = progress.matter or 0,
      session_stale = true,
    }
  end

  ensure_character_sheet_initialized(user_id)

  local diff = normalize_mine_difficulty(run_meta and run_meta.difficulty)
  local bot = get_bot_profile(bot_id, diff)
  local floor = clamp_int((run_meta and run_meta.floor) or bot.floor or 1, 1, CFG.PVE_MAX_LEVEL)
  local stat_mul = mine_stat_multiplier(diff)
  local reward_mul = mine_reward_multiplier(diff)
  local is_boss = bot.is_boss == true or is_boss_floor(floor)
  local reward_xp = math.ceil((tonumber(bot.reward_xp) or 0) * reward_mul)
  reward_xp = aura_apply_to_pve_reward_xp(reward_xp, get_active_server_aura())
  local reward_gold = math.ceil((tonumber(bot.reward_gold) or 0) * reward_mul)
  local reward_ore = math.ceil((tonumber(bot.reward_ore) or 0) * reward_mul)
  -- Материя только из каталога бота (min/max); отдельного «рандомного» дропа с обычных нет.
  local reward_matter = 0
  local mmn = math.max(0, tonumber(bot.reward_matter_min) or 0)
  local mmx = math.max(mmn, tonumber(bot.reward_matter_max) or mmn)
  if mmn > 0 or mmx > 0 then
    reward_matter = math.max(0, math.ceil(math.random(mmn, mmx) * reward_mul))
  end
  local reward_key_id = tostring(bot.reward_key_id or "")
  local reward_key_amount = math.max(0, tonumber(bot.reward_key_amount) or 0)
  local base_reward_ingots = math.max(0, math.ceil((tonumber(bot.reward_ingots) or 0) * reward_mul))
  local ingot_def_id = ingot_def_for_difficulty(diff)
  local actual_ingots = 0
  if base_reward_ingots > 0 then
    if is_boss then
      actual_ingots = base_reward_ingots * 2
    else
      if math.random() < ingot_drop_chance_non_boss(floor) then
        actual_ingots = base_reward_ingots
      end
    end
  end
  local reward_tesseract = 0
  local tesseract_chance = tonumber(bot.reward_tesseract_chance) or 0
  if tesseract_chance > 0 and math.random() < tesseract_chance then
    reward_tesseract = 1
  end
  local reward_recipe_item_id = ""
  local max_retries = 5

  for i = 1, max_retries do
    local progress, version = read_pve_progress(user_id)
    progress.xp = progress.xp + reward_xp
    progress.gold = progress.gold + reward_gold
    progress.ore = progress.ore + reward_ore
    progress.matter = progress.matter + reward_matter
    progress.ingots = progress.ingots + actual_ingots
    progress.tesseracts = (tonumber(progress.tesseracts) or 0) + reward_tesseract
    if reward_key_id ~= "" and reward_key_amount > 0 then
      progress.key_items = progress.key_items or empty_key_items()
      progress.key_items[reward_key_id] = (tonumber(progress.key_items[reward_key_id]) or 0) + reward_key_amount
    end
    if not mine_floor_uses_mine_item_recipe_drop(floor, is_boss) then
      add_blueprint(progress, bot.reward_blueprint)
    end
    progress.level = current_level_from_xp(progress.xp)
    local defeated = progress.defeated or {}
    local current_count = tonumber(defeated[bot_id]) or 0
    defeated[bot_id] = current_count + 1
    progress.defeated = defeated
    progress.mine = progress.mine or {}
    progress.mine.current_difficulty = diff
    progress.mine.selected_floor = floor
    progress.mine.unlocked = normalize_mine_unlocked(progress.mine.unlocked, 1)
    progress.mine.floor_states = type(progress.mine.floor_states) == "table" and progress.mine.floor_states or {}
    local state_key = make_floor_state_key(diff, floor)
    local cur_state = type(progress.mine.floor_states[state_key]) == "table" and progress.mine.floor_states[state_key] or {}
    do
      local base_resp = is_boss and CFG.MINE_RESPAWN_BOSS_SECONDS or CFG.MINE_RESPAWN_NORMAL_SECONDS
      local respawn_sec = aura_mine_respawn_duration_seconds(base_resp, get_active_server_aura())
      cur_state.next_spawn_at = os.time() + respawn_sec
    end
    local fought_affix = tostring((run_meta and run_meta.affix) or "")
    local next_affix = random_affix_for_floor(floor)
    if #CFG.MINE_AFFIX_POOL > 1 then
      local guard = 0
      while next_affix == fought_affix and guard < 12 do
        next_affix = random_affix_for_floor(floor)
        guard = guard + 1
      end
    end
    cur_state.last_affix = next_affix
    cur_state.wins = (tonumber(cur_state.wins) or 0) + 1
    progress.mine.floor_states[state_key] = cur_state
    if floor >= CFG.PVE_MAX_LEVEL and is_boss then
      if diff == "easy" and (progress.mine.unlocked.medium or 0) <= 0 then
        progress.mine.unlocked.medium = 1
      elseif diff == "medium" and (progress.mine.unlocked.hard or 0) <= 0 then
        progress.mine.unlocked.hard = 1
      end
    end

    local ok, err = pcall(function()
      write_pve_progress(user_id, progress, version)
    end)
    if ok then
      local sheet = read_character_sheet(user_id)
      ensure_sheet_inventory_counts(sheet)
      local grant_ok = true
      if actual_ingots > 0 then
        local ok_add = inventory_try_add(sheet, ingot_def_id, actual_ingots)
        if ok_add ~= true then grant_ok = false end
      end
      if grant_ok and reward_tesseract > 0 then
        local ok_add = inventory_try_add(sheet, "tesseract", reward_tesseract)
        if ok_add ~= true then grant_ok = false end
      end
      local bp_r = tostring(bot.reward_blueprint or "")
      local used_v43 = false
      if grant_ok and mine_floor_uses_mine_item_recipe_drop(floor, is_boss) then
        local ok_rec, rid = grant_mine_recipe_drop_v43(sheet, floor, is_boss, diff)
        used_v43 = ok_rec == true
        if rid ~= nil and rid ~= "" then
          reward_recipe_item_id = tostring(rid)
        end
      end
      if grant_ok and used_v43 then
        local col = mine_recipe_color_for_difficulty(diff)
        local rarity = ({ green = "green", blue = "blue", purple = "purple" })[col]
        if rarity ~= nil then add_blueprint(progress, rarity) end
        local ok_bp, err_bp = pcall(function()
          write_pve_progress(user_id, progress, nil)
        end)
        if not ok_bp then
          nk.logger_error("award_pve_victory: blueprint progress write failed: " .. tostring(err_bp))
        end
      end
      if grant_ok and bp_r ~= "" and not used_v43 and not mine_floor_uses_mine_item_recipe_drop(floor, is_boss) then
        local rid = ({ green = "recipe_green", blue = "recipe_blue", purple = "recipe_purple", gold = "recipe_gold" })[bp_r]
        if rid ~= nil then
          local ok_add = inventory_try_add(sheet, rid, 1)
          if ok_add ~= true then grant_ok = false end
        end
      end
      if grant_ok then
        write_character_sheet(user_id, sheet)
      else
        nk.logger_warn("award_pve_victory: не удалось положить лут в инвентарь (сундук полон?)")
      end
      return {
        reward_xp = reward_xp,
        reward_gold = reward_gold,
        reward_ore = reward_ore,
        reward_matter = reward_matter,
        reward_ingots = actual_ingots,
        reward_tesseract = reward_tesseract,
        reward_key_id = reward_key_id,
        reward_key_amount = reward_key_amount,
        reward_blueprint = tostring(bot.reward_blueprint or ""),
        reward_recipe_item_id = reward_recipe_item_id,
        difficulty = diff,
        floor = floor,
        stat_mul = stat_mul,
        level = progress.level,
        xp = progress.xp,
        gold = progress.gold,
        ore = progress.ore,
        matter = progress.matter,
      }
    end

    local err_text = tostring(err)
    if string.find(err_text, "version", 1, true) == nil or i == max_retries then
      nk.logger_error("award_pve_victory: " .. err_text)
      break
    end
  end

  return {
    reward_xp = reward_xp,
    reward_gold = reward_gold,
    reward_ore = reward_ore,
    reward_matter = reward_matter,
    reward_ingots = actual_ingots,
    reward_tesseract = reward_tesseract,
    reward_key_id = reward_key_id,
    reward_key_amount = reward_key_amount,
    reward_blueprint = tostring(bot and bot.reward_blueprint or ""),
    reward_recipe_item_id = reward_recipe_item_id,
    level = 1,
    xp = 0,
    gold = 0,
    ore = 0,
    matter = 0,
  }
end

award_pve_defeat = function(user_id, match_epoch_snapshot)
  local snap = tonumber(match_epoch_snapshot) or 0
  local defeat_xp = aura_apply_to_pve_reward_xp(10, get_active_server_aura())
  if guard_is_epoch_stale_for_match(user_id, snap) then
    local progress = read_pve_progress(user_id)
    return {
      reward_xp = 0,
      reward_gold = 0,
      reward_ore = 0,
      reward_matter = 0,
      level = progress.level or 1,
      xp = progress.xp or 0,
      gold = progress.gold or 0,
      ore = progress.ore or 0,
      matter = progress.matter or 0,
      session_stale = true,
    }
  end

  local max_retries = 5
  for i = 1, max_retries do
    local progress, version = read_pve_progress(user_id)
    progress.xp = progress.xp + defeat_xp
    progress.level = current_level_from_xp(progress.xp)
    local ok, err = pcall(function()
      write_pve_progress(user_id, progress, version)
    end)
    if ok then
      return {
        reward_xp = defeat_xp,
        reward_gold = 0,
        reward_ore = 0,
        reward_matter = 0,
        level = progress.level or 1,
        xp = progress.xp or 0,
        gold = progress.gold or 0,
        ore = progress.ore or 0,
        matter = progress.matter or 0,
      }
    end
    local err_text = tostring(err)
    if string.find(err_text, "version", 1, true) == nil or i == max_retries then
      nk.logger_error("award_pve_defeat: " .. err_text)
      break
    end
  end

  return {
    reward_xp = defeat_xp,
    reward_gold = 0,
    reward_ore = 0,
    reward_matter = 0,
    level = 1,
    xp = 0,
    gold = 0,
    ore = 0,
    matter = 0,
  }
end

local function read_match3_stats(user_id)
  local val, version = Ach.storage_read_match3_summary_val(user_id)
  local stats = {
    played = tonumber(val.played) or 0,
    wins = tonumber(val.wins) or 0,
    losses = tonumber(val.losses) or 0,
  }
  return stats, version
end

local function duel_match3_stats_get(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local stats, _ = read_match3_stats(user_id)
    local val_full, _fv = Ach.storage_read_match3_summary_val(user_id)
    local ach = {}
    if type(val_full.achievement_stats) == "table" then
      ach = val_full.achievement_stats
    end
    local claimed = {}
    if type(val_full.achievement_claimed) == "table" then
      claimed = val_full.achievement_claimed
    end
    return nk.json_encode({
      ok = true,
      played = stats.played or 0,
      wins = stats.wins or 0,
      losses = stats.losses or 0,
      achievement_stats = ach,
      achievement_claimed = claimed,
    })
  end)

  if not ok then
    nk.logger_error("duel_match3_stats_get: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

local function duel_match3_stats_record(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local ok_epoch, err_epoch = guard_assert_client_epoch_matches(user_id, payload)
    if not ok_epoch then
      return nk.json_encode({ ok = false, err = err_epoch })
    end

    ensure_character_sheet_initialized(user_id)

    local won = false
    if payload ~= nil and payload ~= "" then
      local p = nk.json_decode(payload)
      won = p ~= nil and p.won == true
    end

    local max_retries = 5
    for i = 1, max_retries do
      local val, version = Ach.storage_read_match3_summary_val(user_id)
      local played = (tonumber(val.played) or 0) + 1
      local wins = tonumber(val.wins) or 0
      local losses = tonumber(val.losses) or 0
      if won then
        wins = wins + 1
      else
        losses = losses + 1
      end
      val.played = played
      val.wins = wins
      val.losses = losses
      val.updated_at = os.time()

      local write_obj = {
        collection = CFG.STATS_COLLECTION,
        key = CFG.STATS_KEY,
        user_id = user_id,
        value = val,
        permission_read = 1,
        permission_write = 0,
      }
      if version ~= nil and version ~= "" then
        write_obj.version = version
      end

      local write_ok, write_err = pcall(function()
        nk.storage_write({ write_obj })
      end)

      if write_ok then
        return nk.json_encode({
          ok = true,
          played = played,
          wins = wins,
          losses = losses,
        })
      end

      local err_text = tostring(write_err)
      if string.find(err_text, "version", 1, true) == nil or i == max_retries then
        error(write_err)
      end
    end

    return nk.json_encode({ ok = false, err = "retry_exhausted" })
  end)

  if not ok then
    nk.logger_error("duel_match3_stats_record: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

local function duel_match3_pve_catalog_get(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local progress = read_pve_progress(user_id)
    local current_diff = normalize_mine_difficulty(progress.mine and progress.mine.current_difficulty or CFG.MINE_DIFFICULTY_DEFAULT)
    local bots = {}
    for _, bot in pairs(get_merged_bots(current_diff)) do
      local dps_ehp, dps_h, ehp_h = Metrics.bot_dps_ehp_product(bot)
      bots[#bots + 1] = {
        id = bot.id,
        name = bot.name,
        difficulty = bot.difficulty,
        floor = bot.floor or bot.difficulty,
        is_boss = bot.is_boss == true,
        hp_bonus = bot.hp_bonus or 0,
        start_mana = bot.start_mana or 0,
        reward_xp = bot.reward_xp,
        reward_gold = bot.reward_gold,
        reward_ore = bot.reward_ore or 0,
        reward_matter_min = bot.reward_matter_min or 0,
        reward_matter_max = bot.reward_matter_max or 0,
        reward_blueprint = bot.reward_blueprint or "",
        reward_key_id = bot.reward_key_id or "",
        reward_key_amount = bot.reward_key_amount or 0,
        reward_ingots = bot.reward_ingots or 0,
        reward_tesseract_chance = bot.reward_tesseract_chance or 0,
        base_damage = tonumber(bot.base_damage) or tonumber(bot.damage) or 0,
        base_armor = tonumber(bot.base_armor) or tonumber(bot.armor) or 0,
        base_crit = tonumber(bot.base_crit) or tonumber(bot.crit_chance) or 0,
        base_heal = tonumber(bot.base_heal) or tonumber(bot.healing) or 0,
        cost_attack = bot.cost_attack or PveMineCost.DEFAULT_ATTACK,
        cost_banish = bot.cost_banish or PveMineCost.DEFAULT_BANISH,
        metrics_dps = dps_h,
        metrics_ehp = ehp_h,
        metrics_dps_ehp = dps_ehp,
      }
    end
    table.sort(bots, function(a, b)
      local af = tonumber(a.floor) or 0
      local bf = tonumber(b.floor) or 0
      if af ~= bf then return af < bf end
      return tostring(a.id) < tostring(b.id)
    end)

    local unlocked_floor = get_unlocked_floor(progress, current_diff)
    local mine_floors = {}
    for _, b in ipairs(bots) do
      local floor = clamp_int(b.floor or 1, 1, CFG.PVE_MAX_LEVEL)
      local state_key = make_floor_state_key(current_diff, floor)
      local fs = progress.mine and progress.mine.floor_states and progress.mine.floor_states[state_key] or nil
      local left = floor_respawn_left_seconds(progress, current_diff, floor)
      mine_floors[#mine_floors + 1] = {
        floor = floor,
        bot_id = b.id,
        unlocked = floor <= unlocked_floor,
        respawn_left_seconds = left,
        affix = fs and tostring(fs.last_affix or "") or random_affix_for_floor(floor),
        is_boss = b.is_boss == true,
      }
    end

    return nk.json_encode({
      ok = true,
      progression = build_progression_payload_auto(progress, user_id),
      level_xp = CFG.LEVEL_XP,
      max_level = CFG.PVE_MAX_LEVEL,
      mine_difficulty = current_diff,
      barrier_requirements = MINE_BARRIER_REQUIREMENTS,
      mine_floors = mine_floors,
      bots = bots,
    })
  end)

  if not ok then
    nk.logger_error("duel_match3_pve_catalog_get: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

local function duel_character_item_move(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local ok_epoch, err_epoch = guard_assert_client_epoch_matches(user_id, payload)
    if not ok_epoch then
      return nk.json_encode({ ok = false, err = err_epoch })
    end

    local p = {}
    if payload ~= nil and payload ~= "" then
      p = nk.json_decode(payload) or {}
    end
    local op = tostring(p.op or "")
    local sheet = read_character_sheet(user_id)
    ensure_sheet_inventory_counts(sheet)
    local defs = get_merged_item_defs()

    local function json_fail(err)
      return nk.json_encode({ ok = false, err = err })
    end

    if op == "inv_to_equip" then
      local inv_index = tonumber(p.inv_index)
      local slot_index = tonumber(p.slot_index)
      if inv_index == nil or slot_index == nil then return json_fail("bad_indices") end
      inv_index = math.floor(inv_index)
      slot_index = math.floor(slot_index)
      if inv_index < 0 or inv_index > 24 then return json_fail("bad_inv_index") end
      if slot_index < 0 or slot_index > 7 then return json_fail("bad_slot_index") end
      local i = inv_index + 1
      local s = slot_index + 1
      local item = sheet.inventory[i]
      local cnt = tonumber(sheet.inventory_counts[i]) or 0
      if item == nil or item == "" or cnt < 1 then return json_fail("empty_source") end
      local def = defs[item]
      if def == nil then return json_fail("unknown_item") end
      if not item_def_is_equipment(def) then return json_fail("not_equipment") end
      if def.slot ~= EQUIP_ORDER[s] then return json_fail("wrong_slot") end
      local cur = sheet.equipment[s] or ""
      sheet.inventory[i] = cur
      if cur ~= nil and cur ~= "" then
        sheet.inventory_counts[i] = 1
      else
        sheet.inventory_counts[i] = 0
      end
      sheet.equipment[s] = item
    elseif op == "equip_to_inv" then
      local slot_index = tonumber(p.slot_index)
      local inv_index = tonumber(p.inv_index)
      if inv_index == nil or slot_index == nil then return json_fail("bad_indices") end
      inv_index = math.floor(inv_index)
      slot_index = math.floor(slot_index)
      if inv_index < 0 or inv_index > 24 then return json_fail("bad_inv_index") end
      if slot_index < 0 or slot_index > 7 then return json_fail("bad_slot_index") end
      local i = inv_index + 1
      local s = slot_index + 1
      local item = sheet.equipment[s]
      if item == nil or item == "" then return json_fail("empty_source") end
      local cur_inv = sheet.inventory[i] or ""
      if cur_inv == "" then
        sheet.equipment[s] = ""
        sheet.inventory[i] = item
        sheet.inventory_counts[i] = 1
      else
        local def_inv = defs[cur_inv]
        if def_inv == nil then return json_fail("unknown_item") end
        if not item_def_is_equipment(def_inv) then return json_fail("cannot_swap") end
        if def_inv.slot ~= EQUIP_ORDER[s] then return json_fail("cannot_swap") end
        sheet.equipment[s] = cur_inv
        sheet.inventory[i] = item
        sheet.inventory_counts[i] = 1
      end
    elseif op == "inv_swap" then
      local a = tonumber(p.inv_a)
      local b = tonumber(p.inv_b)
      if a == nil or b == nil then return json_fail("bad_indices") end
      a = math.floor(a)
      b = math.floor(b)
      if a < 0 or a > 24 or b < 0 or b > 24 then return json_fail("bad_inv_index") end
      if a ~= b then
        local ia, ib = a + 1, b + 1
        sheet.inventory[ia], sheet.inventory[ib] = sheet.inventory[ib], sheet.inventory[ia]
        local ca = tonumber(sheet.inventory_counts[ia]) or 0
        local cb = tonumber(sheet.inventory_counts[ib]) or 0
        sheet.inventory_counts[ia], sheet.inventory_counts[ib] = cb, ca
      end
    elseif op == "equip_swap" then
      local a = tonumber(p.slot_a)
      local b = tonumber(p.slot_b)
      if a == nil or b == nil then return json_fail("bad_indices") end
      a = math.floor(a)
      b = math.floor(b)
      if a < 0 or a > 7 or b < 0 or b > 7 then return json_fail("bad_slot_index") end
      if a ~= b then
        local sa, sb = a + 1, b + 1
        sheet.equipment[sa], sheet.equipment[sb] = sheet.equipment[sb], sheet.equipment[sa]
      end
    else
      return json_fail("unknown_op")
    end

    write_character_sheet(user_id, sheet)

    local progress = read_pve_progress(user_id)
    return encode_character_ok_response(sheet, progress, user_id)
  end)

  if not ok then
    nk.logger_error("duel_character_item_move: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

local function duel_character_get(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local progress = read_pve_progress(user_id)
    local sheet = read_character_sheet(user_id)
    return encode_character_ok_response(sheet, progress, user_id)
  end)

  if not ok then
    nk.logger_error("duel_character_get: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

local function duel_character_recipe_learn(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local ok_epoch, err_epoch = guard_assert_client_epoch_matches(user_id, payload)
    if not ok_epoch then
      return nk.json_encode({ ok = false, err = err_epoch })
    end

    local p = {}
    if payload ~= nil and payload ~= "" then
      p = nk.json_decode(payload) or {}
    end
    local inv_index = tonumber(p.inv_index)
    if inv_index == nil then
      return nk.json_encode({ ok = false, err = "bad_indices" })
    end
    inv_index = math.floor(inv_index)
    if inv_index < 0 or inv_index > 24 then
      return nk.json_encode({ ok = false, err = "bad_inv_index" })
    end

    local sheet = read_character_sheet(user_id)
    ensure_sheet_inventory_counts(sheet)
    local defs = get_merged_item_defs()
    local i = inv_index + 1
    local item_id = sheet.inventory[i] or ""
    local cnt = tonumber(sheet.inventory_counts[i]) or 0
    if item_id == "" or cnt < 1 then
      return nk.json_encode({ ok = false, err = "empty_source" })
    end

    local def = defs[item_id]
    if def == nil then
      return nk.json_encode({ ok = false, err = "unknown_item" })
    end
    if not item_def_is_recipe(def) then
      return nk.json_encode({ ok = false, err = "not_recipe" })
    end

    local lr = sheet.learned_recipes or {}
    for j = 1, #lr do
      if lr[j] == item_id then
        return nk.json_encode({ ok = false, err = "already_learned" })
      end
    end

    cnt = cnt - 1
    if cnt <= 0 then
      sheet.inventory[i] = ""
      sheet.inventory_counts[i] = 0
    else
      sheet.inventory_counts[i] = cnt
    end

    lr[#lr + 1] = item_id
    sheet.learned_recipes = lr
    write_character_sheet(user_id, sheet)

    local progress = read_pve_progress(user_id)
    return encode_character_ok_response(sheet, progress, user_id)
  end)

  if not ok then
    nk.logger_error("duel_character_recipe_learn: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

local function duel_workshop_craft_start(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local ok_epoch, err_epoch = guard_assert_client_epoch_matches(user_id, payload)
    if not ok_epoch then
      return nk.json_encode({ ok = false, err = err_epoch })
    end

    local p = {}
    if payload ~= nil and payload ~= "" then
      p = nk.json_decode(payload) or {}
    end
    local slot_index = tonumber(p.slot_index)
    if slot_index == nil then
      return nk.json_encode({ ok = false, err = "bad_slot_index" })
    end
    slot_index = math.floor(slot_index)
    if slot_index < 0 or slot_index > 7 then
      return nk.json_encode({ ok = false, err = "bad_slot_index" })
    end

    local output_def_id = tostring(p.output_def_id or "")
    if output_def_id == "" then
      return nk.json_encode({ ok = false, err = "bad_output" })
    end

    ensure_character_sheet_initialized(user_id)
    local defs = get_merged_item_defs()
    local out_def = defs[output_def_id]
    if out_def == nil or not item_def_is_equipment(out_def) then
      return nk.json_encode({ ok = false, err = "unknown_item" })
    end

    local expected_slot = EQUIP_ORDER[slot_index + 1]
    if tostring(out_def.slot or "") ~= expected_slot then
      return nk.json_encode({ ok = false, err = "wrong_workshop_slot" })
    end

    local craft_recipe_id = tostring(out_def.craft_recipe_id or "")
    if craft_recipe_id == "" then
      return nk.json_encode({ ok = false, err = "not_craftable" })
    end

    local sheet = read_character_sheet(user_id)
    ensure_sheet_inventory_counts(sheet)
    ensure_sheet_workshop(sheet)
    local wslot = sheet.workshop_slots[slot_index + 1]
    if wslot.output_def_id ~= nil and wslot.output_def_id ~= "" then
      local wend = tonumber(wslot.ends_at) or 0
      if wend > os.time() then
        return nk.json_encode({ ok = false, err = "workshop_busy" })
      end
      return nk.json_encode({ ok = false, err = "claim_first" })
    end

    if not sheet_has_learned_for_craft(sheet, craft_recipe_id) then
      return nk.json_encode({ ok = false, err = "recipe_not_learned" })
    end

    local tier = clamp_int(tonumber(out_def.tier) or 1, 1, 3)
    local quality = tostring(out_def.quality or "normal")
    if quality ~= "normal" and quality ~= "rare" and quality ~= "epic" and quality ~= "legendary" then
      return nk.json_encode({ ok = false, err = "unsupported_craft_quality" })
    end

    local cost = workshop_craft_cost_from_def(out_def, tier, quality)
    local ore_c = cost.ore
    local gold_c = cost.gold
    local ingot_def = cost.ingot_def
    local ingot_n = cost.ingot_n
    local tess_n = cost.tesseract_n

    if quality == "normal" then
      if tier == 2 and not workshop_has_legendary_fodder(sheet, defs, slot_index, 1) then
        return nk.json_encode({ ok = false, err = "missing_legend_fodder_t1" })
      end
      if tier == 3 and not workshop_has_legendary_fodder(sheet, defs, slot_index, 2) then
        return nk.json_encode({ ok = false, err = "missing_legend_fodder_t2" })
      end
    elseif quality == "rare" then
      if not workshop_has_quality_fodder(sheet, defs, slot_index, tier, "normal") then
        return nk.json_encode({ ok = false, err = "missing_normal_fodder" })
      end
    elseif quality == "epic" then
      if not workshop_has_quality_fodder(sheet, defs, slot_index, tier, "rare") then
        return nk.json_encode({ ok = false, err = "missing_rare_fodder" })
      end
    elseif quality == "legendary" then
      if not workshop_has_quality_fodder(sheet, defs, slot_index, tier, "epic") then
        return nk.json_encode({ ok = false, err = "missing_epic_fodder" })
      end
    end

    if ingot_n > 0 and ingot_def == "" then
      return nk.json_encode({ ok = false, err = "bad_craft_cost" })
    end
    if ingot_n > 0 and inventory_count_def(sheet, ingot_def) < ingot_n then
      return nk.json_encode({ ok = false, err = "not_enough_ingots" })
    end
    if tess_n > 0 and inventory_count_def(sheet, "tesseract") < tess_n then
      return nk.json_encode({ ok = false, err = "not_enough_tesseract" })
    end

    local dur_tbl = CFG.WORKSHOP_CRAFT_DURATION_SEC_BY_TIER
    local dur = dur_tbl and dur_tbl[tier] or (60 * 60)

    local max_retries = 5
    for attempt = 1, max_retries do
      local progress, version = read_pve_progress(user_id)
      if (tonumber(progress.ore) or 0) < ore_c then
        return nk.json_encode({ ok = false, err = "not_enough_ore" })
      end
      if (tonumber(progress.gold) or 0) < gold_c then
        return nk.json_encode({ ok = false, err = "not_enough_gold" })
      end

      progress.ore = (tonumber(progress.ore) or 0) - ore_c
      progress.gold = (tonumber(progress.gold) or 0) - gold_c

      local w_ok, w_err = pcall(function()
        write_pve_progress(user_id, progress, version)
      end)
      if w_ok then
        if ingot_n > 0 then
          if not inventory_remove_def_total(sheet, ingot_def, ingot_n) then
            nk.logger_error("workshop_craft_start: не удалось списать слитки")
            return nk.json_encode({ ok = false, err = "server_error" })
          end
        end
        if tess_n > 0 then
          if not inventory_remove_def_total(sheet, "tesseract", tess_n) then
            nk.logger_error("workshop_craft_start: не удалось списать тессеракты")
            return nk.json_encode({ ok = false, err = "server_error" })
          end
        end
        if quality == "normal" then
          if tier == 2 then
            if not workshop_consume_legendary_fodder(sheet, defs, slot_index, 1) then
              nk.logger_error("workshop_craft_start: не удалось поглотить легенду T1")
              return nk.json_encode({ ok = false, err = "server_error" })
            end
          elseif tier == 3 then
            if not workshop_consume_legendary_fodder(sheet, defs, slot_index, 2) then
              nk.logger_error("workshop_craft_start: не удалось поглотить легенду T2")
              return nk.json_encode({ ok = false, err = "server_error" })
            end
          end
        elseif quality == "rare" then
          if not workshop_consume_quality_fodder(sheet, defs, slot_index, tier, "normal") then
            nk.logger_error("workshop_craft_start: не удалось поглотить normal для rare")
            return nk.json_encode({ ok = false, err = "server_error" })
          end
        elseif quality == "epic" then
          if not workshop_consume_quality_fodder(sheet, defs, slot_index, tier, "rare") then
            nk.logger_error("workshop_craft_start: не удалось поглотить rare для epic")
            return nk.json_encode({ ok = false, err = "server_error" })
          end
        elseif quality == "legendary" then
          if not workshop_consume_quality_fodder(sheet, defs, slot_index, tier, "epic") then
            nk.logger_error("workshop_craft_start: не удалось поглотить epic для legendary")
            return nk.json_encode({ ok = false, err = "server_error" })
          end
        end
        wslot.output_def_id = output_def_id
        wslot.ends_at = os.time() + dur
        write_character_sheet(user_id, sheet)
        return encode_character_ok_response(sheet, progress, user_id)
      end

      local err_text = tostring(w_err)
      if string.find(err_text, "version", 1, true) == nil or attempt == max_retries then
        error(w_err)
      end
    end

    return nk.json_encode({ ok = false, err = "retry_exhausted" })
  end)

  if not ok then
    nk.logger_error("duel_workshop_craft_start: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

local function duel_workshop_craft_claim(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local ok_epoch, err_epoch = guard_assert_client_epoch_matches(user_id, payload)
    if not ok_epoch then
      return nk.json_encode({ ok = false, err = err_epoch })
    end

    local p = {}
    if payload ~= nil and payload ~= "" then
      p = nk.json_decode(payload) or {}
    end
    local slot_index = tonumber(p.slot_index)
    if slot_index == nil then
      return nk.json_encode({ ok = false, err = "bad_slot_index" })
    end
    slot_index = math.floor(slot_index)
    if slot_index < 0 or slot_index > 7 then
      return nk.json_encode({ ok = false, err = "bad_slot_index" })
    end

    ensure_character_sheet_initialized(user_id)
    local sheet = read_character_sheet(user_id)
    ensure_sheet_inventory_counts(sheet)
    ensure_sheet_workshop(sheet)
    local wslot = sheet.workshop_slots[slot_index + 1]
    local oid = tostring(wslot.output_def_id or "")
    if oid == "" then
      return nk.json_encode({ ok = false, err = "empty_workshop_slot" })
    end
    local wend = tonumber(wslot.ends_at) or 0
    if wend > os.time() then
      return nk.json_encode({ ok = false, err = "craft_not_ready" })
    end

    if inventory_try_add(sheet, oid, 1) ~= true then
      return nk.json_encode({ ok = false, err = "inventory_full" })
    end

    wslot.output_def_id = ""
    wslot.ends_at = 0
    write_character_sheet(user_id, sheet)

    local progress = read_pve_progress(user_id)
    return encode_character_ok_response(sheet, progress, user_id)
  end)

  if not ok then
    nk.logger_error("duel_workshop_craft_claim: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

local function duel_player_resources_get(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local progress = read_pve_progress(user_id)
    local resources = build_resource_payload(progress, user_id)
    resources.ok = true
    return nk.json_encode(resources)
  end)

  if not ok then
    nk.logger_error("duel_player_resources_get: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

local function duel_player_resources_spend(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local ok_epoch, err_epoch = guard_assert_client_epoch_matches(user_id, payload)
    if not ok_epoch then
      return nk.json_encode({ ok = false, err = err_epoch })
    end

    local p = {}
    if payload ~= nil and payload ~= "" then
      p = nk.json_decode(payload) or {}
    end

    local resource = tostring(p.resource or "")
    local amount = clamp_int(p.amount, 0, nil)
    local reason = tostring(p.reason or "")
    if resource ~= "energy" then
      return nk.json_encode({ ok = false, err = "unsupported_resource" })
    end
    if amount <= 0 then
      return nk.json_encode({ ok = false, err = "bad_amount" })
    end

    local max_retries = 5
    for i = 1, max_retries do
      local now = os.time()
      local progress, version = read_pve_progress(user_id)
      local energy_max = pve_energy_max_for_user(user_id)
      local available = clamp_int(progress.energy, 0, energy_max)
      if available < amount then
        local resources = build_resource_payload(progress, user_id)
        resources.ok = false
        resources.err = "not_enough_energy"
        resources.resource = resource
        resources.reason = reason
        resources.required = amount
        return nk.json_encode(resources)
      end

      progress.energy = available - amount
      progress.energy_updated_at = now

      local write_ok, write_err = pcall(function()
        write_pve_progress(user_id, progress, version)
      end)
      if write_ok then
        local resources = build_resource_payload(progress, user_id)
        resources.ok = true
        resources.resource = resource
        resources.reason = reason
        resources.spent = amount
        return nk.json_encode(resources)
      end

      local err_text = tostring(write_err)
      if string.find(err_text, "version", 1, true) == nil or i == max_retries then
        error(write_err)
      end
    end

    return nk.json_encode({ ok = false, err = "retry_exhausted" })
  end)

  if not ok then
    nk.logger_error("duel_player_resources_spend: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

local function duel_pve_energy_buy(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end
    local ok_epoch, err_epoch = guard_assert_client_epoch_matches(user_id, payload)
    if not ok_epoch then
      return nk.json_encode({ ok = false, err = err_epoch })
    end
    local p = {}
    if payload ~= nil and payload ~= "" then
      p = nk.json_decode(payload) or {}
    end
    local mode = tostring(p.mode or "")
    if mode ~= "matter" and mode ~= "gold" then
      return nk.json_encode({ ok = false, err = "bad_mode" })
    end
    local matter_cost = math.max(1, math.floor(tonumber(CFG.PVE_ENERGY_BUY_MATTER_COST) or 1))
    local matter_grant = math.max(1, math.floor(tonumber(CFG.PVE_ENERGY_BUY_MATTER_GRANT) or 100))
    local gold_cost = math.max(1, math.floor(tonumber(CFG.PVE_ENERGY_BUY_GOLD_COST) or 1000))
    local gold_grant = math.max(1, math.floor(tonumber(CFG.PVE_ENERGY_BUY_GOLD_GRANT) or 100))
    local max_retries = 5
    for i = 1, max_retries do
      local progress, version = read_pve_progress(user_id)
      local energy_max = pve_energy_max_for_user(user_id)
      local e = clamp_int(progress.energy, 0, energy_max)
      if e >= energy_max then
        return nk.json_encode({ ok = false, err = "energy_full" })
      end
      if mode == "matter" then
        local m = math.max(0, tonumber(progress.matter) or 0)
        if m < matter_cost then
          return nk.json_encode({ ok = false, err = "not_enough_matter" })
        end
        local add = matter_grant
        if e + add > energy_max then
          return nk.json_encode({ ok = false, err = "energy_full" })
        end
        progress.matter = m - matter_cost
        progress.energy = e + add
      else
        local g = math.max(0, tonumber(progress.gold) or 0)
        if g < gold_cost then
          return nk.json_encode({ ok = false, err = "not_enough_gold" })
        end
        local add = gold_grant
        if e + add > energy_max then
          return nk.json_encode({ ok = false, err = "energy_full" })
        end
        progress.gold = g - gold_cost
        progress.energy = e + add
      end
      progress.energy = clamp_int(progress.energy, 0, energy_max)
      progress.energy_updated_at = os.time()
      local write_ok, write_err = pcall(function()
        write_pve_progress(user_id, progress, version)
      end)
      if write_ok then
        local resources = build_resource_payload(progress, user_id)
        resources.ok = true
        return nk.json_encode(resources)
      end
      local err_text = tostring(write_err)
      if string.find(err_text, "version", 1, true) == nil or i == max_retries then
        error(write_err)
      end
    end
    return nk.json_encode({ ok = false, err = "retry_exhausted" })
  end)
  if not ok then
    nk.logger_error("duel_pve_energy_buy: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

local function duel_workshop_craft_rush(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end
    local ok_epoch, err_epoch = guard_assert_client_epoch_matches(user_id, payload)
    if not ok_epoch then
      return nk.json_encode({ ok = false, err = err_epoch })
    end
    local p = {}
    if payload ~= nil and payload ~= "" then
      p = nk.json_decode(payload) or {}
    end
    local slot_index = tonumber(p.slot_index)
    if slot_index == nil or slot_index < 0 or slot_index > 7 then
      return nk.json_encode({ ok = false, err = "bad_slot_index" })
    end
    slot_index = math.floor(slot_index)
    local rush_gold = math.max(1, math.floor(tonumber(CFG.WORKSHOP_CRAFT_RUSH_GOLD) or 500))
    local rush_sec = math.max(60, math.floor(tonumber(CFG.WORKSHOP_CRAFT_RUSH_SECONDS) or 1200))
    ensure_character_sheet_initialized(user_id)
    local max_retries = 5
    for attempt = 1, max_retries do
      local progress, version = read_pve_progress(user_id)
      if (tonumber(progress.gold) or 0) < rush_gold then
        return nk.json_encode({ ok = false, err = "not_enough_gold" })
      end
      local sheet = read_character_sheet(user_id)
      ensure_sheet_inventory_counts(sheet)
      ensure_sheet_workshop(sheet)
      local wslot = sheet.workshop_slots[slot_index + 1]
      if wslot == nil or tostring(wslot.output_def_id or "") == "" then
        return nk.json_encode({ ok = false, err = "empty_workshop_slot" })
      end
      local wend = tonumber(wslot.ends_at) or 0
      if wend <= os.time() then
        return nk.json_encode({ ok = false, err = "craft_already_ready" })
      end
      progress.gold = (tonumber(progress.gold) or 0) - rush_gold
      wend = wend - rush_sec
      if wend < os.time() then
        wend = os.time()
      end
      wslot.ends_at = wend
      local w_ok, w_err = pcall(function()
        write_pve_progress(user_id, progress, version)
      end)
      if w_ok then
        write_character_sheet(user_id, sheet)
        return encode_character_ok_response(sheet, progress, user_id)
      end
      local err_text = tostring(w_err)
      if string.find(err_text, "version", 1, true) == nil or attempt == max_retries then
        error(w_err)
      end
    end
    return nk.json_encode({ ok = false, err = "retry_exhausted" })
  end)
  if not ok then
    nk.logger_error("duel_workshop_craft_rush: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

-- Must mirror duel_matchmaker.lua: Nakama resolves Lua match modules under different names per version.
local function try_match_create(setup)
  local names = { "duel_match3", "modules/duel_match3", "modules.duel_match3" }
  for _, name in ipairs(names) do
    local ok, match_id_or_err = pcall(nk.match_create, name, setup)
    if ok and match_id_or_err then
      return match_id_or_err
    end
  end
  return nil
end

local arena_factory = runtime_lua_require("modules.arena_tournament", "arena_tournament")
local Arena = arena_factory({
  try_match_create = try_match_create,
  make_bot_user_id = make_bot_user_id,
  guard_read_metadata_epoch = guard_read_metadata_epoch,
  guard_assert_client_epoch_matches = guard_assert_client_epoch_matches,
  read_pve_progress = read_pve_progress,
  write_pve_progress = write_pve_progress,
  read_character_sheet = read_character_sheet,
  write_character_sheet = write_character_sheet,
  ensure_sheet_inventory_counts = ensure_sheet_inventory_counts,
  inventory_remove_def_total = inventory_remove_def_total,
  inventory_try_add = inventory_try_add,
})
arena_mirror_commit = Arena.mirror_commit
arena_on_match_finished = Arena.on_match_finished


function parse_floor_from_bot_id(bot_id)
  local sid = tostring(bot_id or "")
  local n = string.match(sid, "mine_(%d+)")
  return clamp_int(n, 1, CFG.PVE_MAX_LEVEL)
end

function get_unlocked_floor(progress, diff)
  local d = normalize_mine_difficulty(diff)
  local mine = progress.mine or {}
  local unlocked = normalize_mine_unlocked(mine.unlocked, 1)
  return clamp_int(unlocked[d] or 1, 0, CFG.PVE_MAX_LEVEL)
end

function floor_respawn_left_seconds(progress, diff, floor)
  local mine = progress.mine or {}
  local floor_states = type(mine.floor_states) == "table" and mine.floor_states or {}
  local k = make_floor_state_key(diff, floor)
  local s = type(floor_states[k]) == "table" and floor_states[k] or nil
  if s == nil then return 0 end
  local left = math.floor((tonumber(s.next_spawn_at) or 0) - os.time())
  return math.max(0, left)
end

local function duel_match3_pve_create(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local ok_epoch, err_epoch = guard_assert_client_epoch_matches(user_id, payload)
    if not ok_epoch then
      return nk.json_encode({ ok = false, err = err_epoch })
    end

    local owner_epoch = guard_read_metadata_epoch(user_id)

    local p = {}
    if payload ~= nil and payload ~= "" then
      p = nk.json_decode(payload) or {}
    end

    local requested_bot_id = tostring(p.bot_id or mine_bot_id_for_floor(1))
    local requested_diff = normalize_mine_difficulty(p.difficulty)
    local requested_floor = clamp_int((p.floor ~= nil and p.floor or parse_floor_from_bot_id(requested_bot_id)), 1, CFG.PVE_MAX_LEVEL)
    local fallback_bot = get_bot_profile(mine_bot_id_for_floor(requested_floor), requested_diff)
    local bot = get_bot_profile(requested_bot_id, requested_diff)
    if bot == nil or bot.id == nil or bot.id == "" then
      bot = fallback_bot
    end
    local floor = clamp_int(bot.floor or requested_floor, 1, CFG.PVE_MAX_LEVEL)
    local diff = requested_diff
    local bot_user_id = make_bot_user_id(bot.id)
    local max_retries = 5

    for i = 1, max_retries do
      local now = os.time()
      local progress, version = read_pve_progress(user_id)
      progress.mine = progress.mine or {}
      progress.mine.current_difficulty = diff
      progress.mine.selected_floor = floor
      progress.mine.unlocked = normalize_mine_unlocked(progress.mine.unlocked, 1)
      if type(progress.mine.floor_states) ~= "table" then progress.mine.floor_states = {} end

      local unlocked_floor = get_unlocked_floor(progress, diff)
      if floor > unlocked_floor then
        return nk.json_encode({
          ok = false,
          err = "barrier_locked",
          floor = floor,
          unlocked_floor = unlocked_floor,
          difficulty = diff,
        })
      end

      local respawn_left = floor_respawn_left_seconds(progress, diff, floor)
      if respawn_left > 0 then
        return nk.json_encode({
          ok = false,
          err = "monster_respawn_pending",
          floor = floor,
          difficulty = diff,
          respawn_left_seconds = respawn_left,
        })
      end

      local energy_max = pve_energy_max_for_user(user_id)
      local cost_list = PveMineCost.normalize(bot.cost_attack, PveMineCost.DEFAULT_ATTACK)
      local ok_cost, miss_res, need_amt, have_amt = PveMineCost.can_afford(progress, user_id, cost_list)
      if not ok_cost then
        local payload = PveMineCost.json_not_enough(progress, user_id, miss_res, need_amt, have_amt)
        payload.ok = false
        return nk.json_encode(payload)
      end

      PveMineCost.apply_list(progress, cost_list)
      local state_key = make_floor_state_key(diff, floor)
      local fstate = type(progress.mine.floor_states[state_key]) == "table" and progress.mine.floor_states[state_key] or {}
      local affix = tostring(fstate.last_affix or "")
      if affix == "" then
        affix = random_affix_for_floor(floor)
        fstate.last_affix = affix
      end
      progress.mine.floor_states[state_key] = fstate

      local write_ok, write_err = pcall(function()
        write_pve_progress(user_id, progress, version)
      end)
      if not write_ok then
        local err_text = tostring(write_err)
        if string.find(err_text, "version", 1, true) ~= nil and i < max_retries then
          -- retry
        else
          error(write_err)
        end
      else
        local match_id = try_match_create({
          mode = "pve",
          owner_user_id = user_id,
          bot_id = bot.id,
          bot_user_id = bot_user_id,
          owner_level = progress.level or 1,
          owner_session_epoch = owner_epoch,
          pve_run = {
            floor = floor,
            difficulty = diff,
            affix = affix,
            stat_mul = mine_stat_multiplier(diff),
            reward_mul = mine_reward_multiplier(diff),
          },
        })
        if match_id == nil or match_id == "" then
          return nk.json_encode({ ok = false, err = "match_create_failed" })
        end

        return nk.json_encode({
          ok = true,
          match_id = match_id,
          bot_id = bot.id,
          bot_name = bot.name,
          bot_user_id = bot_user_id,
          floor = floor,
          difficulty = diff,
          affix = affix,
          energy = progress.energy,
          energy_max = energy_max,
        })
      end
    end

    return nk.json_encode({ ok = false, err = "retry_exhausted" })
  end)

  if not ok then
    nk.logger_error("duel_match3_pve_create: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

local function duel_mine_summon(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local ok_epoch, err_epoch = guard_assert_client_epoch_matches(user_id, payload)
    if not ok_epoch then
      return nk.json_encode({ ok = false, err = err_epoch })
    end

    local p = {}
    if payload ~= nil and payload ~= "" then
      p = nk.json_decode(payload) or {}
    end

    local requested_floor = clamp_int(p.floor, 1, CFG.PVE_MAX_LEVEL)
    local requested_diff = normalize_mine_difficulty(p.difficulty)
    local max_retries = 5

    for i = 1, max_retries do
      local now = os.time()
      local progress, version = read_pve_progress(user_id)
      progress.mine = progress.mine or {}
      progress.mine.unlocked = normalize_mine_unlocked(progress.mine.unlocked, 1)
      progress.mine.current_difficulty = requested_diff
      progress.mine.selected_floor = requested_floor
      if type(progress.mine.floor_states) ~= "table" then progress.mine.floor_states = {} end

      local unlocked_floor = get_unlocked_floor(progress, requested_diff)
      if requested_floor > unlocked_floor then
        return nk.json_encode({
          ok = false,
          err = "barrier_locked",
          floor = requested_floor,
          unlocked_floor = unlocked_floor,
          difficulty = requested_diff,
        })
      end

      if is_boss_floor(requested_floor) then
        return nk.json_encode({
          ok = false,
          err = "boss_summon_forbidden",
          floor = requested_floor,
          difficulty = requested_diff,
        })
      end

      local respawn_left = floor_respawn_left_seconds(progress, requested_diff, requested_floor)
      if respawn_left <= 0 then
        return nk.json_encode({
          ok = true,
          floor = requested_floor,
          difficulty = requested_diff,
          respawn_left_seconds = 0,
          resources = build_resource_payload(progress, user_id),
          progression = build_progression_payload_auto(progress, user_id),
        })
      end

      local energy_max = pve_energy_max_for_user(user_id)
      local available_energy = clamp_int(progress.energy, 0, energy_max)
      if available_energy < CFG.MINE_SUMMON_ENERGY_COST then
        return nk.json_encode({
          ok = false,
          err = "not_enough_energy",
          required = CFG.MINE_SUMMON_ENERGY_COST,
          energy = available_energy,
          energy_max = energy_max,
        })
      end

      local available_gold = math.max(0, tonumber(progress.gold) or 0)
      if available_gold < CFG.MINE_SUMMON_GOLD_COST then
        return nk.json_encode({
          ok = false,
          err = "not_enough_gold",
          required = CFG.MINE_SUMMON_GOLD_COST,
          gold = available_gold,
        })
      end

      progress.energy = available_energy - CFG.MINE_SUMMON_ENERGY_COST
      progress.energy_updated_at = now
      progress.gold = available_gold - CFG.MINE_SUMMON_GOLD_COST

      local state_key = make_floor_state_key(requested_diff, requested_floor)
      local floor_state = type(progress.mine.floor_states[state_key]) == "table" and progress.mine.floor_states[state_key] or {}
      floor_state.next_spawn_at = now
      if tostring(floor_state.last_affix or "") == "" then
        floor_state.last_affix = random_affix_for_floor(requested_floor)
      end
      progress.mine.floor_states[state_key] = floor_state

      local write_ok, write_err = pcall(function()
        write_pve_progress(user_id, progress, version)
      end)
      if write_ok then
        return nk.json_encode({
          ok = true,
          floor = requested_floor,
          difficulty = requested_diff,
          respawn_left_seconds = 0,
          summon_cost = { energy = CFG.MINE_SUMMON_ENERGY_COST, gold = CFG.MINE_SUMMON_GOLD_COST },
          resources = build_resource_payload(progress, user_id),
          progression = build_progression_payload_auto(progress, user_id),
        })
      end

      local err_text = tostring(write_err)
      if string.find(err_text, "version", 1, true) == nil or i == max_retries then
        error(write_err)
      end
    end

    return nk.json_encode({ ok = false, err = "retry_exhausted" })
  end)

  if not ok then
    nk.logger_error("duel_mine_summon: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

function duel_mine_affix_reroll(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local ok_epoch, err_epoch = guard_assert_client_epoch_matches(user_id, payload)
    if not ok_epoch then
      return nk.json_encode({ ok = false, err = err_epoch })
    end

    local p = {}
    if payload ~= nil and payload ~= "" then
      p = nk.json_decode(payload) or {}
    end

    local requested_floor = clamp_int(p.floor, 1, CFG.PVE_MAX_LEVEL)
    local requested_diff = normalize_mine_difficulty(p.difficulty)
    local max_retries = 5
    local bid = mine_bot_id_for_floor(requested_floor)
    local bot_for_cost = get_bot_profile(bid, requested_diff)
    local banish_cost = PveMineCost.normalize(bot_for_cost.cost_banish, PveMineCost.DEFAULT_BANISH)

    for i = 1, max_retries do
      local progress, version = read_pve_progress(user_id)
      progress.mine = progress.mine or {}
      progress.mine.unlocked = normalize_mine_unlocked(progress.mine.unlocked, 1)
      progress.mine.current_difficulty = requested_diff
      progress.mine.selected_floor = requested_floor
      if type(progress.mine.floor_states) ~= "table" then progress.mine.floor_states = {} end

      local unlocked_floor = get_unlocked_floor(progress, requested_diff)
      if requested_floor > unlocked_floor then
        return nk.json_encode({
          ok = false,
          err = "barrier_locked",
          floor = requested_floor,
          unlocked_floor = unlocked_floor,
          difficulty = requested_diff,
        })
      end

      local ok_cost, miss_res, need_amt, have_amt = PveMineCost.can_afford(progress, user_id, banish_cost)
      if not ok_cost then
        local payload = PveMineCost.json_not_enough(progress, user_id, miss_res, need_amt, have_amt)
        payload.ok = false
        return nk.json_encode(payload)
      end

      PveMineCost.apply_list(progress, banish_cost)

      local state_key = make_floor_state_key(requested_diff, requested_floor)
      local floor_state = type(progress.mine.floor_states[state_key]) == "table" and progress.mine.floor_states[state_key] or {}
      local cur = tostring(floor_state.last_affix or "")
      local next_affix = cur
      if #CFG.MINE_AFFIX_POOL == 0 then
        next_affix = ""
      elseif #CFG.MINE_AFFIX_POOL == 1 then
        next_affix = tostring(CFG.MINE_AFFIX_POOL[1] or "")
      else
        local g = 0
        while next_affix == cur and g < 32 do
          local ri = math.random(1, #CFG.MINE_AFFIX_POOL)
          next_affix = tostring(CFG.MINE_AFFIX_POOL[ri] or "")
          g = g + 1
        end
      end
      floor_state.last_affix = next_affix
      progress.mine.floor_states[state_key] = floor_state

      local write_ok, write_err = pcall(function()
        write_pve_progress(user_id, progress, version)
      end)
      if write_ok then
        return nk.json_encode({
          ok = true,
          floor = requested_floor,
          difficulty = requested_diff,
          affix = floor_state.last_affix,
          reroll_cost = banish_cost,
          resources = build_resource_payload(progress, user_id),
          progression = build_progression_payload_auto(progress, user_id),
        })
      end

      local err_text = tostring(write_err)
      if string.find(err_text, "version", 1, true) == nil or i == max_retries then
        error(write_err)
      end
    end

    return nk.json_encode({ ok = false, err = "retry_exhausted" })
  end)

  if not ok then
    nk.logger_error("duel_mine_affix_reroll: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

function duel_mine_barrier_unlock(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local ok_epoch, err_epoch = guard_assert_client_epoch_matches(user_id, payload)
    if not ok_epoch then
      return nk.json_encode({ ok = false, err = err_epoch })
    end

    local p = {}
    if payload ~= nil and payload ~= "" then
      p = nk.json_decode(payload) or {}
    end
    local target_floor = clamp_int(p.floor, 2, CFG.PVE_MAX_LEVEL)
    local diff = normalize_mine_difficulty(p.difficulty)
    local req = MINE_BARRIER_REQUIREMENTS[target_floor]
    if req == nil then
      return nk.json_encode({ ok = false, err = "bad_floor" })
    end

    local max_retries = 5
    for i = 1, max_retries do
      local progress, version = read_pve_progress(user_id)
      progress.mine = progress.mine or {}
      progress.mine.unlocked = normalize_mine_unlocked(progress.mine.unlocked, 1)

      local unlocked = get_unlocked_floor(progress, diff)
      if unlocked >= target_floor then
        return nk.json_encode({
          ok = true,
          floor = target_floor,
          difficulty = diff,
          progression = build_progression_payload_auto(progress, user_id),
        })
      end
      if unlocked < (target_floor - 1) then
        return nk.json_encode({
          ok = false,
          err = "prev_floor_locked",
          unlocked_floor = unlocked,
          required_prev_floor = target_floor - 1,
          difficulty = diff,
        })
      end

      local need_ore = math.max(0, tonumber(req.ore) or 0)
      local need_gold = math.max(0, tonumber(req.gold) or 0)
      local need_matter = math.max(0, tonumber(req.matter) or 0)
      local key_id = tostring(req.key_id or "")
      local key_amount = math.max(0, tonumber(req.key_amount) or 0)

      if (progress.ore or 0) < need_ore then
        return nk.json_encode({ ok = false, err = "not_enough_ore", required = need_ore, ore = progress.ore or 0 })
      end
      if (progress.gold or 0) < need_gold then
        return nk.json_encode({ ok = false, err = "not_enough_gold", required = need_gold, gold = progress.gold or 0 })
      end
      if (progress.matter or 0) < need_matter then
        return nk.json_encode({ ok = false, err = "not_enough_matter", required = need_matter, matter = progress.matter or 0 })
      end
      if key_id ~= "" and key_amount > 0 then
        progress.key_items = progress.key_items or empty_key_items()
        local have_keys = math.max(0, tonumber(progress.key_items[key_id]) or 0)
        if have_keys < key_amount then
          return nk.json_encode({
            ok = false,
            err = "not_enough_key_item",
            key_id = key_id,
            required = key_amount,
            have = have_keys,
          })
        end
      end

      progress.ore = math.max(0, (progress.ore or 0) - need_ore)
      progress.gold = math.max(0, (progress.gold or 0) - need_gold)
      progress.matter = math.max(0, (progress.matter or 0) - need_matter)
      if key_id ~= "" and key_amount > 0 then
        progress.key_items[key_id] = math.max(0, (tonumber(progress.key_items[key_id]) or 0) - key_amount)
      end
      progress.mine.unlocked[diff] = math.max(unlocked, target_floor)
      progress.mine.selected_floor = target_floor
      progress.mine.current_difficulty = diff

      if type(progress.mine.floor_states) ~= "table" then progress.mine.floor_states = {} end
      local sk = make_floor_state_key(diff, target_floor)
      local fs = type(progress.mine.floor_states[sk]) == "table" and progress.mine.floor_states[sk] or {}
      fs.last_affix = random_affix_for_floor(target_floor)
      progress.mine.floor_states[sk] = fs

      local write_ok, write_err = pcall(function()
        write_pve_progress(user_id, progress, version)
      end)
      if write_ok then
        return nk.json_encode({
          ok = true,
          floor = target_floor,
          difficulty = diff,
          progression = build_progression_payload_auto(progress, user_id),
          resources = build_resource_payload(progress, user_id),
        })
      end

      local err_text = tostring(write_err)
      if string.find(err_text, "version", 1, true) == nil or i == max_retries then
        error(write_err)
      end
    end

    return nk.json_encode({ ok = false, err = "retry_exhausted" })
  end)

  if not ok then
    nk.logger_error("duel_mine_barrier_unlock: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

local function validate_action_basic(state, sender_id, action)
  if state.ended then return false, "game_ended" end
  if not state.started then return false, "not_started" end
  if sender_id ~= state.active_user_id then return false, "not_your_turn" end
  if action == nil then return false, "bad_payload" end

  if action.actionType == 1 then
    if not in_active_client(action.fromX, action.fromY) or not in_active_client(action.toX, action.toY) then
      return false, "out_of_bounds"
    end
    if math.abs(action.fromX - action.toX) + math.abs(action.fromY - action.toY) ~= 1 then
      return false, "not_adjacent"
    end
    return true, nil
  end

  if action.actionType == 2 or action.actionType == 3 or action.actionType == 4 then
    if (action.actionType == 2 or action.actionType == 3) and not in_active_client(action.cx, action.cy) then
      return false, "out_of_bounds"
    end
    local st = state.stats[sender_id]
    local need_mana = action_mana_cost(state, action.actionType)
    if st.mana < need_mana then return false, "not_enough_mana" end
    if action.actionType == 2 and st.cross_cd > 0 then return false, "cross_on_cooldown" end
    if action.actionType == 3 and st.square_cd > 0 then return false, "square_on_cooldown" end
    if action.actionType == 4 and st.petard_cd > 0 then return false, "petard_on_cooldown" end
    return true, nil
  end

  if action.actionType == 5 or action.actionType == 6 then
    local st = state.stats[sender_id]
    local need_mana = action_mana_cost(state, action.actionType)
    if st.mana < need_mana then return false, "not_enough_mana" end
    if action.actionType == 5 and (st.shield_cd or 0) > 0 then return false, "shield_on_cooldown" end
    if action.actionType == 6 and (st.fury_cd or 0) > 0 then return false, "fury_on_cooldown" end
    return true, nil
  end

  return false, "unknown_action"
end

local function enumerate_valid_swaps(board)
  local swaps = {}
  for y = 0, CFG.ACTIVE_ROWS - 1 do
    for x = 0, CFG.SIZE - 1 do
      if x + 1 < CFG.SIZE then
        local sim = clone_board(board)
        local ok, _ = try_swap(sim, x, client_to_server_y(y), x + 1, client_to_server_y(y))
        if ok then swaps[#swaps + 1] = { actionType = 1, fromX = x, fromY = y, toX = x + 1, toY = y, cx = -1, cy = -1 } end
      end
      if y + 1 < CFG.ACTIVE_ROWS then
        local sim = clone_board(board)
        local ok, _ = try_swap(sim, x, client_to_server_y(y), x, client_to_server_y(y + 1))
        if ok then swaps[#swaps + 1] = { actionType = 1, fromX = x, fromY = y, toX = x, toY = y + 1, cx = -1, cy = -1 } end
      end
    end
  end
  return swaps
end

local function copy_stats(src)
  return {
    hp = src.hp or CFG.MAX_HP,
    mana = src.mana or 0,
    cross_cd = src.cross_cd or 0,
    square_cd = src.square_cd or 0,
    petard_cd = src.petard_cd or 0,
    shield_cd = src.shield_cd or 0,
    fury_cd = src.fury_cd or 0,
    shield_t1 = src.shield_t1 or 0,
    shield_t2 = src.shield_t2 or 0,
    shield_t3 = src.shield_t3 or 0,
    fury_active = src.fury_active == true,
    max_hp = src.max_hp or CFG.MAX_HP,
    initial_hp = src.initial_hp or src.max_hp or CFG.MAX_HP,
    base_damage = src.base_damage or 0,
    base_armor = src.base_armor or 0,
    base_crit = src.base_crit or 0,
    base_heal = src.base_heal or 0,
  }
end

local function spend_ability_for_sim(state, stats, action_type)
  if action_type == 2 then
    stats.mana = math.max(0, stats.mana - action_mana_cost(state, action_type))
    stats.cross_cd = CFG.CROSS_ABILITY_COOLDOWN
  elseif action_type == 3 then
    stats.mana = math.max(0, stats.mana - action_mana_cost(state, action_type))
    stats.square_cd = CFG.SQUARE_ABILITY_COOLDOWN
  elseif action_type == 4 then
    stats.mana = math.max(0, stats.mana - action_mana_cost(state, action_type))
    stats.petard_cd = CFG.PETARD_ABILITY_COOLDOWN
  elseif action_type == 5 then
    stats.mana = math.max(0, stats.mana - action_mana_cost(state, action_type))
    stats.shield_cd = CFG.SHIELD_ABILITY_COOLDOWN
    apply_shield_stack(stats)
  elseif action_type == 6 then
    stats.mana = math.max(0, stats.mana - action_mana_cost(state, action_type))
    stats.fury_cd = CFG.FURY_ABILITY_COOLDOWN
    stats.fury_active = true
  end
end

local function simulate_and_score_action(state, bot_user_id, player_user_id, action)
  local sim_bot = copy_stats(state.stats[bot_user_id] or {})
  local sim_player = copy_stats(state.stats[player_user_id] or {})
  local sim_state = {
    board = clone_board(state.board),
    stats = {
      [bot_user_id] = sim_bot,
      [player_user_id] = sim_player,
    },
    _sim_metrics = { extra_turn = false, red = 0, yellow = 0, green = 0 },
    _sim_quality_y_min = (tonumber(CFG.BOT_SIM_QUALITY_Y_MIN) or CFG.ACTIVE_Y_MIN),
  }

  if action.actionType == 2 or action.actionType == 3 or action.actionType == 4 or action.actionType == 5 or action.actionType == 6 then
    spend_ability_for_sim(state, sim_bot, action.actionType)
  end

  local before_hp = sim_player.hp
  local ok, _, extra_turn, _, _ = resolve_action(sim_state, action, bot_user_id, player_user_id)
  if not ok then return nil end

  local m = sim_state._sim_metrics or { extra_turn = false, red = 0, yellow = 0, green = 0 }
  local score = {
    extra_turn = (extra_turn == true) or (m.extra_turn == true),
    damage = math.max(0, before_hp - sim_player.hp),
    red = m.red or 0,
    yellow = m.yellow or 0,
    green = m.green or 0,
  }
  return score
end

local function is_better_score(a, b)
  if b == nil then return true end
  if a.extra_turn ~= b.extra_turn then return a.extra_turn end
  if a.damage ~= b.damage then return a.damage > b.damage end
  if a.red ~= b.red then return a.red > b.red end
  if a.yellow ~= b.yellow then return a.yellow > b.yellow end
  if a.green ~= b.green then return a.green > b.green end
  return false
end

local function swap_initial_matches_for_action(state, action)
  if action == nil or action.actionType ~= 1 then return nil end
  local board = clone_board(state.board)
  local fy = client_to_server_y(action.fromY)
  local ty = client_to_server_y(action.toY)
  local ok, matches = try_swap(board, action.fromX, fy, action.toX, ty)
  if not ok then return nil end
  return matches
end

local function match_has_five_plus_line(matches)
  if not matches then return false end
  for _, m in ipairs(matches) do
    if m.count >= 5 then return true end
  end
  return false
end

local function match_has_five_plus_skull(matches)
  if not matches then return false end
  for _, m in ipairs(matches) do
    if m.count >= 5 and m.type == 4 then return true end
  end
  return false
end

local function choose_bot_action(state, bot_user_id, player_user_id)
  local stats = state.stats[bot_user_id]
  if stats == nil then return nil end

  local cross_cost = action_mana_cost(state, 2)
  local square_cost = action_mana_cost(state, 3)
  local petard_cost = action_mana_cost(state, 4)
  local fury_cost = action_mana_cost(state, 6)
  local can_cross = stats.mana >= cross_cost and stats.cross_cd <= 0
  local can_square = stats.mana >= square_cost and stats.square_cd <= 0
  local can_petard = stats.mana >= petard_cost and stats.petard_cd <= 0
  local can_fury = stats.mana >= fury_cost and stats.fury_cd <= 0

  local player_stats = state.stats[player_user_id] or {}
  local player_hp = tonumber(player_stats.hp) or CFG.MAX_HP
  local mana = tonumber(stats.mana) or 0
  local frozen = has_affix(state, "frozen")

  local swaps = enumerate_valid_swaps(state.board)
  local best_extra_swap = nil
  local best_extra_score = nil
  local max_swap_damage = 0
  for _, action in ipairs(swaps) do
    local score = simulate_and_score_action(state, bot_user_id, player_user_id, action)
    if score ~= nil then
      if score.damage ~= nil and score.damage > max_swap_damage then max_swap_damage = score.damage end
      if score.extra_turn and is_better_score(score, best_extra_score) then
        best_extra_score = score
        best_extra_swap = action
      end
    end
  end

  -- в) Цепочка после ярости при старте с маны >= 80 (петарда + способность; при frozen — только петарда).
  local open_m = state.bot_fury_open_mana
  if stats.fury_active and open_m ~= nil and open_m >= 80 then
    if can_petard then
      return { actionType = 4, fromX = -1, fromY = -1, toX = -1, toY = -1, cx = -1, cy = -1 }
    end
    if not frozen then
      local best_ab = nil
      local best_sc = nil
      if can_cross then
        for y = 0, CFG.SIZE - 1 do
          for x = 0, CFG.SIZE - 1 do
            local ac = {
              actionType = 2,
              fromX = -1, fromY = -1, toX = -1, toY = -1,
              cx = x, cy = y,
            }
            local sc = simulate_and_score_action(state, bot_user_id, player_user_id, ac)
            if sc ~= nil and is_better_score(sc, best_sc) then
              best_sc = sc
              best_ab = ac
            end
          end
        end
      end
      if can_square then
        for y = 0, CFG.SIZE - 1 do
          for x = 0, CFG.SIZE - 1 do
            local ac = {
              actionType = 3,
              fromX = -1, fromY = -1, toX = -1, toY = -1,
              cx = x, cy = y,
            }
            local sc = simulate_and_score_action(state, bot_user_id, player_user_id, ac)
            if sc ~= nil and is_better_score(sc, best_sc) then
              best_sc = sc
              best_ab = ac
            end
          end
        end
      end
      if best_ab ~= nil then return best_ab end
    end
  end

  -- После ярости по п. а) — добить свапом 5+.
  if stats.fury_active and best_extra_swap ~= nil then
    return best_extra_swap
  end

  -- а) 5+ в линии + ярость (frozen: мана >= 70).
  if not stats.fury_active and can_fury and best_extra_swap ~= nil then
    local init_m = swap_initial_matches_for_action(state, best_extra_swap)
    local need_mana_a = frozen and 70 or 50
    if match_has_five_plus_line(init_m) and mana >= need_mana_a then
      return { actionType = 6, fromX = -1, fromY = -1, toX = -1, toY = -1, cx = -1, cy = -1 }
    end
  end

  -- б) 5+ черепов в линии + ярость, только свап (frozen: мана 40–49).
  if not stats.fury_active and can_fury then
    local lo = frozen and 40 or 31
    local hi = 49
    if mana >= lo and mana <= hi then
      for _, ac in ipairs(swaps) do
        local im = swap_initial_matches_for_action(state, ac)
        if match_has_five_plus_skull(im) then
          return { actionType = 6, fromX = -1, fromY = -1, toX = -1, toY = -1, cx = -1, cy = -1 }
        end
      end
    end
  end

  -- в) Открытие цепочки: мана >= 80 и хватает на ярость + петарду (+ способность без frozen).
  if not stats.fury_active and can_fury and can_petard then
    if frozen then
      if mana >= 80 and mana >= fury_cost + petard_cost then
        return { actionType = 6, fromX = -1, fromY = -1, toX = -1, toY = -1, cx = -1, cy = -1 }
      end
    else
      local min_ab = nil
      if can_cross then min_ab = cross_cost end
      if can_square and (min_ab == nil or square_cost < min_ab) then min_ab = square_cost end
      if min_ab ~= nil and mana >= 80 and mana >= fury_cost + petard_cost + min_ab then
        return { actionType = 6, fromX = -1, fromY = -1, toX = -1, toY = -1, cx = -1, cy = -1 }
      end
    end
  end

  -- “Молния/петарда” приоритетна, если:
  --  • маны > 50 (как и раньше),
  --  • либо маны >= 30 и петарда добивает прямо сейчас,
  --  • либо петарда + лучший свап по урону добивают (петарда сохраняет ход).
  if can_petard then
    if mana > 50
      or (mana >= petard_cost and CFG.PETARD_DAMAGE >= player_hp)
      or (mana >= petard_cost and (CFG.PETARD_DAMAGE + max_swap_damage) >= player_hp) then
      return { actionType = 4, fromX = -1, fromY = -1, toX = -1, toY = -1, cx = -1, cy = -1 }
    end
  end

  -- Если есть явный 5+ свап (extra turn) — берём его прежде любых способностей.
  if best_extra_swap ~= nil then
    return best_extra_swap
  end

  local candidates = swaps
  if can_cross then
    for y = 0, CFG.SIZE - 1 do
      for x = 0, CFG.SIZE - 1 do
        candidates[#candidates + 1] = {
          actionType = 2,
          fromX = -1, fromY = -1, toX = -1, toY = -1,
          cx = x, cy = y,
        }
      end
    end
  end
  if can_square then
    for y = 0, CFG.SIZE - 1 do
      for x = 0, CFG.SIZE - 1 do
        candidates[#candidates + 1] = {
          actionType = 3,
          fromX = -1, fromY = -1, toX = -1, toY = -1,
          cx = x, cy = y,
        }
      end
    end
  end
  if can_petard then
    candidates[#candidates + 1] = { actionType = 4, fromX = -1, fromY = -1, toX = -1, toY = -1, cx = -1, cy = -1 }
  end

  local best_action = nil
  local best_score = nil
  for _, action in ipairs(candidates) do
    local score = simulate_and_score_action(state, bot_user_id, player_user_id, action)
    if score ~= nil and is_better_score(score, best_score) then
      best_score = score
      best_action = action
    end
  end

  return best_action
end

local function match_init(context, params)
  local invited = {}
  if params and params.invited then
    for _, u in ipairs(params.invited) do
      local p = u.presence or u
      if p and p.user_id then invited[p.user_id] = true end
    end
  end

  local state = {
    invited = invited,
    mode = params and tostring(params.mode or "pvp") or "pvp",
    pvp_pro = truthy_match_param(params and params.pvp_pro),
    owner_user_id = params and params.owner_user_id or nil,
    owner_session_epoch = tonumber(params and params.owner_session_epoch or 0) or 0,
    bot_id = params and params.bot_id or mine_bot_id_for_floor(1),
    bot_user_id = params and params.bot_user_id or make_bot_user_id(params and params.bot_id or mine_bot_id_for_floor(1)),
    owner_level = tonumber(params and params.owner_level or 1) or 1,
    presences = {},
    players_sorted = {},
    stats = {},
    board = nil,
    started = false,
    ended = false,
    active_user_id = nil,
    turn_deadline_tick = 0,
    turn_deadline_paused = false,
    turn_pause_started_tick = 0,
    last_crit = false,
    last_reward = nil,
    bot_turn_pending = false,
    bot_turn_ready_tick = 0,
    bot_long_think_next = true,
    bot_fury_open_mana = nil,
    _bot_pre_mana = nil,
    pve_run = params and params.pve_run or nil,
    arena_mirror = params and params.arena_mirror or nil,
    --- Арена человек×бот: те же базовые статы, что у классического PvP (150 HP и т.д.), без шахты.
    arena_pvp_style = truthy_match_param(params and params.arena_pvp_style),
    reconnect_grace_for_user_id = nil,
    reconnect_deadline_tick = nil,
  }

  return state, CFG.TICK_RATE, "mode=duel_match3"
end

local function match_join_attempt(context, dispatcher, tick, state, presence, metadata)
  if state.ended then return state, false, "ended" end

  if state.mode == "pve" then
    if state.owner_user_id ~= nil and state.owner_user_id ~= "" and presence.user_id ~= state.owner_user_id then
      return state, false, "not_owner"
    end
    if count_present_players(state) >= 1 and state.presences[presence.user_id] == nil then
      return state, false, "full"
    end
    return state, true
  end

  if count_present_players(state) >= 2 and state.presences[presence.user_id] == nil then
    return state, false, "full"
  end

  local has_invites = next(state.invited) ~= nil
  if has_invites and not state.invited[presence.user_id] and state.presences[presence.user_id] == nil then
    return state, false, "not_invited"
  end

  return state, true
end

local function match_join(context, dispatcher, tick, state, presences)
  for _, p in ipairs(presences) do
    state.presences[p.user_id] = p
  end

  -- PvP: повторный вход после обрыва связи — полная рассылка состояния ушедшему игроку.
  if state.mode ~= "pve" and state.started and not state.ended and state.reconnect_grace_for_user_id ~= nil then
    for _, p in ipairs(presences) do
      if p.user_id == state.reconnect_grace_for_user_id then
        state.reconnect_grace_for_user_id = nil
        state.reconnect_deadline_tick = nil
        local cheat = {}
        if state.cheat_rows_allowed ~= nil and state.cheat_rows_allowed[p.user_id] == true then
          cheat = state.cheat_rows or {}
        end
        local msg = make_sync_msg(state, nil, false, nil, cheat, tick)
        dispatcher.broadcast_message(CFG.OP_BOARD_SYNC, nk.json_encode(msg), { p }, nil)
        return state
      end
    end
  end

  -- PvE: владелец вернулся в тот же матч после обрыва.
  if state.mode == "pve" and state.started and not state.ended and state.reconnect_grace_for_user_id ~= nil
      and state.owner_user_id ~= nil and state.owner_user_id ~= "" then
    for _, p in ipairs(presences) do
      if p.user_id == state.reconnect_grace_for_user_id and p.user_id == state.owner_user_id then
        state.reconnect_grace_for_user_id = nil
        state.reconnect_deadline_tick = nil
        local cheat = {}
        if state.cheat_rows_allowed ~= nil and state.cheat_rows_allowed[p.user_id] == true then
          cheat = state.cheat_rows or {}
        end
        local msg = make_sync_msg(state, nil, false, nil, cheat, tick)
        dispatcher.broadcast_message(CFG.OP_BOARD_SYNC, nk.json_encode(msg), { p }, nil)
        return state
      end
    end
  end

  if state.mode == "pve" then
    if not state.started and count_present_players(state) == 1 then
      local player_id = nil
      for uid, _ in pairs(state.presences) do player_id = uid end
      if player_id == nil then return state end

      -- Арена: один живой игрок vs серверный «бот» — правила как у authoritative PvP (150 HP, без экипа).
      if state.arena_pvp_style == true then
        state.bot_id = "arena_bot"
        -- IMPORTANT: arena_tournament may pass a unique bot_user_id per bracket slot.
        -- Do not override it if it's already set.
        if state.bot_user_id == nil or state.bot_user_id == "" then
          state.bot_user_id = make_bot_user_id("arena_bot")
        end

        state.started = true
        state.players_sorted = { player_id, state.bot_user_id }
        state.stats[player_id] = new_stats()
        state.stats[state.bot_user_id] = new_stats()

        state.board = init_board()

        state.cheat_rows = init_cheat_rows()
        state.spawn_queues = {}
        for x = 0, CFG.SIZE - 1 do ensure_spawn_queue(state, x) end
        update_cheat_rows_from_board(state)
        state.cheat_rows_allowed = {}
        local wl = build_cheat_whitelist_set({ player_id })
        local p_email = user_email_lower(player_id)
        local p_allowed = is_user_allowed_for_cheat_rows(player_id, wl)
        state.cheat_rows_allowed[player_id] = p_allowed
        state.cheat_rows_allowed[state.bot_user_id] = is_user_allowed_for_cheat_rows(state.bot_user_id, wl)
        nk.logger_info("duel_match3 cheat_rows_allowed (arena_pvp_style): user_id=" .. tostring(player_id) ..
          " email=" .. tostring(p_email) .. " allowed=" .. tostring(p_allowed))

        state.active_user_id = pick_first_actor(player_id, state.bot_user_id)

        tick_cooldowns(state.stats[state.active_user_id])
        state.turn_deadline_paused = true
        state.turn_pause_started_tick = tick
        state.turn_deadline_tick = tick
        if state.active_user_id == state.bot_user_id then
          state.bot_turn_pending = true
          state.bot_turn_ready_tick = 0
        else
          state.bot_turn_pending = false
          state.bot_turn_ready_tick = 0
        end
        broadcast_sync(dispatcher, state, nil, false, nil, tick)
        return state
      end

      local pve_diff = normalize_mine_difficulty((state.pve_run or {}).difficulty)
      local bot_profile = get_bot_profile(state.bot_id, pve_diff)
      state.bot_id = bot_profile.id
      if state.bot_user_id == nil or state.bot_user_id == "" then
        state.bot_user_id = make_bot_user_id(state.bot_id)
      end

      state.started = true
      state.players_sorted = { player_id, state.bot_user_id }
      state.stats[player_id] = new_stats()
      state.stats[state.bot_user_id] = new_stats()
      local player_level = math.max(1, math.min(CFG.PVE_MAX_LEVEL, tonumber(state.owner_level) or 1))
      local base = character_stats_base_for_level(player_level)
      local sheet = read_character_sheet(player_id)
      local merged = merge_stats_with_equipment(base, sum_equipment_bonuses(sheet))
      state.stats[player_id].max_hp = tonumber(merged.hp) or CFG.MAX_HP
      state.stats[player_id].hp = state.stats[player_id].max_hp
      state.stats[player_id].initial_hp = state.stats[player_id].max_hp
      state.stats[player_id].base_damage = tonumber(merged.damage) or 0
      state.stats[player_id].base_armor = tonumber(merged.armor) or 0
      state.stats[player_id].base_crit = tonumber(merged.crit_chance) or 0
      state.stats[player_id].base_heal = tonumber(merged.healing) or 0

      aura_apply_to_pve_player_stats(state.stats[player_id], get_active_server_aura())

      local pve_run = state.pve_run or {}
      local stat_mul = tonumber(pve_run.stat_mul) or 1.0
      if stat_mul < 1 then stat_mul = 1.0 end
      local bot_hp_bonus = math.max(0, math.ceil((tonumber(bot_profile.hp_bonus) or 0) * stat_mul))
      local bot_start_mana = math.max(0, math.ceil((tonumber(bot_profile.start_mana) or 0) * stat_mul))
      state.stats[state.bot_user_id].max_hp = CFG.MAX_HP + bot_hp_bonus
      state.stats[state.bot_user_id].hp = state.stats[state.bot_user_id].max_hp
      state.stats[state.bot_user_id].mana = math.min(CFG.MAX_MANA, bot_start_mana)
      state.stats[state.bot_user_id].base_damage = math.max(0, math.ceil((tonumber(bot_profile.base_damage) or tonumber(bot_profile.damage) or 0) * stat_mul))
      state.stats[state.bot_user_id].base_armor = math.max(0, math.ceil((tonumber(bot_profile.base_armor) or tonumber(bot_profile.armor) or 0) * stat_mul))
      state.stats[state.bot_user_id].base_crit = math.max(0, (tonumber(bot_profile.base_crit) or tonumber(bot_profile.crit_chance) or 0) * stat_mul)
      state.stats[state.bot_user_id].base_heal = tonumber(bot_profile.base_heal) or tonumber(bot_profile.healing) or 0
      if has_affix(state, "fragility") then
        local bot = state.stats[state.bot_user_id]
        bot.max_hp = math.max(1, math.floor(bot.max_hp * 0.5 + 0.5))
        bot.hp = math.min(bot.hp or bot.max_hp, bot.max_hp)
        bot.mana = math.min(CFG.MAX_MANA, (tonumber(bot.mana) or 0) + 50)
        bot.base_crit = math.max(0, tonumber(bot.base_crit) or 0) + 0.35
      end
      if has_affix(state, "bare_current") then
        state.stats[player_id].mana = 0
        state.stats[state.bot_user_id].mana = 0
      end
      state.stats[state.bot_user_id].initial_hp = state.stats[state.bot_user_id].max_hp
      state.board = init_board()

      state.cheat_rows = init_cheat_rows()
      state.spawn_queues = {}
      for x = 0, CFG.SIZE - 1 do ensure_spawn_queue(state, x) end
      update_cheat_rows_from_board(state)
      state.cheat_rows_allowed = {}
      local wl = build_cheat_whitelist_set({ player_id })
      local p_email = user_email_lower(player_id)
      local p_allowed = is_user_allowed_for_cheat_rows(player_id, wl)
      state.cheat_rows_allowed[player_id] = p_allowed
      state.cheat_rows_allowed[state.bot_user_id] = is_user_allowed_for_cheat_rows(state.bot_user_id, wl)
      nk.logger_info("duel_match3 cheat_rows_allowed (pve): user_id=" .. tostring(player_id) ..
        " email=" .. tostring(p_email) .. " allowed=" .. tostring(p_allowed))

      state.active_user_id = pick_first_actor(player_id, state.bot_user_id)

      tick_cooldowns(state.stats[state.active_user_id])
      state.turn_deadline_paused = true
      state.turn_pause_started_tick = tick
      state.turn_deadline_tick = tick
      if state.active_user_id == state.bot_user_id then
        state.bot_turn_pending = true
        state.bot_turn_ready_tick = 0
      else
        state.bot_turn_pending = false
        state.bot_turn_ready_tick = 0
      end
      broadcast_sync(dispatcher, state, nil, false, nil, tick)
    end
    return state
  end

  if not state.started and count_present_players(state) == 2 then
    state.started = true
    state.players_sorted = sorted_two_players(state.presences)
    state.stats[state.players_sorted[1]] = new_stats()
    state.stats[state.players_sorted[2]] = new_stats()
    if state.pvp_pro == true and state.mode ~= "pve" then
      apply_pvp_pro_stats_from_sheet(state.stats[state.players_sorted[1]], state.players_sorted[1])
      apply_pvp_pro_stats_from_sheet(state.stats[state.players_sorted[2]], state.players_sorted[2])
      nk.logger_info("duel_match3: PvP Pro — статы уровень+экип для обоих игроков")
    end
    state.board = init_board()

    state.cheat_rows = init_cheat_rows()
    state.spawn_queues = {}
    for x = 0, CFG.SIZE - 1 do ensure_spawn_queue(state, x) end
    update_cheat_rows_from_board(state)
    state.cheat_rows_allowed = {}
    local wl = build_cheat_whitelist_set(state.players_sorted)
    for _, uid in ipairs(state.players_sorted) do
      local u_email = user_email_lower(uid)
      local allowed = is_user_allowed_for_cheat_rows(uid, wl)
      state.cheat_rows_allowed[uid] = allowed
      nk.logger_info("duel_match3 cheat_rows_allowed (pvp): user_id=" .. tostring(uid) ..
        " email=" .. tostring(u_email) .. " allowed=" .. tostring(allowed))
    end

    state.active_user_id = pick_first_actor(state.players_sorted[1], state.players_sorted[2])

    tick_cooldowns(state.stats[state.active_user_id])
    state.turn_deadline_paused = true
    state.turn_pause_started_tick = tick
    state.turn_deadline_tick = tick
    broadcast_sync(dispatcher, state, nil, false, nil, tick)
  end

  return state
end

local function match_leave(context, dispatcher, tick, state, presences)
  local left_duelist_uid = nil
  if state.players_sorted and state.started and not state.ended and state.mode ~= "pve" then
    for _, p in ipairs(presences) do
      for _, uid in ipairs(state.players_sorted) do
        if uid == p.user_id then
          left_duelist_uid = p.user_id
          break
        end
      end
      if left_duelist_uid then break end
    end
  end

  for _, p in ipairs(presences) do
    state.presences[p.user_id] = nil
  end

  if state.started and not state.ended then
    local count = count_present_players(state)
    -- Один дуэлянт ушёл по сети — не завершать матч сразу; дать время на JoinMatch.
    if state.mode ~= "pve" and count == 1 and left_duelist_uid ~= nil
        and state.players_sorted ~= nil and #state.players_sorted >= 2 then
      state.reconnect_grace_for_user_id = left_duelist_uid
      state.reconnect_deadline_tick = tick + math.floor(CFG.RECONNECT_GRACE_SECONDS * CFG.TICK_RATE + 0.5)
      local msg = { disconnectedUserId = left_duelist_uid, reconnectGraceSeconds = CFG.RECONNECT_GRACE_SECONDS }
      dispatcher.broadcast_message(CFG.OP_PEER_DISCONNECT, nk.json_encode(msg), nil, nil)
      return state
    end
    if count <= 1 and state.mode ~= "pve" then
      state.ended = true
      local winner = nil
      for uid, _ in pairs(state.presences) do winner = uid end
      if winner ~= nil then
        local ok_arena, err_arena = pcall(function()
          if type(arena_on_match_finished) == "function" then
            arena_on_match_finished(state, winner)
          end
        end)
        if not ok_arena then
          nk.logger_error("arena_on_match_finished failed (disconnect): " .. tostring(err_arena))
        end
        dispatcher.broadcast_message(CFG.OP_GAME_OVER, nk.json_encode({ winnerUserId = winner }), nil, nil)
      end
      return nil
    end
    if count <= 0 and state.mode == "pve" then
      local owner_left = false
      for _, p in ipairs(presences) do
        if state.owner_user_id ~= nil and p.user_id == state.owner_user_id then
          owner_left = true
          break
        end
      end
      if owner_left and state.owner_user_id ~= nil and state.owner_user_id ~= "" then
        state.reconnect_grace_for_user_id = state.owner_user_id
        state.reconnect_deadline_tick = tick + math.floor(CFG.RECONNECT_GRACE_SECONDS * CFG.TICK_RATE + 0.5)
        on_pve_owner_socket_gone(state, tick)
        return state
      end
      state.ended = true
      return nil
    end
  end

  return state
end

local function match_loop(context, dispatcher, tick, state, messages)
  if state.ended then return nil end

  if state.started and not state.ended
      and state.reconnect_deadline_tick ~= nil and state.reconnect_grace_for_user_id ~= nil then
    local q = state.reconnect_grace_for_user_id
    if tick >= state.reconnect_deadline_tick and state.presences[q] == nil then
      state.reconnect_deadline_tick = nil
      state.reconnect_grace_for_user_id = nil

      if state.mode == "pve" then
        local oh = state.owner_user_id
        local st = oh ~= nil and state.stats ~= nil and state.stats[oh] or nil
        if oh ~= nil and st ~= nil and state.bot_user_id ~= nil
            and tonumber(st.hp or 0) > 0 then
          state.ended = true
          state.last_reward = award_pve_defeat(oh, state.owner_session_epoch)
          local game_over_payload = { winnerUserId = state.bot_user_id }
          game_over_payload.rewardXp = state.last_reward.reward_xp or 0
          game_over_payload.rewardGold = 0
          game_over_payload.rewardOre = 0
          game_over_payload.rewardMatter = 0
          game_over_payload.newLevel = state.last_reward.level or 1
          dispatcher.broadcast_message(CFG.OP_GAME_OVER, nk.json_encode(game_over_payload), nil, nil)
        end
        return nil
      end

      state.ended = true
      local winner = other_player_id(state, q)
      if winner ~= nil then
        dispatcher.broadcast_message(CFG.OP_GAME_OVER, nk.json_encode({ winnerUserId = winner }), nil, nil)
      end
      return nil
    end
  end

  if state.turn_deadline_paused == nil then state.turn_deadline_paused = false end
  if state.turn_pause_started_tick == nil then state.turn_pause_started_tick = 0 end

  for _, m in ipairs(messages) do
    if m.op_code == 17 then -- turn input ready (не local: лимит регистров Lua в чанке)
      if state.started and not state.ended and state.turn_deadline_paused == true then
        local sender_ok = false
        if m.sender.user_id == state.active_user_id then
          sender_ok = true
        elseif state.mode == "pve" and state.owner_user_id ~= nil and state.owner_user_id ~= ""
            and m.sender.user_id == state.owner_user_id
            and state.active_user_id == state.bot_user_id then
          sender_ok = true
        end
        if sender_ok then
          state.turn_deadline_paused = false
          state.turn_deadline_tick = tick + turn_seconds_for_state(state) * CFG.TICK_RATE
          if state.mode == "pve" and state.active_user_id == state.bot_user_id then
            state.bot_turn_pending = true
            local think = state.bot_long_think_next and CFG.BOT_THINK_TICKS or CFG.BOT_THINK_TICKS_FAST
            state.bot_turn_ready_tick = tick + think
            state.bot_long_think_next = false
          end
          broadcast_sync(dispatcher, state, nil, false, nil, tick)
        end
      end
    end
    if m.op_code == CFG.OP_PLAYER_LEFT then
      local winner = other_player_id(state, m.sender.user_id)
      state.ended = true
      if winner then
        local ok_arena, err_arena = pcall(function()
          if type(arena_on_match_finished) == "function" then
            arena_on_match_finished(state, winner)
          end
        end)
        if not ok_arena then
          nk.logger_error("arena_on_match_finished failed (timeout): " .. tostring(err_arena))
        end
        dispatcher.broadcast_message(CFG.OP_GAME_OVER, nk.json_encode({ winnerUserId = winner }), nil, nil)
      end
      return nil
    end

    if m.op_code == CFG.OP_SNAPSHOT_REQUEST then
      if state.started and not state.ended and state.board ~= nil then
        local cheat = {}
        if state.cheat_rows_allowed ~= nil and state.cheat_rows_allowed[m.sender.user_id] == true then
          cheat = state.cheat_rows or {}
        end
        local msg = make_sync_msg(state, nil, false, nil, cheat, tick)
        dispatcher.broadcast_message(CFG.OP_BOARD_SYNC, nk.json_encode(msg), { m.sender }, nil)
      end
    end

    if m.op_code == CFG.OP_SELECTION_SYNC then
      if state.started and not state.ended and m.sender.user_id == state.active_user_id then
        local sel = parse_selection(m.data)
        if sel and in_active_client(sel.x, sel.y) then
          dispatcher.broadcast_message(CFG.OP_SELECTION_SYNC, nk.json_encode(sel), nil, m.sender)
        end
      end
    end

    if m.op_code == CFG.OP_ACTION_REQUEST then
      local stale_pve = state.mode == "pve"
        and state.owner_user_id ~= nil
        and state.owner_user_id ~= ""
        and m.sender.user_id == state.owner_user_id
        and guard_is_epoch_stale_for_match(m.sender.user_id, state.owner_session_epoch)
      if stale_pve then
        send_reject(dispatcher, m.sender, "session_stale")
      else
      local action = parse_action(m.data)
      local valid, reason = validate_action_basic(state, m.sender.user_id, action)
      if not valid then
        send_reject(dispatcher, m.sender, reason)
      else
        local actor_id = m.sender.user_id
        local opp_id = other_player_id(state, actor_id)
        local actor_stats = state.stats[actor_id]

        if action.actionType == 2 or action.actionType == 3 or action.actionType == 4 or action.actionType == 5 or action.actionType == 6 then
          local spend = action_mana_cost(state, action.actionType)
          actor_stats.mana = math.max(0, actor_stats.mana - spend)
          if action.actionType == 2 then actor_stats.cross_cd = CFG.CROSS_ABILITY_COOLDOWN end
          if action.actionType == 3 then actor_stats.square_cd = CFG.SQUARE_ABILITY_COOLDOWN end
          if action.actionType == 4 then actor_stats.petard_cd = CFG.PETARD_ABILITY_COOLDOWN end
          if action.actionType == 5 then actor_stats.shield_cd = CFG.SHIELD_ABILITY_COOLDOWN end
          if action.actionType == 6 then actor_stats.fury_cd = CFG.FURY_ABILITY_COOLDOWN end
        end

        local ok, err, extra_turn, keep_turn, anim_steps = resolve_action(state, action, actor_id, opp_id)
        if not ok then
          send_reject(dispatcher, m.sender, err)
        else
          finish_turn_and_broadcast(dispatcher, state, action, extra_turn, keep_turn, tick, CFG.TICK_RATE, anim_steps)
        end
      end
      end
    end
  end

  if state.started and not state.ended and state.turn_deadline_paused and state.turn_pause_started_tick
      and tick >= state.turn_pause_started_tick + 45 * CFG.TICK_RATE then
    state.turn_deadline_paused = false
    state.turn_deadline_tick = tick + turn_seconds_for_state(state) * CFG.TICK_RATE
    if state.mode == "pve" and state.active_user_id == state.bot_user_id then
      state.bot_turn_pending = true
      state.bot_long_think_next = true
      state.bot_turn_ready_tick = tick + CFG.BOT_THINK_TICKS
    end
    broadcast_sync(dispatcher, state, nil, false, nil, tick)
  end

  -- Сначала таймаут хода (человек не успел), затем ход бота — чтобы бот мог сходить в том же тике.
  if state.started and not state.ended and not state.turn_deadline_paused and tick >= state.turn_deadline_tick then
    local current = state.active_user_id
    local next_player = other_player_id(state, current)
    if next_player then
      tick_buffs_end_turn(state.stats[current])
      state.active_user_id = next_player
      tick_cooldowns(state.stats[next_player])
      local need_ack = next_player ~= nil
      if need_ack then
        state.turn_deadline_paused = true
        state.turn_pause_started_tick = tick
        state.turn_deadline_tick = tick
      else
        state.turn_deadline_paused = false
        state.turn_deadline_tick = tick + turn_seconds_for_state(state) * CFG.TICK_RATE
      end
      if state.mode == "pve" then
        if state.active_user_id == state.bot_user_id then
          state.bot_turn_pending = true
          state.bot_long_think_next = (current == state.owner_user_id)
          if state.turn_deadline_paused then
            state.bot_turn_ready_tick = 0
          else
            state.bot_turn_ready_tick = tick + (state.bot_long_think_next and CFG.BOT_THINK_TICKS or CFG.BOT_THINK_TICKS_FAST)
          end
        else
          state.bot_turn_pending = false
          state.bot_turn_ready_tick = 0
        end
      end
      broadcast_sync(dispatcher, state, nil, false, nil, tick)
    end
  end

  if state.mode == "pve" and state.started and not state.ended and state.active_user_id == state.bot_user_id and state.bot_turn_pending and not state.turn_deadline_paused and tick >= (state.bot_turn_ready_tick or 0) then
    state.bot_turn_pending = false
    state.bot_turn_ready_tick = 0
    local actor_id = state.bot_user_id
    local opp_id = state.owner_user_id
    state._bot_pre_mana = state.stats[actor_id] ~= nil and tonumber(state.stats[actor_id].mana) or nil
    local action = choose_bot_action(state, actor_id, opp_id)
    if action == nil then
      tick_buffs_end_turn(state.stats[actor_id])
      state.active_user_id = opp_id
      tick_cooldowns(state.stats[opp_id])
      local need_ack_o = opp_id ~= nil
      if need_ack_o then
        state.turn_deadline_paused = true
        state.turn_pause_started_tick = tick
        state.turn_deadline_tick = tick
      else
        state.turn_deadline_paused = false
        state.turn_deadline_tick = tick + turn_seconds_for_state(state) * CFG.TICK_RATE
      end
      state.bot_turn_pending = false
      state.bot_turn_ready_tick = 0
      broadcast_sync(dispatcher, state, nil, false, nil, tick)
    else
      local actor_stats = state.stats[actor_id]
      if action.actionType == 2 or action.actionType == 3 or action.actionType == 4 or action.actionType == 5 or action.actionType == 6 then
        local spend = action_mana_cost(state, action.actionType)
        actor_stats.mana = math.max(0, actor_stats.mana - spend)
        if action.actionType == 2 then actor_stats.cross_cd = CFG.CROSS_ABILITY_COOLDOWN end
        if action.actionType == 3 then actor_stats.square_cd = CFG.SQUARE_ABILITY_COOLDOWN end
        if action.actionType == 4 then actor_stats.petard_cd = CFG.PETARD_ABILITY_COOLDOWN end
        if action.actionType == 5 then actor_stats.shield_cd = CFG.SHIELD_ABILITY_COOLDOWN end
        if action.actionType == 6 then actor_stats.fury_cd = CFG.FURY_ABILITY_COOLDOWN end
      end

      local ok, err, extra_turn, keep_turn, anim_steps = resolve_action(state, action, actor_id, opp_id)
      if ok then
        finish_turn_and_broadcast(dispatcher, state, action, extra_turn, keep_turn, tick, CFG.TICK_RATE, anim_steps)
      else
        nk.logger_warn("bot action rejected: " .. tostring(err))
        tick_buffs_end_turn(state.stats[actor_id])
        state.active_user_id = opp_id
        tick_cooldowns(state.stats[opp_id])
        local need_ack_e = opp_id ~= nil
        if need_ack_e then
          state.turn_deadline_paused = true
          state.turn_pause_started_tick = tick
          state.turn_deadline_tick = tick
        else
          state.turn_deadline_paused = false
          state.turn_deadline_tick = tick + turn_seconds_for_state(state) * CFG.TICK_RATE
        end
        state.bot_turn_pending = false
        state.bot_turn_ready_tick = 0
        broadcast_sync(dispatcher, state, nil, false, nil, tick)
      end
    end
  end

  return state
end

local function match_terminate(context, dispatcher, tick, state, grace_seconds)
  return state
end

local function match_signal(context, dispatcher, tick, state, data)
  return state, "ok"
end

-- Сводный каталог (fallback + Storage) для отладки и синхронизации с клиентом по id/slot/статам.
local function duel_match3_item_catalog_get(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end
    local defs = get_merged_item_defs()
    local list = {}
    for id, d in pairs(defs) do
      list[#list + 1] = {
        id = id,
        kind = d.kind or "equipment",
        slot = d.slot or "",
        max_stack = d.max_stack or 1,
        tier = d.tier or 1,
        quality = d.quality or "normal",
        recipe_slot = d.recipe_slot or "",
        craft_recipe_id = d.craft_recipe_id or "",
        hp = d.hp or 0,
        damage = d.damage or 0,
        armor = d.armor or 0,
        crit_chance = d.crit_chance or 0,
        healing = d.healing or 0,
      }
    end
    table.sort(list, function(a, b) return tostring(a.id) < tostring(b.id) end)
    return nk.json_encode({ ok = true, items = list })
  end)
  if not ok then
    nk.logger_error("duel_match3_item_catalog_get: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

Ach.configure({
  decode_storage_value = decode_storage_value,
  read_pve_progress = read_pve_progress,
  write_pve_progress = write_pve_progress,
  ensure_character_sheet_initialized = ensure_character_sheet_initialized,
  guard_assert_client_epoch_matches = guard_assert_client_epoch_matches,
})

nk.register_rpc(duel_match3_stats_get, "duel_match3_stats_get")
nk.register_rpc(duel_match3_stats_record, "duel_match3_stats_record")
nk.register_rpc(Ach.rpc_achievement_sync, "duel_match3_achievement_sync")
nk.register_rpc(Ach.rpc_achievement_claim_step, "duel_match3_achievement_claim_step")
nk.register_rpc(duel_match3_pve_catalog_get, "duel_match3_pve_catalog_get")
nk.register_rpc(duel_match3_pve_create, "duel_match3_pve_create")
nk.register_rpc(duel_mine_summon, "duel_mine_summon")
nk.register_rpc(duel_mine_affix_reroll, "duel_mine_affix_reroll")
nk.register_rpc(duel_mine_barrier_unlock, "duel_mine_barrier_unlock")
nk.register_rpc(duel_character_get, "duel_character_get")
nk.register_rpc(duel_character_item_move, "duel_character_item_move")
nk.register_rpc(duel_character_recipe_learn, "duel_character_recipe_learn")
nk.register_rpc(duel_workshop_craft_start, "duel_workshop_craft_start")
nk.register_rpc(duel_workshop_craft_claim, "duel_workshop_craft_claim")
nk.register_rpc(duel_player_resources_get, "duel_player_resources_get")
nk.register_rpc(duel_player_resources_spend, "duel_player_resources_spend")
nk.register_rpc(duel_pve_energy_buy, "duel_pve_energy_buy")
nk.register_rpc(duel_workshop_craft_rush, "duel_workshop_craft_rush")
nk.register_rpc(duel_match3_item_catalog_get, "duel_match3_item_catalog_get")
nk.register_rpc(duel_match3_server_aura_get, "duel_match3_server_aura_get")
nk.register_rpc(Arena.duel_arena_queue_join, "duel_arena_queue_join")
nk.register_rpc(Arena.duel_arena_queue_leave, "duel_arena_queue_leave")
nk.register_rpc(Arena.duel_arena_queue_poll, "duel_arena_queue_poll")

return {
  match_init = match_init,
  match_join_attempt = match_join_attempt,
  match_join = match_join,
  match_leave = match_leave,
  match_loop = match_loop,
  match_terminate = match_terminate,
  match_signal = match_signal,
}
