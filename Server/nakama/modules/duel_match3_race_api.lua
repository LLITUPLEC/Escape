-- Race («Спуск») RPC + friends invite wiring.
-- Вынесено из duel_match3.lua из‑за лимита локальных переменных Lua (200).
local nk = require("nakama")

local function runtime_lua_require(name_nested, name_root)
  local ok, mod = pcall(require, name_nested)
  if ok and mod ~= nil then return mod end
  return require(name_root)
end

return function(deps)
  local CFG = deps.CFG
  local ContestGoals = deps.ContestGoals
  local Guard = deps.Guard
  local decode_storage_value = deps.decode_storage_value
  local try_match_create = deps.try_match_create
  local make_bot_user_id = deps.make_bot_user_id
  local read_pve_progress = deps.read_pve_progress
  local write_pve_progress = deps.write_pve_progress
  local build_resource_payload = deps.build_resource_payload
  local race_goal_mana = deps.race_goal_mana

  local function race_progress_balance(progress, resource)
    local r = string.lower(tostring(resource or ""))
    if r == "matter" then return math.max(0, tonumber(progress.matter) or 0) end
    if r == "gold" then return math.max(0, tonumber(progress.gold) or 0) end
    if r == "ore" then return math.max(0, tonumber(progress.ore) or 0) end
    if r == "ingots" then return math.max(0, tonumber(progress.ingots) or 0) end
    if r == "energy" then return math.max(0, tonumber(progress.energy) or 0) end
    return nil
  end

  local function race_progress_spend(progress, resource, amount)
    local r = string.lower(tostring(resource or ""))
    local need = math.max(0, math.floor(tonumber(amount) or 0))
    if need <= 0 then return true end
    if r == "matter" then
      progress.matter = math.max(0, (tonumber(progress.matter) or 0) - need)
      return true
    end
    if r == "gold" then
      progress.gold = math.max(0, (tonumber(progress.gold) or 0) - need)
      return true
    end
    if r == "ore" then
      progress.ore = math.max(0, (tonumber(progress.ore) or 0) - need)
      return true
    end
    if r == "ingots" then
      progress.ingots = math.max(0, (tonumber(progress.ingots) or 0) - need)
      return true
    end
    if r == "energy" then
      progress.energy = math.max(0, (tonumber(progress.energy) or 0) - need)
      return true
    end
    return false
  end

  local function race_progress_grant(progress, resource, amount)
    local r = string.lower(tostring(resource or ""))
    local add = math.max(0, math.floor(tonumber(amount) or 0))
    if add <= 0 then return true end
    if r == "matter" then
      progress.matter = math.max(0, (tonumber(progress.matter) or 0) + add)
      return true
    end
    if r == "gold" then
      progress.gold = math.max(0, (tonumber(progress.gold) or 0) + add)
      return true
    end
    if r == "ore" then
      progress.ore = math.max(0, (tonumber(progress.ore) or 0) + add)
      return true
    end
    if r == "ingots" then
      progress.ingots = math.max(0, (tonumber(progress.ingots) or 0) + add)
      return true
    end
    if r == "energy" then
      progress.energy = math.max(0, (tonumber(progress.energy) or 0) + add)
      return true
    end
    return false
  end

  local function race_normalize_entry_costs(costs)
    local out = {}
    if type(costs) ~= "table" then return out end
    for _, line in ipairs(costs) do
      if type(line) == "table" then
        local res = string.lower(tostring(line.resource or ""))
        local amt = math.max(0, math.floor(tonumber(line.amount) or 0))
        if res ~= "" and amt > 0 then
          out[#out + 1] = { resource = res, amount = amt }
        end
      end
    end
    return out
  end

  local function race_consume_entry_pending(user_id)
    if user_id == nil or user_id == "" then return end
    local ok_all, err_all = pcall(function()
      local max_retries = 5
      for i = 1, max_retries do
        local progress, version = read_pve_progress(user_id)
        if progress.race_entry_pending ~= true then
          return
        end
        progress.race_entry_pending = false
        progress.race_entry_costs = nil
        local write_ok, write_err = pcall(function()
          write_pve_progress(user_id, progress, version)
        end)
        if write_ok then return end
        local err_text = tostring(write_err)
        if string.find(err_text, "version", 1, true) == nil or i == max_retries then
          error(write_err)
        end
      end
    end)
    if not ok_all then
      nk.logger_error("race_consume_entry_pending: " .. tostring(err_all))
    end
  end

  local function duel_match3_race_info(ctx, payload)
    local ok, result = pcall(function()
      local user_id = ctx and ctx.user_id or ""
      if user_id == nil or user_id == "" then
        return nk.json_encode({ ok = false, err = "unauthorized" })
      end
      local cfg = ContestGoals.race_public_config()
      return nk.json_encode({
        ok = true,
        goal_mana = cfg.goal_mana,
        mana_bonus_every = cfg.mana_bonus_every,
        entry = cfg.entry,
        rewards = cfg.rewards,
      })
    end)
    if not ok then
      nk.logger_error("duel_match3_race_info: " .. tostring(result))
      return nk.json_encode({ ok = false, err = "server_error" })
    end
    return result
  end

  local function duel_match3_race_enter(ctx, payload)
    local ok, result = pcall(function()
      local user_id = ctx and ctx.user_id or ""
      if user_id == nil or user_id == "" then
        return nk.json_encode({ ok = false, err = "unauthorized" })
      end

      local ok_epoch, err_epoch = Guard.assert_client_epoch_matches(user_id, payload)
      if not ok_epoch then
        return nk.json_encode({ ok = false, err = err_epoch })
      end

      local costs = race_normalize_entry_costs(ContestGoals.race_entry_costs())
      if #costs == 0 then
        costs = { { resource = "matter", amount = math.max(1, math.floor(tonumber(CFG.RACE_ENTRY_MATTER) or 2)) } }
      end

      local max_retries = 5
      for i = 1, max_retries do
        local progress, version = read_pve_progress(user_id)

        if progress.race_entry_pending == true then
          local resources = build_resource_payload(progress, user_id)
          resources.ok = true
          resources.reason = "race_enter_prepaid"
          resources.entry = type(progress.race_entry_costs) == "table" and progress.race_entry_costs or costs
          resources.spent = 0
          resources.resource = resources.entry[1] and resources.entry[1].resource or ""
          return nk.json_encode(resources)
        end

        for _, line in ipairs(costs) do
          local res = tostring(line.resource or "")
          local need = math.max(0, math.floor(tonumber(line.amount) or 0))
          local have = race_progress_balance(progress, res)
          if have == nil then
            return nk.json_encode({ ok = false, err = "unsupported_entry_resource", resource = res })
          end
          if have < need then
            local resources = build_resource_payload(progress, user_id)
            resources.ok = false
            resources.err = "not_enough_" .. res
            resources.required = need
            resources.resource = res
            resources.reason = "race_enter"
            resources.entry = costs
            return nk.json_encode(resources)
          end
        end

        for _, line in ipairs(costs) do
          race_progress_spend(progress, line.resource, line.amount)
        end
        progress.race_entry_pending = true
        progress.race_entry_costs = costs

        local write_ok, write_err = pcall(function()
          write_pve_progress(user_id, progress, version)
        end)
        if write_ok then
          local resources = build_resource_payload(progress, user_id)
          resources.ok = true
          resources.reason = "race_enter"
          resources.entry = costs
          resources.spent = costs[1] and costs[1].amount or 0
          resources.resource = costs[1] and costs[1].resource or ""
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
      nk.logger_error("duel_match3_race_enter: " .. tostring(result))
      return nk.json_encode({ ok = false, err = "server_error" })
    end
    return result
  end

  local function duel_match3_race_cancel(ctx, payload)
    local ok, result = pcall(function()
      local user_id = ctx and ctx.user_id or ""
      if user_id == nil or user_id == "" then
        return nk.json_encode({ ok = false, err = "unauthorized" })
      end

      local ok_epoch, err_epoch = Guard.assert_client_epoch_matches(user_id, payload)
      if not ok_epoch then
        return nk.json_encode({ ok = false, err = err_epoch })
      end

      local max_retries = 5
      for i = 1, max_retries do
        local progress, version = read_pve_progress(user_id)
        if progress.race_entry_pending ~= true then
          local resources = build_resource_payload(progress, user_id)
          resources.ok = true
          resources.reason = "race_cancel_noop"
          resources.spent = 0
          return nk.json_encode(resources)
        end

        local costs = race_normalize_entry_costs(progress.race_entry_costs)
        if #costs == 0 then
          costs = race_normalize_entry_costs(ContestGoals.race_entry_costs())
        end
        if #costs == 0 then
          costs = { { resource = "matter", amount = math.max(1, math.floor(tonumber(CFG.RACE_ENTRY_MATTER) or 2)) } }
        end

        for _, line in ipairs(costs) do
          race_progress_grant(progress, line.resource, line.amount)
        end
        progress.race_entry_pending = false
        progress.race_entry_costs = nil

        local write_ok, write_err = pcall(function()
          write_pve_progress(user_id, progress, version)
        end)
        if write_ok then
          local resources = build_resource_payload(progress, user_id)
          resources.ok = true
          resources.reason = "race_cancel"
          resources.entry = costs
          resources.spent = -(costs[1] and costs[1].amount or 0)
          resources.resource = costs[1] and costs[1].resource or ""
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
      nk.logger_error("duel_match3_race_cancel: " .. tostring(result))
      return nk.json_encode({ ok = false, err = "server_error" })
    end
    return result
  end

  local function duel_match3_race_create(ctx, payload)
    local ok, result = pcall(function()
      local user_id = ctx and ctx.user_id or ""
      if user_id == nil or user_id == "" then
        return nk.json_encode({ ok = false, err = "unauthorized" })
      end

      local ok_epoch, err_epoch = Guard.assert_client_epoch_matches(user_id, payload)
      if not ok_epoch then
        return nk.json_encode({ ok = false, err = err_epoch })
      end

      local owner_epoch = Guard.read_metadata_epoch(user_id)
      local bot_id = "race_bot"
      local bot_user_id = make_bot_user_id(bot_id)
      local bot_names = { "Странник", "Тень", "Путник", "Эхо", "Скиталец" }
      local bot_name = bot_names[math.random(1, #bot_names)]

      local match_id = try_match_create({
        mode = "pve",
        pvp_race = true,
        arena_pvp_style = true,
        owner_user_id = user_id,
        bot_id = bot_id,
        bot_user_id = bot_user_id,
        owner_level = 1,
        owner_session_epoch = owner_epoch,
        pve_run = {
          floor = 1,
          difficulty = "easy",
          affix = "",
          stat_mul = 1,
          reward_mul = 0,
          arena_suppress_all = true,
        },
      })
      if match_id == nil or match_id == "" then
        return nk.json_encode({ ok = false, err = "match_create_failed" })
      end

      nk.logger_info("duel_match3_race_create: user=" .. tostring(user_id)
        .. " match=" .. tostring(match_id)
        .. " bot=" .. tostring(bot_name))

      return nk.json_encode({
        ok = true,
        match_id = match_id,
        bot_id = bot_id,
        bot_name = bot_name,
        bot_user_id = bot_user_id,
        race_goal_mana = race_goal_mana(nil),
      })
    end)

    if not ok then
      nk.logger_error("duel_match3_race_create: " .. tostring(result))
      return nk.json_encode({ ok = false, err = "server_error" })
    end
    return result
  end

  local FriendsRace = runtime_lua_require("modules.duel_friends_race", "duel_friends_race")({
    CFG = CFG,
    ContestGoals = ContestGoals,
    Guard = Guard,
    decode_storage_value = decode_storage_value,
    try_match_create = try_match_create,
    read_pve_progress = read_pve_progress,
    write_pve_progress = write_pve_progress,
    race_progress_balance = race_progress_balance,
    race_progress_spend = race_progress_spend,
    race_progress_grant = race_progress_grant,
    race_normalize_entry_costs = race_normalize_entry_costs,
  })

  return {
    race_consume_entry_pending = race_consume_entry_pending,
    duel_match3_race_info = duel_match3_race_info,
    duel_match3_race_enter = duel_match3_race_enter,
    duel_match3_race_cancel = duel_match3_race_cancel,
    duel_match3_race_create = duel_match3_race_create,
    duel_friends_race_invite = FriendsRace.duel_friends_race_invite,
    duel_friends_race_respond = FriendsRace.duel_friends_race_respond,
    duel_friends_race_clear = FriendsRace.duel_friends_race_clear,
  }
end
