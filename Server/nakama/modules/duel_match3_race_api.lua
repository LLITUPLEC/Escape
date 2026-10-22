-- Race RPCs share one durable server-only entry ledger across every match path.
local nk = require("nakama")
local function runtime_lua_require(nested, root)
  local ok, mod = pcall(require, nested)
  if ok and mod ~= nil then return mod end
  return require(root)
end

return function(deps)
  local Entry = runtime_lua_require("modules.duel_match3_race_entry", "duel_match3_race_entry").new(deps)
  local function costs()
    local lines = deps.ContestGoals.race_entry_costs()
    if type(lines) ~= "table" or #lines == 0 then
      lines = { { resource = "matter", amount = math.max(1, math.floor(tonumber(deps.CFG.RACE_ENTRY_MATTER) or 2)) } }
    end
    return lines
  end
  local function rpc(name, fn, skip_epoch)
    return function(ctx, payload)
      local uid = ctx and ctx.user_id or ""
      if uid == "" then return nk.json_encode({ ok = false, err = "unauthorized" }) end
      if not skip_epoch then
        local epoch_ok, epoch_err = deps.Guard.assert_client_epoch_matches(uid, payload)
        if not epoch_ok then return nk.json_encode({ ok = false, err = epoch_err }) end
      end
      local ok, value = pcall(fn, uid, payload)
      if not ok then
        nk.logger_error(name .. ": " .. tostring(value))
        return nk.json_encode({ ok = false, err = "server_error" })
      end
      return nk.json_encode(value)
    end
  end
  local function resources(progress, uid, reason, entry, spent)
    local result = deps.build_resource_payload(progress, uid)
    result.ok, result.reason = true, reason
    result.entry, result.spent = entry and entry.costs or {}, spent or 0
    result.resource = result.entry[1] and result.entry[1].resource or ""
    return result
  end
  local api = { Entry = Entry }
  api.duel_match3_race_info = rpc("duel_match3_race_info", function()
    local cfg = deps.ContestGoals.race_public_config()
    return { ok = true, goal_mana = cfg.goal_mana, mana_bonus_every = cfg.mana_bonus_every, entry = cfg.entry, rewards = cfg.rewards }
  end, true)
  api.duel_match3_race_enter = rpc("duel_match3_race_enter", function(uid)
    local entry_costs = costs()
    local ok, err, progress, entry = Entry.pay(uid, entry_costs)
    if not ok then
      local result = { ok = false, err = err, entry = entry_costs, reason = "race_enter" }
      for _, cost in ipairs(entry_costs) do
        if err == "not_enough_" .. tostring(cost.resource) then result.required, result.resource = cost.amount, cost.resource end
      end
      return result
    end
    local spent = entry.newly_paid and (entry.costs[1] and entry.costs[1].amount or 0) or 0
    return resources(progress, uid, spent > 0 and "race_enter" or "race_enter_prepaid", entry, spent)
  end)
  api.duel_match3_race_cancel = rpc("duel_match3_race_cancel", function(uid)
    local ok, err, progress, entry = Entry.cancel(uid)
    if not ok then return { ok = false, err = err } end
    return resources(progress, uid, entry and "race_cancel" or "race_cancel_noop", entry,
      entry and -(entry.costs[1] and entry.costs[1].amount or 0) or 0)
  end)
  api.duel_match3_race_create = rpc("duel_match3_race_create", function(uid)
    local paid, err = Entry.require_pending(uid)
    if not paid then return { ok = false, err = err } end
    local bot_id = "race_bot"
    local bot_user_id = deps.make_bot_user_id(bot_id)
    local names = { "Странник", "Тень", "Путник", "Эхо", "Скиталец" }
    local mid = deps.try_match_create({
      mode = "pve", pvp_race = true, arena_pvp_style = true, owner_user_id = uid,
      bot_id = bot_id, bot_user_id = bot_user_id, owner_level = 1,
      owner_session_epoch = deps.Guard.read_metadata_epoch(uid),
      pve_run = { floor = 1, difficulty = "easy", affix = "", stat_mul = 1, reward_mul = 0, arena_suppress_all = true },
    })
    if not mid or mid == "" then return { ok = false, err = "match_create_failed" } end
    return { ok = true, match_id = mid, bot_id = bot_id, bot_name = names[math.random(1, #names)],
      bot_user_id = bot_user_id, race_goal_mana = deps.race_goal_mana(nil) }
  end)
  local friend_deps = {}
  for key, value in pairs(deps) do friend_deps[key] = value end
  friend_deps.Entry = Entry
  friend_deps.race_normalize_entry_costs = function(lines) return Entry.normalize_costs(lines) or {} end
  friend_deps.race_progress_balance = function(progress, resource)
    if resource == "matter" or resource == "gold" or resource == "ore" or resource == "energy" then return tonumber(progress[resource]) or 0 end
    return nil
  end
  local friends = runtime_lua_require("modules.duel_friends_race", "duel_friends_race")(friend_deps)
  api.duel_friends_race_invite = friends.duel_friends_race_invite
  api.duel_friends_race_respond = friends.duel_friends_race_respond
  api.duel_friends_race_clear = friends.duel_friends_race_clear
  return api
end
