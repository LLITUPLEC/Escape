--[[
  Серверная логика достижений (storage duel_match3_stats.summary, матч-хуки, RPC sync/claim).
  Вынесено в отдельный файл из-за лимита Lua на число локальных переменных в одном chunk (~200).
]]
local nk = require("nakama")

local function runtime_lua_require_nested(name_nested, name_root)
  local ok, mod = pcall(require, name_nested)
  if ok and mod ~= nil then return mod end
  return require(name_root)
end

local CFG = runtime_lua_require_nested("modules.duel_match3_config", "duel_match3_config")
local AchCat = runtime_lua_require_nested("modules.duel_match3_achievement_catalog", "duel_match3_achievement_catalog")

local M = {}

--- Заполняется из duel_match3.lua один раз перед регистрацией RPC (после определения read_pve_progress и т.д.).
local deps = {}

function M.configure(d)
  if type(d) ~= "table" then
    return
  end
  deps = d
end

local function decode_storage(row)
  local fn = deps.decode_storage_value
  if type(fn) == "function" then
    return fn(row)
  end
  return nil
end

local function ensure_sheet(uid)
  local fn = deps.ensure_character_sheet_initialized
  if type(fn) == "function" then fn(uid) end
end

local function read_pv(uid)
  return deps.read_pve_progress(uid)
end

local function write_pv(uid, progress, version)
  return deps.write_pve_progress(uid, progress, version)
end

local function guard_epoch(uid, payload)
  local fn = deps.guard_assert_client_epoch_matches
  if type(fn) == "function" then
    return fn(uid, payload)
  end
  return true, ""
end

local ACH_STAT_CROSS = "uses.cross"
local ACH_STAT_SQUARE = "uses.square"
local ACH_STAT_PETARD = "uses.petard"
local ACH_STAT_FURY = "uses.fury"
local ACH_STAT_SHIELD = "uses.shield"
local ACH_STAT_DNN = "dnn.double_line5_same_turn"
local ACH_STAT_WIN1 = "dnn.win_at_one_hp"
local ACH_STAT_BLACKSMITH = "slaughter.tournament_smith_final"
local ACH_STAT_ORE_TOURN = "slaughter.tournament_ore_final"
local ACH_STAT_GOLD_TOURN = "slaughter.tournament_gold_final"
local ACH_STAT_DUEL_TRI = "slaughter.duel_tri_win"
local ACH_STAT_PETARD_FINISH = "slaughter.duel_petard_finish"
local ACH_STAT_PETARD_PVP_FINISH = "slaughter.finish_petard_pvp"
local ACH_STAT_FINAL_LOSS = "slaughter.tournament_final_loss"
local ACH_STAT_PVE_KILL_MINE_2 = "pve.kill.mine_2"

function M.is_human(uid)
  if uid == nil or uid == "" then return false end
  return string.sub(uid, 1, 7) ~= CFG.BOT_USER_ID_PREFIX
end

function M.ensure_counters(state)
  if state._ach_counters == nil then
    state._ach_counters = {}
  end
end

function M.inc_session(state, uid, key, delta)
  if delta == nil or delta == 0 or key == nil or key == "" then return end
  if not M.is_human(uid) then return end
  M.ensure_counters(state)
  if state._ach_counters[uid] == nil then
    state._ach_counters[uid] = {}
  end
  local prev = tonumber(state._ach_counters[uid][key]) or 0
  state._ach_counters[uid][key] = prev + delta
end

function M.map_action_to_stat(action_type)
  local at = tonumber(action_type) or 0
  if at == 2 then return ACH_STAT_CROSS end
  if at == 3 then return ACH_STAT_SQUARE end
  if at == 4 then return ACH_STAT_PETARD end
  if at == 5 then return ACH_STAT_SHIELD end
  if at == 6 then return ACH_STAT_FURY end
  return nil
end

function M.snapshot_hp_was_exactly_one(state)
  if state == nil then return end
  if state._ach_hp_was_exactly_one == nil then
    state._ach_hp_was_exactly_one = {}
  end
  local ps = state.players_sorted
  if ps == nil then return end
  for _, uid in ipairs(ps) do
    if M.is_human(uid) and state.stats[uid] ~= nil then
      local hp = tonumber(state.stats[uid].hp)
      if hp == 1 then
        state._ach_hp_was_exactly_one[uid] = true
      end
    end
  end
end

local function claim_has(claimed_arr, tok)
  if claimed_arr == nil then return false end
  for _, c in ipairs(claimed_arr) do
    if c == tok then return true end
  end
  return false
end

local function claim_append(claimed_arr, tok)
  if claim_has(claimed_arr, tok) then return claimed_arr end
  local out = claimed_arr or {}
  out[#out + 1] = tok
  return out
end

local function apply_wallet_grant(user_id, chain_id, step_idx)
  if user_id == nil or user_id == "" then return end
  local rewards = AchCat.step_rewards(chain_id, step_idx)
  if type(rewards) ~= "table" or #rewards == 0 then return end
  local max_retries = 5
  for attempt = 1, max_retries do
    ensure_sheet(user_id)
    local progress, ver = read_pv(user_id)
    if not AchCat.apply_wallet_rewards(rewards, progress) then
      return
    end
    local okw, erw = pcall(function()
      write_pv(user_id, progress, ver)
    end)
    if okw then
      return
    end
    local err_text = tostring(erw)
    if string.find(err_text, "version", 1, true) == nil or attempt == max_retries then
      nk.logger_warn("achievement wallet grant failed: " .. tostring(chain_id) .. " step " .. tostring(step_idx))
      return
    end
  end
end

function M.storage_read_match3_summary_val(user_id)
  local rows = nk.storage_read({
    {
      collection = CFG.STATS_COLLECTION,
      key = CFG.STATS_KEY,
      user_id = user_id,
    },
  })

  if rows == nil or #rows == 0 then
    return {}, nil
  end
  local row = rows[1]
  local val = decode_storage(row) or {}
  return val, row.version
end

local function increment_stats_in_val(val, delta_map)
  if val == nil or delta_map == nil then return false end
  local ast = val.achievement_stats
  if type(ast) ~= "table" then
    ast = {}
    val.achievement_stats = ast
  end
  local changed = false
  for key, dv in pairs(delta_map) do
    local delta = tonumber(dv) or 0
    if delta ~= 0 and key ~= nil and key ~= "" then
      local prev = tonumber(ast[key]) or 0
      ast[key] = prev + delta
      changed = true
    end
  end
  return changed == true
end

local function persistent_merge_increment(user_id, delta_map)
  if user_id == nil or user_id == "" or not M.is_human(user_id) then
    return
  end
  if delta_map == nil then
    return
  end
  if next(delta_map) == nil then
    return
  end

  local max_retries = 5
  for attempt = 1, max_retries do
    local val, ver = M.storage_read_match3_summary_val(user_id)
    local incr_ok = increment_stats_in_val(val, delta_map)
    if not incr_ok then
      return true
    end

    val.updated_at = os.time()

    local write_ok, write_err = pcall(function()
      local wo = {
        collection = CFG.STATS_COLLECTION,
        key = CFG.STATS_KEY,
        user_id = user_id,
        value = val,
        permission_read = 1,
        permission_write = 0,
      }
      if ver ~= nil and ver ~= "" then
        wo.version = ver
      end
      nk.storage_write({ wo })
    end)

    if write_ok then
      return true
    end

    local err_text = tostring(write_err)
    if string.find(err_text, "version", 1, true) == nil or attempt == max_retries then
      nk.logger_error("achievement_persistent_merge_increment: " .. tostring(write_err))
      return false
    end
  end
  return false
end

function M.merge_persistent_stats(user_id, delta_map)
  return persistent_merge_increment(user_id, delta_map)
end

local function chain_def_by_id(chain_id)
  return AchCat.chain_by_id(chain_id)
end

local function chain_cumulative_need(chain, step_ix)
  return AchCat.cumulative_need(chain, step_ix)
end

local function is_achievement_pvp_context(state)
  if state == nil then
    return false
  end
  if state.mode ~= "pve" then
    return true
  end
  -- Турнир арены (в т.ч. бот в 1/4): технически mode=pve, но это PvP-контекст.
  if state.arena_mirror ~= nil then
    return true
  end
  return false
end

function M.flush_match_finish(state, winner, actor, opponent, action_type)
  if winner == nil or winner == "" then
    return
  end

  local am = state.arena_mirror

  local function extra_for(uid)
    local e = {}
    if uid ~= winner then
      return e
    end
    if not M.is_human(uid) then
      return e
    end

    -- «Бойня»: дуэль/турнир против людей и ботов арены; без solo PvE (шахта, mode == "pve").
    if am ~= nil then
      if tostring(am.round or "") == "final" then
        local ak = string.lower(tostring(am.kind or "smith"))
        if ak == "ore" then
          e[ACH_STAT_ORE_TOURN] = (e[ACH_STAT_ORE_TOURN] or 0) + 1
        elseif ak == "gold" then
          e[ACH_STAT_GOLD_TOURN] = (e[ACH_STAT_GOLD_TOURN] or 0) + 1
        else
          e[ACH_STAT_BLACKSMITH] = (e[ACH_STAT_BLACKSMITH] or 0) + 1
        end
      end
      e[ACH_STAT_DUEL_TRI] = (e[ACH_STAT_DUEL_TRI] or 0) + 1
    elseif state.mode ~= "pve" then
      e[ACH_STAT_DUEL_TRI] = (e[ACH_STAT_DUEL_TRI] or 0) + 1
    end

    local at_n = tonumber(action_type) or 0
    if is_achievement_pvp_context(state) and actor ~= nil and opponent ~= nil and uid == actor and uid == winner and at_n == 4 then
      local oh = tonumber(state.stats[opponent] and state.stats[opponent].hp) or 0
      if oh <= 0 then
        e[ACH_STAT_PETARD_FINISH] = (e[ACH_STAT_PETARD_FINISH] or 0) + 1
        e[ACH_STAT_PETARD_PVP_FINISH] = (e[ACH_STAT_PETARD_PVP_FINISH] or 0) + 1
      end
    end

    if state._ach_hp_was_exactly_one ~= nil and state._ach_hp_was_exactly_one[uid] == true then
      e[ACH_STAT_WIN1] = (e[ACH_STAT_WIN1] or 0) + 1
    end
    return e
  end

  local function extra_for_loser(uid)
    local e = {}
    if uid == winner then
      return e
    end
    if not M.is_human(uid) then
      return e
    end
    if am ~= nil and tostring(am.round or "") == "final" then
      e[ACH_STAT_FINAL_LOSS] = (e[ACH_STAT_FINAL_LOSS] or 0) + 1
    end
    return e
  end

  local plist = {}
  local seen = {}
  if state.players_sorted ~= nil then
    for _, u in ipairs(state.players_sorted) do
      if u ~= nil and u ~= "" and not seen[u] then
        seen[u] = true
        plist[#plist + 1] = u
      end
    end
  else
    plist[#plist + 1] = winner
    local oth = opponent
    if oth ~= nil and oth ~= "" and not seen[oth] then
      plist[#plist + 1] = oth
    end
  end

  for _, uid in ipairs(plist) do
    if M.is_human(uid) then
      M.ensure_counters(state)
      local bag = (state._ach_counters and state._ach_counters[uid]) or {}
      local ex = extra_for(uid)
      local deltas = {}

      local function merge_into(key, amt)
        if amt == nil or amt == 0 then return end
        deltas[key] = (tonumber(deltas[key]) or 0) + amt
      end

      for k, v in pairs(bag) do
        merge_into(k, v)
      end
      for k, v in pairs(ex) do
        merge_into(k, v)
      end
      for k, v in pairs(extra_for_loser(uid)) do
        merge_into(k, v)
      end

      if next(deltas) ~= nil then
        persistent_merge_increment(uid, deltas)
      end
    end
  end
end

local function stats_payload_for_client(user_id)
  local fn = deps.compute_character_display_stats
  if type(fn) ~= "function" then return nil end
  local stats = fn(user_id)
  if stats == nil then return nil end
  return {
    hp = math.max(1, math.floor(tonumber(stats.hp) or 0)),
    damage = math.max(0, math.floor(tonumber(stats.damage) or 0)),
    armor = math.max(0, math.floor(tonumber(stats.armor) or 0)),
    healing = math.max(0, math.floor(tonumber(stats.healing) or 0)),
    crit_chance = math.max(0, math.min(1, tonumber(stats.crit_chance) or 0)),
  }
end

local function progression_wallet_payload(progress)
  if type(progress) ~= "table" then return nil end
  return {
    gold = math.max(0, math.floor(tonumber(progress.gold) or 0)),
    ore = math.max(0, math.floor(tonumber(progress.ore) or 0)),
    matter = math.max(0, math.floor(tonumber(progress.matter) or 0)),
  }
end

function M.flatten_stats_for_client(ast)
  local out = {}
  if type(ast) ~= "table" then
    return out
  end
  for k, v in pairs(ast) do
    local n = tonumber(v)
    if n ~= nil and k ~= nil and k ~= "" then
      out[#out + 1] = { k = tostring(k), v = math.floor(n + 0.00001) }
    end
  end
  return out
end

function M.rpc_achievement_sync(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local ok_epoch, err_epoch = guard_epoch(user_id, payload)
    if not ok_epoch then
      return nk.json_encode({ ok = false, err = err_epoch })
    end

    ensure_sheet(user_id)

    local val, _ = M.storage_read_match3_summary_val(user_id)
    local ast = {}
    if type(val.achievement_stats) == "table" then
      ast = val.achievement_stats
    end
    local claimed = {}
    if type(val.achievement_claimed) == "table" then
      claimed = val.achievement_claimed
    end

    local flat = M.flatten_stats_for_client(ast)
    return nk.json_encode({
      ok = true,
      achievement_stats_flat = flat,
      achievement_claimed = claimed,
      updated_at = tonumber(val.updated_at) or os.time(),
    })
  end)

  if not ok then
    nk.logger_error("duel_match3_achievement_sync: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

function M.rpc_achievement_claim_step(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local ok_epoch, err_epoch = guard_epoch(user_id, payload)
    if not ok_epoch then
      return nk.json_encode({ ok = false, err = err_epoch })
    end

    ensure_sheet(user_id)

    local p = {}
    if payload ~= nil and payload ~= "" then
      p = nk.json_decode(payload) or {}
    end
    local chain_id = tostring(p.chain_id or "")
    local step_ix = tonumber(p.step_index)
    if chain_id == "" or step_ix == nil then
      return nk.json_encode({ ok = false, err = "bad_request" })
    end
    step_ix = math.floor(step_ix)

    local def = chain_def_by_id(chain_id)
    if def == nil then
      return nk.json_encode({ ok = false, err = "unknown_chain" })
    end

    local need = chain_cumulative_need(def, step_ix)
    if need == nil then
      return nk.json_encode({ ok = false, err = "bad_step" })
    end
    local key = def.counter_key

    local max_retries = 6
    for attempt = 1, max_retries do
      local val, ver = M.storage_read_match3_summary_val(user_id)

      local ast = val.achievement_stats
      if type(ast) ~= "table" then ast = {} end

      local cur = tonumber(ast[key]) or 0
      if cur < need then
        return nk.json_encode({ ok = false, err = "threshold_not_met" })
      end

      local claimed = val.achievement_claimed
      if claimed == nil or type(claimed) ~= "table" then
        claimed = {}
      end

      for j = 0, step_ix - 1 do
        local ptok = tostring(chain_id) .. ":" .. tostring(j)
        if not claim_has(claimed, ptok) then
          return nk.json_encode({ ok = false, err = "prerequisite_not_claimed" })
        end
      end

      local tok = tostring(chain_id) .. ":" .. tostring(step_ix)
      if claim_has(claimed, tok) then
        return nk.json_encode({ ok = false, err = "already_claimed" })
      end

      claimed = claim_append(claimed, tok)
      val.achievement_claimed = claimed

      val.updated_at = os.time()

      local write_ok, write_err = pcall(function()
        local wo = {
          collection = CFG.STATS_COLLECTION,
          key = CFG.STATS_KEY,
          user_id = user_id,
          value = val,
          permission_read = 1,
          permission_write = 0,
        }
        if ver ~= nil and ver ~= "" then wo.version = ver end
        nk.storage_write({ wo })
      end)

      if write_ok then
        apply_wallet_grant(user_id, chain_id, step_ix)
        local progress_after, _ = read_pv(user_id)
        local stats_payload = stats_payload_for_client(user_id)
        local wallet_payload = progression_wallet_payload(progress_after)
        local out = {
          ok = true,
          token = tok,
          chain_id = chain_id,
          step_index = step_ix,
        }
        if stats_payload ~= nil then out.stats = stats_payload end
        if wallet_payload ~= nil then out.progression = wallet_payload end
        return nk.json_encode(out)
      end

      local err_text = tostring(write_err)
      if string.find(err_text, "version", 1, true) == nil or attempt == max_retries then
        return nk.json_encode({ ok = false, err = "storage_write_failed" })
      end
    end

    return nk.json_encode({ ok = false, err = "retry_exhausted" })
  end)

  if not ok then
    nk.logger_error("duel_match3_achievement_claim_step: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

--- resolve_action: DNN (импортируем константу как поле модуля при необходимости — строка здесь же).
function M.stat_key_dnn()
  return ACH_STAT_DNN
end

return M
