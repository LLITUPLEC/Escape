local nk = require("nakama")

local SIZE = 6
local MAX_HP = 150
local MAX_MANA = 100
local TURN_SECONDS = 30
local TICK_RATE = 5
local BOT_THINK_SECONDS = 5.0
local BOT_THINK_TICKS = math.max(1, math.floor(BOT_THINK_SECONDS * TICK_RATE + 0.5))
local CROSS_ABILITY_COST = 20
local SQUARE_ABILITY_COST = 20
local PETARD_ABILITY_COST = 30
local CROSS_ABILITY_COOLDOWN = 2
local SQUARE_ABILITY_COOLDOWN = 2
local PETARD_ABILITY_COOLDOWN = 1
local ABILITY_BASE_DAMAGE = 3
local PETARD_DAMAGE = 15
local SKULL_DAMAGE = 5
local ANKH_HEAL = 1
local GEM_MANA = { [1] = 5, [2] = 3, [3] = 1 }
local SPAWN_POOL = { 1, 2, 3, 4, 5 }

local OP_BOARD_SYNC = 10
local OP_GAME_OVER = 11
local OP_PLAYER_LEFT = 12
local OP_ACTION_REQUEST = 13
local OP_ACTION_REJECT = 14
local OP_SELECTION_SYNC = 15
local OP_SNAPSHOT_REQUEST = 16
local STATS_COLLECTION = "duel_match3_stats"
local STATS_KEY = "summary"
local PVE_PROGRESS_COLLECTION = "duel_match3_progress"
local PVE_PROGRESS_KEY = "profile"
local BOT_USER_ID_PREFIX = "zz-bot-"

local LEVEL_XP = { 0, 100, 240, 420, 650, 940, 1300, 1740, 2280, 2920 }
local award_pve_victory

local BOTS = {
  slime_1 = {
    id = "slime_1", name = "Слизень-разведчик", difficulty = 1,
    hp_bonus = 0, start_mana = 0,
    ai_ability_chance = 0.12, petard_bias = 0.20, cross_bias = 0.40, square_bias = 0.40,
    reward_xp = 40, reward_gold = 20,
  },
  goblin_2 = {
    id = "goblin_2", name = "Гоблин-подрывник", difficulty = 2,
    hp_bonus = 15, start_mana = 5,
    ai_ability_chance = 0.20, petard_bias = 0.40, cross_bias = 0.35, square_bias = 0.25,
    reward_xp = 55, reward_gold = 30,
  },
  knight_3 = {
    id = "knight_3", name = "Костяной рыцарь", difficulty = 3,
    hp_bonus = 25, start_mana = 10,
    ai_ability_chance = 0.24, petard_bias = 0.25, cross_bias = 0.35, square_bias = 0.40,
    reward_xp = 75, reward_gold = 45,
  },
  necro_4 = {
    id = "necro_4", name = "Некромант Пыли", difficulty = 4,
    hp_bonus = 35, start_mana = 14,
    ai_ability_chance = 0.28, petard_bias = 0.30, cross_bias = 0.40, square_bias = 0.30,
    reward_xp = 95, reward_gold = 60,
  },
  hydra_5 = {
    id = "hydra_5", name = "Гидра Глубин", difficulty = 5,
    hp_bonus = 50, start_mana = 18,
    ai_ability_chance = 0.30, petard_bias = 0.20, cross_bias = 0.30, square_bias = 0.50,
    reward_xp = 120, reward_gold = 80,
  },
  titan_6 = {
    id = "titan_6", name = "Титан Разлома", difficulty = 6,
    hp_bonus = 65, start_mana = 22,
    ai_ability_chance = 0.33, petard_bias = 0.34, cross_bias = 0.33, square_bias = 0.33,
    reward_xp = 150, reward_gold = 105,
  },
  dragon_7 = {
    id = "dragon_7", name = "Огненный Дракон", difficulty = 7,
    hp_bonus = 85, start_mana = 26,
    ai_ability_chance = 0.36, petard_bias = 0.44, cross_bias = 0.28, square_bias = 0.28,
    reward_xp = 185, reward_gold = 135,
  },
  lich_8 = {
    id = "lich_8", name = "Лич Пустоты", difficulty = 8,
    hp_bonus = 100, start_mana = 30,
    ai_ability_chance = 0.40, petard_bias = 0.30, cross_bias = 0.45, square_bias = 0.25,
    reward_xp = 225, reward_gold = 170,
  },
  leviathan_9 = {
    id = "leviathan_9", name = "Левиафан Теней", difficulty = 9,
    hp_bonus = 120, start_mana = 35,
    ai_ability_chance = 0.44, petard_bias = 0.35, cross_bias = 0.25, square_bias = 0.40,
    reward_xp = 270, reward_gold = 210,
  },
  emperor_10 = {
    id = "emperor_10", name = "Император Бездны", difficulty = 10,
    hp_bonus = 150, start_mana = 40,
    ai_ability_chance = 0.50, petard_bias = 0.36, cross_bias = 0.32, square_bias = 0.32,
    reward_xp = 320, reward_gold = 260,
  },
}

math.randomseed(os.time())

local function idx(x, y)
  return y * SIZE + x + 1
end

local function in_bounds(x, y)
  return x >= 0 and x < SIZE and y >= 0 and y < SIZE
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

local function get_bot_profile(bot_id)
  return BOTS[bot_id] or BOTS["slime_1"]
end

local function make_bot_user_id(bot_id)
  return BOT_USER_ID_PREFIX .. tostring(bot_id or "slime_1")
end

local function current_level_from_xp(xp)
  local level = 1
  local safe_xp = math.max(0, tonumber(xp) or 0)
  for i = 2, #LEVEL_XP do
    if safe_xp >= LEVEL_XP[i] then
      level = i
    else
      break
    end
  end
  if level > 10 then level = 10 end
  return level
end

local function new_stats()
  return { hp = MAX_HP, mana = 0, cross_cd = 0, square_cd = 0, petard_cd = 0, max_hp = MAX_HP }
end

local function tick_cooldowns(stats)
  if stats.cross_cd > 0 then stats.cross_cd = stats.cross_cd - 1 end
  if stats.square_cd > 0 then stats.square_cd = stats.square_cd - 1 end
  if stats.petard_cd > 0 then stats.petard_cd = stats.petard_cd - 1 end
end

local function would_create_match(board, x, y, t)
  if x >= 2 and bget(board, x - 1, y) == t and bget(board, x - 2, y) == t then return true end
  if y >= 2 and bget(board, x, y - 1) == t and bget(board, x, y - 2) == t then return true end
  return false
end

local function init_board()
  local board = {}
  for y = 0, SIZE - 1 do
    for x = 0, SIZE - 1 do
      local t = 1
      local tries = 0
      repeat
        t = SPAWN_POOL[math.random(1, #SPAWN_POOL)]
        tries = tries + 1
      until tries >= 20 or not would_create_match(board, x, y, t)
      bset(board, x, y, t)
    end
  end
  return board
end

local function do_swap(board, x1, y1, x2, y2)
  local t = bget(board, x1, y1)
  bset(board, x1, y1, bget(board, x2, y2))
  bset(board, x2, y2, t)
end

local function find_matches(board)
  local results = {}

  for y = 0, SIZE - 1 do
    local x = 0
    while x < SIZE do
      local t = bget(board, x, y)
      if t == 0 then
        x = x + 1
      else
        local len = 1
        while x + len < SIZE and bget(board, x + len, y) == t do
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

  for x = 0, SIZE - 1 do
    local y = 0
    while y < SIZE do
      local t = bget(board, x, y)
      if t == 0 then
        y = y + 1
      else
        local len = 1
        while y + len < SIZE and bget(board, x, y + len) == t do
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
      bset(board, c.x, c.y, 0)
    end
  end
end

local function apply_gravity_and_refill(board)
  for x = 0, SIZE - 1 do
    local write_y = SIZE - 1
    for y = SIZE - 1, 0, -1 do
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

  for y = 0, SIZE - 1 do
    for x = 0, SIZE - 1 do
      if bget(board, x, y) == 0 then
        bset(board, x, y, SPAWN_POOL[math.random(1, #SPAWN_POOL)])
      end
    end
  end

  return find_matches(board)
end

local function try_swap(board, x1, y1, x2, y2)
  if not in_bounds(x1, y1) or not in_bounds(x2, y2) then return false, nil end
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
      if in_bounds(nx, cy) then bset(board, nx, cy, 0) end
    end
    for dy = -2, 2 do
      if dy ~= 0 then
        local ny = cy + dy
        if in_bounds(cx, ny) then bset(board, cx, ny, 0) end
      end
    end
  elseif action_type == 3 then
    for dy = -1, 1 do
      for dx = -1, 1 do
        local nx, ny = cx + dx, cy + dy
        if in_bounds(nx, ny) then bset(board, nx, ny, 0) end
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

  for _, m in ipairs(matches) do
    if m.count >= 5 then extra_turn = true end
    if sim ~= nil and m.count >= 5 then sim.extra_turn = true end
    if m.type == 1 or m.type == 2 or m.type == 3 then
      local gain = (GEM_MANA[m.type] or 0) * m.count
      actor.mana = math.min(MAX_MANA, actor.mana + gain)
      if sim ~= nil then
        if m.type == 1 then sim.red = sim.red + m.count
        elseif m.type == 2 then sim.yellow = sim.yellow + m.count
        elseif m.type == 3 then sim.green = sim.green + m.count end
      end
    elseif m.type == 4 then
      opp.hp = math.max(0, opp.hp - SKULL_DAMAGE * m.count)
    elseif m.type == 5 then
      actor.hp = math.min(actor.max_hp or MAX_HP, actor.hp + ANKH_HEAL * m.count)
    end
  end

  return extra_turn
end

local function other_player_id(state, uid)
  if #state.players_sorted < 2 then return nil end
  if state.players_sorted[1] == uid then return state.players_sorted[2] end
  return state.players_sorted[1]
end

local function make_sync_msg(state, action, extra_turn, anim_steps)
  local a_id = state.players_sorted[1]
  local b_id = state.players_sorted[2]
  local a = state.stats[a_id]
  local b = state.stats[b_id]

  return {
    board = clone_board(state.board),
    aHp = a.hp, aMana = a.mana, aCrossCd = a.cross_cd, aSquareCd = a.square_cd, aPetardCd = a.petard_cd,
    aMaxHp = a.max_hp or MAX_HP,
    bHp = b.hp, bMana = b.mana, bCrossCd = b.cross_cd, bSquareCd = b.square_cd, bPetardCd = b.petard_cd,
    bMaxHp = b.max_hp or MAX_HP,
    extraTurn = extra_turn or false,
    activeUserId = state.active_user_id,
    actionType = action and action.actionType or 0,
    fromX = action and action.fromX or -1,
    fromY = action and action.fromY or -1,
    toX = action and action.toX or -1,
    toY = action and action.toY or -1,
    abilityX = action and action.cx or -1,
    abilityY = action and action.cy or -1,
    animSteps = anim_steps or {},
  }
end

local function broadcast_sync(dispatcher, state, action, extra_turn, anim_steps)
  local msg = make_sync_msg(state, action, extra_turn, anim_steps)
  dispatcher.broadcast_message(OP_BOARD_SYNC, nk.json_encode(msg), nil, nil)
end

local function send_reject(dispatcher, presence, reason)
  local payload = nk.json_encode({ reason = reason or "invalid_action" })
  dispatcher.broadcast_message(OP_ACTION_REJECT, payload, { presence }, nil)
end

local function finish_turn_and_broadcast(dispatcher, state, action, extra_turn, keep_turn, tick, tick_rate, anim_steps)
  local actor = state.active_user_id
  local opponent = other_player_id(state, actor)

  if state.stats[actor].hp <= 0 or state.stats[opponent].hp <= 0 then
    local winner = state.stats[actor].hp > 0 and actor or opponent
    state.ended = true
    broadcast_sync(dispatcher, state, action, extra_turn, anim_steps)

    local game_over_payload = { winnerUserId = winner }
    if state.mode == "pve" and winner == state.owner_user_id then
      state.last_reward = award_pve_victory(state.owner_user_id, state.bot_id)
      game_over_payload.rewardXp = state.last_reward.reward_xp or 0
      game_over_payload.rewardGold = state.last_reward.reward_gold or 0
      game_over_payload.newLevel = state.last_reward.level or 1
    end
    dispatcher.broadcast_message(OP_GAME_OVER, nk.json_encode(game_over_payload), nil, nil)
    return
  end

  if keep_turn then
    state.active_user_id = actor
    state.turn_deadline_tick = tick + TURN_SECONDS * tick_rate
  elseif extra_turn then
    state.active_user_id = actor
  else
    state.active_user_id = opponent
    tick_cooldowns(state.stats[opponent])
  end

  if not keep_turn then
    state.turn_deadline_tick = tick + TURN_SECONDS * tick_rate
  end
  if state.mode == "pve" then
    if state.active_user_id == state.bot_user_id then
      state.bot_turn_pending = true
      state.bot_turn_ready_tick = tick + BOT_THINK_TICKS
    else
      state.bot_turn_pending = false
      state.bot_turn_ready_tick = 0
    end
  end
  broadcast_sync(dispatcher, state, action, extra_turn, anim_steps)
end

local function clone_step(board, phase)
  return { phase = phase, board = clone_board(board) }
end

local function collect_ability_cells(action_type, cx, cy)
  local cells = {}
  local used = {}
  local function add_cell(x, y)
    if not in_bounds(x, y) then return end
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
  if action_type == 4 then
    opp.hp = math.max(0, opp.hp - PETARD_DAMAGE)
    return
  end

  local cells = collect_ability_cells(action_type, cx, cy)
  local skulls = 0

  for _, c in ipairs(cells) do
    local t = bget(state.board, c.x, c.y)
    if t == 1 or t == 2 or t == 3 then
      actor.mana = math.min(MAX_MANA, actor.mana + (GEM_MANA[t] or 0))
      if sim ~= nil then
        if t == 1 then sim.red = sim.red + 1
        elseif t == 2 then sim.yellow = sim.yellow + 1
        elseif t == 3 then sim.green = sim.green + 1 end
      end
    elseif t == 5 then
      actor.hp = math.min(actor.max_hp or MAX_HP, actor.hp + ANKH_HEAL)
    elseif t == 4 then
      skulls = skulls + 1
    end
  end

  opp.hp = math.max(0, opp.hp - ABILITY_BASE_DAMAGE - SKULL_DAMAGE * skulls)
end

local function resolve_action(state, action, actor_id, opponent_id)
  local initial_matches = {}
  local anim_steps = {}
  local keep_turn = false
  if action.actionType == 1 then
    local ok, matches = try_swap(state.board, action.fromX, action.fromY, action.toX, action.toY)
    if not ok then return false, "invalid_swap", false, false, anim_steps end
    initial_matches = matches or {}
  elseif action.actionType == 4 then
    apply_ability_rewards(state, actor_id, opponent_id, action.actionType, -1, -1)
    keep_turn = true
    return true, nil, false, true, anim_steps
  else
    apply_ability_rewards(state, actor_id, opponent_id, action.actionType, action.cx, action.cy)
    apply_ability(state.board, action.actionType, action.cx, action.cy)
    anim_steps[#anim_steps + 1] = clone_step(state.board, 1)
    initial_matches = {}
  end

  local extra_turn = false

  if #initial_matches > 0 then
    extra_turn = apply_match_effects(state, actor_id, opponent_id, initial_matches, extra_turn)
    clear_matches(state.board, initial_matches)
    anim_steps[#anim_steps + 1] = clone_step(state.board, 1)
  end

  while true do
    local cascade = apply_gravity_and_refill(state.board)
    anim_steps[#anim_steps + 1] = clone_step(state.board, 2)
    if #cascade == 0 then break end
    extra_turn = apply_match_effects(state, actor_id, opponent_id, cascade, extra_turn)
    clear_matches(state.board, cascade)
    anim_steps[#anim_steps + 1] = clone_step(state.board, 1)
  end

  return true, nil, extra_turn, keep_turn, anim_steps
end

local function count_present_players(state)
  local n = 0
  for _, _ in pairs(state.presences) do n = n + 1 end
  return n
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
  if type(v) == "string" then return nk.json_decode(v) end
  return nil
end

local function read_pve_progress(user_id)
  local rows = nk.storage_read({
    {
      collection = PVE_PROGRESS_COLLECTION,
      key = PVE_PROGRESS_KEY,
      user_id = user_id,
    },
  })

  if rows == nil or #rows == 0 then
    local base = { xp = 0, gold = 0, level = 1, defeated = {} }
    return base, nil
  end

  local row = rows[1]
  local val = decode_storage_value(row) or {}
  local progress = {
    xp = math.max(0, tonumber(val.xp) or 0),
    gold = math.max(0, tonumber(val.gold) or 0),
    level = math.max(1, tonumber(val.level) or 1),
    defeated = type(val.defeated) == "table" and val.defeated or {},
  }
  progress.level = current_level_from_xp(progress.xp)
  return progress, row.version
end

local function write_pve_progress(user_id, progress, version)
  local write_obj = {
    collection = PVE_PROGRESS_COLLECTION,
    key = PVE_PROGRESS_KEY,
    user_id = user_id,
    value = {
      xp = progress.xp,
      gold = progress.gold,
      level = progress.level,
      defeated = progress.defeated,
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

award_pve_victory = function(user_id, bot_id)
  local bot = get_bot_profile(bot_id)
  local reward_xp = bot.reward_xp or 0
  local reward_gold = bot.reward_gold or 0
  local max_retries = 5

  for i = 1, max_retries do
    local progress, version = read_pve_progress(user_id)
    progress.xp = progress.xp + reward_xp
    progress.gold = progress.gold + reward_gold
    progress.level = current_level_from_xp(progress.xp)
    local defeated = progress.defeated or {}
    local current_count = tonumber(defeated[bot_id]) or 0
    defeated[bot_id] = current_count + 1
    progress.defeated = defeated

    local ok, err = pcall(function()
      write_pve_progress(user_id, progress, version)
    end)
    if ok then
      return {
        reward_xp = reward_xp,
        reward_gold = reward_gold,
        level = progress.level,
        xp = progress.xp,
        gold = progress.gold,
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
    level = 1,
    xp = 0,
    gold = 0,
  }
end

local function read_match3_stats(user_id)
  local rows = nk.storage_read({
    {
      collection = STATS_COLLECTION,
      key = STATS_KEY,
      user_id = user_id,
    },
  })

  if rows == nil or #rows == 0 then
    return { played = 0, wins = 0, losses = 0 }, nil
  end

  local row = rows[1]
  local val = decode_storage_value(row) or {}
  local stats = {
    played = tonumber(val.played) or 0,
    wins = tonumber(val.wins) or 0,
    losses = tonumber(val.losses) or 0,
  }
  return stats, row.version
end

local function duel_match3_stats_get(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local stats = read_match3_stats(user_id)
    return nk.json_encode({
      ok = true,
      played = stats.played or 0,
      wins = stats.wins or 0,
      losses = stats.losses or 0,
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

    local won = false
    if payload ~= nil and payload ~= "" then
      local p = nk.json_decode(payload)
      won = p ~= nil and p.won == true
    end

    local max_retries = 5
    for i = 1, max_retries do
      local stats, version = read_match3_stats(user_id)
      stats.played = (stats.played or 0) + 1
      if won then
        stats.wins = (stats.wins or 0) + 1
      else
        stats.losses = (stats.losses or 0) + 1
      end

      local write_obj = {
        collection = STATS_COLLECTION,
        key = STATS_KEY,
        user_id = user_id,
        value = {
          played = stats.played,
          wins = stats.wins,
          losses = stats.losses,
          updated_at = os.time(),
        },
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
          played = stats.played,
          wins = stats.wins,
          losses = stats.losses,
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
    local bots = {}
    for _, bot in pairs(BOTS) do
      bots[#bots + 1] = {
        id = bot.id,
        name = bot.name,
        difficulty = bot.difficulty,
        hp_bonus = bot.hp_bonus or 0,
        start_mana = bot.start_mana or 0,
        reward_xp = bot.reward_xp,
        reward_gold = bot.reward_gold,
      }
    end
    table.sort(bots, function(a, b) return tostring(a.id) < tostring(b.id) end)

    return nk.json_encode({
      ok = true,
      progression = {
        level = progress.level,
        xp = progress.xp,
        gold = progress.gold,
        max_level = 10,
      },
      level_xp = LEVEL_XP,
      bots = bots,
    })
  end)

  if not ok then
    nk.logger_error("duel_match3_pve_catalog_get: " .. tostring(result))
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

local function duel_match3_pve_create(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local p = {}
    if payload ~= nil and payload ~= "" then
      p = nk.json_decode(payload) or {}
    end

    local requested_bot_id = tostring(p.bot_id or "slime_1")
    local bot = get_bot_profile(requested_bot_id)
    local bot_user_id = make_bot_user_id(bot.id)
    local progress = read_pve_progress(user_id)

    local match_id = try_match_create({
      mode = "pve",
      owner_user_id = user_id,
      bot_id = bot.id,
      bot_user_id = bot_user_id,
      owner_level = progress.level or 1,
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
    })
  end)

  if not ok then
    nk.logger_error("duel_match3_pve_create: " .. tostring(result))
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
    if not in_bounds(action.fromX, action.fromY) or not in_bounds(action.toX, action.toY) then
      return false, "out_of_bounds"
    end
    if math.abs(action.fromX - action.toX) + math.abs(action.fromY - action.toY) ~= 1 then
      return false, "not_adjacent"
    end
    return true, nil
  end

  if action.actionType == 2 or action.actionType == 3 or action.actionType == 4 then
    if (action.actionType == 2 or action.actionType == 3) and not in_bounds(action.cx, action.cy) then
      return false, "out_of_bounds"
    end
    local st = state.stats[sender_id]
    local need_mana = action.actionType == 2 and CROSS_ABILITY_COST
      or (action.actionType == 3 and SQUARE_ABILITY_COST or PETARD_ABILITY_COST)
    if st.mana < need_mana then return false, "not_enough_mana" end
    if action.actionType == 2 and st.cross_cd > 0 then return false, "cross_on_cooldown" end
    if action.actionType == 3 and st.square_cd > 0 then return false, "square_on_cooldown" end
    if action.actionType == 4 and st.petard_cd > 0 then return false, "petard_on_cooldown" end
    return true, nil
  end

  return false, "unknown_action"
end

local function enumerate_valid_swaps(board)
  local swaps = {}
  for y = 0, SIZE - 1 do
    for x = 0, SIZE - 1 do
      if x + 1 < SIZE then
        local sim = clone_board(board)
        local ok, _ = try_swap(sim, x, y, x + 1, y)
        if ok then swaps[#swaps + 1] = { actionType = 1, fromX = x, fromY = y, toX = x + 1, toY = y, cx = -1, cy = -1 } end
      end
      if y + 1 < SIZE then
        local sim = clone_board(board)
        local ok, _ = try_swap(sim, x, y, x, y + 1)
        if ok then swaps[#swaps + 1] = { actionType = 1, fromX = x, fromY = y, toX = x, toY = y + 1, cx = -1, cy = -1 } end
      end
    end
  end
  return swaps
end

local function copy_stats(src)
  return {
    hp = src.hp or MAX_HP,
    mana = src.mana or 0,
    cross_cd = src.cross_cd or 0,
    square_cd = src.square_cd or 0,
    petard_cd = src.petard_cd or 0,
    max_hp = src.max_hp or MAX_HP,
  }
end

local function spend_ability_for_sim(stats, action_type)
  if action_type == 2 then
    stats.mana = math.max(0, stats.mana - CROSS_ABILITY_COST)
    stats.cross_cd = CROSS_ABILITY_COOLDOWN
  elseif action_type == 3 then
    stats.mana = math.max(0, stats.mana - SQUARE_ABILITY_COST)
    stats.square_cd = SQUARE_ABILITY_COOLDOWN
  elseif action_type == 4 then
    stats.mana = math.max(0, stats.mana - PETARD_ABILITY_COST)
    stats.petard_cd = PETARD_ABILITY_COOLDOWN
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
  }

  if action.actionType == 2 or action.actionType == 3 or action.actionType == 4 then
    spend_ability_for_sim(sim_bot, action.actionType)
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

local function choose_bot_action(state, bot_user_id, player_user_id)
  local stats = state.stats[bot_user_id]
  if stats == nil then return nil end

  local can_cross = stats.mana >= CROSS_ABILITY_COST and stats.cross_cd <= 0
  local can_square = stats.mana >= SQUARE_ABILITY_COST and stats.square_cd <= 0
  local can_petard = stats.mana >= PETARD_ABILITY_COST and stats.petard_cd <= 0

  -- При запасе маны бот всегда сначала тратит петарду, затем (из-за keep_turn) выбирает следующий ход по общей оценке.
  if can_petard and (stats.mana or 0) > 50 then
    return { actionType = 4, fromX = -1, fromY = -1, toX = -1, toY = -1, cx = -1, cy = -1 }
  end

  local candidates = enumerate_valid_swaps(state.board)
  if can_cross then
    for y = 0, SIZE - 1 do
      for x = 0, SIZE - 1 do
        candidates[#candidates + 1] = {
          actionType = 2,
          fromX = -1, fromY = -1, toX = -1, toY = -1,
          cx = x, cy = y,
        }
      end
    end
  end
  if can_square then
    for y = 0, SIZE - 1 do
      for x = 0, SIZE - 1 do
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
    owner_user_id = params and params.owner_user_id or nil,
    bot_id = params and params.bot_id or "slime_1",
    bot_user_id = params and params.bot_user_id or make_bot_user_id(params and params.bot_id or "slime_1"),
    owner_level = tonumber(params and params.owner_level or 1) or 1,
    presences = {},
    players_sorted = {},
    stats = {},
    board = nil,
    started = false,
    ended = false,
    active_user_id = nil,
    turn_deadline_tick = 0,
    last_reward = nil,
    bot_turn_pending = false,
    bot_turn_ready_tick = 0,
  }

  return state, TICK_RATE, "mode=duel_match3"
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

  if state.mode == "pve" then
    if not state.started and count_present_players(state) == 1 then
      local player_id = nil
      for uid, _ in pairs(state.presences) do player_id = uid end
      if player_id == nil then return state end

      local bot_profile = get_bot_profile(state.bot_id)
      state.bot_id = bot_profile.id
      if state.bot_user_id == nil or state.bot_user_id == "" then
        state.bot_user_id = make_bot_user_id(state.bot_id)
      end

      state.started = true
      state.players_sorted = { player_id, state.bot_user_id }
      state.stats[player_id] = new_stats()
      state.stats[state.bot_user_id] = new_stats()
      local player_level = math.max(1, math.min(10, tonumber(state.owner_level) or 1))
      local player_hp_bonus = (player_level - 1) * 5
      state.stats[player_id].max_hp = MAX_HP + player_hp_bonus
      state.stats[player_id].hp = state.stats[player_id].max_hp

      local bot_hp_bonus = bot_profile.hp_bonus or 0
      local bot_start_mana = math.max(0, tonumber(bot_profile.start_mana) or 0)
      state.stats[state.bot_user_id].max_hp = MAX_HP + bot_hp_bonus
      state.stats[state.bot_user_id].hp = state.stats[state.bot_user_id].max_hp
      state.stats[state.bot_user_id].mana = math.min(MAX_MANA, bot_start_mana)
      state.board = init_board()
      state.active_user_id = player_id
      tick_cooldowns(state.stats[player_id])
      state.turn_deadline_tick = tick + TURN_SECONDS * TICK_RATE
      broadcast_sync(dispatcher, state, nil, false)
    end
    return state
  end

  if not state.started and count_present_players(state) == 2 then
    state.started = true
    state.players_sorted = sorted_two_players(state.presences)
    state.stats[state.players_sorted[1]] = new_stats()
    state.stats[state.players_sorted[2]] = new_stats()
    state.board = init_board()

    if math.random(0, 1) == 0 then
      state.active_user_id = state.players_sorted[1]
    else
      state.active_user_id = state.players_sorted[2]
    end

    tick_cooldowns(state.stats[state.active_user_id])
    state.turn_deadline_tick = tick + TURN_SECONDS * TICK_RATE
    broadcast_sync(dispatcher, state, nil, false)
  end

  return state
end

local function match_leave(context, dispatcher, tick, state, presences)
  for _, p in ipairs(presences) do
    state.presences[p.user_id] = nil
  end

  if state.started and not state.ended then
    local count = count_present_players(state)
    if count <= 1 and state.mode ~= "pve" then
      state.ended = true
      local winner = nil
      for uid, _ in pairs(state.presences) do winner = uid end
      if winner ~= nil then
        dispatcher.broadcast_message(OP_GAME_OVER, nk.json_encode({ winnerUserId = winner }), nil, nil)
      end
      return nil
    end
    if count <= 0 and state.mode == "pve" then
      state.ended = true
      return nil
    end
  end

  return state
end

local function match_loop(context, dispatcher, tick, state, messages)
  if state.ended then return nil end

  for _, m in ipairs(messages) do
    if m.op_code == OP_PLAYER_LEFT then
      local winner = other_player_id(state, m.sender.user_id)
      state.ended = true
      if winner then
        dispatcher.broadcast_message(OP_GAME_OVER, nk.json_encode({ winnerUserId = winner }), nil, nil)
      end
      return nil
    end

    if m.op_code == OP_SNAPSHOT_REQUEST then
      if state.started and not state.ended and state.board ~= nil then
        local msg = make_sync_msg(state, nil, false)
        dispatcher.broadcast_message(OP_BOARD_SYNC, nk.json_encode(msg), { m.sender }, nil)
      end
    end

    if m.op_code == OP_SELECTION_SYNC then
      if state.started and not state.ended and m.sender.user_id == state.active_user_id then
        local sel = parse_selection(m.data)
        if sel and in_bounds(sel.x, sel.y) then
          dispatcher.broadcast_message(OP_SELECTION_SYNC, nk.json_encode(sel), nil, m.sender)
        end
      end
    end

    if m.op_code == OP_ACTION_REQUEST then
      local action = parse_action(m.data)
      local valid, reason = validate_action_basic(state, m.sender.user_id, action)
      if not valid then
        send_reject(dispatcher, m.sender, reason)
      else
        local actor_id = m.sender.user_id
        local opp_id = other_player_id(state, actor_id)
        local actor_stats = state.stats[actor_id]

        if action.actionType == 2 or action.actionType == 3 or action.actionType == 4 then
          local spend = action.actionType == 2 and CROSS_ABILITY_COST
            or (action.actionType == 3 and SQUARE_ABILITY_COST or PETARD_ABILITY_COST)
          actor_stats.mana = math.max(0, actor_stats.mana - spend)
          if action.actionType == 2 then actor_stats.cross_cd = CROSS_ABILITY_COOLDOWN end
          if action.actionType == 3 then actor_stats.square_cd = SQUARE_ABILITY_COOLDOWN end
          if action.actionType == 4 then actor_stats.petard_cd = PETARD_ABILITY_COOLDOWN end
        end

        local ok, err, extra_turn, keep_turn, anim_steps = resolve_action(state, action, actor_id, opp_id)
        if not ok then
          send_reject(dispatcher, m.sender, err)
        else
          finish_turn_and_broadcast(dispatcher, state, action, extra_turn, keep_turn, tick, TICK_RATE, anim_steps)
        end
      end
    end
  end

  -- Сначала таймаут хода (человек не успел), затем ход бота — чтобы бот мог сходить в том же тике.
  if state.started and not state.ended and tick >= state.turn_deadline_tick then
    local current = state.active_user_id
    local next_player = other_player_id(state, current)
    if next_player then
      state.active_user_id = next_player
      tick_cooldowns(state.stats[next_player])
      state.turn_deadline_tick = tick + TURN_SECONDS * TICK_RATE
      if state.mode == "pve" then
        if state.active_user_id == state.bot_user_id then
          state.bot_turn_pending = true
          state.bot_turn_ready_tick = tick + BOT_THINK_TICKS
        else
          state.bot_turn_pending = false
          state.bot_turn_ready_tick = 0
        end
      end
      broadcast_sync(dispatcher, state, nil, false)
    end
  end

  if state.mode == "pve" and state.started and not state.ended and state.active_user_id == state.bot_user_id and state.bot_turn_pending and tick >= (state.bot_turn_ready_tick or 0) then
    state.bot_turn_pending = false
    state.bot_turn_ready_tick = 0
    local actor_id = state.bot_user_id
    local opp_id = state.owner_user_id
    local action = choose_bot_action(state, actor_id, opp_id)
    if action == nil then
      state.active_user_id = opp_id
      tick_cooldowns(state.stats[opp_id])
      state.turn_deadline_tick = tick + TURN_SECONDS * TICK_RATE
      state.bot_turn_pending = false
      state.bot_turn_ready_tick = 0
      broadcast_sync(dispatcher, state, nil, false)
    else
      local actor_stats = state.stats[actor_id]
      if action.actionType == 2 or action.actionType == 3 or action.actionType == 4 then
        local spend = action.actionType == 2 and CROSS_ABILITY_COST
          or (action.actionType == 3 and SQUARE_ABILITY_COST or PETARD_ABILITY_COST)
        actor_stats.mana = math.max(0, actor_stats.mana - spend)
        if action.actionType == 2 then actor_stats.cross_cd = CROSS_ABILITY_COOLDOWN end
        if action.actionType == 3 then actor_stats.square_cd = SQUARE_ABILITY_COOLDOWN end
        if action.actionType == 4 then actor_stats.petard_cd = PETARD_ABILITY_COOLDOWN end
      end

      local ok, err, extra_turn, keep_turn, anim_steps = resolve_action(state, action, actor_id, opp_id)
      if ok then
        finish_turn_and_broadcast(dispatcher, state, action, extra_turn, keep_turn, tick, TICK_RATE, anim_steps)
      else
        nk.logger_warn("bot action rejected: " .. tostring(err))
        state.active_user_id = opp_id
        tick_cooldowns(state.stats[opp_id])
        state.turn_deadline_tick = tick + TURN_SECONDS * TICK_RATE
        state.bot_turn_pending = false
        state.bot_turn_ready_tick = 0
        broadcast_sync(dispatcher, state, nil, false)
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

nk.register_rpc(duel_match3_stats_get, "duel_match3_stats_get")
nk.register_rpc(duel_match3_stats_record, "duel_match3_stats_record")
nk.register_rpc(duel_match3_pve_catalog_get, "duel_match3_pve_catalog_get")
nk.register_rpc(duel_match3_pve_create, "duel_match3_pve_create")

return {
  match_init = match_init,
  match_join_attempt = match_join_attempt,
  match_join = match_join,
  match_leave = match_leave,
  match_loop = match_loop,
  match_terminate = match_terminate,
  match_signal = match_signal,
}
