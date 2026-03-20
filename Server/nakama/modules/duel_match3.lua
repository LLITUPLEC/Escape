local nk = require("nakama")

local SIZE = 6
local MAX_HP = 150
local MAX_MANA = 150
local TURN_SECONDS = 30
local TICK_RATE = 5
local ABILITY_COST = 20
local ABILITY_COOLDOWN = 2
local ABILITY_BASE_DAMAGE = 3
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

local function new_stats()
  return { hp = MAX_HP, mana = 0, cross_cd = 0, square_cd = 0 }
end

local function tick_cooldowns(stats)
  if stats.cross_cd > 0 then stats.cross_cd = stats.cross_cd - 1 end
  if stats.square_cd > 0 then stats.square_cd = stats.square_cd - 1 end
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
  else
    for dy = -1, 1 do
      for dx = -1, 1 do
        local nx, ny = cx + dx, cy + dy
        if in_bounds(nx, ny) then bset(board, nx, ny, 0) end
      end
    end
  end
end

local function apply_match_effects(state, actor_id, opponent_id, matches, extra_turn)
  local actor = state.stats[actor_id]
  local opp = state.stats[opponent_id]

  for _, m in ipairs(matches) do
    if m.count >= 5 then extra_turn = true end
    if m.type == 1 or m.type == 2 or m.type == 3 then
      local gain = (GEM_MANA[m.type] or 0) * m.count
      actor.mana = math.min(MAX_MANA, actor.mana + gain)
    elseif m.type == 4 then
      opp.hp = math.max(0, opp.hp - SKULL_DAMAGE * m.count)
    elseif m.type == 5 then
      actor.hp = math.min(MAX_HP, actor.hp + ANKH_HEAL * m.count)
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
    aHp = a.hp, aMana = a.mana, aCrossCd = a.cross_cd, aSquareCd = a.square_cd,
    bHp = b.hp, bMana = b.mana, bCrossCd = b.cross_cd, bSquareCd = b.square_cd,
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

local function finish_turn_and_broadcast(dispatcher, state, action, extra_turn, tick, tick_rate, anim_steps)
  local actor = state.active_user_id
  local opponent = other_player_id(state, actor)

  if state.stats[actor].hp <= 0 or state.stats[opponent].hp <= 0 then
    local winner = state.stats[actor].hp > 0 and actor or opponent
    state.ended = true
    broadcast_sync(dispatcher, state, action, extra_turn, anim_steps)
    dispatcher.broadcast_message(OP_GAME_OVER, nk.json_encode({ winnerUserId = winner }), nil, nil)
    return
  end

  if extra_turn then
    tick_cooldowns(state.stats[actor])
    state.active_user_id = actor
  else
    state.active_user_id = opponent
    tick_cooldowns(state.stats[opponent])
  end

  state.turn_deadline_tick = tick + TURN_SECONDS * tick_rate
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
  else
    for dy = -1, 1 do
      for dx = -1, 1 do add_cell(cx + dx, cy + dy) end
    end
  end
  return cells
end

local function apply_ability_rewards(state, actor_id, opponent_id, action_type, cx, cy)
  local actor = state.stats[actor_id]
  local opp = state.stats[opponent_id]
  local cells = collect_ability_cells(action_type, cx, cy)
  local skulls = 0

  for _, c in ipairs(cells) do
    local t = bget(state.board, c.x, c.y)
    if t == 1 or t == 2 or t == 3 then
      actor.mana = math.min(MAX_MANA, actor.mana + (GEM_MANA[t] or 0))
    elseif t == 5 then
      actor.hp = math.min(MAX_HP, actor.hp + ANKH_HEAL)
    elseif t == 4 then
      skulls = skulls + 1
    end
  end

  opp.hp = math.max(0, opp.hp - ABILITY_BASE_DAMAGE - SKULL_DAMAGE * skulls)
end

local function resolve_action(state, action, actor_id, opponent_id)
  local initial_matches = {}
  local anim_steps = {}
  if action.actionType == 1 then
    local ok, matches = try_swap(state.board, action.fromX, action.fromY, action.toX, action.toY)
    if not ok then return false, "invalid_swap", false, anim_steps end
    initial_matches = matches or {}
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

  return true, nil, extra_turn, anim_steps
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

  if action.actionType == 2 or action.actionType == 3 then
    if not in_bounds(action.cx, action.cy) then return false, "out_of_bounds" end
    local st = state.stats[sender_id]
    if st.mana < ABILITY_COST then return false, "not_enough_mana" end
    if action.actionType == 2 and st.cross_cd > 0 then return false, "cross_on_cooldown" end
    if action.actionType == 3 and st.square_cd > 0 then return false, "square_on_cooldown" end
    return true, nil
  end

  return false, "unknown_action"
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
    presences = {},
    players_sorted = {},
    stats = {},
    board = nil,
    started = false,
    ended = false,
    active_user_id = nil,
    turn_deadline_tick = 0,
  }

  return state, TICK_RATE, "mode=duel_match3"
end

local function match_join_attempt(context, dispatcher, tick, state, presence, metadata)
  if state.ended then return state, false, "ended" end

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
    if count <= 1 then
      state.ended = true
      local winner = nil
      for uid, _ in pairs(state.presences) do winner = uid end
      if winner ~= nil then
        dispatcher.broadcast_message(OP_GAME_OVER, nk.json_encode({ winnerUserId = winner }), nil, nil)
      end
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

        if action.actionType == 2 or action.actionType == 3 then
          actor_stats.mana = math.max(0, actor_stats.mana - ABILITY_COST)
          if action.actionType == 2 then actor_stats.cross_cd = ABILITY_COOLDOWN end
          if action.actionType == 3 then actor_stats.square_cd = ABILITY_COOLDOWN end
        end

        local ok, err, extra_turn, anim_steps = resolve_action(state, action, actor_id, opp_id)
        if not ok then
          send_reject(dispatcher, m.sender, err)
        else
          finish_turn_and_broadcast(dispatcher, state, action, extra_turn, tick, TICK_RATE, anim_steps)
        end
      end
    end
  end

  if state.started and not state.ended and tick >= state.turn_deadline_tick then
    local current = state.active_user_id
    local next_player = other_player_id(state, current)
    if next_player then
      state.active_user_id = next_player
      tick_cooldowns(state.stats[next_player])
      state.turn_deadline_tick = tick + TURN_SECONDS * TICK_RATE
      broadcast_sync(dispatcher, state, nil, false)
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

return {
  match_init = match_init,
  match_join_attempt = match_join_attempt,
  match_join = match_join,
  match_leave = match_leave,
  match_loop = match_loop,
  match_terminate = match_terminate,
  match_signal = match_signal,
}
