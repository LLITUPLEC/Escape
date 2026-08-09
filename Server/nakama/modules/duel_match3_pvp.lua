-- PvP: уровни в BoardSync и награда за победу (вынесено из duel_match3.lua).
return function(deps)
  local nk = deps.nk
  local CFG = deps.CFG
  local clamp_int = deps.clamp_int
  local read_pve_progress = deps.read_pve_progress
  local write_pve_progress = deps.write_pve_progress
  local current_level_from_xp = deps.current_level_from_xp
  local get_bot_profile = deps.get_bot_profile
  local normalize_mine_difficulty = deps.normalize_mine_difficulty
  local award_pve_defeat = deps.award_pve_defeat
  local ContestGoals = deps.ContestGoals
  local add_blueprint = deps.add_blueprint
  local empty_key_items = deps.empty_key_items

  local P = {}

  function P.is_human_duelist_uid(state, uid)
    if uid == nil or uid == "" then return false end
    if state ~= nil and uid == state.bot_user_id then return false end
    return string.sub(tostring(uid), 1, 7) ~= "zz-bot-"
  end

  local function resolve_duelist_level(state, uid)
    if not P.is_human_duelist_uid(state, uid) then
      if state.mode == "pve" then
        local diff = normalize_mine_difficulty((state.pve_run or {}).difficulty)
        local bot = get_bot_profile(state.bot_id, diff)
        return math.max(1, tonumber(bot and (bot.floor or bot.difficulty)) or 1)
      end
      return 1
    end
    local progress = read_pve_progress(uid)
    return clamp_int(progress.level or 1, 1, CFG.PVE_MAX_LEVEL)
  end

  function P.cache_player_levels(state)
    if state.player_levels == nil then state.player_levels = {} end
    for _, uid in ipairs(state.players_sorted or {}) do
      if state.player_levels[uid] == nil then
        state.player_levels[uid] = resolve_duelist_level(state, uid)
      end
    end
  end

  local function apply_flat_rewards_to_progress(progress, flat)
    flat = flat or {}
    progress.xp = math.max(0, tonumber(progress.xp) or 0) + math.max(0, tonumber(flat.xp) or 0)
    progress.gold = math.max(0, tonumber(progress.gold) or 0) + math.max(0, tonumber(flat.gold) or 0)
    progress.ore = math.max(0, tonumber(progress.ore) or 0) + math.max(0, tonumber(flat.ore) or 0)
    progress.matter = math.max(0, tonumber(progress.matter) or 0) + math.max(0, tonumber(flat.matter) or 0)
    progress.ingots = math.max(0, tonumber(progress.ingots) or 0) + math.max(0, tonumber(flat.ingots) or 0)
    progress.tesseracts = math.max(0, tonumber(progress.tesseracts) or 0) + math.max(0, tonumber(flat.tesseract) or 0)
    local key_id = tostring(flat.key_id or "")
    local key_amount = math.max(0, math.floor(tonumber(flat.key_amount) or 0))
    if key_id ~= "" and key_amount > 0 and type(empty_key_items) == "function" then
      progress.key_items = progress.key_items or empty_key_items()
      progress.key_items[key_id] = (tonumber(progress.key_items[key_id]) or 0) + key_amount
    end
    local bp = tostring(flat.blueprint or "")
    if bp ~= "" and type(add_blueprint) == "function" then
      add_blueprint(progress, bp)
    end
    -- recipe_item_id: структура готова; выдача предмета — отдельным пайплайном инвентаря.
    progress.level = current_level_from_xp(progress.xp)
  end

  function P.award_victory(user_id, opts)
    opts = opts or {}
    local flat = {
      xp = tonumber(opts.xp) or tonumber(CFG.PVP_WIN_XP) or 50,
      gold = tonumber(opts.gold) or tonumber(CFG.PVP_WIN_GOLD) or 75,
      ore = tonumber(opts.ore) or 0,
      matter = tonumber(opts.matter) or 0,
      ingots = tonumber(opts.ingots) or 0,
      tesseract = tonumber(opts.tesseract) or 0,
      key_id = tostring(opts.key_id or ""),
      key_amount = tonumber(opts.key_amount) or 0,
      blueprint = tostring(opts.blueprint or ""),
      recipe_item_id = tostring(opts.recipe_item_id or ""),
    }
    local max_retries = 5
    for i = 1, max_retries do
      local progress, version = read_pve_progress(user_id)
      apply_flat_rewards_to_progress(progress, flat)
      local ok, err = pcall(function()
        write_pve_progress(user_id, progress, version)
      end)
      if ok then
        return {
          reward_xp = flat.xp,
          reward_gold = flat.gold,
          reward_ore = flat.ore,
          reward_matter = flat.matter,
          reward_ingots = flat.ingots,
          reward_tesseract = flat.tesseract,
          reward_key_id = flat.key_id,
          reward_key_amount = flat.key_amount,
          reward_blueprint = flat.blueprint,
          reward_recipe_item_id = flat.recipe_item_id,
          level = progress.level or 1,
          xp = progress.xp or 0,
          gold = progress.gold or 0,
        }
      end
      local err_text = tostring(err)
      if string.find(err_text, "version", 1, true) == nil or i == max_retries then
        nk.logger_error("award_pvp_victory: " .. err_text)
        break
      end
    end

    return {
      reward_xp = flat.xp,
      reward_gold = flat.gold,
      reward_ore = flat.ore,
      reward_matter = flat.matter,
      reward_ingots = flat.ingots,
      reward_tesseract = flat.tesseract,
      reward_key_id = flat.key_id,
      reward_key_amount = flat.key_amount,
      reward_blueprint = flat.blueprint,
      reward_recipe_item_id = flat.recipe_item_id,
      level = 1,
      xp = 0,
      gold = 0,
    }
  end

  local function resolve_race_reward_opts(state)
    local lines = nil
    if state ~= nil and type(state.race_reward_lines) == "table" then
      lines = state.race_reward_lines
    elseif type(ContestGoals) == "table" and type(ContestGoals.race_rewards) == "function" then
      local ok, got = pcall(ContestGoals.race_rewards)
      if ok then lines = got end
    end
    local flat
    if type(ContestGoals) == "table" and type(ContestGoals.flatten_reward_lines) == "function" then
      flat = ContestGoals.flatten_reward_lines(lines)
    else
      flat = {
        xp = tonumber(CFG.RACE_WIN_XP) or 200,
        gold = 0,
        ore = 0,
        matter = tonumber(CFG.RACE_WIN_MATTER) or 10,
        ingots = 0,
        tesseract = 0,
        key_id = "",
        key_amount = 0,
        blueprint = "",
        recipe_item_id = "",
      }
    end
    return {
      xp = flat.xp,
      gold = flat.gold,
      ore = flat.ore,
      matter = flat.matter,
      ingots = flat.ingots,
      tesseract = flat.tesseract,
      key_id = flat.key_id,
      key_amount = flat.key_amount,
      blueprint = flat.blueprint,
      recipe_item_id = flat.recipe_item_id,
    }
  end

  local function fill_game_over_reward_fields(game_over_payload, reward)
    if type(game_over_payload) ~= "table" or type(reward) ~= "table" then return end
    game_over_payload.rewardXp = reward.reward_xp or 0
    game_over_payload.rewardGold = reward.reward_gold or 0
    game_over_payload.rewardOre = reward.reward_ore or 0
    game_over_payload.rewardMatter = reward.reward_matter or 0
    game_over_payload.rewardIngots = reward.reward_ingots or 0
    game_over_payload.rewardTesseract = reward.reward_tesseract or 0
    game_over_payload.rewardKeyId = reward.reward_key_id or ""
    game_over_payload.rewardKeyAmount = reward.reward_key_amount or 0
    game_over_payload.rewardBlueprint = reward.reward_blueprint or ""
    game_over_payload.rewardRecipeItemId = reward.reward_recipe_item_id or ""
    game_over_payload.newLevel = reward.level or 1
  end

  function P.apply_game_over_rewards(state, winner, game_over_payload)
    if state == nil or winner == nil or winner == "" then
      return
    end
    local is_race = state.pvp_race == true
    -- Race vs bot создаётся как mode=pve — награды «Спуска» всё равно выдаём.
    if not is_race and (state.mode == "pve" or state.arena_mirror ~= nil) then
      return
    end
    if P.is_human_duelist_uid(state, winner) then
      local opts = nil
      if is_race then
        opts = resolve_race_reward_opts(state)
      end
      state.last_reward = P.award_victory(winner, opts)
      fill_game_over_reward_fields(game_over_payload, state.last_reward)
    end
    if type(award_pve_defeat) == "function" then
      -- В race vs bot проигравший-бот не получает defeat XP.
      if not (is_race and state.mode == "pve") then
        local loser = deps.other_player_id(state, winner)
        if loser ~= nil and P.is_human_duelist_uid(state, loser) then
          award_pve_defeat(loser, 0)
        end
      end
    end
  end

  local function resolve_race_draw_opts(state)
    local flat = {
      xp = math.max(0, math.floor(tonumber(CFG.RACE_DRAW_XP) or 200)),
      gold = 0,
      ore = 0,
      matter = 0,
      ingots = 0,
      tesseract = 0,
      key_id = "",
      key_amount = 0,
      blueprint = "",
      recipe_item_id = "",
    }
    local lines = nil
    if state ~= nil and type(state.race_entry_costs) == "table" then
      lines = state.race_entry_costs
    elseif type(ContestGoals) == "table" and type(ContestGoals.race_entry_costs) == "function" then
      local ok, got = pcall(ContestGoals.race_entry_costs)
      if ok then lines = got end
    end
    if type(lines) == "table" then
      for _, row in ipairs(lines) do
        if type(row) == "table" then
          local res = string.lower(tostring(row.resource or ""))
          local amt = math.max(0, math.floor(tonumber(row.amount) or 0))
          if amt > 0 then
            if res == "xp" then
              flat.xp = flat.xp + amt
            elseif res == "gold" then
              flat.gold = flat.gold + amt
            elseif res == "ore" then
              flat.ore = flat.ore + amt
            elseif res == "matter" then
              flat.matter = flat.matter + amt
            elseif res == "ingots" then
              flat.ingots = flat.ingots + amt
            elseif res == "tesseract" or res == "tesseracts" then
              flat.tesseract = flat.tesseract + amt
            end
          end
        end
      end
    end
    if flat.matter <= 0 and flat.gold <= 0 and flat.ore <= 0 and flat.ingots <= 0 then
      flat.matter = math.max(0, math.floor(tonumber(CFG.RACE_ENTRY_MATTER) or 2))
    end
    return flat
  end

  --- Ничья в «Спуске»: обоим людям — возврат entry + RACE_DRAW_XP.
  function P.apply_race_draw_rewards(state, game_over_payload)
    if state == nil or state.pvp_race ~= true then
      return
    end
    local opts = resolve_race_draw_opts(state)
    local last = nil
    for _, uid in ipairs(state.players_sorted or {}) do
      if P.is_human_duelist_uid(state, uid) then
        last = P.award_victory(uid, opts)
      end
    end
    if last == nil then
      last = {
        reward_xp = opts.xp,
        reward_gold = opts.gold,
        reward_ore = opts.ore,
        reward_matter = opts.matter,
        reward_ingots = opts.ingots,
        reward_tesseract = opts.tesseract,
        reward_key_id = "",
        reward_key_amount = 0,
        reward_blueprint = "",
        reward_recipe_item_id = "",
        level = 1,
      }
    end
    state.last_reward = last
    fill_game_over_reward_fields(game_over_payload, last)
  end

  return P
end
