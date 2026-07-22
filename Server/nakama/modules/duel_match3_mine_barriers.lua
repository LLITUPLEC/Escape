local nk = require("nakama")

--- Барьеры шахты + RPC смены сложности (вынесено из duel_match3.lua из‑за лимита локалей).
--- require("modules.duel_match3_mine_barriers") → factory(deps)
return function(deps)
  local CFG = deps.CFG
  local Guard = deps.Guard
  local normalize_mine_difficulty = deps.normalize_mine_difficulty
  local normalize_mine_unlocked = deps.normalize_mine_unlocked
  local make_floor_state_key = deps.make_floor_state_key
  local get_unlocked_floor = deps.get_unlocked_floor
  local read_pve_progress = deps.read_pve_progress
  local write_pve_progress = deps.write_pve_progress
  local build_progression_payload_auto = deps.build_progression_payload_auto
  local build_resource_payload = deps.build_resource_payload
  local random_affix_for_floor = deps.random_affix_for_floor
  local empty_key_items = deps.empty_key_items

  local M = {}

  --- Множитель стоимости барьера: easy ×1, medium ×3, hard ×5 (от базы лёгкой шахты).
  function M.mine_barrier_cost_multiplier(diff)
    local d = normalize_mine_difficulty(diff)
    if d == "medium" then return 3 end
    if d == "hard" then return 5 end
    return 1
  end

  local function scale_barrier_requirement(req, mul)
    if type(req) ~= "table" then
      return nil
    end
    local m = math.max(1, math.floor(tonumber(mul) or 1))
    local out = {
      key_id = req.key_id,
      key_amount = math.max(0, tonumber(req.key_amount) or 0),
    }
    if req.ore ~= nil then
      out.ore = math.max(0, math.floor((tonumber(req.ore) or 0) * m))
    end
    if req.gold ~= nil then
      out.gold = math.max(0, math.floor((tonumber(req.gold) or 0) * m))
    end
    if req.matter ~= nil then
      out.matter = math.max(0, math.floor((tonumber(req.matter) or 0) * m))
    end
    if req.level ~= nil then
      out.level = math.max(0, tonumber(req.level) or 0)
    end
    return out
  end

  local function barrier_requirement_for_floor(diff, floor)
    local base = CFG.MINE_BARRIER_REQUIREMENTS[floor]
    if base == nil then
      return nil
    end
    return scale_barrier_requirement(base, M.mine_barrier_cost_multiplier(diff))
  end

  function M.build_barrier_requirements_for_diff(diff)
    local mul = M.mine_barrier_cost_multiplier(diff)
    local out = {}
    for floor, req in pairs(CFG.MINE_BARRIER_REQUIREMENTS) do
      out[floor] = scale_barrier_requirement(req, mul)
    end
    return out
  end

  local function mine_floor_wins(progress, diff, floor)
    local mine = progress and progress.mine or nil
    local floor_states = type(mine) == "table" and type(mine.floor_states) == "table" and mine.floor_states or nil
    if floor_states == nil then
      return 0
    end
    local fs = floor_states[make_floor_state_key(diff, floor)]
    if type(fs) ~= "table" then
      return 0
    end
    return math.max(0, tonumber(fs.wins) or 0)
  end

  function M.duel_mine_barrier_unlock(ctx, payload)
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
      local target_floor = CFG.clamp_int(p.floor, 2, CFG.PVE_MAX_LEVEL)
      local diff = normalize_mine_difficulty(p.difficulty)
      local req = barrier_requirement_for_floor(diff, target_floor)
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

        local prev_floor = target_floor - 1
        local prev_wins = mine_floor_wins(progress, diff, prev_floor)
        if prev_wins < 1 then
          return nk.json_encode({
            ok = false,
            err = "prev_monster_not_defeated",
            required_prev_floor = prev_floor,
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

  --- Переключение Лёгкая / Средняя / Тяжёлая. Medium/hard только если unlocked[diff] >= 1.
  function M.duel_mine_set_difficulty(ctx, payload)
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
      local requested = normalize_mine_difficulty(p.difficulty)

      local max_retries = 5
      for i = 1, max_retries do
        local progress, version = read_pve_progress(user_id)
        progress.mine = progress.mine or {}
        progress.mine.unlocked = normalize_mine_unlocked(progress.mine.unlocked, 1)

        local unlocked_floor = get_unlocked_floor(progress, requested)
        if requested ~= "easy" and unlocked_floor < 1 then
          return nk.json_encode({
            ok = false,
            err = "difficulty_locked",
            difficulty = requested,
            unlocked = progress.mine.unlocked,
            progression = build_progression_payload_auto(progress, user_id),
          })
        end

        progress.mine.current_difficulty = requested
        local sel = CFG.clamp_int(progress.mine.selected_floor or 1, 1, CFG.PVE_MAX_LEVEL)
        if unlocked_floor > 0 and sel > unlocked_floor then
          progress.mine.selected_floor = unlocked_floor
        elseif unlocked_floor <= 0 then
          progress.mine.selected_floor = 1
        end

        local write_ok, write_err = pcall(function()
          write_pve_progress(user_id, progress, version)
        end)
        if write_ok then
          return nk.json_encode({
            ok = true,
            difficulty = requested,
            unlocked = progress.mine.unlocked,
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
      nk.logger_error("duel_mine_set_difficulty: " .. tostring(result))
      return nk.json_encode({ ok = false, err = "server_error" })
    end
    return result
  end

  return M
end
