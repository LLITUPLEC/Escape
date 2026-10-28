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
local AchCat = runtime_lua_require("modules.duel_match3_achievement_catalog", "duel_match3_achievement_catalog")
local CharStats = runtime_lua_require("modules.duel_match3_character_stats", "duel_match3_character_stats")
local build_session_guard = runtime_lua_require("modules.duel_match3_session_guard", "duel_match3_session_guard")
local Guard = build_session_guard({ nk = nk, CFG = CFG })
-- Stable dispatch table: callbacks capture this initialized table, and resolve
-- modules/functions from its fields only after runtime initialization completes.
local Runtime = {}
Runtime.Rounds = runtime_lua_require("modules.duel_match3_rounds", "duel_match3_rounds")
Runtime.Inventory = runtime_lua_require("modules.duel_match3_inventory", "duel_match3_inventory")
Runtime.ConsumableModule = runtime_lua_require("modules.duel_consumables", "duel_consumables")
Runtime.ConsumableEffects = Runtime.ConsumableModule.Effects
Runtime.MineRecipes = runtime_lua_require("modules.duel_mine_recipe_drops", "duel_mine_recipe_drops")
Runtime.RateLimit = runtime_lua_require("modules.duel_api_rate_limit", "duel_api_rate_limit")
Runtime.StorageMaintenance = runtime_lua_require("modules.duel_storage_maintenance", "duel_storage_maintenance")
Runtime.RateLimit.configure_maintenance(function() Runtime.StorageMaintenance.maybe_run() end)
Runtime.Presence = runtime_lua_require("modules.duel_match3_presence", "duel_match3_presence")
Runtime.Lifecycle = runtime_lua_require("modules.duel_match3_lifecycle", "duel_match3_lifecycle")
Runtime.MatchLifecycle = Runtime.Lifecycle.new({guard=Guard.assert_client_epoch_matches,
  forfeit=function(...)return Runtime.forfeit_match(...)end,
  cancel=function(...)return Runtime.Prestart.cancel(...)end,
  cancel_request=function(uid,request_id)
    local ok,err,receipt,progress=Runtime.PveEntry.cancel_request(uid,request_id)
    if err~="admission_not_found"then return ok,err,receipt,progress end
    return Runtime.RaceEntry.cancel_request(uid,request_id)
  end,
  energy_max=function(...)return Runtime.resume_energy_max(...)end,
  current_progression=function(uid)return Runtime.cancel_progression(uid)end,
  refund_state=function(...)return Runtime.PveEntry.refund_state(...)end,
  request_id=function(state,uid)return Runtime.request_for_match(state,uid)end,
  resolve_cancel=function(uid,epoch)return Runtime.Resume.recover(uid,epoch,{})end,
})
Runtime.Cosmetics = runtime_lua_require("modules.duel_player_cosmetics", "duel_player_cosmetics")
Runtime.Shop = runtime_lua_require("modules.duel_shop", "duel_shop")
Runtime.Mail = runtime_lua_require("modules.duel_mail", "duel_mail")
Runtime.GlobalBoss = runtime_lua_require("modules.duel_global_boss", "duel_global_boss")
Runtime.GlobalBossRules = runtime_lua_require("modules.duel_global_boss_rules", "duel_global_boss_rules")
Runtime.BossBattle = runtime_lua_require("modules.duel_global_boss_battle", "duel_global_boss_battle")

Runtime.finish_arena_result = function(state, winner)
  if state.arena_mirror == nil or type(Runtime.arena_on_match_finished) ~= "function" then return true end
  local ok, result = pcall(Runtime.arena_on_match_finished, state, winner)
  if not ok then
    state._arena_pending_result = { winner = winner }
    nk.logger_error("arena result pending: " .. tostring(result))
    return false
  end
  state._arena_pending_result = nil
  if type(result) == "table" then
    state._arena_reward_receipt = result.reward_receipt
    state._arena_craft_receipts = result.craft_receipts
  end
  return true
end

Runtime.apply_result_rewards = function(state, winner, is_draw, payload)
  local fn = is_draw and Runtime.Pvp.apply_race_draw_rewards or Runtime.Pvp.apply_game_over_rewards
  if not state.pvp_race then return fn(state, winner, payload) end
  local ok, result
  if is_draw then ok, result = pcall(fn, state, payload)
  else ok, result = pcall(fn, state, winner, payload) end
  if not ok or result == false then
    state._race_pending_result = { winner = winner, is_draw = is_draw == true, payload = payload }
    if not ok then nk.logger_error("Race result pending: " .. tostring(result)) end
    return false
  end
  payload.rewardError = nil
  state._race_pending_result = nil
  return true
end

Runtime.emit_game_over = function(dispatcher, state, payload)
  if state._arena_pending_result or state._race_pending_result or state._pve_pending_result then
    state._pending_game_over = payload
    return false
  end
  local receipt = state._arena_reward_receipt
  if receipt and receipt.user_id == payload.winnerUserId then
    payload.rewardGold, payload.rewardOre = receipt.gold or 0, receipt.ore or 0
  end
  if type(state._arena_craft_receipts) == "table" then
    payload.arenaCraftRewards = {}
    for uid, craft in pairs(state._arena_craft_receipts) do
      local row = { userId=uid, outcome=craft.outcome, ingots=0, ingotDef="", recipeItemId="", tesseracts=0, pendingCount=0 }
      for _, item in ipairs(craft.items or {}) do
        if item.def_id:match("^ingot_") then row.ingots, row.ingotDef = item.amount, item.def_id
        elseif item.def_id == "tesseract" then row.tesseracts = item.amount
        elseif item.def_id:match("^recipe_") then row.recipeItemId = item.def_id end
      end
      for _, item in ipairs(craft.pending_items or {}) do row.pendingCount = row.pendingCount + item.amount end
      payload.arenaCraftRewards[#payload.arenaCraftRewards + 1] = row
    end
  end
  if not state.global_boss and Runtime.cancel_unused_journal_tickets then
    local ok,ready=pcall(Runtime.cancel_unused_journal_tickets,state)
    if not ok or ready~=true then state._terminal_pending=true;state._pending_game_over=payload;return false end
  end
  if Runtime.Lifecycle and not (state.global_boss and state.global_boss.practice==true) then
    local published,ready=pcall(Runtime.Lifecycle.publish,state,payload)
    if not published or ready~=true then
      state._terminal_pending=true;state._pending_game_over=payload
      return false
    end
  end
  state._terminal_pending=false
  state._terminal_delivered=true
  state._terminal_payload=payload
  state._pending_game_over = nil
  dispatcher.broadcast_message(CFG.OP_GAME_OVER, nk.json_encode(payload), nil, nil)
  return true
end

Runtime.retry_pending_results = function(dispatcher, state, tick)
  if state._prestart_cancel_pending then
    if tick >= (state._next_result_retry_tick or 0)then
      state._next_result_retry_tick=tick+CFG.TICK_RATE
      Runtime.Prestart.cancel(dispatcher,state,state._prestart_cancel.user_id)
    end
    return state._prestart_cancel_pending==true or state._terminal_pending==true
  end
  if not state._arena_pending_result and not state._race_pending_result and not state._pve_pending_result and not state._terminal_pending then return false end
  if tick >= (state._next_result_retry_tick or 0) then
    state._next_result_retry_tick = tick + CFG.TICK_RATE
    if state._arena_pending_result then Runtime.finish_arena_result(state, state._arena_pending_result.winner) end
    if state._race_pending_result then
      local pending = state._race_pending_result
      Runtime.apply_result_rewards(state, pending.winner, pending.is_draw, pending.payload)
    end
    if state._pve_pending_result then
      local p = state._pve_pending_result
      local receipt = Runtime.award_pve_victory(p.uid, p.bot_id, p.epoch, p.run, p.result_id, p.outcome, p.attempt)
      state.last_reward = receipt
      if receipt.intent_persisted or receipt.reward_pending ~= true then
        state._pve_pending_result = nil
        if state._pending_game_over then Runtime.assign_pve_reward_payload(state._pending_game_over, receipt) end
      end
    end
    if not state._arena_pending_result and not state._race_pending_result and not state._pve_pending_result and state._pending_game_over then
      Runtime.emit_game_over(dispatcher, state, state._pending_game_over)
    end
  end
  return state._arena_pending_result ~= nil or state._race_pending_result ~= nil or state._pve_pending_result ~= nil or state._terminal_pending==true
end

Runtime.assign_pve_reward_payload = function(payload, receipt)
  for client, server in pairs({ rewardXp = "reward_xp", rewardGold = "reward_gold", rewardOre = "reward_ore",
      rewardMatter = "reward_matter", rewardIngots = "reward_ingots", rewardTesseract = "reward_tesseract",
      rewardKeyAmount = "reward_key_amount", itemLootPendingCount = "item_loot_pending_count" }) do
    payload[client] = receipt[server] or 0
  end
  payload.rewardKeyId, payload.rewardBlueprint = receipt.reward_key_id or "", receipt.reward_blueprint or ""
  payload.rewardRecipeItemId = receipt.reward_recipe_item_id or ""
  payload.rewardPending, payload.newLevel = receipt.reward_pending == true, receipt.level or 1
  payload.deliveredItems, payload.pendingItems = receipt.delivered_items or {}, receipt.pending_items or {}
end

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
-- clamp_int / character_stats_base_for_level — в duel_match3_config.lua.
-- ─────────────────────────────────────────────────────────────────────────────


function is_boss_floor(floor)
  return floor == 4 or floor == 8 or floor == 12
end

function mine_bot_id_for_floor(floor)
  return "mine_" .. tostring(CFG.clamp_int(floor, 1, CFG.PVE_MAX_LEVEL))
end

-- Стоимость боя / «Прогнать» (duel_match3_pve_mine_cost.lua).
local function pve_energy_max_for_user(user_id)
  return CFG.PVE_ENERGY_MAX_BASE
end

function empty_key_items()
  return { miner_key = 0, dark_key = 0 }
end

local build_pve_mine_cost = runtime_lua_require("modules.duel_match3_pve_mine_cost", "duel_match3_pve_mine_cost")
local PveMineCost = build_pve_mine_cost({
  clamp_int = CFG.clamp_int,
  pve_energy_max_for_user = pve_energy_max_for_user,
  empty_key_items = empty_key_items,
})

function build_floor_bot_entry(floor)
  local f = CFG.clamp_int(floor, 1, CFG.PVE_MAX_LEVEL)
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
    name = CFG.mine_monster_name(f),
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
  return normalize_mine_difficulty(diff) .. ":" .. tostring(CFG.clamp_int(floor, 1, CFG.PVE_MAX_LEVEL))
end

local function truthy_match_param(v)
  if v == true then return true end
  if v == 1 then return true end
  if type(v) == "string" and string.lower(v) == "true" then return true end
  return false
end

local function is_pvp_race(state)
  return type(state) == "table" and state.pvp_race == true
end

local function race_goal_from_config()
  local n = tonumber(CFG and CFG.RACE_GOAL_MANA)
  if type(n) == "number" and n >= 1 then
    return math.floor(n)
  end
  return 200
end

local function race_max_from_goal(goal)
  local g = tonumber(goal)
  if type(g) ~= "number" or g < 1 then
    g = race_goal_from_config()
  else
    g = math.floor(g)
  end
  local mult = tonumber(CFG and CFG.RACE_MAX_MANA_MULT)
  if type(mult) ~= "number" or mult < 1 then
    mult = 1.2
  end
  return math.max(g, math.floor(g * mult + 1e-9))
end

local function race_goal_mana(state)
  if type(state) == "table" then
    local frozen = tonumber(state.race_goal_mana)
    if type(frozen) == "number" and frozen >= 1 then
      return math.floor(frozen)
    end
  end
  if type(Runtime.ContestGoals) == "table" and type(Runtime.ContestGoals.race_goal_mana) == "function" then
    local ok, g = pcall(Runtime.ContestGoals.race_goal_mana)
    if ok then
      local n = tonumber(g)
      if type(n) == "number" and n >= 1 then
        return math.floor(n)
      end
    else
      nk.logger_warn("race_goal_mana ContestGoals failed: " .. tostring(g))
    end
  end
  return race_goal_from_config()
end

local function max_mana_for_state(state)
  if not is_pvp_race(state) then
    return CFG.MAX_MANA
  end
  if type(state) == "table" then
    local frozen = tonumber(state.race_max_mana)
    if type(frozen) == "number" and frozen >= 1 then
      return math.floor(frozen)
    end
  end
  return race_max_from_goal(race_goal_mana(state))
end

local function race_petard_bank_cap()
  return math.max(0, math.floor(tonumber(CFG.RACE_PETARD_BANK_CAP) or 50))
end

local function race_ankh_petard_mult()
  return math.max(1, math.floor(tonumber(CFG.RACE_ANKH_PETARD_BANK) or 2))
end

local function race_add_petard_bank(actor, amount)
  if actor == nil then return end
  -- amount = число уничтоженных анхов; в банк кладём с множителем RACE_ANKH_PETARD_BANK.
  local ankhs = math.max(0, math.floor(tonumber(amount) or 0))
  if ankhs <= 0 then return end
  local add = ankhs * race_ankh_petard_mult()
  local cap = race_petard_bank_cap()
  local cur = math.max(0, tonumber(actor.race_petard_bank) or 0)
  actor.race_petard_bank = math.min(cap, cur + add)
end

local function race_mana_bonus_for_actor(state, actor_id)
  if not is_pvp_race(state) or actor_id == nil then return 0 end
  local every = math.max(1, math.floor(tonumber(CFG.RACE_MANA_BONUS_EVERY_ACTIONS) or 5))
  local turns = 0
  if state.race_actions ~= nil then
    turns = math.max(0, tonumber(state.race_actions[actor_id]) or 0)
  end
  -- В реальном ходе текущий action уже «идёт» (ещё не записан в race_actions).
  -- В симуляции бота (_sim_metrics) не прибавляем — иначе завышаем бонус.
  if state._sim_metrics == nil then
    turns = turns + 1
  end
  return math.floor(turns / every)
end

--- Списывает ману цели (не ниже 0). Возвращает фактически списанное.
local function race_drain_mana(state, target, amount)
  if target == nil then return 0 end
  local before = math.max(0, tonumber(target.mana) or 0)
  local want = math.max(0, tonumber(amount) or 0)
  local after = math.max(0, before - want)
  target.mana = after
  local drained = before - after
  if drained > 0 then
    state._action_mana_drain = (state._action_mana_drain or 0) + drained
  end
  return drained
end

local function add_mana_capped(state, st, gain)
  if st == nil then return end
  local g = math.max(0, tonumber(gain) or 0)
  if g <= 0 then return end
  st.mana = math.min(max_mana_for_state(state), (tonumber(st.mana) or 0) + g)
end

local function new_stats()
  return {
    hp = CFG.MAX_HP,
    mana = 0,
    race_petard_bank = 0,
    cross_cd = 0,
    square_cd = 0,
    petard_cd = 0,
    shield_cd = 0,
    fury_cd = 0,
    shield_t1 = 0,
    shield_t2 = 0,
    shield_t3 = 0,
    fury_active = false,
    fury_bomb_bonus = 0,
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
  return Runtime.ConsumableEffects.armor(math.max(0, base) + get_shield_stacks(st) * CFG.SHIELD_ARMOR_PER_STACK, st)
end

--- Один раз за ход при лечении от капсул: PvE и PvP Pro — база+экип+щит; классический PvP/арена — только бонус от щита.
local function get_turn_heal_flat_bonus(state, st)
  if st == nil then return 0 end
  local shield = get_shield_stacks(st) * CFG.SHIELD_HEAL_PER_STACK
  if state.mode == "pve" or state.pvp_pro == true then
    return math.max(0, tonumber(st.base_heal) or 0) + shield
  end
  return shield
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

  -- Fury: число бомб фиксируется в момент применения способности на весь ход (вкл. доп. ходы 5+).
  if attacker ~= nil and attacker.fury_active == true then
    dmg = dmg + math.max(0, tonumber(attacker.fury_bomb_bonus) or 0)
  end

  -- Crit chance: base crit (PVE) + fury crit (existing mechanic).
  local crit = false
  local baseCrit = attacker ~= nil and (tonumber(attacker.base_crit) or 0) or 0
  local furyCrit = attacker ~= nil and attacker.fury_active == true and CFG.FURY_CRIT_CHANCE or 0
  local critChance = Runtime.ConsumableEffects.crit(baseCrit + furyCrit, attacker)
  if critChance > 0 and math.random() < critChance then
    dmg = dmg * 2
    crit = true
  end

  local affix = tostring((((state or {}).pve_run or {}).affix) or "")
  if affix == "bare_current" then
    dmg = dmg * 2
  end

  return Runtime.ConsumableEffects.damage(dmg, attacker), crit
end

local function deal_damage(state, board, attacker, target, base_damage)
  local raw, crit = roll_outgoing_damage(state, board, attacker, base_damage)
  local reduced = math.max(0, raw - get_armor(target))
  if state.global_boss and target==state.stats[state.bot_user_id] then
    state._global_boss_damage_raw=(tonumber(state._global_boss_damage_raw) or 0)+reduced
  end
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
  st.fury_bomb_bonus = 0
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

--- base_gain с камня; actor_id — для бонуса «Спуск» (+1 маны за камень каждые 5 ходов игрока).
function mana_gain_per_object(state, base_gain, actor_id)
  local g = math.max(0, tonumber(base_gain) or 0)
  if g <= 0 then return 0 end
  if has_affix(state, "bare_current") then return 0 end
  if has_affix(state, "overload") then return 1 end
  if is_pvp_race(state) then
    g = g + race_mana_bonus_for_actor(state, actor_id)
  end
  return g
end

function turn_seconds_for_state(state)
  if has_affix(state, "scree") then
    return math.max(1, math.floor(CFG.TURN_SECONDS / 3))
  end
  return CFG.TURN_SECONDS
end

--- Задержка «думалки» бота в тиках. Для ONLINE_POOL (arena_human_like_bot) long-think = random [7..22]с.
local function bot_think_delay_ticks(state, long_think)
  if state ~= nil and state.arena_human_like_bot == true and long_think == true then
    local lo = tonumber(CFG.BOT_THINK_SECONDS_HUMAN_LIKE_MIN) or 7
    local hi = tonumber(CFG.BOT_THINK_SECONDS_HUMAN_LIKE_MAX) or 22
    if hi < lo then hi = lo end
    local sec = lo + math.random(0, hi - lo)
    return math.max(1, math.floor(sec * CFG.TICK_RATE + 0.5))
  end
  if long_think == true then
    return CFG.BOT_THINK_TICKS
  end
  return CFG.BOT_THINK_TICKS_FAST
end

--- Y_MIN для симуляции качества хода бота (ONLINE_POOL в финале → 1, иначе CFG).
local function bot_sim_quality_y_min(state)
  local am = state and state.arena_mirror or nil
  if state ~= nil and state.arena_human_like_bot == true
      and am ~= nil and tostring(am.round or "") == "final" then
    return tonumber(CFG.BOT_SIM_QUALITY_Y_MIN_HUMAN_LIKE_FINAL) or 1
  end
  return tonumber(CFG.BOT_SIM_QUALITY_Y_MIN) or CFG.ACTIVE_Y_MIN
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

-- Forward declarations (used by safety shuffle).

-- If the active 6x6 board ends up with zero valid swaps (rare but possible),
-- shuffle the active area until at least one valid swap exists.
-- Preview rows (server y=0..1) are never touched.
local function ensure_active_board_has_valid_swaps(state)
  if state == nil or state.board == nil then return false end

  local function has_any_valid_swap()
    local swaps = Runtime.enumerate_valid_swaps(state.board)
    return swaps ~= nil and #swaps > 0
  end

  -- Fast path.
  if has_any_valid_swap() then return false end

  local function shuffle_once()
    -- Collect active cells.
    local cells = {}
    for y = CFG.ACTIVE_Y_MIN, CFG.HEIGHT - 1 do
      for x = 0, CFG.SIZE - 1 do
        cells[#cells + 1] = bget(state.board, x, y)
      end
    end

    -- Fisher–Yates.
    for i = #cells, 2, -1 do
      local j = math.random(1, i)
      cells[i], cells[j] = cells[j], cells[i]
    end

    -- Write back.
    local k = 1
    for y = CFG.ACTIVE_Y_MIN, CFG.HEIGHT - 1 do
      for x = 0, CFG.SIZE - 1 do
        bset(state.board, x, y, cells[k])
        k = k + 1
      end
    end

    update_cheat_rows_from_board(state)
  end

  -- Try a number of shuffles; require "no immediate matches" and "has at least one swap".
  for _ = 1, 60 do
    shuffle_once()
    local m = Runtime.find_matches(state.board)
    if (m == nil or #m == 0) and has_any_valid_swap() then
      return true
    end
  end

  -- Fallback: regenerate active area (no matches) and retry shuffle a bit.
  for _ = 1, 4 do
    regenerate_active_board_without_matches(state)
    if has_any_valid_swap() then return true end
    for _ = 1, 30 do
      shuffle_once()
      local m = Runtime.find_matches(state.board)
      if (m == nil or #m == 0) and has_any_valid_swap() then
        return true
      end
    end
  end

  return false
end

local function do_swap(board, x1, y1, x2, y2)
  local t = bget(board, x1, y1)
  bset(board, x1, y1, bget(board, x2, y2))
  bset(board, x2, y2, t)
end

Runtime.find_matches = function(board)
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

  return Runtime.find_matches(board)
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

  return Runtime.find_matches(board)
end

local function try_swap(board, x1, y1, x2, y2)
  if not in_active_server(x1, y1) or not in_active_server(x2, y2) then return false, nil end
  if math.abs(x1 - x2) + math.abs(y1 - y2) ~= 1 then return false, nil end

  do_swap(board, x1, y1, x2, y2)
  local matches = Runtime.find_matches(board)
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

  -- find_matches() возвращает линии по горизонтали/вертикали.
  -- В L/T-формах один и тот же камень попадает в две линии (пересечение),
  -- и прежняя логика считала его дважды (мана/урон/хил). Считаем эффекты по уникальным клеткам.
  local seen = {} -- key -> true
  local unique_counts = {} -- type -> count

  local function mark_cell(t, x, y)
    if t == nil then return end
    local k = tostring(t) .. ":" .. tostring(x) .. ":" .. tostring(y)
    if seen[k] then return end
    seen[k] = true
    unique_counts[t] = (unique_counts[t] or 0) + 1
  end

  for _, m in ipairs(matches) do
    if m.count >= 5 then extra_turn = true end
    if sim ~= nil and m.count >= 5 then sim.extra_turn = true end
    if m.cells ~= nil then
      for _, c in ipairs(m.cells) do
        if c ~= nil and in_active_server(c.x, c.y) then
          mark_cell(m.type, c.x, c.y)
        end
      end
    end
  end

  local function cnt(t)
    return tonumber(unique_counts[t]) or 0
  end

  -- Mana gems (1..3)
  for t = 1, 3 do
    local c = cnt(t)
    if c > 0 then
      local gain = mana_gain_per_object(state, CFG.GEM_MANA[t] or 0, actor_id) * c
      add_mana_capped(state, actor, gain)
      if sim ~= nil then
        if t == 1 then sim.red = sim.red + c
        elseif t == 2 then sim.yellow = sim.yellow + c
        elseif t == 3 then sim.green = sim.green + c end
      end
    end
  end

  -- Skulls (4)
  do
    local c = cnt(4)
    if c > 0 then
      if is_pvp_race(state) then
        -- «Спуск»: черепа не дают ману атакующему; −2 маны сопернику за каждый.
        if opp ~= nil then
          race_drain_mana(state, opp, (tonumber(CFG.RACE_SKULL_MANA_DRAIN) or 2) * c)
        end
      elseif affix == "energy_block" then
        actor.base_damage = math.max(0, tonumber(actor.base_damage) or 0) + 5 * c
      else
        -- Весь урон от черепов за ход (все каскады) + база персонажа — один бросок в конце resolve_action.
        state._action_damage_flat = (state._action_damage_flat or 0) + CFG.SKULL_DAMAGE * c
      end
      if affix == "monster_rage" and actor_id == state.bot_user_id then
        state._monster_rage_bombs_action = (state._monster_rage_bombs_action or 0) + c
      end
    end
  end

  -- Ankhs (5): суммируем за весь resolve_action, одно лечение в конце хода (не по каждому каскаду).
  -- В race: вместо хила копятся заряды петарды (списание маны сопернику).
  do
    local c = cnt(5)
    if c > 0 then
      if is_pvp_race(state) then
        race_add_petard_bank(actor, c)
        if sim ~= nil then sim.ankh = (sim.ankh or 0) + c end
      else
        state._action_ankh_count = (state._action_ankh_count or 0) + c
        if sim ~= nil then sim.ankh = (sim.ankh or 0) + c end
        if affix == "mana_vampire" and opp ~= nil then
          local vamp_gain = mana_gain_per_object(state, 2, opponent_id) * c
          add_mana_capped(state, opp, vamp_gain)
        end
      end
    end
  end

  return extra_turn
end

local function other_player_id(state, uid)
  if #state.players_sorted < 2 then return nil end
  if state.players_sorted[1] == uid then return state.players_sorted[2] end
  return state.players_sorted[1]
end

local function make_sync_msg(state, action, extra_turn, anim_steps, cheatRowsForUser, tick, sync_action_actor_uid)
  local a_id = state.players_sorted[1]
  local b_id = state.players_sorted[2]
  Runtime.Pvp.cache_player_levels(state)
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

  local server_now_unix_ms = math.floor(type(nk.time)=="function"and nk.time()or os.time()*1000)
  local turn_ends_at_unix_ms = 0
  if state.turn_deadline_paused then
    turn_ends_at_unix_ms = 0
  elseif state.global_boss and state.started and not state.ended and(state.turn_deadline_unix_ms or 0)>0 then
    turn_ends_at_unix_ms=state.turn_deadline_unix_ms
  elseif state.started and not state.ended and tick ~= nil and state.turn_deadline_tick ~= nil then
    local rem_ticks = math.max(0, (state.turn_deadline_tick or 0) - tick)
    local rem_ms = math.floor((rem_ticks * 1000) / CFG.TICK_RATE)
    turn_ends_at_unix_ms = server_now_unix_ms + rem_ms
  end

  return {
    board = export_active_board(),
    cheatRows = cheatRowsForUser or {},
    boardPopup = state._sync_popup or "",
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
    aConsumableId = tostring(a.consumable_id or ""),
    aDamageMultiplier = tonumber(a.consumable_damage_mult) or 1,
    aArmorMultiplier = tonumber(a.consumable_armor_mult) or 1,
    aHealingMultiplier = tonumber(a.consumable_healing_mult) or 1,
    aConsumableCritAdd = tonumber(a.consumable_crit_add) or 0,
    aShieldT1 = a.shield_t1 or 0,
    aShieldT2 = a.shield_t2 or 0,
    aShieldT3 = a.shield_t3 or 0,
    aFuryTurns = a.fury_active == true and 1 or 0,
    aFuryBonus = (a.fury_active == true) and math.max(0, tonumber(a.fury_bomb_bonus) or 0) or 0,
    aLevel = (state.player_levels and state.player_levels[a_id]) or 1,
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
    bConsumableId = tostring(b.consumable_id or ""),
    bDamageMultiplier = tonumber(b.consumable_damage_mult) or 1,
    bArmorMultiplier = tonumber(b.consumable_armor_mult) or 1,
    bHealingMultiplier = tonumber(b.consumable_healing_mult) or 1,
    bConsumableCritAdd = tonumber(b.consumable_crit_add) or 0,
    bountyEndsAt = state._consumable_bindings and state.owner_user_id and state._consumable_bindings[state.owner_user_id]
      and state._consumable_bindings[state.owner_user_id].bounty and state._consumable_bindings[state.owner_user_id].bounty.ends_at or 0,
    bShieldT1 = b.shield_t1 or 0,
    bShieldT2 = b.shield_t2 or 0,
    bShieldT3 = b.shield_t3 or 0,
    bFuryTurns = b.fury_active == true and 1 or 0,
    bFuryBonus = (b.fury_active == true) and math.max(0, tonumber(b.fury_bomb_bonus) or 0) or 0,
    bLevel = (state.player_levels and state.player_levels[b_id]) or 1,
    aMaxMana = max_mana_for_state(state),
    bMaxMana = max_mana_for_state(state),
    aPetardBank = math.max(0, tonumber(a.race_petard_bank) or 0),
    bPetardBank = math.max(0, tonumber(b.race_petard_bank) or 0),
    aRaceActions = (state.race_actions and tonumber(state.race_actions[a_id])) or 0,
    bRaceActions = (state.race_actions and tonumber(state.race_actions[b_id])) or 0,
    raceGoalMana = is_pvp_race(state) and race_goal_mana(state) or 0,
    raceLastTurnUserId = (is_pvp_race(state) and state.race_last_turn_user_id) or "",
    raceManaBonusEvery = is_pvp_race(state)
      and math.max(1, math.floor(tonumber(CFG.RACE_MANA_BONUS_EVERY_ACTIONS) or 5))
      or 0,
    manaDrain = math.max(0, tonumber(state._action_mana_drain) or 0),
    pvpRace = is_pvp_race(state),
    extraTurn = extra_turn or false,
    activeUserId = state.active_user_id,
    roundNumber = Runtime.Rounds.number(state),
    actionActorUserId = sync_action_actor_uid or "",
    serverNowUnixMs = server_now_unix_ms,
    turnEndsAtUnixMs = turn_ends_at_unix_ms,
    turnPaused = state.turn_deadline_paused == true,
    turnDurationSeconds = state.global_boss and 30 or turn_seconds_for_state(state),
    actionType = action and action.actionType or 0,
    fromX = action and action.fromX or -1,
    fromY = action and action.fromY or -1,
    toX = action and action.toX or -1,
    toY = action and action.toY or -1,
    abilityX = action and action.cx or -1,
    abilityY = action and action.cy or -1,
    critTriggered = state.last_crit == true,
    pveAffix = current_affix_id(state),
    globalBoss = state.global_boss ~= nil,
    globalBossPractice = state.global_boss and state.global_boss.practice==true or false,
    globalBossEventId = state.global_boss and state.global_boss.event_id or "",
    globalBossMyDamage = state.global_boss and state.global_boss.my_damage or 0,
    globalBossTotalDamage = state.global_boss and state.global_boss.total_damage or 0,
    globalBossQualified = state.global_boss and state.global_boss.qualified == true or false,
    bossOwnTurn = state.global_boss and state.global_boss.boss_own_turn or 0,
    bossScreamCooldown = state.global_boss and Runtime.GlobalBossRules.scream_cooldown(state) or 0,
    bossActionSequence = state.global_boss and state._boss_sequence or 0,
    bossAdaptationTriggered = state.global_boss and sync_action_actor_uid==state.bot_user_id and state.global_boss.adaptation_triggered==true or false,
    bossScreamTriggered = state.global_boss and sync_action_actor_uid==state.bot_user_id and state.global_boss.scream_triggered == true or false,
    bossScreamDamage = state.global_boss and sync_action_actor_uid==state.bot_user_id and state.global_boss.scream_triggered and state.global_boss.scream_damage or 0,
    bossPlayerTurnsLeft = state.global_boss and math.max(0,(state._boss_config and state._boss_config.max_turns or 30)-(state.global_boss.player_turns or 0)) or 0,
    animSteps = anim_steps or {},
  }
end

--- Арена (турнир 8 игроков): forward refs — реализации ниже после инвентаря.

local function broadcast_sync(dispatcher, state, action, extra_turn, anim_steps, tick, sync_action_actor_uid)
  -- Синки без действия (старт матча, таймаут хода) не должны тащить critTriggered с прошлого хода.
  if action == nil then
    state.last_crit = false
  end

  local mirror_ok, mirror_err = pcall(Runtime.arena_mirror_commit, state)
  if not mirror_ok then nk.logger_error("arena mirror deferred: " .. tostring(mirror_err)) end

  local uid_for_sync = sync_action_actor_uid or ""

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
    local msg = make_sync_msg(state, action, extra_turn, anim_steps, {}, tick, uid_for_sync)
    dispatcher.broadcast_message(CFG.OP_BOARD_SYNC, nk.json_encode(msg), nil, nil)
    state._sync_popup = nil
    return
  end

  local msg_allowed = make_sync_msg(state, action, extra_turn, anim_steps, state.cheat_rows or {}, tick, uid_for_sync)
  local msg_other = make_sync_msg(state, action, extra_turn, anim_steps, {}, tick, uid_for_sync)

  if #allowed_presences > 0 then
    dispatcher.broadcast_message(CFG.OP_BOARD_SYNC, nk.json_encode(msg_allowed), allowed_presences, nil)
  end
  if #other_presences > 0 then
    dispatcher.broadcast_message(CFG.OP_BOARD_SYNC, nk.json_encode(msg_other), other_presences, nil)
  end
  state._sync_popup = nil
end

local function send_reject(dispatcher, presence, reason)
  local payload = nk.json_encode({ reason = reason or "invalid_action" })
  dispatcher.broadcast_message(CFG.OP_ACTION_REJECT, payload, { presence }, nil)
end

local function broadcast_pvp_game_over(dispatcher, state, winner, actor, opponent, action_type, is_draw)
  local game_over_payload = {
    winnerUserId = winner or "",
    isDraw = is_draw == true,
  }
  if is_draw == true and is_pvp_race(state) then
    Runtime.apply_result_rewards(state, winner, true, game_over_payload)
  elseif is_draw ~= true and winner ~= nil and winner ~= "" then
    Runtime.apply_result_rewards(state, winner, false, game_over_payload)
  else
    game_over_payload.rewardXp = 0
    game_over_payload.rewardGold = 0
    game_over_payload.rewardOre = 0
    game_over_payload.rewardMatter = 0
  end
  if is_pvp_race(state) ~= true then
    local ok_achi, err_achi = pcall(function()
      Ach.flush_match_finish(state, winner, actor, opponent, action_type)
    end)
    if not ok_achi then
      nk.logger_error("achievement_flush_match_finish failed: " .. tostring(err_achi))
    end
  end
  Runtime.StatsApi.record_match_finish(state, winner, is_draw)
  Runtime.emit_game_over(dispatcher, state, game_over_payload)
end

--- Race («Спуск»): завершить матч после last-turn (ход/пропуск/таймаут) — сравнение маны.
--- Возвращает true, если матч завершён.
local function finish_race_after_last_turn(dispatcher, state, last_actor, action, tick, anim_steps, sync_action_actor_uid)
  if not is_pvp_race(state) or state.ended or last_actor == nil then
    return false
  end
  local opponent = other_player_id(state, last_actor)
  if opponent == nil then return false end

  local goal = race_goal_mana(state)
  local actor_mana = tonumber(state.stats[last_actor] and state.stats[last_actor].mana) or 0
  local opp_mana = tonumber(state.stats[opponent] and state.stats[opponent].mana) or 0
  local action_type = action and tonumber(action.actionType) or 0

  local winner = state.race_threshold_user_id or opponent
  local is_draw = false
  if actor_mana >= goal then
    if actor_mana > opp_mana then
      winner = last_actor
    elseif opp_mana > actor_mana then
      winner = opponent
    else
      winner = ""
      is_draw = true
    end
  end

  state.ended = true
  state.bot_turn_pending = false
  state.bot_turn_ready_tick = 0
  broadcast_sync(dispatcher, state, action, false, anim_steps, tick, sync_action_actor_uid)
  broadcast_pvp_game_over(dispatcher, state, winner, last_actor, opponent, action_type, is_draw)
  return true
end

--- Race: пропуск/таймаут во время «последнего хода» → сразу финал матча.
local function try_finish_race_on_last_turn_forfeit(dispatcher, state, forfeiting_user_id, tick)
  if not is_pvp_race(state) or state.ended or forfeiting_user_id == nil then
    return false
  end
  if state.race_last_turn_user_id == nil or state.race_last_turn_user_id ~= forfeiting_user_id then
    return false
  end
  return finish_race_after_last_turn(dispatcher, state, forfeiting_user_id, nil, tick, nil, nil)
end

--- Race («Спуск»): проверка цели по мане + last-turn для первого хода.
--- Возвращает true, если матч уже завершён или ход принудительно передан на last-turn.
local function try_finish_race_after_action(dispatcher, state, actor, opponent, action, extra_turn, keep_turn, tick, tick_rate, anim_steps, sync_action_actor_uid)
  if not is_pvp_race(state) or state.ended or actor == nil or opponent == nil then
    return false
  end

  local goal = race_goal_mana(state)
  local actor_mana = tonumber(state.stats[actor] and state.stats[actor].mana) or 0
  local first = state.race_first_user_id
  local action_type = action and tonumber(action.actionType) or 0
  local turn_segment_done = (keep_turn ~= true) and (extra_turn ~= true)

  local function end_race(winner, is_draw)
    state.ended = true
    state.bot_turn_pending = false
    state.bot_turn_ready_tick = 0
    broadcast_sync(dispatcher, state, action, extra_turn, anim_steps, tick, sync_action_actor_uid)
    broadcast_pvp_game_over(dispatcher, state, winner, actor, opponent, action_type, is_draw)
    return true
  end

  local function pass_to_last_turn()
    state.race_threshold_user_id = actor
    state.race_last_turn_user_id = opponent
    tick_buffs_end_turn(state.stats[actor])
    Ach.clear_five_plus_streak(state, actor)
    Runtime.Rounds.set_active(state, opponent)
    tick_cooldowns(state.stats[opponent])
    -- Как обычная смена хода: ждём OP 17, затем дедлайн; для PvE сразу планируем ход бота.
    local need_ack = state.active_user_id ~= nil
    if need_ack then
      state.turn_deadline_paused = true
      state.turn_pause_started_tick = tick
      state.turn_deadline_tick = tick
    else
      state.turn_deadline_paused = false
      state.turn_deadline_tick = tick + turn_seconds_for_state(state) * tick_rate
    end
    if state.mode == "pve" then
      if state.active_user_id == state.bot_user_id then
        state.bot_turn_pending = true
        state.bot_long_think_next = (actor == state.owner_user_id)
        if state.turn_deadline_paused then
          state.bot_turn_ready_tick = 0
        else
          state.bot_turn_ready_tick = tick + bot_think_delay_ticks(state, state.bot_long_think_next == true)
        end
      else
        state.bot_turn_pending = false
        state.bot_turn_ready_tick = 0
      end
    end
    broadcast_sync(dispatcher, state, action, false, anim_steps, tick, sync_action_actor_uid)
    state._action_mana_drain = 0
    return true
  end

  -- Конец «последнего хода» второго игрока: сравниваем / отдаём победу первому.
  if state.race_last_turn_user_id == actor and turn_segment_done then
    return finish_race_after_last_turn(dispatcher, state, actor, action, tick, anim_steps, sync_action_actor_uid)
  end

  if actor_mana < goal then
    return false
  end

  -- Первый игрок набрал цель: даём last-turn сопернику.
  if first ~= nil and actor == first then
    if state.race_last_turn_user_id == nil then
      return pass_to_last_turn()
    end
    return false
  end

  -- Второй игрок набрал цель вне last-turn → сразу победа.
  if state.race_last_turn_user_id == nil then
    return end_race(actor, false)
  end

  -- На last-turn продолжаем сегмент (keep/extra), финал — когда ход закончится.
  return false
end

local function finish_turn_and_broadcast(dispatcher, state, action, extra_turn, keep_turn, tick, tick_rate, anim_steps)
  local actor = state.active_user_id
  local opponent = other_player_id(state, actor)
  local prev_active_user_id = state.active_user_id
  local action_type = action and tonumber(action.actionType) or 0
  local sync_action_actor_uid = (action ~= nil and actor ~= nil) and actor or nil

  if actor ~= nil and action ~= nil then
    if action_type == 1 then
      state._daily_player_actions = state._daily_player_actions or {}
      state._daily_player_actions[actor] = (state._daily_player_actions[actor] or 0) + 1
    end
    local ask = Ach.map_action_to_stat(action.actionType)
    if ask ~= nil then
      Ach.inc_session(state, actor, ask, 1)
    end
    if is_pvp_race(state) then
      if state.race_actions == nil then state.race_actions = {} end
      state.race_actions[actor] = (tonumber(state.race_actions[actor]) or 0) + 1
    end
  end

  Ach.snapshot_hp_was_exactly_one(state)

  if not keep_turn and actor ~= nil and opponent ~= nil then
    apply_turn_end_affix_effects(state, actor, opponent, extra_turn == true)
  end

  Ach.snapshot_hp_was_exactly_one(state)

  if try_finish_race_after_action(dispatcher, state, actor, opponent, action, extra_turn, keep_turn, tick, tick_rate, anim_steps, sync_action_actor_uid) then
    return
  end

  if state.stats[actor].hp <= 0 or state.stats[opponent].hp <= 0 then
    local winner = state.stats[actor].hp > 0 and actor or opponent
    state.ended = true
    broadcast_sync(dispatcher, state, action, extra_turn, anim_steps, tick, sync_action_actor_uid)

    local game_over_payload = { winnerUserId = winner }
    -- Arena tournament meta for client UX (no history bracket, show modal on final win / any loss).
    if state.arena_mirror ~= nil then
      game_over_payload.arenaTournamentId = state.arena_mirror.tournament_id or ""
      game_over_payload.arenaRound = state.arena_mirror.round or ""
      game_over_payload.arenaBetTier = state.arena_mirror.bet_tier or ""
      game_over_payload.arenaKind = string.lower(tostring(state.arena_mirror.kind or "smith"))
    end
    if state.pvp_race == true then
      Runtime.apply_result_rewards(state, winner, false, game_over_payload)
    elseif state.mode == "pve" and winner == state.owner_user_id then
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
        local achievement_effects = Ach.build_match_finish_effects(state, winner, actor, opponent, action_type)
        local stats_effects = Runtime.StatsApi.build_match_finish_effects(state, winner, false)
        state._finalization_managed = state._finalization_managed or {}
        state._finalization_managed[state.owner_user_id] = true
        local finished_at = os.time()
        local binding = state._consumable_bindings and state._consumable_bindings[state.owner_user_id] or {}
        local outcome = { achievement_deltas = achievement_effects[state.owner_user_id] or {},
          currency_gold_multiplier = Runtime.ConsumableEffects.currency_multiplier(binding.bounty, finished_at, Runtime.ServerAura.currency_multiplier(Runtime.ServerAura.get_active(), "gold")),
          currency_ore_multiplier = Runtime.ConsumableEffects.currency_multiplier(binding.bounty, finished_at, Runtime.ServerAura.currency_multiplier(Runtime.ServerAura.get_active(), "ore")),
          match_stats = (stats_effects[state.owner_user_id] or {}).match_stats }
        local attempt = {}
        state.last_reward = Runtime.award_pve_victory(state.owner_user_id, state.bot_id, state.owner_session_epoch,
          state.pve_run, state.stats_result_id, outcome, attempt)
        if state.last_reward.reward_pending == true and not state.last_reward.intent_persisted then
          state._pve_pending_result = { uid = state.owner_user_id, bot_id = state.bot_id, epoch = state.owner_session_epoch,
            run = state.pve_run, result_id = state.stats_result_id, outcome = outcome, attempt = attempt }
        end
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
      game_over_payload.rewardPending = state.last_reward.reward_pending == true
      game_over_payload.itemLootPendingCount = state.last_reward.item_loot_pending_count or 0
      game_over_payload.deliveredItems = state.last_reward.delivered_items or {}
      game_over_payload.pendingItems = state.last_reward.pending_items or {}
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
        state.last_reward = Runtime.award_pve_defeat(state.owner_user_id, state.owner_session_epoch)
      end
      game_over_payload.rewardXp = state.last_reward.reward_xp or 0
      game_over_payload.rewardGold = 0
      game_over_payload.rewardOre = 0
      game_over_payload.rewardMatter = 0
      game_over_payload.newLevel = state.last_reward.level or 1
    elseif state.mode ~= "pve" and winner ~= nil then
      Runtime.Pvp.apply_game_over_rewards(state, winner, game_over_payload)
    end

    -- Arena финал: награда в UI с учётом side-bet (±20% от базы за каждую ставку).
    if game_over_payload.arenaRound == "final"
        and state.arena_mirror ~= nil
        and type(Runtime.arena_final_prize_gold_ore) == "function" then
      local bt = string.lower(tostring(state.arena_mirror.bet_tier or game_over_payload.arenaBetTier or "green"))
      local ak = string.lower(tostring(state.arena_mirror.kind or "smith"))
      local rg, ro = Runtime.arena_final_prize_gold_ore(ak, bt)
      if type(Runtime.arena_final_prize_for_winner) == "function" and winner ~= nil then
        local prize_ok, prize_gold, prize_ore = pcall(Runtime.arena_final_prize_for_winner,
          state.arena_mirror.tournament_id, winner, ak, bt)
        if prize_ok then rg, ro = prize_gold, prize_ore
        else nk.logger_error("arena reward display deferred: " .. tostring(prize_gold)) end
      end
      game_over_payload.rewardGold = tonumber(rg) or 0
      game_over_payload.rewardOre = tonumber(ro) or 0
    end
    Runtime.finish_arena_result(state, winner)
    local ok_achi, err_achi = pcall(function()
      Ach.flush_match_finish(state, winner, actor, opponent, action_type)
    end)
    if not ok_achi then
      nk.logger_error("achievement_flush_match_finish failed: " .. tostring(err_achi))
    end
    Runtime.StatsApi.record_match_finish(state, winner, false)
    Runtime.emit_game_over(dispatcher, state, game_over_payload)
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
    Ach.clear_five_plus_streak(state, actor)
    Runtime.Rounds.set_active(state, opponent)
    tick_cooldowns(state.stats[opponent])
    if state.mode == "pve" and actor == state.bot_user_id then
      state.bot_kill_plan = nil
    end
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
        state.bot_turn_ready_tick = tick + bot_think_delay_ticks(state, state.bot_long_think_next == true)
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
      ensure_active_board_has_valid_swaps(state)
    end
  end
  broadcast_sync(dispatcher, state, action, extra_turn, anim_steps, tick, sync_action_actor_uid)
  state._action_mana_drain = 0
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
    if is_pvp_race(state) then
      local bank = math.max(0, tonumber(actor and actor.race_petard_bank) or 0)
      bank = math.min(race_petard_bank_cap(), math.floor(bank))
      if bank > 0 and opp ~= nil then
        race_drain_mana(state, opp, bank)
      end
      if actor ~= nil then
        actor.race_petard_bank = 0
      end
      return false
    end
    local crit = deal_damage(state, state.board, actor, opp, CFG.PETARD_DAMAGE)
    return crit
  end

  local cells = collect_ability_cells(action_type, cx, cy)
  local skulls = 0
  local monster_rage_bombs = 0

  for _, c in ipairs(cells) do
    local t = bget(state.board, c.x, c.y)
    if t == 1 or t == 2 or t == 3 then
      add_mana_capped(state, actor, mana_gain_per_object(state, CFG.GEM_MANA[t] or 0, actor_id))
      if sim ~= nil then
        if t == 1 then sim.red = sim.red + 1
        elseif t == 2 then sim.yellow = sim.yellow + 1
        elseif t == 3 then sim.green = sim.green + 1 end
      end
    elseif t == 5 then
      if is_pvp_race(state) then
        race_add_petard_bank(actor, 1)
        if sim ~= nil then sim.ankh = (sim.ankh or 0) + 1 end
      else
        state._action_ankh_count = (state._action_ankh_count or 0) + 1
        if sim ~= nil then sim.ankh = (sim.ankh or 0) + 1 end
        if affix == "mana_vampire" and opp ~= nil then
          local vamp_gain = mana_gain_per_object(state, 2, opponent_id)
          add_mana_capped(state, opp, vamp_gain)
        end
      end
    elseif t == 4 then
      if is_pvp_race(state) then
        skulls = skulls + 1
      elseif affix == "energy_block" then
        actor.base_damage = math.max(0, tonumber(actor.base_damage) or 0) + 5
      else
        skulls = skulls + 1
      end
      if affix == "monster_rage" and actor_id == state.bot_user_id then
        monster_rage_bombs = monster_rage_bombs + 1
      end
    end
  end

  if is_pvp_race(state) then
    if skulls > 0 and opp ~= nil then
      race_drain_mana(state, opp, (tonumber(CFG.RACE_SKULL_MANA_DRAIN) or 2) * skulls)
    end
    return false
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
  state._action_ankh_count = 0
  state._monster_rage_bombs_action = 0
  state._action_mana_drain = 0
  state._sync_popup = nil
  local initial_matches = {}
  local anim_steps = {}
  local keep_turn = false
  local crit_triggered = false
  local five_plus_segments = 0
  local function tally_five_plus(matches)
    if matches == nil then return end
    for _, m in ipairs(matches) do
      local c = tonumber(m.count)
      if c ~= nil and c >= 5 then
        five_plus_segments = five_plus_segments + 1
        Ach.inc_session(state, actor_id, Ach.stat_key_line5(), 1)
      end
      if c ~= nil and c >= 6 then
        Ach.inc_session(state, actor_id, Ach.stat_key_line6(), 1)
      end
    end
  end
  if action.actionType == 1 then
    local fy = client_to_server_y(action.fromY)
    local ty = client_to_server_y(action.toY)
    local ok, matches = try_swap(state.board, action.fromX, fy, action.toX, ty)
    if not ok then
      return false, "invalid_swap", false, false, anim_steps
    end
    initial_matches = matches or {}
  elseif action.actionType == 4 then
    if apply_ability_rewards(state, actor_id, opponent_id, action.actionType, -1, -1) then crit_triggered = true end
    keep_turn = true
    state.last_crit = crit_triggered
    -- keep_turn-способности не считаются «ходом» для dnn.triple_extra
    return true, nil, false, true, anim_steps
  elseif action.actionType == 5 then
    apply_shield_stack(state.stats[actor_id])
    keep_turn = true
    state.last_crit = false
    return true, nil, false, true, anim_steps
  elseif action.actionType == 6 then
    local fus = state.stats[actor_id]
    fus.fury_active = true
    fus.fury_bomb_bonus = count_skulls(state.board)
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

  -- Safety: if the resulting active board has zero valid swaps, reshuffle it.
  -- This avoids rare softlocks (no moves + no mana).
  local did_shuffle = ensure_active_board_has_valid_swaps(state)
  if did_shuffle then
    state._sync_popup = "Нет доступных ходов.\nПеремешиваем поле"
  end

  -- Одно лечение за ход от всех капсул (матчи + каскады + зона креста/квадрата): анкх-значения + бонус один раз.
  do
    local ac = tonumber(state._action_ankh_count) or 0
    state._action_ankh_count = 0
    if ac > 0 and not is_pvp_race(state) and not (state.global_boss and actor_id==state.bot_user_id) then
      local act = state.stats[actor_id]
      if act ~= nil then
        local add = Runtime.ConsumableEffects.healing(CFG.ANKH_HEAL * ac + get_turn_heal_flat_bonus(state, act), act)
        act.hp = math.min(act.max_hp or CFG.MAX_HP, (act.hp or CFG.MAX_HP) + add)
      end
    end
  end

  -- Один расчёт урона за ход: сумма бомб (все каскады) + урон способности (если был); base_damage персонажа и крит — в roll_outgoing_damage.
  if not is_pvp_race(state) and (state._action_damage_flat or 0) > 0 then
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

  Ach.note_five_plus_streak(state, actor_id, five_plus_segments >= 1)

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
  if state._prestart_pending==true then return end
  if state.turn_deadline_paused == true then
    state.turn_deadline_paused = false
    state.turn_deadline_tick = tick + turn_seconds_for_state(state) * CFG.TICK_RATE
    state.turn_pause_started_tick = tick
    if state.active_user_id == state.bot_user_id then
      state.bot_turn_pending = true
      state.bot_long_think_next = false
      state.bot_turn_ready_tick = tick + bot_think_delay_ticks(state, false)
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

-- Global server aura (extracted: duel_match3_server_aura.lua)
local ServerAura = runtime_lua_require("modules.duel_match3_server_aura", "duel_match3_server_aura")({
  nk = nk,
  CFG = CFG,
  decode_storage_value = decode_storage_value,
})

-- Цели состязаний (Спуск / future): Storage duel_match3_contest_goals.
Runtime.ServerAura=ServerAura
Runtime.ContestGoals = runtime_lua_require("modules.duel_match3_contest_goals", "duel_match3_contest_goals")({
  nk = nk,
  CFG = CFG,
  decode_storage_value = decode_storage_value,
})

-- Equipment slot order must match client enum EquipmentSlotId (0..7).
local EQUIP_ORDER = {
  "Helmet", "Shoulders", "Chest", "Gloves", "Legs", "Feet", "WeaponLeft", "WeaponRight",
}

local ITEM_DEFS_CACHE_TTL_SEC = 30
local _item_defs_merged_cache = nil
local _item_defs_merged_cache_at = 0

local function normalize_stored_item_def(def)
  if type(def) ~= "table" then return nil end
  local kind = tostring(def.kind or "")
  if kind == "" then kind = "equipment" end
  local tier = CFG.clamp_int(tonumber(def.tier) or 1, 1, 3)
  local quality = tostring(def.quality or "normal")
  if quality == "" then quality = "normal" end
  local recipe_slot = tostring(def.recipe_slot or def.recipe_target_slot or "")

  if kind == "recipe" or kind == "material" or kind == "tesseract" or kind == "consumable" then
    local max_stack = tonumber(def.max_stack)
    if max_stack == nil then
      if kind == "material" then max_stack = 100 else max_stack = 1 end
    end
    max_stack = math.max(1, math.floor(max_stack))
    local sale_price = tonumber(def.sale_price)
    if sale_price == nil then
      if kind == "material" then sale_price = 20 else sale_price = 100 end
    end
    sale_price = math.max(0, math.floor(sale_price))
    return {
      kind = kind,
      effect_id = kind=="consumable" and tostring(def.effect_id or "") or "",
      slot = "",
      max_stack = max_stack,
      tier = tier,
      quality = quality,
      recipe_slot = recipe_slot,
      craft_recipe_id = "",
      sale_price = sale_price,
      hp = 0,
      damage = 0,
      armor = 0,
      crit_chance = 0.0,
      healing = 0,
    }
  end

  local slot = tostring(def.slot or "")
  if slot == "" then return nil end
  local sale_price = math.max(0, math.floor(tonumber(def.sale_price) or 100))
  local out = {
    kind = "equipment",
    slot = slot,
    max_stack = 1,
    tier = tier,
    quality = quality,
    recipe_slot = "",
    craft_recipe_id = tostring(def.craft_recipe_id or ""),
    craft_item_id = tostring(def.craft_item_id or ""),
    craft_minutes = math.max(0, math.floor(tonumber(def.craft_minutes) or 0)),
    sale_price = sale_price,
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
  if type(sheet) == "table" then Runtime.Inventory.normalize(sheet) end
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

-- Item defs: Storage only (no Lua fallback). Missing catalog => empty + error log.
local function get_merged_item_defs()
  local now = os.time()
  if _item_defs_merged_cache ~= nil and (now - _item_defs_merged_cache_at) < ITEM_DEFS_CACHE_TTL_SEC then
    return _item_defs_merged_cache
  end
  local from_st = read_item_defs_from_storage()
  if from_st == nil then
    nk.logger_error(string.format(
      "item_defs catalog unavailable: collection=%s key=%s user_id=%s",
      tostring(CFG.ITEM_DEFS_COLLECTION),
      tostring(CFG.ITEM_DEFS_KEY),
      tostring(CFG.ITEM_DEFS_STORAGE_USER_ID)
    ))
    from_st = {}
  end
  Runtime.Shop.merge_defs(from_st)
  _item_defs_merged_cache = from_st
  _item_defs_merged_cache_at = now
  return from_st
end

local BOTS_CACHE_TTL_SEC = 30
local _bots_merged_by_diff = {}
local _bots_merged_at_by_diff = {}

local function normalize_stored_bot(id_key, def)
  if type(def) ~= "table" then return nil end
  local floor = CFG.clamp_int(def.floor ~= nil and def.floor or def.difficulty, 1, CFG.PVE_MAX_LEVEL)
  local fallback = build_floor_bot_entry(floor)
  local bid = mine_bot_id_for_floor(floor)
  local name = CFG.mine_monster_name(floor, tostring(def.name or fallback.name or ""))
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

-- Bots: Storage only (BOTS_KEY + optional per-difficulty keys).
local function get_merged_bots(difficulty)
  local diff = normalize_mine_difficulty(difficulty or CFG.MINE_DIFFICULTY_DEFAULT)
  local now = os.time()
  local cached = _bots_merged_by_diff[diff]
  local cached_at = _bots_merged_at_by_diff[diff]
  if cached ~= nil and cached_at ~= nil and (now - cached_at) < BOTS_CACHE_TTL_SEC then
    return cached
  end
  local merged = {}
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
  if next(merged) == nil then
    nk.logger_error(string.format(
      "bot_defs catalog unavailable: collection=%s difficulty=%s user_id=%s",
      tostring(CFG.BOTS_COLLECTION),
      tostring(diff),
      tostring(CFG.BOTS_STORAGE_USER_ID)
    ))
  end
  -- Дыры в Storage (нет mine_1 и т.п.) — добиваем шаблоном этажа, иначе UI/бой ломаются.
  for floor = 1, CFG.PVE_MAX_LEVEL do
    local id = mine_bot_id_for_floor(floor)
    if merged[id] == nil then
      merged[id] = build_floor_bot_entry(floor)
    end
  end
  _bots_merged_by_diff[diff] = merged
  _bots_merged_at_by_diff[diff] = now
  return merged
end

local function get_bot_profile(bot_id, difficulty_opt)
  local diff = normalize_mine_difficulty(difficulty_opt or CFG.MINE_DIFFICULTY_DEFAULT)
  local bots = get_merged_bots(diff)
  if bot_id == nil or bot_id == "" then return nil end
  return bots[bot_id]
end

local function normalize_character_sheet(val)
  val = Runtime.Inventory.normalize(val or {})
  local eq = val.equipment
  if type(eq) ~= "table" then eq = {} end
  for i = 1, 8 do
    if eq[i] == nil or eq[i] == false then eq[i] = "" end
    if type(eq[i]) ~= "string" then eq[i] = tostring(eq[i]) end
  end
  local inv = val.inventory
  if type(inv) ~= "table" then inv = {} end
  for i = 1, Runtime.Inventory.capacity(val) do
    if inv[i] == nil or inv[i] == false then inv[i] = "" end
    if type(inv[i]) ~= "string" then inv[i] = tostring(inv[i]) end
  end
  local ic_in = val.inventory_counts
  if type(ic_in) ~= "table" then ic_in = {} end
  local inventory_counts = {}
  for i = 1, Runtime.Inventory.capacity(val) do
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
    inventory_extra_slots = val.inventory_extra_slots,
    inventory_pending_loot = val.inventory_pending_loot,
    inventory_sale_receipts = val.inventory_sale_receipts,
    inventory_slot_receipts = val.inventory_slot_receipts,
    consumables = val.consumables,
    first_recipe_received = val.first_recipe_received == true,
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
    if not ok then error(rows) end
    local sheet = normalize_character_sheet({})
    sheet._storage_version = "*"
    return sheet
  end
  local val = decode_storage_value(rows[1]) or {}
  local sheet = normalize_character_sheet(val)
  Runtime.Inventory.observe_recipes(sheet, get_merged_item_defs())
  sheet._storage_version = rows[1].version
  return sheet
end

Runtime.build_character_sheet_write = function(user_id, sheet)
  return {
      collection = CFG.CHARACTER_SHEET_COLLECTION,
      key = CFG.CHARACTER_SHEET_KEY,
      user_id = user_id,
      value = {
        equipment = sheet.equipment,
        inventory = sheet.inventory,
        inventory_counts = sheet.inventory_counts or {},
        inventory_extra_slots = sheet.inventory_extra_slots or 0,
        inventory_pending_loot = sheet.inventory_pending_loot or {},
        inventory_sale_receipts = sheet.inventory_sale_receipts or {},
        inventory_slot_receipts = sheet.inventory_slot_receipts or {},
        consumables = sheet.consumables or {schema=1},
        first_recipe_received = sheet.first_recipe_received == true,
        learned_recipes = sheet.learned_recipes or {},
        workshop_slots = sheet.workshop_slots or {},
        updated_at = os.time(),
      },
      permission_read = 1,
      permission_write = 0,
      version = sheet._storage_version or "*",
  }
end

-- All inventory writers carry their read version, including match rewards.
-- Economy operations may atomically commit their currency changes in this batch.
Runtime.build_character_sheet_writes = function(user_id, sheet, progress, progress_version)
  if progress ~= nil then Runtime.Inventory.sync_resource_counters(sheet, progress, get_merged_item_defs()) end
  local writes = { Runtime.build_character_sheet_write(user_id, sheet) }
  if progress ~= nil then
    progress.updated_at = os.time()
    writes[#writes + 1] = {
      collection = CFG.PVE_PROGRESS_COLLECTION,
      key = CFG.PVE_PROGRESS_KEY,
      user_id = user_id,
      value = progress,
      permission_read = 1,
      permission_write = 0,
      version = (progress_version ~= nil and progress_version ~= "") and progress_version or "*",
    }
  end
  return writes
end

local function write_character_sheet(user_id, sheet, progress, progress_version)
  local acks = nk.storage_write(Runtime.build_character_sheet_writes(user_id, sheet, progress, progress_version))
  for _, ack in ipairs(acks or {}) do
    if ack.collection == CFG.CHARACTER_SHEET_COLLECTION and ack.key == CFG.CHARACTER_SHEET_KEY then
      sheet._storage_version = ack.version
      break
    end
  end
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
  if not ok then error(rows) end
  if rows ~= nil and #rows > 0 then return end

  local created, create_err = pcall(write_character_sheet, user_id, normalize_character_sheet({}))
  if created then return end
  if string.find(tostring(create_err), "version", 1, true) ~= nil then
    -- Another request may have initialized the sheet after our missing read.
    -- Accept that race only after confirming an actual stored object exists.
    local existing = nk.storage_read({
      { collection = CFG.CHARACTER_SHEET_COLLECTION, key = CFG.CHARACTER_SHEET_KEY, user_id = user_id },
    })
    if existing ~= nil and #existing > 0 then return end
  end
  error(create_err)
end

--- 8/8 экипировано, один tier и одно quality: ×1.25 к hp/damage/armor/healing с вещей (см. gen_item_catalog.js SET_BONUS_EQ).
--- Крит с экипа не умножается (там же mergedFromSlotStats: crit = база + сумма с перчаток).
local SET_BONUS_FULL_EQUIP_STATS = 1.25

local function equipment_full_set_stat_multiplier(sheet)
  if sheet == nil or type(sheet.equipment) ~= "table" then
    return 1.0
  end
  local defs = get_merged_item_defs()
  local slots = { "Helmet", "Shoulders", "Chest", "Gloves", "Legs", "Feet", "WeaponLeft", "WeaponRight" }
  local first_tier, first_q = nil, nil
  for i = 1, 8 do
    local def_id = sheet.equipment[i]
    if def_id == nil or def_id == "" then
      return 1.0
    end
    local d = defs[def_id]
    if d == nil or not item_def_is_equipment(d) or tostring(d.slot or "") ~= slots[i] then
      return 1.0
    end
    local t = CFG.clamp_int(tonumber(d.tier) or 1, 1, 99)
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
  local eq = sheet and sheet.equipment or {}
  local slots = { "Helmet", "Shoulders", "Chest", "Gloves", "Legs", "Feet", "WeaponLeft", "WeaponRight" }
  for i = 1, 8 do
    local def_id = eq[i]
    if def_id ~= nil and def_id ~= "" then
      local d = defs[def_id]
      if d ~= nil and item_def_is_equipment(d) and tostring(d.slot or "") == slots[i] then
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
  for i = 1, Runtime.Inventory.capacity(sheet) do
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

--- Spendable inventory resources; progress counters also include already consumed drops.
local function inventory_resource_totals(sheet)
  if sheet == nil then return 0, 0 end
  ensure_sheet_inventory_counts(sheet)
  local totals = Runtime.Inventory.resource_totals(sheet, get_merged_item_defs())
  return totals.ingots, totals.tesseracts
end

local function build_resource_payload(progress, user_id, sheet_opt)
  local sheet = sheet_opt or read_character_sheet(user_id)
  local totals = Runtime.Inventory.resource_totals(sheet, get_merged_item_defs())
  local ingots, tesseracts = totals.ingots, totals.tesseracts
  local energy_max = pve_energy_max_for_user(user_id)
  local miner_key = math.max(0, tonumber(progress.key_items and progress.key_items.miner_key) or 0)
  local dark_key = math.max(0, tonumber(progress.key_items and progress.key_items.dark_key) or 0)
  -- Итог по ключам для HUD: раньше в progress.keys писалось отдельно и не синхронизировалось с key_items.
  local keys_total = miner_key + dark_key
  return {
    energy = CFG.clamp_int(progress.energy, 0, energy_max),
    energy_max = energy_max,
    ore = math.max(0, tonumber(progress.ore) or 0),
    gold = math.max(0, tonumber(progress.gold) or 0),
    ingots = ingots,
    matter = math.max(0, tonumber(progress.matter) or 0),
    keys = keys_total,
    blueprint_green = totals.blueprint_green,
    blueprint_blue = totals.blueprint_blue,
    blueprint_purple = totals.blueprint_purple,
    blueprint_gold = totals.blueprint_gold,
    tesseracts = tesseracts,
    miner_key = miner_key,
    dark_key = dark_key,
  }
end

local function build_progression_payload(progress, user_id, sheet_opt)
  local resources = build_resource_payload(progress, user_id, sheet_opt)
  return {
    level = progress.level or 1,
    xp = progress.xp or 0,
    gold = resources.gold,
    max_level = CFG.PVE_MAX_LEVEL,
    energy = resources.energy,
    energy_max = resources.energy_max,
    ore = resources.ore,
    ingots = resources.ingots,
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
  local stats, cosmetics_inventory_changed = CharStats.compute_character_display_stats(user_id, sheet)
  if cosmetics_inventory_changed then sheet = Runtime.readCharacterSnapshot(user_id) end
  progress = Runtime.readProgressSnapshot(user_id) -- Late-bound: cosmetics may credit the wallet; the local reader is defined below this closure.
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
      inventory_capacity = Runtime.Inventory.capacity(sheet),
      inventory_slot_price_matter = Runtime.Inventory.SLOT_PRICE_MATTER,
      pending_loot = Runtime.Inventory.pending_payload(sheet),
      consumables = Runtime.ConsumableModule.status(sheet),
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

--- craft_recipe_id matches scroll def_id: recipe_drop_t{tier}_{color|gold}_{Slot}.
local function sheet_has_learned_for_craft(sheet, craft_recipe_id)
  return sheet_has_learned(sheet, craft_recipe_id)
end

local function item_def_is_legendary_quality(def)
  return def ~= nil and tostring(def.quality or "") == "legendary"
end

--- Легенда нужного тира и слота (§4.2: поглощение для зелёного T2/T3).
local function workshop_legend_fodder_matches(def, slot_name, legend_tier)
  if def == nil or not item_def_is_equipment(def) or not item_def_is_legendary_quality(def) then
    return false
  end
  if CFG.clamp_int(tonumber(def.tier) or 0, 1, 3) ~= legend_tier then return false end
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
  for inv_i = 1, Runtime.Inventory.capacity(sheet) do
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
  for inv_i = 1, Runtime.Inventory.capacity(sheet) do
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
  if CFG.clamp_int(tonumber(def.tier) or 0, 1, 3) ~= tier then return false end
  return tostring(def.quality or "") == quality_str
end

local function workshop_has_quality_fodder(sheet, defs, slot_index, tier, quality_str)
  local slot_name = EQUIP_ORDER[slot_index + 1]
  local eq_i = slot_index + 1
  local eq_id = sheet.equipment[eq_i] or ""
  if eq_id ~= "" and workshop_fodder_matches_tier_slot_quality(defs[eq_id], slot_name, tier, quality_str) then
    return true
  end
  for inv_i = 1, Runtime.Inventory.capacity(sheet) do
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
  for inv_i = 1, Runtime.Inventory.capacity(sheet) do
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

--- Явное поглощение по craft_item_id из каталога (предпочтительно над эвристикой quality/tier).
local function workshop_has_exact_item_fodder(sheet, craft_item_id)
  craft_item_id = tostring(craft_item_id or "")
  if craft_item_id == "" then return true end
  for eq_i = 1, 8 do
    if tostring(sheet.equipment[eq_i] or "") == craft_item_id then
      return true
    end
  end
  for inv_i = 1, Runtime.Inventory.capacity(sheet) do
    local id = sheet.inventory[inv_i] or ""
    if id == craft_item_id and (tonumber(sheet.inventory_counts[inv_i]) or 0) >= 1 then
      return true
    end
  end
  return false
end

local function workshop_consume_exact_item_fodder(sheet, craft_item_id)
  craft_item_id = tostring(craft_item_id or "")
  if craft_item_id == "" then return true end
  for eq_i = 1, 8 do
    if tostring(sheet.equipment[eq_i] or "") == craft_item_id then
      sheet.equipment[eq_i] = ""
      return true
    end
  end
  for inv_i = 1, Runtime.Inventory.capacity(sheet) do
    local id = sheet.inventory[inv_i] or ""
    if id == craft_item_id then
      local cnt = tonumber(sheet.inventory_counts[inv_i]) or 0
      if cnt >= 1 then
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

--- Длительность крафта: craft_minutes из каталога, иначе WORKSHOP_CRAFT_DURATION_SEC_BY_TIER.
local function workshop_craft_duration_seconds(out_def, tier)
  local mins = tonumber(out_def and out_def.craft_minutes)
  if mins ~= nil and mins > 0 then
    return math.floor(mins) * 60
  end
  local dur_tbl = CFG.WORKSHOP_CRAFT_DURATION_SEC_BY_TIER
  return (dur_tbl and dur_tbl[tier]) or (60 * 60)
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
  for i = 1, Runtime.Inventory.capacity(sheet) do
    if sheet.inventory[i] == def_id then
      n = n + (tonumber(sheet.inventory_counts[i]) or 0)
    end
  end
  return n
end

local function inventory_try_add(sheet, def_id, amount)
  return Runtime.Inventory.try_add(sheet, def_id, amount, get_merged_item_defs())
end

--- Цвет рецепта с монстров шахты: по **сложности** (easy → green, medium → blue, hard → purple).
local function mine_recipe_color_for_difficulty(diff)
  local d = normalize_mine_difficulty(diff)
  if d == "medium" then return "blue" end
  if d == "hard" then return "purple" end
  return "green"
end

--- Тир в id свитка. Экип/предметы — только T1; цвет (green/blue/purple) различает сложность.
local function mine_tier_from_diff(_diff)
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
  local f = CFG.clamp_int(floor, 1, CFG.PVE_MAX_LEVEL)
  local r = f % 4
  if r == 1 then return 0.25 end
  if r == 2 then return 0.5 end
  if r == 3 then return 0.75 end
  return 1.0
end

--- id предмета-рецепта: recipe_drop_t1_{цвет}_{Slot} (T1-only; цвет = сложность шахты).

--- Этажи с дропом рецепта (только не-боссы): 10 нет — иначе пересечение с боссом 4/8/12; боссы рецептов не дают.
local function mine_recipe_drop_chance_for_floor(floor)
  local f = CFG.clamp_int(floor, 1, CFG.PVE_MAX_LEVEL)
  return Runtime.MineRecipes.chance(f)
end

local function mine_floor_uses_mine_item_recipe_drop(floor, is_boss)
  if is_boss then return false end
  return mine_recipe_drop_chance_for_floor(floor) > 0
end

--- Один рецепт на этаж; фактическая общая таблица находится в duel_mine_recipe_drops.
--- def_id свитка для этажа с дропом рецепта или пусто, если этаж вне списка.
--- Формат: recipe_drop_t1_{green|blue|purple}_{Slot} (mine_tier всегда 1).
local function mine_recipe_item_id_for_floor_index(floor, color, mine_tier)
  return Runtime.MineRecipes.recipe_id(floor, color, CFG.clamp_int(tonumber(mine_tier) or 1, 1, 3))
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
  for i = 1, Runtime.Inventory.capacity(sheet) do
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
  for i = 1, Runtime.Inventory.capacity(sheet) do
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
  -- Only the system-owned allowlist is trusted. Entries contain immutable user IDs,
  -- never email addresses or user-editable account metadata.
  if storage_user_id ~= "00000000-0000-0000-0000-000000000000" then return {} end
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
    return {}
  end

  local row = rows[1]
  local val = decode_storage_value(row) or {}
  return type(val) == "table" and type(val.user_ids) == "table" and val.user_ids or {}
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

  -- Read only the system bucket; player-owned buckets cannot grant privileges.
  local global_list = read_cheat_whitelist_emails_for_user_id(CFG.CHEAT_WHITELIST_USER_ID)
  absorb(global_list)

  return set
end


local function is_user_allowed_for_cheat_rows(user_id, whitelist_set)
  if type(user_id) ~= "string" or user_id == "" then return false end
  local set = whitelist_set
  if set == nil then
    set = build_cheat_whitelist_set({ user_id })
  end
  return set[string.lower(user_id)] == true
end

function normalize_mine_unlocked(raw, default_floor)
  local out = {
    easy = CFG.clamp_int(raw and raw.easy or default_floor, 1, CFG.PVE_MAX_LEVEL),
    medium = CFG.clamp_int(raw and raw.medium or 0, 0, CFG.PVE_MAX_LEVEL),
    hard = CFG.clamp_int(raw and raw.hard or 0, 0, CFG.PVE_MAX_LEVEL),
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
    local cap = CFG.clamp_int(storage_max, 0, nil)
    if cap <= 0 then
      progress.energy = 0
      progress.energy_updated_at = now
      return
    end
    local regen_top = math.min(cap, math.max(0, tonumber(CFG.PVE_ENERGY_REGEN_CAP) or 100))
    local energy = CFG.clamp_int(progress.energy, 0, cap)
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
      nickname_changes = 0,
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
    energy = CFG.clamp_int(initial_energy, 0, energy_max),
    energy_updated_at = math.floor(tonumber(val.energy_updated_at) or now),
    nickname_changes = math.max(0, math.floor(tonumber(val.nickname_changes) or 0)),
    mine = type(val.mine) == "table" and val.mine or {},
    -- Prepaid вход в «Спуск»: списан до очереди, возвращается при отмене поиска.
    race_entry_pending = val.race_entry_pending == true,
    race_entry_costs = type(val.race_entry_costs) == "table" and val.race_entry_costs or nil,
  }
  progress.mine.current_difficulty = normalize_mine_difficulty(progress.mine.current_difficulty)
  progress.mine.selected_floor = CFG.clamp_int(progress.mine.selected_floor or 1, 1, CFG.PVE_MAX_LEVEL)
  progress.mine.unlocked = normalize_mine_unlocked(progress.mine.unlocked, 1)
  if type(progress.mine.floor_states) ~= "table" then progress.mine.floor_states = {} end
  progress.key_items.miner_key = math.max(0, tonumber(progress.key_items.miner_key) or 0)
  progress.key_items.dark_key = math.max(0, tonumber(progress.key_items.dark_key) or 0)
  progress.level = current_level_from_xp(progress.xp)
  apply_energy_regen(progress, energy_max, now)
  return progress, row.version
end

--- §4.6 / §14 фаза 5: статы как в PvE (уровень + экип + достижения), без mine_stat_multiplier и без ауры.
local function apply_pvp_pro_stats_from_sheet(actor, user_id)
  CharStats.apply_player_combat_stats_from_sheet(actor, user_id, nil)
end

Runtime.build_pve_progress_write = function(user_id, progress, version)
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
      nickname_changes = math.max(0, math.floor(tonumber(progress.nickname_changes) or 0)),
      mine = progress.mine,
      race_entry_pending = progress.race_entry_pending == true,
      race_entry_costs = type(progress.race_entry_costs) == "table" and progress.race_entry_costs or nil,
      updated_at = os.time(),
    },
    permission_read = 1,
    permission_write = 0,
  }
  write_obj.version = (version ~= nil and version ~= "") and version or "*"
  return write_obj
end

local function write_pve_progress(user_id, progress, version)
  nk.storage_write({ Runtime.build_pve_progress_write(user_id, progress, version) })
end

function random_affix_for_floor(floor)
  local f = CFG.clamp_int(floor, 1, CFG.PVE_MAX_LEVEL)
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

Runtime.readProgressSnapshot=read_pve_progress
Runtime.Journal = runtime_lua_require("modules.duel_match3_journal", "duel_match3_journal").new({
  stats_collection = CFG.STATS_COLLECTION, stats_key = CFG.STATS_KEY,
})
Runtime.PveEntry = runtime_lua_require("modules.duel_match3_pve_entry", "duel_match3_pve_entry").new({
  read_pve_progress = read_pve_progress, build_pve_progress_write = Runtime.build_pve_progress_write,
  journal = Runtime.Journal, energy_max = pve_energy_max_for_user,
})
Runtime.Consumables = Runtime.ConsumableModule.new({
  Guard=Guard,read_sheet=read_character_sheet,read_progress=read_pve_progress,get_defs=get_merged_item_defs,
  build_sheet_write=Runtime.build_character_sheet_write,encode_character=encode_character_ok_response,
  journal=function()return Runtime.Journal end,is_human=Ach.is_human,
  bind_extra_writes=function(state,uid)
    if state.mode=="pve" then return Runtime.PveEntry.consume_writes(uid,state.pve_run and state.pve_run.launch_token,state.stats_result_id) end
    return {}
  end,
})
Runtime.PveRewards = runtime_lua_require("modules.duel_match3_pve_rewards", "duel_match3_pve_rewards").new({
  normalize_difficulty = normalize_mine_difficulty, get_bot_profile = get_bot_profile,
  clamp_floor = function(f) return CFG.clamp_int(f, 1, CFG.PVE_MAX_LEVEL) end,
  max_floor = CFG.PVE_MAX_LEVEL, is_boss_floor = is_boss_floor,
  reward_multiplier = mine_reward_multiplier, stat_multiplier = mine_stat_multiplier,
  apply_reward_xp = function(xp) return ServerAura.apply_to_pve_reward_xp(xp, ServerAura.get_active()) end,
  ingot_drop_chance = ingot_drop_chance_non_boss, ingot_def = ingot_def_for_difficulty,
  uses_recipe_drop = mine_floor_uses_mine_item_recipe_drop, recipe_color = mine_recipe_color_for_difficulty,
  pick_recipe = function(uid, floor, boss, diff)
    local chance = mine_recipe_drop_chance_for_floor(floor)
    if not mine_floor_uses_mine_item_recipe_drop(floor, boss) or math.random() > chance then return "" end
    local id = mine_recipe_item_id_for_floor_index(floor, mine_recipe_color_for_difficulty(diff), mine_tier_from_diff(diff))
    if id == "" or sheet_has_learned_for_craft(read_character_sheet(uid), id) then return "" end
    return id
  end,
  respawn_seconds = function(boss)
    return ServerAura.mine_respawn_duration_seconds(boss and CFG.MINE_RESPAWN_BOSS_SECONDS or CFG.MINE_RESPAWN_NORMAL_SECONDS, ServerAura.get_active())
  end,
  next_affix = function(floor, fought)
    local affix = random_affix_for_floor(floor)
    if #CFG.MINE_AFFIX_POOL > 1 then
      for _ = 1, 12 do if affix ~= tostring(fought or "") then break end; affix = random_affix_for_floor(floor) end
    end
    return affix
  end,
  read_progress = read_pve_progress, read_sheet = read_character_sheet, get_defs = get_merged_item_defs,
  inventory = function() return Runtime.Inventory end,
  build_sheet_writes = Runtime.build_character_sheet_writes,
  consume_writes = function(uid, token, id)
    local writes, err = Runtime.PveEntry.consume_writes(uid, token, id)
    assert(writes, err); return writes
  end,
  empty_keys = empty_key_items, add_blueprint = add_blueprint, level_from_xp = current_level_from_xp,
  normalize_unlocked = normalize_mine_unlocked, floor_key = make_floor_state_key,
  journal = function() return Runtime.Journal end,
  is_stale = function(uid, epoch) return Guard.is_epoch_stale_for_match(uid, tonumber(epoch) or 0) end,
})
Runtime.award_pve_victory = function(user_id, bot_id, epoch, run_meta, result_id, outcome, attempt)
  return Runtime.PveRewards.award(user_id, bot_id, epoch, run_meta, result_id, outcome, attempt)
end

Runtime.award_pve_defeat = function(user_id, match_epoch_snapshot)
  local snap = tonumber(match_epoch_snapshot) or 0
  local defeat_xp = ServerAura.apply_to_pve_reward_xp(10, ServerAura.get_active())
  if Guard.is_epoch_stale_for_match(user_id, snap) then
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

local build_pvp = runtime_lua_require("modules.duel_match3_pvp", "duel_match3_pvp")
Runtime.Pvp = build_pvp({
  nk = nk,
  CFG = CFG,
  clamp_int = CFG.clamp_int,
  read_pve_progress = read_pve_progress,
  write_pve_progress = write_pve_progress,
  current_level_from_xp = current_level_from_xp,
  get_bot_profile = get_bot_profile,
  normalize_mine_difficulty = normalize_mine_difficulty,
  award_pve_defeat = Runtime.award_pve_defeat,
  other_player_id = other_player_id,
  ContestGoals = Runtime.ContestGoals,
  add_blueprint = add_blueprint,
  empty_key_items = empty_key_items,
  get_race_entry = function() return Runtime.RaceEntry end,
  build_match_finish_effects = function(...) return Runtime.StatsApi.build_match_finish_effects(...) end,
})

-- Stats RPC / arena tournament counters: отдельный chunk (лимит 200 local).
Runtime.StatsApi = runtime_lua_require("modules.duel_match3_stats_api", "duel_match3_stats_api")({
  finalization_journal = Runtime.Journal,
  CFG = CFG,
  Ach = Ach,
  Guard = Guard,
  read_pve_progress = read_pve_progress,
  normalize_mine_difficulty = normalize_mine_difficulty,
  ensure_character_sheet_initialized = ensure_character_sheet_initialized,
})

function get_unlocked_floor(progress, diff)
  local d = normalize_mine_difficulty(diff)
  local mine = progress.mine or {}
  local unlocked = normalize_mine_unlocked(mine.unlocked, 1)
  local raw = unlocked[d]
  -- В Lua 0 truthy: `0 or 1` → 0; для medium/hard с 0 этажей явно оставляем 0, затем поднимаем до 1.
  local v
  if raw == nil then
    v = (d == "easy") and 1 or 0
  else
    v = CFG.clamp_int(raw, 0, CFG.PVE_MAX_LEVEL)
  end
  -- 1-й этаж выбранной сложности всегда доступен (барьеры с этажа 2).
  if v < 1 then v = 1 end
  return v
end

local MineBarriers = runtime_lua_require("modules.duel_match3_mine_barriers", "duel_match3_mine_barriers")({
  CFG = CFG,
  Guard = Guard,
  normalize_mine_difficulty = normalize_mine_difficulty,
  normalize_mine_unlocked = normalize_mine_unlocked,
  make_floor_state_key = make_floor_state_key,
  get_unlocked_floor = get_unlocked_floor,
  read_pve_progress = read_pve_progress,
  write_pve_progress = write_pve_progress,
  build_progression_payload_auto = build_progression_payload_auto,
  build_resource_payload = build_resource_payload,
  random_affix_for_floor = random_affix_for_floor,
  empty_key_items = empty_key_items,
})

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
    local unlocked_map = normalize_mine_unlocked(progress.mine and progress.mine.unlocked or nil, 1)
    local mine_floors = {}
    for _, b in ipairs(bots) do
      local floor = CFG.clamp_int(b.floor or 1, 1, CFG.PVE_MAX_LEVEL)
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
        wins = math.max(0, tonumber(fs and fs.wins or 0) or 0),
      }
    end

    return nk.json_encode({
      ok = true,
      progression = build_progression_payload_auto(progress, user_id),
      level_xp = CFG.LEVEL_XP,
      max_level = CFG.PVE_MAX_LEVEL,
      mine_difficulty = current_diff,
      mine_unlocked = unlocked_map,
      barrier_cost_multiplier = MineBarriers.mine_barrier_cost_multiplier(current_diff),
      barrier_requirements = MineBarriers.build_barrier_requirements_for_diff(current_diff),
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
  CFG = CFG,
  try_match_create = try_match_create,
  make_bot_user_id = make_bot_user_id,
  guard_read_metadata_epoch = Guard.read_metadata_epoch,
  guard_assert_client_epoch_matches = Guard.assert_client_epoch_matches,
  read_pve_progress = read_pve_progress,
  write_pve_progress = write_pve_progress,
  build_pve_progress_write = Runtime.build_pve_progress_write,
  read_character_sheet = read_character_sheet,
  write_character_sheet = write_character_sheet,
  build_character_sheet_write = Runtime.build_character_sheet_write,
  ensure_sheet_inventory_counts = ensure_sheet_inventory_counts,
  inventory_remove_def_total = inventory_remove_def_total,
  inventory_try_add = inventory_try_add,
  get_item_defs = get_merged_item_defs,
  stats_inc_arena_tournament_played = Runtime.StatsApi.stats_inc_arena_tournament_played,
  achievement_merge_stats = Ach.merge_persistent_stats,
})
Runtime.arena_mirror_commit = Arena.mirror_commit
Runtime.arena_on_match_finished = Arena.on_match_finished
Runtime.arena_on_match_created = Arena.on_match_created
Runtime.arena_final_prize_gold_ore = Arena.final_prize_for_kind_and_bet
Runtime.arena_final_prize_for_winner = Arena.final_prize_for_winner


function parse_floor_from_bot_id(bot_id)
  local sid = tostring(bot_id or "")
  local n = string.match(sid, "mine_(%d+)")
  return CFG.clamp_int(n, 1, CFG.PVE_MAX_LEVEL)
end


Runtime.EconomyRpc = runtime_lua_require("modules.duel_match3_economy_rpc", "duel_match3_economy_rpc")({
  CFG = CFG,
  Guard = Guard,
  EQUIP_ORDER = EQUIP_ORDER,
  read_character_sheet = read_character_sheet,
  write_character_sheet = write_character_sheet,
  ensure_sheet_inventory_counts = ensure_sheet_inventory_counts,
  ensure_character_sheet_initialized = ensure_character_sheet_initialized,
  ensure_sheet_workshop = ensure_sheet_workshop,
  get_merged_item_defs = get_merged_item_defs,
  item_def_is_equipment = item_def_is_equipment,
  item_def_is_recipe = item_def_is_recipe,
  item_max_stack = item_max_stack,
  encode_character_ok_response = encode_character_ok_response,
  sheet_has_learned = sheet_has_learned,
  sheet_has_learned_for_craft = sheet_has_learned_for_craft,
  workshop_craft_cost_from_def = workshop_craft_cost_from_def,
  workshop_has_legendary_fodder = workshop_has_legendary_fodder,
  workshop_consume_legendary_fodder = workshop_consume_legendary_fodder,
  workshop_has_quality_fodder = workshop_has_quality_fodder,
  workshop_consume_quality_fodder = workshop_consume_quality_fodder,
  workshop_has_exact_item_fodder = workshop_has_exact_item_fodder,
  workshop_consume_exact_item_fodder = workshop_consume_exact_item_fodder,
  workshop_craft_duration_seconds = workshop_craft_duration_seconds,
  inventory_count_def = inventory_count_def,
  inventory_try_add = inventory_try_add,
  inventory_remove_def_total = inventory_remove_def_total,
  inventory_can_fit = inventory_can_fit,
  read_pve_progress = read_pve_progress,
  write_pve_progress = write_pve_progress,
  build_resource_payload = build_resource_payload,
  build_progression_payload_auto = build_progression_payload_auto,
  pve_energy_max_for_user = pve_energy_max_for_user,
  empty_key_items = empty_key_items,
})

Runtime.InventoryRpc = runtime_lua_require("modules.duel_match3_inventory_rpc", "duel_match3_inventory_rpc")({
  Guard = Guard, read_character_sheet = read_character_sheet, write_character_sheet = write_character_sheet,
  read_pve_progress = read_pve_progress, get_merged_item_defs = get_merged_item_defs,
  encode_character_ok_response = encode_character_ok_response,
})

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
    local uid = ctx and ctx.user_id or ""
    if uid == "" then return nk.json_encode({ ok = false, err = "unauthorized" }) end
    local epoch_ok, epoch_err = Guard.assert_client_epoch_matches(uid, payload)
    if not epoch_ok then return nk.json_encode({ ok = false, err = epoch_err }) end
    if Runtime.Recovery then
      local recovered, err = Runtime.Recovery.maybe_recover(uid, { force = true })
      if not recovered then return nk.json_encode({ ok = false, err = err or "recovery_pending" }) end
    end
    local epoch = Guard.read_metadata_epoch(uid)
    local request = payload and payload ~= "" and nk.json_decode(payload) or {}
    if not Runtime.ResumeModule.valid_request(request.request_id)then return nk.json_encode({ok=false,err="bad_payload"})end
    local previous=Runtime.Resume.recover(uid,epoch,{})
    if not previous.ok then return nk.json_encode(previous)end
    if previous.active then
      if previous.mode=="pve"and not previous.is_global_boss and not previous.pvp_race and previous.arena_tournament_id==""then
        previous.resumed=true;previous.energy=read_pve_progress(uid).energy;previous.energy_max=pve_energy_max_for_user(uid)
        return nk.json_encode(previous)
      end
      return nk.json_encode({ok=false,err="match_in_progress",active_match=previous})
    end
    local requested_id = tostring(request.bot_id or mine_bot_id_for_floor(1))
    local diff = normalize_mine_difficulty(request.difficulty)
    local requested_floor = CFG.clamp_int(request.floor or parse_floor_from_bot_id(requested_id), 1, CFG.PVE_MAX_LEVEL)
    local bot = get_bot_profile(requested_id, diff) or get_bot_profile(mine_bot_id_for_floor(requested_floor), diff)
    if not bot or tostring(bot.id or "") == "" then return nk.json_encode({ ok = false, err = "bot_catalog_unavailable" }) end
    local floor = CFG.clamp_int(bot.floor or requested_floor, 1, CFG.PVE_MAX_LEVEL)
    local costs = PveMineCost.normalize(bot.cost_attack, PveMineCost.DEFAULT_ATTACK)
    local metadata = { bot_id = bot.id, floor = floor, difficulty = diff,
      request_id=request.request_id,
      request_key = tostring(bot.id) .. ":" .. diff .. ":" .. floor .. ":" .. epoch ..(request.request_id and ":"..request.request_id or"") }
    local valid_err
    local paid, err, entry, progress = Runtime.PveEntry.reserve(uid, costs, metadata, function(fresh)
      fresh.mine = fresh.mine or {}
      fresh.mine.unlocked = normalize_mine_unlocked(fresh.mine.unlocked, 1)
      if floor > get_unlocked_floor(fresh, diff) then valid_err = "barrier_locked"; return false, valid_err end
      if floor_respawn_left_seconds(fresh, diff, floor) > 0 then valid_err = "monster_respawn_pending"; return false, valid_err end
      fresh.mine.current_difficulty, fresh.mine.selected_floor = diff, floor
      fresh.mine.floor_states = type(fresh.mine.floor_states) == "table" and fresh.mine.floor_states or {}
      local key = make_floor_state_key(diff, floor)
      local state = fresh.mine.floor_states[key] or {}
      if tostring(state.last_affix or "") == "" then state.last_affix = random_affix_for_floor(floor) end
      fresh.mine.floor_states[key] = state
      metadata.affix = state.last_affix
      return true
    end)
    if not paid then
      local response = { ok = false, err = err or valid_err or "launch_failed", floor = floor, difficulty = diff }
      if tostring(err):sub(1, 11) == "not_enough_" then
        local fresh = read_pve_progress(uid)
        local resource, needed = tostring(err):sub(12), 0
        for _, cost in ipairs(costs) do if cost.resource == resource then needed = needed + cost.amount end end
        response = PveMineCost.json_not_enough(fresh, uid, resource, needed, PveMineCost.amount(fresh, uid, resource))
        response.ok = false
      end
      return nk.json_encode(response)
    end
    local meta = entry.metadata or metadata
    local match_id = entry.status == "bound" and entry.match_id or nil
    if not match_id or match_id == "" then
      match_id = try_match_create({ mode = "pve", owner_user_id = uid, bot_id = bot.id,
        bot_user_id = make_bot_user_id(bot.id), owner_level = progress.level or 1, owner_session_epoch = epoch,
        pve_run = { floor = floor, difficulty = diff, affix = meta.affix or "",
          stat_mul = mine_stat_multiplier(diff), reward_mul = mine_reward_multiplier(diff), launch_token = entry.token,
          request_id=meta.request_id } })
    end
    if not match_id or match_id == "" then
      local refunded, refund_err, receipt, updated = Runtime.PveEntry.refund_launch(uid, entry.token, "match_create_failed")
      -- A competing create may have bound this same paid token successfully.
      -- Return its real match rather than claiming a refund/error that did not occur.
      local reusable = refunded and receipt and receipt.status == "bound" and tostring(receipt.match_id or "") ~= ""
      if refunded and receipt and receipt.status == "consumed" and tostring(receipt.match_id or "") ~= "" then
        local probed, live = pcall(nk.match_get, receipt.match_id)
        reusable = probed and live ~= nil
      end
      if reusable then
        local fresh = read_pve_progress(uid)
        return nk.json_encode({ ok = true, match_id = receipt.match_id, bot_id = bot.id, bot_name = bot.name,
          bot_user_id = make_bot_user_id(bot.id), floor = floor, difficulty = diff,
          affix = receipt.metadata and receipt.metadata.affix or meta.affix or "",
          energy = fresh.energy, energy_max = pve_energy_max_for_user(uid) })
      end
      return nk.json_encode({ ok = false, err = "match_create_failed", refund_pending = not refunded or (receipt and receipt.status == "refund_pending") or false,
        refund_error = refund_err, energy = updated and updated.energy, energy_max = pve_energy_max_for_user(uid) })
    end
    return nk.json_encode({ ok = true, match_id = match_id, bot_id = bot.id, bot_name = bot.name,
      bot_user_id = make_bot_user_id(bot.id), floor = floor, difficulty = diff, affix = meta.affix or "",
      energy = progress.energy, energy_max = pve_energy_max_for_user(uid),request_id=meta.request_id })
  end)
  if not ok then nk.logger_error("duel_match3_pve_create: " .. tostring(result)); return nk.json_encode({ ok = false, err = "server_error" }) end
  return result
end

local RaceApi = runtime_lua_require("modules.duel_match3_race_api", "duel_match3_race_api")({
  CFG = CFG,
  ContestGoals = Runtime.ContestGoals,
  Guard = Guard,
  decode_storage_value = decode_storage_value,
  try_match_create = try_match_create,
  make_bot_user_id = make_bot_user_id,
  read_pve_progress = read_pve_progress,
  write_pve_progress = write_pve_progress,
  build_pve_progress_write = Runtime.build_pve_progress_write,
  current_level_from_xp = current_level_from_xp,
  build_resource_payload = build_resource_payload,
  race_goal_mana = race_goal_mana,
  recover_match=function(...)return Runtime.Resume.recover(...)end,
})
Runtime.RaceEntry = RaceApi.Entry
Runtime.Recovery = runtime_lua_require("modules.duel_match3_recovery", "duel_match3_recovery").new({
  journal = Runtime.Journal, race_entry = Runtime.RaceEntry, pve_entry = Runtime.PveEntry,
  handlers = { pve = function(uid, id, plan) return Runtime.PveRewards.build(uid, id, plan) end },
  resume_race_result = Runtime.Pvp.resume_race_result,
})
Runtime.RateLimit.configure_maintenance(function(ctx)
  Runtime.Recovery.maybe_recover(ctx.user_id)
  Runtime.StorageMaintenance.maybe_run()
end)
Runtime.StorageMaintenance.configure({
  recover = function(uid) return Runtime.Recovery.maybe_recover(uid, { force = true }) end,
  can_prune_receipt = function(uid, receipt)
    local rows = nk.storage_read({ { user_id = uid, collection = "duel_match3_journal", key = "pending" } })
    local value = rows and rows[1] and decode_storage_value(rows[1])
    local entries = value and value.entries or {}
    local id = tostring(receipt.run_id or "")
    return id == "" or not (entries[id] or entries[id .. ":pve"] or entries[id .. ":stats"])
  end,
})

local function duel_mine_summon(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local ok_epoch, err_epoch = Guard.assert_client_epoch_matches(user_id, payload)
    if not ok_epoch then
      return nk.json_encode({ ok = false, err = err_epoch })
    end

    local p = {}
    if payload ~= nil and payload ~= "" then
      p = nk.json_decode(payload) or {}
    end

    local requested_floor = CFG.clamp_int(p.floor, 1, CFG.PVE_MAX_LEVEL)
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
      local available_energy = CFG.clamp_int(progress.energy, 0, energy_max)
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

    local ok_epoch, err_epoch = Guard.assert_client_epoch_matches(user_id, payload)
    if not ok_epoch then
      return nk.json_encode({ ok = false, err = err_epoch })
    end

    local p = {}
    if payload ~= nil and payload ~= "" then
      p = nk.json_decode(payload) or {}
    end

    local requested_floor = CFG.clamp_int(p.floor, 1, CFG.PVE_MAX_LEVEL)
    local requested_diff = normalize_mine_difficulty(p.difficulty)
    local max_retries = 5
    local bid = mine_bot_id_for_floor(requested_floor)
    local bot_for_cost = get_bot_profile(bid, requested_diff)
    if bot_for_cost == nil then
      return nk.json_encode({
        ok = false,
        err = "bot_catalog_unavailable",
        bot_id = bid,
        floor = requested_floor,
        difficulty = requested_diff,
      })
    end
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

local function validate_action_basic(state, sender_id, action, tick)
  if state.ended then return false, "game_ended" end
  if not state.started then return false, "not_started" end
  if sender_id ~= state.active_user_id then return false, "not_your_turn" end
  -- Input is processed before the loop's timeout settlement. Reject an expired
  -- decision here, before mana/cooldowns or a five-match extra turn can change it.
  -- Paused animation/OP17 phases do not have a running decision deadline.
  if tick ~= nil and not state.turn_deadline_paused and state.turn_deadline_tick ~= nil
      and tick >= state.turn_deadline_tick then return false, "turn_expired" end
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
    if is_pvp_race(state) then
      return false, "ability_disabled"
    end
    local st = state.stats[sender_id]
    local need_mana = action_mana_cost(state, action.actionType)
    if st.mana < need_mana then return false, "not_enough_mana" end
    if action.actionType == 5 and (st.shield_cd or 0) > 0 then return false, "shield_on_cooldown" end
    if action.actionType == 6 and (st.fury_cd or 0) > 0 then return false, "fury_on_cooldown" end
    return true, nil
  end

  return false, "unknown_action"
end

Runtime.enumerate_valid_swaps = function(board)
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

local function choose_bot_action(state, bot_user_id, player_user_id)
  local function copy_stats(src)
    return {
      hp = src.hp or CFG.MAX_HP,
      mana = src.mana or 0,
      race_petard_bank = math.max(0, tonumber(src.race_petard_bank) or 0),
      cross_cd = src.cross_cd or 0,
      square_cd = src.square_cd or 0,
      petard_cd = src.petard_cd or 0,
      shield_cd = src.shield_cd or 0,
      fury_cd = src.fury_cd or 0,
      shield_t1 = src.shield_t1 or 0,
      shield_t2 = src.shield_t2 or 0,
      shield_t3 = src.shield_t3 or 0,
      fury_active = src.fury_active == true,
      fury_bomb_bonus = math.max(0, tonumber(src.fury_bomb_bonus) or 0),
      max_hp = src.max_hp or CFG.MAX_HP,
      initial_hp = src.initial_hp or src.max_hp or CFG.MAX_HP,
      base_damage = src.base_damage or 0,
      base_armor = src.base_armor or 0,
      base_crit = src.base_crit or 0,
      base_heal = src.base_heal or 0,
      consumable_id = src.consumable_id,
      consumable_damage_mult = src.consumable_damage_mult,
      consumable_armor_mult = src.consumable_armor_mult,
      consumable_healing_mult = src.consumable_healing_mult,
      consumable_crit_add = src.consumable_crit_add,
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
      -- fury_bomb_bonus выставляет resolve_action по board симуляции
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
      _sim_metrics = { extra_turn = false, red = 0, yellow = 0, green = 0, ankh = 0 },
      _sim_quality_y_min = bot_sim_quality_y_min(state),
    }
    if state.global_boss then
      sim_state.global_boss=state.global_boss
      sim_state.owner_user_id=state.owner_user_id;sim_state.bot_user_id=state.bot_user_id
      sim_state.mode="pve"
    end

    if action.actionType == 2 or action.actionType == 3 or action.actionType == 4 or action.actionType == 5 or action.actionType == 6 then
      spend_ability_for_sim(state, sim_bot, action.actionType)
    end

    local before_hp = sim_player.hp
    local ok, _, extra_turn, _, _ = resolve_action(sim_state, action, bot_user_id, player_user_id)
    if not ok then return nil end

    local m = sim_state._sim_metrics or { extra_turn = false, red = 0, yellow = 0, green = 0, ankh = 0 }
    local red = m.red or 0
    local yellow = m.yellow or 0
    local green = m.green or 0
    local ankh = m.ankh or 0
    local score = {
      extra_turn = (extra_turn == true) or (m.extra_turn == true),
      damage = math.max(0, before_hp - sim_player.hp),
      red = red,
      yellow = yellow,
      green = green,
      ankh = ankh,
      -- Грязный прирост маны с камней (без учёта стоимости способности).
      mana_value = red * (CFG.GEM_MANA[1] or 5) + yellow * (CFG.GEM_MANA[2] or 3) + green * (CFG.GEM_MANA[3] or 1),
      -- Фактическая мана после хода (траты способности уже вычтены) — ключ для удержания щита.
      mana_after = math.max(0, tonumber(sim_bot.mana) or 0),
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

  --- Выживание: сначала порог маны на щит, затем мана; хил может «купить» slack маны у свапа без анкха.
  local function is_better_survival_score(a, b, shield_need)
    if b == nil then return true end
    local need = tonumber(shield_need) or CFG.SHIELD_ABILITY_COST or 40
    local slack = tonumber(CFG.BOT_SURVIVAL_HEAL_MANA_SLACK) or 5

    local am = tonumber(a.mana_after) or 0
    local bm = tonumber(b.mana_after) or 0
    local a_ok = am >= need
    local b_ok = bm >= need
    if a_ok ~= b_ok then return a_ok end

    local ah = a.ankh or 0
    local bh = b.ankh or 0
    -- Способность с лечением vs свап без: можно уступить до slack маны.
    if ah > 0 and bh <= 0 then
      if am >= bm - slack then return true end
      return false
    end
    if bh > 0 and ah <= 0 then
      if bm >= am - slack then return false end
      return true
    end

    if am ~= bm then return am > bm end
    if ah ~= bh then return ah > bh end
    if a.extra_turn ~= b.extra_turn then return a.extra_turn end
    if a.damage ~= b.damage then return a.damage > b.damage end
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

  local function match_has_skull_line(matches, min_count)
    local need = tonumber(min_count) or 3
    if not matches then return false end
    for _, m in ipairs(matches) do
      if m.type == 4 and (tonumber(m.count) or 0) >= need then return true end
    end
    return false
  end

  --- Оценка урона одного удара (без крита) с учётом брони цели.
  local function estimate_hit_damage(raw_base, attacker_stats, fury_bonus, target_stats)
    local raw = math.max(0, tonumber(raw_base) or 0)
    raw = raw + math.max(0, tonumber(attacker_stats and attacker_stats.base_damage) or 0)
    raw = raw + math.max(0, tonumber(fury_bonus) or 0)
    return math.max(0, Runtime.ConsumableEffects.damage(raw,attacker_stats) - get_armor(target_stats))
  end

  local stats = state.stats[bot_user_id]
  if stats == nil then return nil end

  local cross_cost = action_mana_cost(state, 2)
  local square_cost = action_mana_cost(state, 3)
  local petard_cost = action_mana_cost(state, 4)
  local shield_cost = action_mana_cost(state, 5)
  local fury_cost = action_mana_cost(state, 6)
  local can_cross = stats.mana >= cross_cost and stats.cross_cd <= 0
  local can_square = stats.mana >= square_cost and stats.square_cd <= 0
  local can_petard = stats.mana >= petard_cost and stats.petard_cd <= 0
  local can_shield = stats.mana >= shield_cost and (stats.shield_cd or 0) <= 0
  local can_fury = stats.mana >= fury_cost and (stats.fury_cd or 0) <= 0
  -- «Спуск»: щит/ярость выключены — бот не выбирает их (иначе validate отклонит ход).
  if is_pvp_race(state) then
    can_shield = false
    can_fury = false
  end

  local player_stats = state.stats[player_user_id] or {}
  local player_hp = tonumber(player_stats.hp) or CFG.MAX_HP
  local bot_hp = tonumber(stats.hp) or CFG.MAX_HP
  local mana = tonumber(stats.mana) or 0
  local frozen = has_affix(state, "frozen")

  local enter_hp = tonumber(CFG.BOT_SURVIVAL_HP_ENTER) or 30
  local exit_hp = tonumber(CFG.BOT_SURVIVAL_HP_EXIT) or 75
  if bot_hp >= exit_hp then
    state.bot_survival_mode = false
  elseif bot_hp < enter_hp and bot_hp < player_hp then
    state.bot_survival_mode = true
  end
  local survival = state.bot_survival_mode == true

  -- Один проход по свапам: урон / доп.ход / линия бомб / выживание (скоры кэшируем).
  local swaps = Runtime.enumerate_valid_swaps(state.board)
  local swap_scores = {}
  local best_extra_swap = nil
  local best_extra_score = nil
  local best_damage_swap = nil
  local best_damage_score = nil
  local best_bomb_swap = nil
  local best_bomb_score = nil
  local best_survival_swap = nil
  local best_survival_score = nil
  local max_swap_damage = 0
  local max_bomb_swap_damage = 0

  for i, action in ipairs(swaps) do
    local score = simulate_and_score_action(state, bot_user_id, player_user_id, action)
    swap_scores[i] = score
    if score ~= nil then
      local dmg = score.damage or 0
      if dmg > max_swap_damage then max_swap_damage = dmg end
      if best_damage_score == nil or dmg > (best_damage_score.damage or 0) then
        best_damage_score = score
        best_damage_swap = action
      elseif dmg == (best_damage_score.damage or 0) and is_better_score(score, best_damage_score) then
        best_damage_score = score
        best_damage_swap = action
      end
      if score.extra_turn and is_better_score(score, best_extra_score) then
        best_extra_score = score
        best_extra_swap = action
      end
      if is_better_survival_score(score, best_survival_score, shield_cost) then
        best_survival_score = score
        best_survival_swap = action
      end
      -- Линия бомб: доп. clone+try_swap только если есть урон (черепа бьют).
      if dmg > 0 then
        local init_m = swap_initial_matches_for_action(state, action)
        if match_has_skull_line(init_m, 3) then
          if dmg > max_bomb_swap_damage then
            max_bomb_swap_damage = dmg
            best_bomb_score = score
            best_bomb_swap = action
          elseif dmg == max_bomb_swap_damage and is_better_score(score, best_bomb_score) then
            best_bomb_score = score
            best_bomb_swap = action
          end
        end
      end
    end
  end

  -- ─── «Спуск»: мана/банк петарды; щит/ярость уже выключены выше ───
  if is_pvp_race(state) then
    local function race_better(a, b)
      if b == nil then return true end
      if a == nil then return false end
      if a.extra_turn ~= b.extra_turn then return a.extra_turn == true end
      local ama = tonumber(a.mana_after) or 0
      local bma = tonumber(b.mana_after) or 0
      if ama ~= bma then return ama > bma end
      local av = tonumber(a.mana_value) or 0
      local bv = tonumber(b.mana_value) or 0
      if av ~= bv then return av > bv end
      return (tonumber(a.ankh) or 0) > (tonumber(b.ankh) or 0)
    end

    -- Свободный 5+ свап всегда предпочтительнее платной способности.
    if best_extra_swap ~= nil then
      return best_extra_swap
    end

    local best_act = nil
    local best_sc = nil
    for i, action in ipairs(swaps) do
      local sc = swap_scores[i]
      if sc ~= nil and race_better(sc, best_sc) then
        best_sc = sc
        best_act = action
      end
    end

    local function consider_race_ability(action_type, five_plus_only)
      for y = 0, CFG.SIZE - 1 do
        for x = 0, CFG.SIZE - 1 do
          local ac = {
            actionType = action_type,
            fromX = -1, fromY = -1, toX = -1, toY = -1,
            cx = x, cy = y,
          }
          local sc = simulate_and_score_action(state, bot_user_id, player_user_id, ac)
          if sc ~= nil then
            if five_plus_only then
              if sc.extra_turn == true then
                best_sc = sc
                best_act = ac
                return true
              end
            else
              -- Крест/квадрат: только если выгоднее свапа по мане после стоимости, либо даёт 5+.
              if sc.extra_turn == true or race_better(sc, best_sc) then
                best_sc = sc
                best_act = ac
              end
            end
          end
        end
      end
      return false
    end

    -- 5+ от креста/квадрата важнее петарды (даже при полном банке).
    if can_cross and consider_race_ability(2, true) then return best_act end
    if can_square and consider_race_ability(3, true) then return best_act end

    local bank = math.max(0, tonumber(stats.race_petard_bank) or 0)
    local goal = race_goal_mana(state)
    local opp_mana = tonumber(player_stats.mana) or 0
    local opp_to_goal = goal - opp_mana
    local bank_min = math.max(1, math.floor(tonumber(CFG.RACE_BOT_PETARD_BANK_MIN) or 30))
    local bank_press = math.max(0, math.floor(tonumber(CFG.RACE_BOT_PETARD_BANK_PRESSURE) or 15))
    local opp_near = math.max(1, math.floor(tonumber(CFG.RACE_BOT_PETARD_OPP_TO_GOAL) or 50))
    if can_petard and (bank >= bank_min or (bank > bank_press and opp_to_goal < opp_near)) then
      return { actionType = 4, fromX = -1, fromY = -1, toX = -1, toY = -1, cx = -1, cy = -1 }
    end

    if can_cross then consider_race_ability(2, false) end
    if can_square then consider_race_ability(3, false) end
    return best_act
  end

  local bombs_on_board = count_skulls(state.board)
  local fury_bonus_now = (stats.fury_active == true) and math.max(0, tonumber(stats.fury_bomb_bonus) or 0) or 0
  local petard_dmg_now = can_petard
    and estimate_hit_damage(CFG.PETARD_DAMAGE, stats, fury_bonus_now, player_stats)
    or 0
  -- 2+ щита у соперника: петарда почти бесполезна (15 − 8/12 брони), кроме добивания.
  local opp_shield_stacks = get_shield_stacks(player_stats)
  local function petard_is_finisher()
    if not can_petard then return false end
    if petard_dmg_now >= player_hp then return true end
    if max_swap_damage > 0 and (petard_dmg_now + max_swap_damage) >= player_hp then return true end
    if best_bomb_swap ~= nil and (petard_dmg_now + max_bomb_swap_damage) >= player_hp then return true end
    return false
  end
  local function can_spend_petard()
    return can_petard and (opp_shield_stacks < 2 or petard_is_finisher())
  end

  local function clear_kill_plan()
    state.bot_kill_plan = nil
  end

  --- Добивание петардой / петарда+бомбы / свап (дешёвые оценки, без новых симов).
  local function try_finish_actions(use_bomb_line)
    if can_petard and petard_dmg_now >= player_hp then
      clear_kill_plan()
      return { actionType = 4, fromX = -1, fromY = -1, toX = -1, toY = -1, cx = -1, cy = -1 }
    end
    local swap_dmg = use_bomb_line and max_bomb_swap_damage or max_swap_damage
    local swap_act = use_bomb_line and best_bomb_swap or best_damage_swap
    if can_petard and swap_act ~= nil and (petard_dmg_now + swap_dmg) >= player_hp then
      -- Петарда сохраняет ход → следующим тиком добьём свапом.
      state.bot_kill_plan = use_bomb_line and "bomb_swap" or "damage_swap"
      return { actionType = 4, fromX = -1, fromY = -1, toX = -1, toY = -1, cx = -1, cy = -1 }
    end
    if swap_act ~= nil and swap_dmg >= player_hp then
      clear_kill_plan()
      return swap_act
    end
    return nil
  end

  -- Продолжение плана добивания после ярости/петарды.
  if state.bot_kill_plan == "petard" then
    if can_petard then
      state.bot_kill_plan = (best_bomb_swap ~= nil) and "bomb_swap" or "damage_swap"
      return { actionType = 4, fromX = -1, fromY = -1, toX = -1, toY = -1, cx = -1, cy = -1 }
    end
    clear_kill_plan()
  end
  if state.bot_kill_plan == "bomb_swap" then
    clear_kill_plan()
    if best_bomb_swap ~= nil then return best_bomb_swap end
    if best_damage_swap ~= nil then return best_damage_swap end
  end
  if state.bot_kill_plan == "damage_swap" then
    clear_kill_plan()
    if best_damage_swap ~= nil then return best_damage_swap end
  end

  -- Всегда: если можно добить сейчас — бьём (даже в выживании).
  do
    local finish = try_finish_actions(false)
    if finish ~= nil then return finish end
    -- При нехватке маны на «просто петарду+любой урон» — только линия бомб.
    if can_petard and best_bomb_swap ~= nil and (petard_dmg_now + max_bomb_swap_damage) >= player_hp then
      state.bot_kill_plan = "bomb_swap"
      return { actionType = 4, fromX = -1, fromY = -1, toX = -1, toY = -1, cx = -1, cy = -1 }
    end
  end

  -- Ярость → петарда → линия бомб, если маны хватает и оценка покрывает HP игрока.
  local fury_finish_min = tonumber(CFG.BOT_FURY_FINISH_MIN_MANA) or 60
  if not stats.fury_active and can_fury and can_petard
      and mana >= fury_finish_min
      and mana >= (fury_cost + petard_cost)
      and best_bomb_swap ~= nil then
    local bonus = bombs_on_board
    local petard_fury = estimate_hit_damage(CFG.PETARD_DAMAGE, stats, bonus, player_stats)
    -- Симулированный урон свапа уже с бронёй; бонус ярости добавляется к raw ≈ +bonus к HP-урону.
    local bomb_fury = max_bomb_swap_damage + bonus
    if (petard_fury + bomb_fury) >= player_hp then
      state.bot_kill_plan = "petard"
      return { actionType = 6, fromX = -1, fromY = -1, toX = -1, toY = -1, cx = -1, cy = -1 }
    end
  end

  -- Режим выживания / критическое HP без килла: щит, затем удержание порога маны на щит.
  local critical_hp = bot_hp < enter_hp
  if survival or critical_hp then
    if can_shield then
      return { actionType = 5, fromX = -1, fromY = -1, toX = -1, toY = -1, cx = -1, cy = -1 }
    end

    -- Выбор хода: mana_after >= стоимость щита важнее хила/«грязной» маны с способности.
    local best_sc = best_survival_score
    local best_act = best_survival_swap

    local function consider_ability(action_type)
      for y = 0, CFG.SIZE - 1 do
        for x = 0, CFG.SIZE - 1 do
          local ac = {
            actionType = action_type,
            fromX = -1, fromY = -1, toX = -1, toY = -1,
            cx = x, cy = y,
          }
          local sc = simulate_and_score_action(state, bot_user_id, player_user_id, ac)
          if sc ~= nil and is_better_survival_score(sc, best_sc, shield_cost) then
            best_sc = sc
            best_act = ac
          end
        end
      end
    end
    if can_cross then consider_ability(2) end
    if can_square then consider_ability(3) end
    if best_act ~= nil then return best_act end
    if best_damage_swap ~= nil then return best_damage_swap end
    return nil
  end

  -- в) Цепочка после ярости при старте с маны >= 80 (петарда + способность; при frozen — только петарда).
  local open_m = state.bot_fury_open_mana
  if stats.fury_active and open_m ~= nil and open_m >= 80 then
    if can_spend_petard() then
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
  -- Не открываем, если петарда упрётся в 2+ щита и не добивает.
  if not stats.fury_active and can_fury and can_spend_petard() then
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
  --  • добивает (одна или с свапом),
  --  • либо маны > 50 и у соперника меньше 2 щитов (иначе 15 урона съедается бронёй).
  if can_petard then
    if petard_is_finisher() then
      if petard_dmg_now < player_hp then
        state.bot_kill_plan = (best_bomb_swap ~= nil) and "bomb_swap" or "damage_swap"
      else
        clear_kill_plan()
      end
      return { actionType = 4, fromX = -1, fromY = -1, toX = -1, toY = -1, cx = -1, cy = -1 }
    elseif mana > 50 and opp_shield_stacks < 2 then
      clear_kill_plan()
      return { actionType = 4, fromX = -1, fromY = -1, toX = -1, toY = -1, cx = -1, cy = -1 }
    end
  end

  -- Если есть явный 5+ свап (extra turn) — берём его прежде любых способностей.
  if best_extra_swap ~= nil then
    return best_extra_swap
  end

  local best_action = best_damage_swap
  local best_score = best_damage_score

  local function consider_candidate(action)
    local score = simulate_and_score_action(state, bot_user_id, player_user_id, action)
    if score ~= nil and is_better_score(score, best_score) then
      best_score = score
      best_action = action
    end
  end

  if can_cross then
    for y = 0, CFG.SIZE - 1 do
      for x = 0, CFG.SIZE - 1 do
        consider_candidate({
          actionType = 2,
          fromX = -1, fromY = -1, toX = -1, toY = -1,
          cx = x, cy = y,
        })
      end
    end
  end
  if can_square then
    for y = 0, CFG.SIZE - 1 do
      for x = 0, CFG.SIZE - 1 do
        consider_candidate({
          actionType = 3,
          fromX = -1, fromY = -1, toX = -1, toY = -1,
          cx = x, cy = y,
        })
      end
    end
  end
  if can_petard and (opp_shield_stacks < 2 or petard_is_finisher()) then
    consider_candidate({ actionType = 4, fromX = -1, fromY = -1, toX = -1, toY = -1, cx = -1, cy = -1 })
  end

  -- Свапы уже симулированы: выбираем лучший по кэшу без повторного resolve.
  for i, action in ipairs(swaps) do
    local score = swap_scores[i]
    if score ~= nil and is_better_score(score, best_score) then
      best_score = score
      best_action = action
    end
  end

  return best_action
end

Runtime.JournalAdmission = runtime_lua_require("modules.duel_match3_journal_admission", "duel_match3_journal_admission")
Runtime.admit_journal_tickets = function(state)
  return Runtime.JournalAdmission.admit(state,{journal=Runtime.Journal,is_human=Ach.is_human,
    consumable_eligible=Runtime.ConsumableEffects.eligible,pve_entry=Runtime.PveEntry})
end
Runtime.cancel_unused_journal_tickets = function(state)
  return Runtime.JournalAdmission.cancel_unused(state,Runtime.Journal)
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
    match_id = context and context.match_id or nil,
    join_deadline_tick = math.floor((CFG.MATCH_ADMISSION_SECONDS or 20) * CFG.TICK_RATE + 0.5),
    stats_result_id = nk.uuid_v4(),
    mode = params and tostring(params.mode or "pvp") or "pvp",
    pvp_pro = truthy_match_param(params and params.pvp_pro),
    pvp_race = truthy_match_param(params and params.pvp_race),
    owner_user_id = params and params.owner_user_id or nil,
    owner_session_epoch = tonumber(params and params.owner_session_epoch or 0) or 0,
    bot_id = params and params.bot_id or mine_bot_id_for_floor(1),
    bot_name = params and(params.bot_name or params.bot_display_name)or nil,
    bot_user_id = params and params.bot_user_id or make_bot_user_id(params and params.bot_id or mine_bot_id_for_floor(1)),
    owner_level = tonumber(params and params.owner_level or 1) or 1,
    presences = {},
    players_sorted = {},
    stats = {},
    board = nil,
    started = false,
    _prestart_pending = not(params and params.arena_mirror),
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
    bot_survival_mode = false,
    bot_kill_plan = nil,
    _bot_pre_mana = nil,
    pve_run = params and params.pve_run or nil,
    global_boss = params and params.global_boss or nil,
    arena_mirror = params and params.arena_mirror or nil,
    --- Арена человек×бот: те же базовые статы, что у классического PvP (150 HP и т.д.), без шахты.
    arena_pvp_style = truthy_match_param(params and params.arena_pvp_style),
    --- Бот с ником из ONLINE_POOL_NAMES: замедленная «думалка» 7–22с.
    arena_human_like_bot = truthy_match_param(params and params.arena_human_like_bot),
    reconnect_deadlines = {},
    player_levels = {},
  }

  if state.arena_mirror ~= nil then
    -- Tournament fights use the fixed classic statistics, including a no-show.
    -- Publish those at creation so the bracket never has a phantom zero-HP pair.
    if state.mode=="pve" then state.players_sorted={state.owner_user_id,state.bot_user_id}
    else for uid in pairs(state.invited) do state.players_sorted[#state.players_sorted+1]=uid end;table.sort(state.players_sorted) end
    for _,uid in ipairs(state.players_sorted) do state.stats[uid]=new_stats() end
    Runtime.arena_on_match_created(state)
    Runtime.arena_mirror_commit(state)
  end
  if state.global_boss then
    local bound,err=Runtime.GlobalBoss.bind_state(state,state.global_boss.spawn_nonce)
    assert(bound,err)
    state.global_boss.spawn_nonce=nil
  end
  if state.mode == "pve" and not state.global_boss and not state.pvp_race and state.arena_mirror == nil
      and not (state.pve_run and state.pve_run.arena_suppress_all) then
    local bound, err = Runtime.PveEntry.bind(state.owner_user_id, state.pve_run and state.pve_run.launch_token,
      state.match_id, state.stats_result_id)
    assert(bound, err)
  end

  local tracked,track_error=Runtime.Resume.track(state)
  assert(tracked,track_error)
  local admitted,admission_err=Runtime.admit_journal_tickets(state)
  assert(admitted,admission_err)
  if state.pvp_race then
    local users={}
    if state.mode=="pve"then users={state.owner_user_id,state.bot_user_id}
    else for uid in pairs(state.invited)do users[#users+1]=uid end;table.sort(users)end
    local paid,why=RaceApi.Entry.claim(users,state.stats_result_id,state.match_id,state._journal_tickets)
    assert(paid,why)
  end
  return state, CFG.TICK_RATE, state.global_boss and state.global_boss.practice==true
    and Runtime.GlobalBoss.practice_label(state.owner_user_id)or "mode=duel_match3"
end

local function match_join_attempt(context, dispatcher, tick, state, presence, metadata)
  if state.ended then return state, false, "ended" end
  if metadata and metadata.session_epoch~=nil and Runtime.Resume then
    local _,resumed=Runtime.Resume.signal(dispatcher,state,tick,{user_id=presence.user_id,session_epoch=metadata.session_epoch})
    if not resumed.ok then return state,false,resumed.err end
  end
  local allowed, reason = Runtime.Presence.can_rejoin(state, presence, tick)
  if not allowed then return state, false, reason end

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
  if Runtime.Resume then
    local tracked=Runtime.Resume.track(state,presences)
    if not tracked then dispatcher.match_kick(presences);return state end
  end
  Runtime.Presence.join(state, presences, dispatcher)
  if state.global_boss then return Runtime.BossBattle.join(dispatcher,state,tick) end
  -- Any admitted participant gets the full state, including overlap replacement.
  if state.started and not state.ended then
    for _, p in ipairs(presences) do
      local cheat = {}
      if state.cheat_rows_allowed ~= nil and state.cheat_rows_allowed[p.user_id] == true then
        cheat = state.cheat_rows or {}
      end
      local msg = make_sync_msg(state, nil, false, nil, cheat, tick, "")
      dispatcher.broadcast_message(CFG.OP_BOARD_SYNC, nk.json_encode(msg), { p }, nil)
    end
    return state
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

        if state.pvp_race == true then
          local paid, err = RaceApi.Entry.claim({player_id}, state.stats_result_id, state.match_id,state._journal_tickets)
          if not paid then
            nk.logger_warn("Race start rejected: " .. tostring(err))
            state.ended = true
            dispatcher.match_kick(presences)
            return nil
          end
        end
        state.started = true
        state.players_sorted = { player_id, state.bot_user_id }
        state.stats[player_id] = new_stats()
        state.stats[state.bot_user_id] = new_stats()

        state.board = init_board()
        ensure_active_board_has_valid_swaps(state)

        state.cheat_rows = init_cheat_rows()
        state.spawn_queues = {}
        for x = 0, CFG.SIZE - 1 do ensure_spawn_queue(state, x) end
        update_cheat_rows_from_board(state)
        state.cheat_rows_allowed = {}
        local wl = build_cheat_whitelist_set({ player_id })
        local p_allowed = is_user_allowed_for_cheat_rows(player_id, wl)
        state.cheat_rows_allowed[player_id] = p_allowed
        state.cheat_rows_allowed[state.bot_user_id] = is_user_allowed_for_cheat_rows(state.bot_user_id, wl)
        nk.logger_info("duel_match3 cheat_rows_allowed (arena_pvp_style): user_id=" .. tostring(player_id) ..
          " allowed=" .. tostring(p_allowed))

        Runtime.Rounds.start(state, pick_first_actor(player_id, state.bot_user_id))

        if state.pvp_race == true then
          state.race_first_user_id = state.active_user_id
          state.race_last_turn_user_id = nil
          state.race_threshold_user_id = nil
          state.race_actions = {}
          state.race_actions[player_id] = 0
          state.race_actions[state.bot_user_id] = 0
          -- Не передаём временную таблицу в max_mana_for_state (GopherLua panic на сравнении).
          local goal = race_goal_mana(nil)
          state.race_goal_mana = goal
          state.race_max_mana = race_max_from_goal(goal)
          state.race_reward_lines = Runtime.ContestGoals.race_rewards()
          state.race_entry_costs = Runtime.ContestGoals.race_entry_costs()
          if state.stats[player_id] ~= nil then
            state.stats[player_id].race_petard_bank = 0
          end
          if state.stats[state.bot_user_id] ~= nil then
            state.stats[state.bot_user_id].race_petard_bank = 0
          end
          nk.logger_info("duel_match3: PvP Race vs bot — first=" .. tostring(state.race_first_user_id)
            .. " goal=" .. tostring(state.race_goal_mana)
            .. " max=" .. tostring(state.race_max_mana))
        end

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
        broadcast_sync(dispatcher, state, nil, false, nil, tick, nil)
        return state
      end

      local pve_diff = normalize_mine_difficulty((state.pve_run or {}).difficulty)
      local bot_profile = get_bot_profile(state.bot_id, pve_diff)
      if bot_profile == nil or bot_profile.id == nil or bot_profile.id == "" then
        nk.logger_error(string.format(
          "pve match join: bot missing bot_id=%s difficulty=%s",
          tostring(state.bot_id),
          tostring(pve_diff)
        ))
        return state
      end
      state.bot_id = bot_profile.id
      if state.bot_user_id == nil or state.bot_user_id == "" then
        state.bot_user_id = make_bot_user_id(state.bot_id)
      end

      state.started = true
      state.players_sorted = { player_id, state.bot_user_id }
      state.stats[player_id] = new_stats()
      state.stats[state.bot_user_id] = new_stats()
      CharStats.apply_player_combat_stats_from_sheet(state.stats[player_id], player_id, {
        aura = ServerAura.get_active(),
      })

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
      ensure_active_board_has_valid_swaps(state)

      state.cheat_rows = init_cheat_rows()
      state.spawn_queues = {}
      for x = 0, CFG.SIZE - 1 do ensure_spawn_queue(state, x) end
      update_cheat_rows_from_board(state)
      state.cheat_rows_allowed = {}
      local wl = build_cheat_whitelist_set({ player_id })
      local p_allowed = is_user_allowed_for_cheat_rows(player_id, wl)
      state.cheat_rows_allowed[player_id] = p_allowed
      state.cheat_rows_allowed[state.bot_user_id] = is_user_allowed_for_cheat_rows(state.bot_user_id, wl)
      nk.logger_info("duel_match3 cheat_rows_allowed (pve): user_id=" .. tostring(player_id) ..
        " allowed=" .. tostring(p_allowed))

      Runtime.Rounds.start(state, pick_first_actor(player_id, state.bot_user_id))

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
      if not state.pvp_race and state.arena_mirror == nil and not pve_run.arena_suppress_all then
        local consumed, err = Runtime.Consumables.preview_match(state, {player_id})
        if not consumed then
          nk.logger_warn("PvE start deferred: " .. tostring(err))
          state.ended = true; dispatcher.match_kick(presences); return nil
        end
        Runtime.Consumables.apply_match(state)
      end
      broadcast_sync(dispatcher, state, nil, false, nil, tick, nil)
    end
    return state
  end

  if not state.started and count_present_players(state) == 2 then
    state.players_sorted = sorted_two_players(state.presences)
    if state.pvp_race == true then
      local paid, err = RaceApi.Entry.claim(state.players_sorted, state.stats_result_id, state.match_id,state._journal_tickets)
      if not paid then
        nk.logger_warn("Race start rejected: " .. tostring(err))
        state.ended = true
        dispatcher.match_kick(presences)
        return nil
      end
    end
    state.started = true
    state.stats[state.players_sorted[1]] = new_stats()
    state.stats[state.players_sorted[2]] = new_stats()
    if state.pvp_pro == true and state.mode ~= "pve" then
      apply_pvp_pro_stats_from_sheet(state.stats[state.players_sorted[1]], state.players_sorted[1])
      apply_pvp_pro_stats_from_sheet(state.stats[state.players_sorted[2]], state.players_sorted[2])
      nk.logger_info("duel_match3: PvP Pro — статы уровень+экип для обоих игроков")
    end
    state.board = init_board()
    ensure_active_board_has_valid_swaps(state)

    state.cheat_rows = init_cheat_rows()
    state.spawn_queues = {}
    for x = 0, CFG.SIZE - 1 do ensure_spawn_queue(state, x) end
    update_cheat_rows_from_board(state)
    state.cheat_rows_allowed = {}
    local wl = build_cheat_whitelist_set(state.players_sorted)
    for _, uid in ipairs(state.players_sorted) do
      local allowed = is_user_allowed_for_cheat_rows(uid, wl)
      state.cheat_rows_allowed[uid] = allowed
      nk.logger_info("duel_match3 cheat_rows_allowed (pvp): user_id=" .. tostring(uid) ..
        " allowed=" .. tostring(allowed))
    end

    Runtime.Rounds.start(state, pick_first_actor(state.players_sorted[1], state.players_sorted[2]))
    if state.pvp_race == true then
      state.race_first_user_id = state.active_user_id
      state.race_last_turn_user_id = nil
      state.race_threshold_user_id = nil
      state.race_actions = {}
      -- Цель/потолок/награды фиксируем на старте матча (Storage можно менять между матчами).
      local goal = race_goal_mana(nil)
      state.race_goal_mana = goal
      state.race_max_mana = race_max_from_goal(goal)
      state.race_reward_lines = Runtime.ContestGoals.race_rewards()
      state.race_entry_costs = Runtime.ContestGoals.race_entry_costs()
      for _, uid in ipairs(state.players_sorted) do
        state.race_actions[uid] = 0
        if state.stats[uid] ~= nil then
          state.stats[uid].race_petard_bank = 0
        end
      end
      nk.logger_info("duel_match3: PvP Race (Спуск) — first=" .. tostring(state.race_first_user_id)
        .. " goal=" .. tostring(state.race_goal_mana)
        .. " max=" .. tostring(state.race_max_mana))
    end

    tick_cooldowns(state.stats[state.active_user_id])
    state.turn_deadline_paused = true
    state.turn_pause_started_tick = tick
    state.turn_deadline_tick = tick
    local bound,bind_error = Runtime.Consumables.preview_match(state,state.players_sorted)
    if not bound then
      nk.logger_warn("Battle start deferred: "..tostring(bind_error));state.ended=true;dispatcher.match_kick(presences);return nil
    end
    Runtime.Consumables.apply_match(state)
    broadcast_sync(dispatcher, state, nil, false, nil, tick, nil)
  end

  return state
end

--- Краткий GAME_OVER после сдачи/разрыва: дополняем метой арены и призом финала (как при обычном OP_GAME_OVER),
--- чтобы клиент показал золото/руду и заголовок «Победа в турнире!» без дублирующего OP_PLAYER_LEFT/UI.
local function augment_arena_game_over_payload(state, payload)
  if payload == nil or state == nil then return end
  if state.arena_mirror ~= nil then
    payload.arenaTournamentId = state.arena_mirror.tournament_id or ""
    payload.arenaRound = state.arena_mirror.round or ""
    payload.arenaBetTier = state.arena_mirror.bet_tier or ""
    payload.arenaKind = string.lower(tostring(state.arena_mirror.kind or "smith"))
  end
  local ar = string.lower(tostring(payload.arenaRound or ""))
  if ar ~= "final" then return end
  if state.arena_mirror ~= nil and type(Runtime.arena_final_prize_gold_ore) == "function" then
    local bt = string.lower(tostring(state.arena_mirror.bet_tier or payload.arenaBetTier or "green"))
    local ak = string.lower(tostring(state.arena_mirror.kind or "smith"))
    local rg, ro = Runtime.arena_final_prize_gold_ore(ak, bt)
    local win_uid = tostring(payload.winnerUserId or payload.winner or "")
    if type(Runtime.arena_final_prize_for_winner) == "function" and win_uid ~= "" then
      rg, ro = Runtime.arena_final_prize_for_winner(state.arena_mirror.tournament_id, win_uid, ak, bt)
    end
    payload.rewardXp = tonumber(payload.rewardXp or 0) or 0
    payload.rewardMatter = tonumber(payload.rewardMatter or 0) or 0
    payload.rewardGold = tonumber(rg) or 0
    payload.rewardOre = tonumber(ro) or 0
  end
end

--- Победа из‑за сдачи / disconnect / reconnect timeout — с наградами (в т.ч. Спуск).
local function finalize_pvp_forfeit_win(dispatcher, state, winner, reason_tag)
  if winner == nil or winner == "" then return end
  local tag = tostring(reason_tag or "forfeit")
  Runtime.finish_arena_result(state, winner)
  if is_pvp_race(state) ~= true then
    local ok_achi, err_achi = pcall(function()
      Ach.flush_match_finish(state, winner, nil, nil, 0)
    end)
    if not ok_achi then
      nk.logger_error("achievement_flush_match_finish failed (" .. tag .. "): " .. tostring(err_achi))
    end
  end
  local game_over_payload = {
    winnerUserId = winner,
    isDraw = false,
    endReason = tag,
  }
  Runtime.apply_result_rewards(state, winner, false, game_over_payload)
  local augment_ok, augment_err = pcall(augment_arena_game_over_payload, state, game_over_payload)
  if not augment_ok then nk.logger_error("arena reward display deferred: " .. tostring(augment_err)) end
  Runtime.StatsApi.record_match_finish(state, winner, false)
  Runtime.emit_game_over(dispatcher, state, game_over_payload)
end

-- Socket OP12 and the acknowledged HTTP signal use the very same result path.
-- Never treat an unstarted public duel as a played victory or grant a free prize.
Runtime.forfeit_match = function(dispatcher,state,uid,reason)
  if Runtime.Prestart and state._prestart_pending==true and state.arena_mirror==nil then
    return Runtime.Prestart.cancel(dispatcher,state,uid)
  end
  if state.global_boss then return Runtime.BossBattle.finish(dispatcher,state,state._boss_tick or 0,reason) end
  if state.ended then return end
  state.ended=true
  state.end_reason=reason
  state.bot_turn_pending=false;state.bot_turn_ready_tick=0
  if not state.started and state.arena_mirror==nil then
    Runtime.emit_game_over(dispatcher,state,{winnerUserId="",isDraw=true,matchCancelled=true,
      endReason=reason=="player_surrender" and "admission_cancelled" or "opponent_not_ready",retryAllowed=true})
    return
  end
  if not state.started then
    state.players_sorted={}
    if state.mode=="pve" then state.players_sorted={state.owner_user_id,state.bot_user_id}
    else for id in pairs(state.invited or {}) do state.players_sorted[#state.players_sorted+1]=id end;table.sort(state.players_sorted) end
    for _,id in ipairs(state.players_sorted) do state.stats[id]=state.stats[id] or new_stats() end
    state.arena_no_show_users={}
    for _,id in ipairs(state.players_sorted) do
      if id:sub(1,7)~="zz-bot-" and not state.presences[id] then state.arena_no_show_users[id]=true end
    end
  end
  local winner=other_player_id(state,uid)
  if not winner then return end
  if state.mode=="pve" and state.arena_mirror==nil and not state.pvp_race then
    state.last_reward=Runtime.award_pve_defeat(state.owner_user_id,state.owner_session_epoch)
    Runtime.StatsApi.record_match_finish(state,winner,false)
    Runtime.emit_game_over(dispatcher,state,{winnerUserId=winner,isDraw=false,endReason=reason,
      rewardXp=state.last_reward.reward_xp or 0,newLevel=state.last_reward.level or 1})
  else finalize_pvp_forfeit_win(dispatcher,state,winner,reason) end
end

local function match_leave(context, dispatcher, tick, state, presences)
  local left = Runtime.Presence.leave(state, presences, tick,
    math.floor((state.arena_mirror~=nil and (CFG.ARENA_RECONNECT_GRACE_SECONDS or 20) or CFG.RECONNECT_GRACE_SECONDS) * CFG.TICK_RATE + 0.5))
  if state.global_boss then state._boss_disconnect_tick=tick;return state end
  if state.started and not state.ended then
    for _, uid in ipairs(left) do
      if state.mode == "pve" and uid == state.owner_user_id then
        on_pve_owner_socket_gone(state, tick)
      elseif state.mode ~= "pve" then
        local msg = { disconnectedUserId = uid, reconnectGraceSeconds = state.arena_mirror~=nil and (CFG.ARENA_RECONNECT_GRACE_SECONDS or 20) or CFG.RECONNECT_GRACE_SECONDS }
        dispatcher.broadcast_message(CFG.OP_PEER_DISCONNECT, nk.json_encode(msg), nil, nil)
      end
    end
  end
  -- Keep the state even when both sockets are gone; each retains its grace.
  return state
end

local function match_loop(context, dispatcher, tick, state, messages)
  if state._prestart_cancel then
    if Runtime.retry_pending_results(dispatcher,state,tick)then return state end
    if state._terminal_delivered then return nil end
    return state
  end
  -- A joined reveal is not a played battle. Stale ownership or a deferred
  -- ready must not bypass the same bounded admission policy, including bosses.
  if state._prestart_pending==true and not state.ended and state.arena_mirror==nil
      and tick>=(state.join_deadline_tick or math.huge)then
    Runtime.Prestart.cancel(dispatcher,state,state.owner_user_id)
    if Runtime.retry_pending_results(dispatcher,state,tick)then return state end
    if state._terminal_delivered then return nil end
    return state
  end
  if state.global_boss then state._boss_tick=tick;return Runtime.BossBattle.loop(dispatcher,state,tick,messages) end
  if state.ended then
    if Runtime.retry_pending_results(dispatcher, state, tick) then return state end
    return nil
  end

  -- Invitations must not leave a tournament round waiting forever for a no-show.
  if not state.started and tick >= (state.join_deadline_tick or math.huge) then
    if state.arena_mirror ~= nil then
      local absent=state.owner_user_id
      if state.mode~="pve" then
        local ids={};for uid in pairs(state.invited or {}) do ids[#ids+1]=uid end;table.sort(ids)
        for _,uid in ipairs(ids) do if not state.presences[uid] then absent=uid;break end end
        absent=absent or ids[1]
      end
      Runtime.forfeit_match(dispatcher,state,absent,"join_timeout")
    else Runtime.forfeit_match(dispatcher,state,state.owner_user_id,"opponent_not_ready") end
    if Runtime.retry_pending_results(dispatcher, state, tick) then return state end
    return nil
  end

  if state.started then
    local expired = Runtime.Presence.expired(state, tick)
    if #expired > 0 then
      local q = expired[1]
      Runtime.forfeit_match(dispatcher,state,q,"reconnect_timeout")
      if Runtime.retry_pending_results(dispatcher, state, tick) then return state end
      return nil
    end
  end

  if state.turn_deadline_paused == nil then state.turn_deadline_paused = false end
  if state.turn_pause_started_tick == nil then state.turn_pause_started_tick = 0 end

  messages = Runtime.Presence.filter_messages(state, messages)
  messages = Runtime.RateLimit.filter_match_messages(state, messages, nk.time(), function(presence, reason)
    send_reject(dispatcher, presence, reason)
  end)
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
          if Runtime.Resume and Runtime.Resume.stale(state,m.sender.user_id)then
            send_reject(dispatcher,m.sender,"session_stale")
          else
          local committed=true
          if Runtime.Prestart then committed=Runtime.Prestart.commit(state,tick)end
          if committed then
          state.turn_deadline_paused = false
          state.turn_deadline_tick = tick + turn_seconds_for_state(state) * CFG.TICK_RATE
          if state.mode == "pve" and state.active_user_id == state.bot_user_id then
            state.bot_turn_pending = true
            state.bot_turn_ready_tick = tick + bot_think_delay_ticks(state, state.bot_long_think_next == true)
            state.bot_long_think_next = false
          end
          end
          broadcast_sync(dispatcher, state, nil, false, nil, tick, nil)
          end
        end
      end
    end
    if m.op_code == CFG.OP_PLAYER_LEFT and state.started and not state.ended
        and state.presences[m.sender.user_id] ~= nil
        and (state.players_sorted[1] == m.sender.user_id or state.players_sorted[2] == m.sender.user_id) then
      if Runtime.Resume and Runtime.Resume.stale(state,m.sender.user_id)then
        send_reject(dispatcher,m.sender,"session_stale")
      else
      Runtime.forfeit_match(dispatcher,state,m.sender.user_id,"player_left")
      if Runtime.retry_pending_results(dispatcher, state, tick) then return state end
      return nil
      end
    end

    if m.op_code == CFG.OP_SNAPSHOT_REQUEST then
      if state.started and not state.ended and state.board ~= nil then
        local cheat = {}
        if state.cheat_rows_allowed ~= nil and state.cheat_rows_allowed[m.sender.user_id] == true then
          cheat = state.cheat_rows or {}
        end
        local msg = make_sync_msg(state, nil, false, nil, cheat, tick, "")
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
      -- Arena human×bot matches are mode=pve but must not reject on session_epoch bump
      -- (reconnect/claim mid-match). Mine PvE rewards still use this guard.
      local stale_pve = state.mode == "pve"
        and state.arena_mirror == nil
        and state.owner_user_id ~= nil
        and state.owner_user_id ~= ""
        and m.sender.user_id == state.owner_user_id
        and Guard.is_epoch_stale_for_match(m.sender.user_id, state.owner_session_epoch)
      stale_pve=stale_pve or(Runtime.Resume and Runtime.Resume.stale(state,m.sender.user_id))
      if stale_pve then
        send_reject(dispatcher, m.sender, "session_stale")
      else
      local action = parse_action(m.data)
      local valid, reason = validate_action_basic(state, m.sender.user_id, action, tick)
      if not valid then
        send_reject(dispatcher, m.sender, reason)
      else
        local committed=true
        if Runtime.Prestart then committed=Runtime.Prestart.commit(state,tick)end
        if not committed then
          send_reject(dispatcher,m.sender,"battle_start_pending")
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
  end

  if state.started and not state.ended and state.turn_deadline_paused and state.turn_pause_started_tick
      and tick >= state.turn_pause_started_tick + (CFG.TURN_READY_TIMEOUT_SECONDS or 8) * CFG.TICK_RATE then
    if Runtime.Prestart and not Runtime.Prestart.commit(state,tick)then return state end
    state.turn_deadline_paused = false
    state.turn_deadline_tick = tick + turn_seconds_for_state(state) * CFG.TICK_RATE
    if state.mode == "pve" and state.active_user_id == state.bot_user_id then
      state.bot_turn_pending = true
      state.bot_long_think_next = true
      state.bot_turn_ready_tick = tick + bot_think_delay_ticks(state, true)
    end
    broadcast_sync(dispatcher, state, nil, false, nil, tick, nil)
  end

  -- Сначала таймаут хода (человек не успел), затем ход бота — чтобы бот мог сходить в том же тике.
  if state.started and not state.ended and not state.turn_deadline_paused and tick >= state.turn_deadline_tick then
    local current = state.active_user_id
    -- Race: пропуск «последнего хода» завершает матч (без передачи хода дальше).
    if try_finish_race_on_last_turn_forfeit(dispatcher, state, current, tick) then
      return state
    end
    local next_player = other_player_id(state, current)
    if next_player then
      tick_buffs_end_turn(state.stats[current])
      Ach.clear_five_plus_streak(state, current)
      Runtime.Rounds.set_active(state, next_player)
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
            state.bot_turn_ready_tick = tick + bot_think_delay_ticks(state, state.bot_long_think_next == true)
          end
        else
          state.bot_turn_pending = false
          state.bot_turn_ready_tick = 0
        end
      end
      broadcast_sync(dispatcher, state, nil, false, nil, tick, nil)
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
      -- Race last-turn: нечем ходить = пропуск → финал матча.
      if try_finish_race_on_last_turn_forfeit(dispatcher, state, actor_id, tick) then
        return state
      end
      tick_buffs_end_turn(state.stats[actor_id])
      Runtime.Rounds.set_active(state, opp_id)
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
      broadcast_sync(dispatcher, state, nil, false, nil, tick, nil)
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
        if try_finish_race_on_last_turn_forfeit(dispatcher, state, actor_id, tick) then
          return state
        end
        tick_buffs_end_turn(state.stats[actor_id])
        Runtime.Rounds.set_active(state, opp_id)
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
        broadcast_sync(dispatcher, state, nil, false, nil, tick, nil)
      end
    end
  end

  return state
end

local function match_terminate(context, dispatcher, tick, state, grace_seconds)
  return state
end

local function match_signal(context, dispatcher, tick, state, data)
  local decoded,recovery=pcall(nk.json_decode,data)
  if decoded and type(recovery)=="table"and recovery.kind=="duel_recover"then
    local current,response=Runtime.Resume.signal(dispatcher,state,tick,recovery)
    return current,nk.json_encode(response)
  end
  if state.global_boss then
    local ok,p=pcall(nk.json_decode,data)
    if state.global_boss.practice==true and ok and type(p)=="table"and(p.kind=="duel_forfeit"or p.kind=="duel_cancel")and p.user_id==state.owner_user_id then
      if state._prestart_pending then Runtime.Prestart.cancel(dispatcher,state,p.user_id)
      elseif p.kind=="duel_cancel"then return state,nk.json_encode({ok=false,err="battle_already_started"})
      else Runtime.BossBattle.finish(dispatcher,state,tick,"player_surrender")end
      return state,nk.json_encode({ok=state._terminal_delivered==true,accepted=true,terminal=state._terminal_delivered==true,
        practice=true,
        match_cancelled=state._terminal_payload and state._terminal_payload.matchCancelled==true,
        end_reason=state._terminal_payload and state._terminal_payload.endReason or"player_surrender"})
    end
    if ok and type(p)=="table"and p.kind=="boss_resume"and p.user_id==state.owner_user_id then
      local current,response=Runtime.Resume.signal(dispatcher,state,tick,p)
      return current,nk.json_encode(response)
    end
  end
  return Runtime.MatchLifecycle.signal(dispatcher,state,data)
end

-- Каталог предметов из Storage для отладки и синхронизации с клиентом по id/slot/статам.
local function duel_match3_item_catalog_get(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end
    local defs = get_merged_item_defs()
    if next(defs) == nil then
      return nk.json_encode({ ok = false, err = "item_catalog_unavailable" })
    end
    local list = {}
    for id, d in pairs(defs) do
      list[#list + 1] = {
        id = id,
        kind = d.kind or "equipment",
        effect_id = d.effect_id or "",
        slot = d.slot or "",
        max_stack = d.max_stack or 1,
        tier = d.tier or 1,
        quality = d.quality or "normal",
        recipe_slot = d.recipe_slot or "",
        craft_recipe_id = d.craft_recipe_id or "",
        craft_item_id = d.craft_item_id or "",
        craft_minutes = d.craft_minutes or 0,
        sale_price = d.sale_price or 0,
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

CharStats.configure({
  CFG = CFG,
  storage_read_match3_summary_val = Ach.storage_read_match3_summary_val,
  ensure_character_sheet_initialized = ensure_character_sheet_initialized,
  read_pve_progress = read_pve_progress,
  read_character_sheet = read_character_sheet,
  ensure_sheet_inventory_counts = ensure_sheet_inventory_counts,
  sum_equipment_bonuses = sum_equipment_bonuses,
  merge_stats_with_equipment = merge_stats_with_equipment,
  is_human = Ach.is_human,
  aura_apply_to_pve_player_stats = ServerAura.apply_to_pve_player_stats,
  cosmetics_hp_bonus = function(uid)return Runtime.Cosmetics.passive_hp_bonus(uid)end,
  cosmetics_hp_bonus_read_only = function(uid)return Runtime.Cosmetics.passive_hp_bonus_read_only(uid)end,
  cosmetics_bonuses = function(uid)return Runtime.Cosmetics.passive_bonuses(uid)end,
  cosmetics_bonuses_read_only = function(uid)return Runtime.Cosmetics.passive_bonuses_read_only(uid)end,
  read_stats_snapshot = function(uid)
    local rows=nk.storage_read({{collection=CFG.STATS_COLLECTION,key=CFG.STATS_KEY,user_id=uid}})
    assert(type(rows)=="table","practice_stats_unavailable");return rows[1]and decode_storage_value(rows[1])or{}
  end,
  read_progress_snapshot = function(uid)
    local rows=nk.storage_read({{collection=CFG.PVE_PROGRESS_COLLECTION,key=CFG.PVE_PROGRESS_KEY,user_id=uid}})
    assert(type(rows)=="table","practice_progress_unavailable")
    local progress=rows[1]and decode_storage_value(rows[1])or{}
    progress.level=current_level_from_xp(progress.xp or 0);return progress
  end,
})

Runtime.Cosmetics.configure({get_defs=get_merged_item_defs})
Runtime.readCharacterSnapshot=read_character_sheet
Runtime.Daily=runtime_lua_require("modules.duel_daily","duel_daily")
Runtime.Daily.configure({read_progress=read_pve_progress,read_sheet=read_character_sheet,get_defs=get_merged_item_defs,
  build_sheet_write=Runtime.build_character_sheet_write,build_progress_write=Runtime.build_pve_progress_write,
  guard_epoch=Guard.assert_client_epoch_matches})
Runtime.Guidance=runtime_lua_require("modules.duel_player_guidance","duel_player_guidance")
Runtime.Guidance.configure({get_defs=get_merged_item_defs,guard_epoch=Guard.assert_client_epoch_matches})
Runtime.Shop.configure({read_progress=read_pve_progress,read_sheet=read_character_sheet,get_defs=get_merged_item_defs,
  build_sheet_write=Runtime.build_character_sheet_write,build_progress_write=Runtime.build_pve_progress_write,
  guard_epoch=Guard.assert_client_epoch_matches,energy_max=pve_energy_max_for_user})
Runtime.Mail.configure({read_progress=read_pve_progress,read_sheet=read_character_sheet,get_defs=get_merged_item_defs,
  build_sheet_write=Runtime.build_character_sheet_write,build_progress_write=Runtime.build_pve_progress_write,
  guard_epoch=Guard.assert_client_epoch_matches})
Runtime.GlobalBoss.configure({read_sheet=read_character_sheet,get_defs=get_merged_item_defs,
  build_sheet_write=Runtime.build_character_sheet_write,try_match_create=try_match_create,
  recover_match=function(...)return Runtime.Resume.recover(...)end,
  guard_epoch=Guard.assert_client_epoch_matches,mail=Runtime.Mail})
Runtime.resume_energy_max=pve_energy_max_for_user
Runtime.cancel_progression=function(uid)return build_progression_payload_auto(read_pve_progress(uid),uid)end
Runtime.request_for_match=function(state,uid)
  if state.pvp_race then return Runtime.RaceEntry.request_id(uid,state.stats_result_id,state.match_id)end
  return(state.pve_run or{}).request_id
end
Runtime.current_match_epoch=function(uid)
  local account=nk.account_get_id(uid)
  assert(account and account.user,"session_account_unavailable")
  local raw=(account.user.metadata or{})[CFG.SESSION_EPOCH_ACCOUNT_META]
  if raw==nil then return 0 end
  local epoch=tonumber(raw);assert(epoch and epoch>=0 and epoch==math.floor(epoch),"invalid_session_epoch")
  return epoch
end
Runtime.ResumeModule=runtime_lua_require("modules.duel_match3_resume","duel_match3_resume")
Runtime.Resume=Runtime.ResumeModule.new({guard=Guard.assert_client_epoch_matches,lifecycle=Runtime.Lifecycle,
  presence=Runtime.Presence,epoch=Runtime.current_match_epoch,bot_profile=get_bot_profile,
  boss_event=Runtime.GlobalBoss.get_event,
  request_id=Runtime.request_for_match,
  expire=function(...)return Runtime.forfeit_match(...)end,
  grace_ticks=function(state)return math.floor((state.arena_mirror and(CFG.ARENA_RECONNECT_GRACE_SECONDS or 20)or CFG.RECONNECT_GRACE_SECONDS)*CFG.TICK_RATE+.5)end,
  recover_entries=function(uid)return Runtime.Recovery.maybe_recover(uid,{force=true})end})
Runtime.Prestart=runtime_lua_require("modules.duel_match3_prestart","duel_match3_prestart").new({
  consumables=Runtime.Consumables,pve=Runtime.PveEntry,race=RaceApi.Entry,boss=Runtime.GlobalBoss,
  can_start=function(state)
    for _,uid in ipairs(state.players_sorted or{})do
      if Ach.is_human(uid)and Runtime.Resume.stale(state,uid)then return false,"session_stale"end
    end
    return true
  end,
  energy_max=pve_energy_max_for_user,progress=read_pve_progress,tick_rate=CFG.TICK_RATE,emit=Runtime.emit_game_over})
Runtime.BossBattle.configure({api=Runtime.GlobalBoss,CFG=CFG,new_stats=new_stats,new_board=init_board,
  ensure_board=ensure_active_board_has_valid_swaps,init_cheat_rows=init_cheat_rows,ensure_queue=ensure_spawn_queue,
  allow_cheat_rows=is_user_allowed_for_cheat_rows,
  update_cheat_rows=update_cheat_rows_from_board,read_sheet=read_character_sheet,effects=Runtime.ConsumableEffects,
  apply_player=function(actor,uid,practice)CharStats.apply_player_combat_stats_from_sheet(actor,uid,{aura=ServerAura.get_active(),read_only=practice==true})end,
  choose=choose_bot_action,validate=validate_action_basic,resolve=resolve_action,action_cost=action_mana_cost,
  tick_buffs=tick_buffs_end_turn,tick_cooldowns=tick_cooldowns,parse=parse_action,
  broadcast=broadcast_sync,reject=send_reject,emit=Runtime.emit_game_over,
  commit_prestart=function(state,tick)return Runtime.Prestart.commit(state,tick)end,
  stale=function(state)return Runtime.Resume.stale(state,state.owner_user_id)or Guard.is_epoch_stale_for_match(state.owner_user_id,state.owner_session_epoch)end,
  filter_messages=function(state,messages,tick,dispatcher)
    return Runtime.RateLimit.filter_match_messages(state,Runtime.Presence.filter_messages(state,messages),nk.time(),
      function(presence,reason)send_reject(dispatcher,presence,reason)end)
  end})

Ach.configure({
  finalization_journal = Runtime.Journal,
  decode_storage_value = decode_storage_value,
  read_pve_progress = read_pve_progress,
  write_pve_progress = write_pve_progress,
  ensure_character_sheet_initialized = ensure_character_sheet_initialized,
  guard_assert_client_epoch_matches = Guard.assert_client_epoch_matches,
  compute_character_display_stats = CharStats.compute_character_display_stats,
})

AchCat.configure({
  decode_storage_value = decode_storage_value,
})

Runtime.RateLimit.register_rpc(Runtime.StatsApi.duel_match3_stats_get, "duel_match3_stats_get")
Runtime.RateLimit.register_rpc(Runtime.Daily.rpc_get,"duel_daily_get")
Runtime.RateLimit.register_rpc(Runtime.Shop.rpc_get,"duel_shop_get")
Runtime.RateLimit.register_rpc(Runtime.Shop.rpc_buy,"duel_shop_buy")
Runtime.RateLimit.register_rpc(Runtime.Mail.rpc_get,"duel_mail_get")
Runtime.RateLimit.register_rpc(Runtime.Mail.rpc_claim,"duel_mail_claim")
Runtime.RateLimit.register_rpc(Runtime.GlobalBoss.rpc_get,"duel_global_boss_get")
Runtime.RateLimit.register_rpc(Runtime.GlobalBoss.rpc_enter,"duel_global_boss_enter")
Runtime.RateLimit.register_transient_rpc(Runtime.GlobalBoss.rpc_practice,"duel_global_boss_practice")
Runtime.RateLimit.register_rpc(Runtime.GlobalBoss.rpc_leaderboard,"duel_global_boss_leaderboard_get")
Runtime.RateLimit.register_rpc(Runtime.Guidance.rpc_get,"duel_player_guidance_get")
Runtime.RateLimit.register_rpc(Runtime.Guidance.rpc_set,"duel_player_guidance_set")
Runtime.RateLimit.register_rpc(Runtime.MatchLifecycle.rpc,"duel_match3_forfeit")
Runtime.RateLimit.register_rpc(Runtime.MatchLifecycle.cancel_rpc,"duel_match3_cancel")
Runtime.RateLimit.register_rpc(Runtime.Resume.rpc,"duel_match3_recover")
Runtime.RateLimit.register_rpc(Runtime.Daily.rpc_claim,"duel_daily_claim")
Runtime.RateLimit.register_rpc(Runtime.Consumables.rpc_get,"duel_consumables_get")
Runtime.RateLimit.register_rpc(Runtime.Consumables.rpc_use,"duel_consumables_use")
Runtime.RateLimit.register_rpc(Runtime.StatsApi.duel_match3_stats_record, "duel_match3_stats_record")
Runtime.RateLimit.register_rpc(Ach.rpc_achievement_sync, "duel_match3_achievement_sync")
Runtime.RateLimit.register_rpc(Ach.rpc_achievement_claim_step, "duel_match3_achievement_claim_step")
Runtime.RateLimit.register_rpc(AchCat.rpc_achievement_catalog_get, "duel_match3_achievement_catalog_get")
Runtime.RateLimit.register_rpc(duel_match3_pve_catalog_get, "duel_match3_pve_catalog_get")
Runtime.RateLimit.register_rpc(duel_match3_pve_create, "duel_match3_pve_create")
Runtime.RateLimit.register_rpc(RaceApi.duel_match3_race_info, "duel_match3_race_info")
Runtime.RateLimit.register_rpc(RaceApi.duel_match3_race_enter, "duel_match3_race_enter")
Runtime.RateLimit.register_rpc(RaceApi.duel_match3_race_cancel, "duel_match3_race_cancel")
Runtime.RateLimit.register_rpc(RaceApi.duel_match3_race_create, "duel_match3_race_create")
Runtime.RateLimit.register_rpc(RaceApi.duel_friends_race_invite, "duel_friends_race_invite")
Runtime.RateLimit.register_rpc(RaceApi.duel_friends_race_respond, "duel_friends_race_respond")
Runtime.RateLimit.register_rpc(RaceApi.duel_friends_race_clear, "duel_friends_race_clear")
Runtime.RateLimit.register_rpc(duel_mine_summon, "duel_mine_summon")
Runtime.RateLimit.register_rpc(duel_mine_affix_reroll, "duel_mine_affix_reroll")
Runtime.RateLimit.register_rpc(MineBarriers.duel_mine_barrier_unlock, "duel_mine_barrier_unlock")
Runtime.RateLimit.register_rpc(MineBarriers.duel_mine_set_difficulty, "duel_mine_set_difficulty")
Runtime.RateLimit.register_rpc(Runtime.InventoryRpc.duel_character_inventory_slot_buy, "duel_character_inventory_slot_buy")
Runtime.RateLimit.register_rpc(Runtime.InventoryRpc.duel_character_loot_deliver, "duel_character_loot_deliver")
Runtime.RateLimit.register_rpc(Runtime.EconomyRpc.duel_character_get, "duel_character_get")
Runtime.RateLimit.register_rpc(Runtime.EconomyRpc.duel_character_item_move, "duel_character_item_move")
Runtime.RateLimit.register_rpc(Runtime.EconomyRpc.duel_character_recipe_learn, "duel_character_recipe_learn")
Runtime.RateLimit.register_rpc(Runtime.EconomyRpc.duel_character_item_sell, "duel_character_item_sell")
Runtime.RateLimit.register_rpc(Runtime.EconomyRpc.duel_workshop_craft_start, "duel_workshop_craft_start")
Runtime.RateLimit.register_rpc(Runtime.EconomyRpc.duel_workshop_craft_claim, "duel_workshop_craft_claim")
Runtime.RateLimit.register_rpc(Runtime.EconomyRpc.duel_player_resources_get, "duel_player_resources_get")
Runtime.RateLimit.register_rpc(Runtime.EconomyRpc.duel_player_resources_spend, "duel_player_resources_spend")
Runtime.RateLimit.register_rpc(Runtime.EconomyRpc.duel_pve_energy_buy, "duel_pve_energy_buy")
Runtime.RateLimit.register_rpc(Runtime.EconomyRpc.duel_workshop_craft_rush, "duel_workshop_craft_rush")
Runtime.RateLimit.register_rpc(Runtime.EconomyRpc.duel_nickname_status_get, "duel_nickname_status_get")
Runtime.RateLimit.register_rpc(Runtime.EconomyRpc.duel_nickname_change, "duel_nickname_change")
Runtime.RateLimit.register_rpc(duel_match3_item_catalog_get, "duel_match3_item_catalog_get")
Runtime.RateLimit.register_rpc(ServerAura.rpc_get, "duel_match3_server_aura_get")
Runtime.RateLimit.register_rpc(Arena.duel_arena_queue_join, "duel_arena_queue_join")
Runtime.RateLimit.register_rpc(Arena.duel_arena_queue_leave, "duel_arena_queue_leave")
Runtime.RateLimit.register_rpc(Arena.duel_arena_queue_poll, "duel_arena_queue_poll")
Runtime.RateLimit.register_rpc(Arena.duel_arena_place_bet, "duel_arena_place_bet")

if type(nk.run_once) == "function" then
  nk.run_once(function()
    Runtime.StorageMaintenance.bootstrap()
    if Runtime.GlobalBoss and type(nk.match_create)=="function"then
      nk.match_create("duel_global_boss_dispatcher",{bootstrap=true})
    end
  end)
end

return {
  _global_boss_api=Runtime.GlobalBoss,
  match_init = match_init,
  match_join_attempt = match_join_attempt,
  match_join = match_join,
  match_leave = match_leave,
  match_loop = match_loop,
  match_terminate = match_terminate,
  match_signal = match_signal,
}
