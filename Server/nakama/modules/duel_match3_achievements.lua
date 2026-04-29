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
local ACH_STAT_DUEL_TRI = "slaughter.duel_tri_win"
local ACH_STAT_PETARD_FINISH = "slaughter.duel_petard_finish"

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

local ACH_EVAL_CHAINS = {
  { id = "obs.cross", key = ACH_STAT_CROSS, th = { 10, 50, 250, 500 } },
  { id = "obs.square", key = ACH_STAT_SQUARE, th = { 10, 50, 250, 500 } },
  { id = "obs.petard", key = ACH_STAT_PETARD, th = { 10, 50, 250, 500 } },
  { id = "obs.fury", key = ACH_STAT_FURY, th = { 10, 50, 250, 500 } },
  { id = "obs.shield", key = ACH_STAT_SHIELD, th = { 10, 50, 250, 500 } },
  { id = "sl.blacksmith", key = ACH_STAT_BLACKSMITH, th = { 5, 25, 100, 500 } },
  { id = "sl.duel", key = ACH_STAT_DUEL_TRI, th = { 5, 25, 100, 500 } },
  { id = "sl.petard_finish", key = ACH_STAT_PETARD_FINISH, th = { 5, 50, 100, 500 } },
  { id = "dnn.double_line", key = ACH_STAT_DNN, th = { 1 } },
  { id = "dnn.win_1hp", key = ACH_STAT_WIN1, th = { 1 } },
}

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

local function chain_def_by_id(chain_id)
  local cid = tostring(chain_id or "")
  for _, ch in ipairs(ACH_EVAL_CHAINS) do
    if tostring(ch.id) == cid then
      return ch
    end
  end
  return nil
end

local function chain_cumulative_need(thlist, step_ix)
  if type(thlist) ~= "table" then return nil end
  local ix = tonumber(step_ix)
  if ix == nil then return nil end
  ix = math.floor(ix)
  if ix < 0 then return nil end
  local sum = 0
  for i = 0, ix do
    local v = tonumber(thlist[i + 1])
    if v == nil then return nil end
    sum = sum + math.max(0, v)
  end
  return sum
end

local function apply_wallet_grant(user_id, chain_id, step_idx)
  if user_id == nil or user_id == "" then return end
  local max_retries = 5
  for attempt = 1, max_retries do
    ensure_sheet(user_id)
    local progress, ver = read_pv(user_id)
    local chain = tostring(chain_id or "")
    local step = tonumber(step_idx) or 0
    if chain == "sl.blacksmith" then
      local gmap = { 1000, 5000, 15000, 0 }
      local ga = tonumber(gmap[step + 1]) or 0
      if ga > 0 then
        progress.gold = math.max(0, tonumber(progress.gold) or 0) + ga
      end
    elseif chain == "sl.duel" then
      local omap = { 1000, 5000, 10000, 0 }
      local oa = tonumber(omap[step + 1]) or 0
      if oa > 0 then
        progress.ore = math.max(0, tonumber(progress.ore) or 0) + oa
      end
    elseif chain == "sl.petard_finish" then
      local mmap = { 5, 50, 100, 500 }
      local ma = tonumber(mmap[step + 1]) or 0
      if ma > 0 then
        progress.matter = math.max(0, tonumber(progress.matter) or 0) + ma
      end
    else
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
      nk.logger_warn("achievement wallet grant failed: " .. chain .. " step " .. tostring(step))
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
        e[ACH_STAT_BLACKSMITH] = (e[ACH_STAT_BLACKSMITH] or 0) + 1
      end
      e[ACH_STAT_DUEL_TRI] = (e[ACH_STAT_DUEL_TRI] or 0) + 1
    elseif state.mode ~= "pve" then
      e[ACH_STAT_DUEL_TRI] = (e[ACH_STAT_DUEL_TRI] or 0) + 1
    end

    local at_n = tonumber(action_type) or 0
    if state.mode ~= "pve" and actor ~= nil and opponent ~= nil and uid == actor and uid == winner and at_n == 4 then
      local oh = tonumber(state.stats[opponent] and state.stats[opponent].hp) or 0
      if oh <= 0 then
        e[ACH_STAT_PETARD_FINISH] = (e[ACH_STAT_PETARD_FINISH] or 0) + 1
      end
    end

    if state._ach_hp_was_exactly_one ~= nil and state._ach_hp_was_exactly_one[uid] == true then
      e[ACH_STAT_WIN1] = (e[ACH_STAT_WIN1] or 0) + 1
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

      if next(deltas) ~= nil then
        persistent_merge_increment(uid, deltas)
      end
    end
  end
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

    local thlist = def.th or {}
    local need = chain_cumulative_need(thlist, step_ix)
    if need == nil then
      return nk.json_encode({ ok = false, err = "bad_step" })
    end
    local key = def.key

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
        return nk.json_encode({ ok = true, token = tok, chain_id = chain_id, step_index = step_ix })
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
