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

  function P.award_victory(user_id, opts)
    opts = opts or {}
    local win_xp = tonumber(opts.xp) or tonumber(CFG.PVP_WIN_XP) or 50
    local win_gold = tonumber(opts.gold) or tonumber(CFG.PVP_WIN_GOLD) or 75
    local win_matter = tonumber(opts.matter) or 0
    local max_retries = 5
    for i = 1, max_retries do
      local progress, version = read_pve_progress(user_id)
      progress.xp = math.max(0, tonumber(progress.xp) or 0) + win_xp
      progress.gold = math.max(0, tonumber(progress.gold) or 0) + win_gold
      progress.matter = math.max(0, tonumber(progress.matter) or 0) + win_matter
      progress.level = current_level_from_xp(progress.xp)
      local ok, err = pcall(function()
        write_pve_progress(user_id, progress, version)
      end)
      if ok then
        return {
          reward_xp = win_xp,
          reward_gold = win_gold,
          reward_ore = 0,
          reward_matter = win_matter,
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
      reward_xp = win_xp,
      reward_gold = win_gold,
      reward_ore = 0,
      reward_matter = win_matter,
      level = 1,
      xp = 0,
      gold = 0,
    }
  end

  function P.apply_game_over_rewards(state, winner, game_over_payload)
    if state == nil or state.mode == "pve" or state.arena_mirror ~= nil or winner == nil or winner == "" then
      return
    end
    if P.is_human_duelist_uid(state, winner) then
      local opts = nil
      if state.pvp_race == true then
        opts = {
          xp = tonumber(CFG.RACE_WIN_XP) or 200,
          gold = 0,
          matter = tonumber(CFG.RACE_WIN_MATTER) or 10,
        }
      end
      state.last_reward = P.award_victory(winner, opts)
      game_over_payload.rewardXp = state.last_reward.reward_xp or 0
      game_over_payload.rewardGold = state.last_reward.reward_gold or 0
      game_over_payload.rewardOre = 0
      game_over_payload.rewardMatter = state.last_reward.reward_matter or 0
      game_over_payload.newLevel = state.last_reward.level or 1
    end
    if type(award_pve_defeat) ~= "function" then return end
    local loser = deps.other_player_id(state, winner)
    if loser ~= nil and P.is_human_duelist_uid(state, loser) then
      award_pve_defeat(loser, 0)
    end
  end

  return P
end
