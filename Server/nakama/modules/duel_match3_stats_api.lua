-- Статистика Match3: чтение RPC, запись только из authoritative match handler.
local nk = require("nakama")

return function(deps)
  local CFG = deps.CFG
  local Ach = deps.Ach
  local read_pve_progress = deps.read_pve_progress
  local normalize_mine_difficulty = deps.normalize_mine_difficulty

  local function read_match3_stats(user_id)
    local val, version = Ach.storage_read_match3_summary_val(user_id)
    local stats = {
      played = tonumber(val.played) or 0,
      wins = tonumber(val.wins) or 0,
      losses = tonumber(val.losses) or 0,
    }
    return stats, version
  end

  local function stats_inc_arena_tournament_played(user_id, arena_kind)
    if user_id == nil or user_id == "" then
      return false
    end
    local k = string.lower(tostring(arena_kind or "smith"))
    if k ~= "smith" and k ~= "ore" and k ~= "gold" then
      k = "smith"
    end
    local max_retries = 5
    for attempt = 1, max_retries do
      local val, version = Ach.storage_read_match3_summary_val(user_id)
      if type(val) ~= "table" then
        val = {}
      end
      if type(val.summary) ~= "table" then
        val.summary = {}
      end
      if type(val.summary.arena_tournaments) ~= "table" then
        val.summary.arena_tournaments = {}
      end
      if type(val.summary.arena_tournaments[k]) ~= "table" then
        val.summary.arena_tournaments[k] = {}
      end
      local bag = val.summary.arena_tournaments[k]
      bag.played = (tonumber(bag.played) or 0) + 1
      bag.updated_at = os.time()
      val.summary.arena_tournaments[k] = bag
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
        return true
      end
      local err_text = tostring(write_err)
      if string.find(err_text, "version", 1, true) == nil or attempt == max_retries then
        nk.logger_error("stats_inc_arena_tournament_played: " .. err_text)
        return false
      end
    end
    return false
  end

  local function match_stats_mode(state)
    if state.arena_mirror ~= nil then return "arena" end
    if state.pvp_race == true then return "race" end
    if state.mode == "pve" then return "mine" end
    if state.pvp_pro == true then return "pro" end
    return "duel"
  end

  local function mode_bag(modes, mode)
    if type(modes) ~= "table" then modes = {} end
    local bag = modes[mode]
    if type(bag) ~= "table" then
      bag = { played = 0, wins = 0, losses = 0 }
    end
    bag.played = math.max(0, math.floor(tonumber(bag.played) or 0))
    bag.wins = math.max(0, math.floor(tonumber(bag.wins) or 0))
    bag.losses = math.max(0, math.floor(tonumber(bag.losses) or 0))
    modes[mode] = bag
    return modes, bag
  end

  local function build_modes_array(modes)
    local order = { "duel", "pro", "race", "arena", "mine" }
    local labels = {
      duel = "Дуэль",
      pro = "Схватка",
      race = "Спуск",
      arena = "Турниры",
      mine = "Шахта",
    }
    local out = {}
    local seen = {}
    for _, id in ipairs(order) do
      local _, bag = mode_bag(modes, id)
      out[#out + 1] = {
        id = id,
        label = labels[id] or id,
        played = bag.played,
        wins = bag.wins,
        losses = bag.losses,
      }
      seen[id] = true
    end
    if type(modes) == "table" then
      for id, _ in pairs(modes) do
        local key = tostring(id or "")
        if key ~= "" and not seen[key] then
          local _, bag = mode_bag(modes, key)
          out[#out + 1] = {
            id = key,
            label = key,
            played = bag.played,
            wins = bag.wins,
            losses = bag.losses,
          }
        end
      end
    end
    return out
  end

  local function build_arena_tournaments_array(summary)
    local out = {}
    local labels = { smith = "Кузнец", ore = "Руда", gold = "Золото" }
    local src = type(summary) == "table" and summary.arena_tournaments or nil
    for _, id in ipairs({ "smith", "ore", "gold" }) do
      local bag = type(src) == "table" and src[id] or nil
      out[#out + 1] = {
        id = id,
        label = labels[id] or id,
        played = math.max(0, math.floor(tonumber(bag and bag.played) or 0)),
      }
    end
    return out
  end

  local function build_mine_floors_array(user_id)
    local out = {}
    local total_wins = 0
    local progress = read_pve_progress(user_id)
    local mine = type(progress) == "table" and progress.mine or nil
    local floor_states = type(mine) == "table" and mine.floor_states or nil
    if type(floor_states) ~= "table" then
      return out, 0
    end
    for key, st in pairs(floor_states) do
      if type(st) == "table" then
        local wins = math.max(0, math.floor(tonumber(st.wins) or 0))
        if wins > 0 then
          total_wins = total_wins + wins
          local diff = "easy"
          local floor = 1
          local k = tostring(key or "")
          local colon = string.find(k, ":", 1, true)
          if colon ~= nil then
            diff = string.sub(k, 1, colon - 1)
            floor = math.max(1, math.floor(tonumber(string.sub(k, colon + 1)) or 1))
          end
          out[#out + 1] = {
            difficulty = normalize_mine_difficulty(diff),
            floor = floor,
            wins = wins,
          }
        end
      end
    end
    table.sort(out, function(a, b)
      if a.difficulty == b.difficulty then
        return (a.floor or 0) < (b.floor or 0)
      end
      local rank = { easy = 1, medium = 2, hard = 3 }
      return (rank[a.difficulty] or 9) < (rank[b.difficulty] or 9)
    end)
    return out, total_wins
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
      local modes = type(val_full.modes) == "table" and val_full.modes or {}
      local summary = type(val_full.summary) == "table" and val_full.summary or {}
      local mine_floors, mine_wins = build_mine_floors_array(user_id)
      return nk.json_encode({
        ok = true,
        played = stats.played or 0,
        wins = stats.wins or 0,
        losses = stats.losses or 0,
        modes = build_modes_array(modes),
        arena_tournaments = build_arena_tournaments_array(summary),
        mine_total_wins = mine_wins,
        mine_floors = mine_floors,
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

  -- Compatibility response for old clients. No payload can mutate server stats.
  local function duel_match3_stats_record(ctx, payload)
    if ctx == nil or ctx.user_id == nil or ctx.user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end
    return nk.json_encode({ ok = false, err = "client_result_recording_disabled" })
  end

  local function record_user_result(user_id, result_id, mode, won)
    for attempt = 1, 5 do
      local val, version = Ach.storage_read_match3_summary_val(user_id)
      local recent = type(val.match_result_ids) == "table" and val.match_result_ids or {}
      for _, id in ipairs(recent) do
        if id == result_id then return true end
      end

      val.played = math.max(0, tonumber(val.played) or 0) + 1
      val.wins = math.max(0, tonumber(val.wins) or 0) + (won and 1 or 0)
      val.losses = math.max(0, tonumber(val.losses) or 0) + (won and 0 or 1)
      local modes, bag = mode_bag(val.modes, mode)
      bag.played = bag.played + 1
      bag.wins = bag.wins + (won and 1 or 0)
      bag.losses = bag.losses + (won and 0 or 1)
      val.modes = modes
      -- Bounded deduplication history; the current match also keeps a success map.
      local result_ids = {}
      for i = math.max(1, #recent - 62), #recent do
        result_ids[#result_ids + 1] = recent[i]
      end
      result_ids[#result_ids + 1] = result_id
      val.match_result_ids = result_ids
      val.updated_at = os.time()

      local ok, err = pcall(function()
        nk.storage_write({
          {
            collection = CFG.STATS_COLLECTION,
            key = CFG.STATS_KEY,
            user_id = user_id,
            value = val,
            version = version ~= nil and version ~= "" and version or "*",
            permission_read = 1,
            permission_write = 0,
          },
        })
      end)
      if ok then return true end
      if string.find(tostring(err), "version", 1, true) == nil or attempt == 5 then
        nk.logger_error("match3 authoritative result write failed: " .. tostring(err))
        return false
      end
    end
    return false
  end

  -- Internal API only: the finished authoritative match supplies identities and
  -- mode. Local SoloBot and client RPC payloads never reach this path.
  local function record_match_finish(state, winner, is_draw)
    if state == nil or state.started ~= true or state.ended ~= true then return false end
    -- Preserve existing behavior: draws do not increment wins/losses/played.
    if is_draw == true or winner == nil or winner == "" then return false end
    local players = state.players_sorted
    if type(players) ~= "table" or #players ~= 2 or players[1] == players[2] then return false end
    if winner ~= players[1] and winner ~= players[2] then return false end
    local result_id = state.stats_result_id
    if type(result_id) ~= "string" or result_id == "" then return false end

    state._stats_finish_recorded = state._stats_finish_recorded or {}
    local all_ok = true
    for _, uid in ipairs(players) do
      if Ach.is_human(uid) and state._stats_finish_recorded[uid] ~= true then
        local ok, recorded = pcall(record_user_result, uid, result_id, match_stats_mode(state), uid == winner)
        if ok and recorded then
          state._stats_finish_recorded[uid] = true
        else
          all_ok = false
          if not ok then nk.logger_error("match3 authoritative result failed: " .. tostring(recorded)) end
        end
      end
    end
    return all_ok
  end

  return {
    read_match3_stats = read_match3_stats,
    stats_inc_arena_tournament_played = stats_inc_arena_tournament_played,
    duel_match3_stats_get = duel_match3_stats_get,
    duel_match3_stats_record = duel_match3_stats_record,
    record_match_finish = record_match_finish,
  }
end
