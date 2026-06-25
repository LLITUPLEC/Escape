--[[
  Каталог достижений: чтение duel_match3_achievement_defs/catalog из Storage,
  fallback для dev без записи, награды, пороги claim, RPC для клиента.
]]
local nk = require("nakama")

local function runtime_lua_require_nested(name_nested, name_root)
  local ok, mod = pcall(require, name_nested)
  if ok and mod ~= nil then return mod end
  return require(name_root)
end

local CFG = runtime_lua_require_nested("modules.duel_match3_config", "duel_match3_config")
local Fallback = runtime_lua_require_nested("modules.duel_match3_achievement_catalog_fallback", "duel_match3_achievement_catalog_fallback")

local M = {}

local deps = {}
local _cache = nil
local _cache_at = 0
local _cache_source = nil
local _chain_index = nil

function M.configure(d)
  if type(d) == "table" then deps = d end
end

local function decode_storage(row)
  local fn = deps.decode_storage_value
  if type(fn) == "function" then return fn(row) end
  if row == nil then return nil end
  if type(row.value) == "table" then return row.value end
  if type(row.value) == "string" and row.value ~= "" then
    local ok, v = pcall(nk.json_decode, row.value)
    if ok then return v end
  end
  return nil
end

local function chains_array_len(chains)
  if type(chains) ~= "table" then return 0 end
  local n = #chains
  if n > 0 then return n end
  local c = 0
  for _ in pairs(chains) do
    c = c + 1
  end
  return c
end

local function chains_to_array(chains)
  if type(chains) ~= "table" then return nil end
  if #chains > 0 then return chains end
  local arr = {}
  for k, v in pairs(chains) do
    if type(v) == "table" then
      if v.id == nil or v.id == "" then
        v.id = tostring(k)
      end
      arr[#arr + 1] = v
    end
  end
  if #arr == 0 then return nil end
  table.sort(arr, function(a, b)
    return tostring(a.id or "") < tostring(b.id or "")
  end)
  return arr
end

local function normalize_catalog_val(val)
  if type(val) ~= "table" then return nil end
  local chains = chains_to_array(val.chains)
  if chains == nil or chains_array_len(chains) == 0 then
    return nil
  end
  val.chains = chains
  if val.schema_version == nil then
    val.schema_version = tonumber(val.version) or 1
  end
  if val.updated_at == nil or tonumber(val.updated_at) == 0 then
    val.updated_at = tonumber(val.updated_at) or os.time()
  end
  return val
end

local function read_from_storage()
  local uid = CFG.ACHIEVEMENT_DEFS_STORAGE_USER_ID
  if uid == nil or uid == "" then return nil end
  local ok, rows = pcall(function()
    return nk.storage_read({
      {
        collection = CFG.ACHIEVEMENT_DEFS_COLLECTION,
        key = CFG.ACHIEVEMENT_DEFS_KEY,
        user_id = uid,
      },
    })
  end)
  if not ok then
    nk.logger_warn("achievement_catalog storage_read error: " .. tostring(rows))
    return nil
  end
  if rows == nil or #rows == 0 then
    nk.logger_warn("achievement_catalog storage miss: collection="
      .. tostring(CFG.ACHIEVEMENT_DEFS_COLLECTION)
      .. " key=" .. tostring(CFG.ACHIEVEMENT_DEFS_KEY)
      .. " user_id=" .. tostring(uid))
    return nil
  end
  local val = decode_storage(rows[1])
  val = normalize_catalog_val(val)
  if val == nil then
    nk.logger_warn("achievement_catalog storage invalid value for user_id=" .. tostring(uid))
  end
  return val
end

local function normalize_chain(ch)
  if type(ch) ~= "table" then return nil end
  local id = tostring(ch.id or "")
  if id == "" then return nil end
  local key = tostring(ch.counter_key or "")
  if key == "" then return nil end
  local steps = ch.steps
  if type(steps) ~= "table" or #steps == 0 then return nil end
  local th = {}
  for i = 1, #steps do
    local st = steps[i]
    if type(st) ~= "table" then return nil end
    th[i] = math.max(0, math.floor(tonumber(st.threshold_delta) or 0))
  end
  return {
    id = id,
    category = tostring(ch.category or ""),
    title_ru = tostring(ch.title_ru or id),
    counter_key = key,
    threshold_mode = tostring(ch.threshold_mode or "cumulative_delta"),
    event = ch.event,
    steps = steps,
  }
end

local function rebuild_index(catalog)
  _chain_index = {}
  if catalog == nil or type(catalog.chains) ~= "table" then return end
  for _, ch in ipairs(catalog.chains) do
    local n = normalize_chain(ch)
    if n ~= nil then
      _chain_index[n.id] = n
    end
  end
end

function M.invalidate_cache()
  _cache = nil
  _cache_at = 0
  _cache_source = nil
  _chain_index = nil
end

function M.get_catalog()
  local ttl = tonumber(CFG.ACHIEVEMENT_CATALOG_CACHE_TTL_SEC) or 30
  local now = os.time()
  if _cache ~= nil and (now - _cache_at) < ttl then
    return _cache, _cache_source or "cache"
  end
  local cat = read_from_storage()
  local source = "storage"
  if cat == nil then
    cat = Fallback.get()
    source = "fallback"
    if cat == nil then
      cat = { schema_version = 1, categories = {}, chains = {} }
      source = "empty"
    else
      nk.logger_warn("achievement_catalog: using Lua fallback (storage unreadable or missing)")
    end
  end
  _cache = cat
  _cache_at = now
  _cache_source = source
  rebuild_index(cat)
  return cat, source
end

function M.chain_by_id(chain_id)
  M.get_catalog()
  if _chain_index == nil then return nil end
  return _chain_index[tostring(chain_id or "")]
end

function M.all_chains()
  local cat = M.get_catalog()
  local out = {}
  if cat == nil or type(cat.chains) ~= "table" then return out end
  for _, ch in ipairs(cat.chains) do
    local n = normalize_chain(ch)
    if n ~= nil then out[#out + 1] = n end
  end
  return out
end

--- Совместимость с legacy ACH_EVAL_CHAINS: { id, key, th }.
function M.eval_chain_list()
  local out = {}
  for _, ch in ipairs(M.all_chains()) do
    local th = {}
    for i = 1, #ch.steps do
      th[i] = math.max(0, math.floor(tonumber(ch.steps[i].threshold_delta) or 0))
    end
    out[#out + 1] = { id = ch.id, key = ch.counter_key, th = th }
  end
  return out
end

function M.cumulative_need(chain, step_ix)
  if chain == nil then return nil end
  local steps = chain.steps
  if type(steps) ~= "table" then return nil end
  local ix = tonumber(step_ix)
  if ix == nil then return nil end
  ix = math.floor(ix)
  if ix < 0 or ix >= #steps then return nil end
  local mode = tostring(chain.threshold_mode or "cumulative_delta")
  if mode == "absolute_total" then
    return math.max(0, math.floor(tonumber(steps[ix + 1].threshold_delta) or 0))
  end
  local sum = 0
  for i = 1, ix + 1 do
    sum = sum + math.max(0, math.floor(tonumber(steps[i].threshold_delta) or 0))
  end
  return sum
end

function M.step_rewards(chain_id, step_ix)
  local ch = M.chain_by_id(chain_id)
  if ch == nil then return nil end
  local ix = tonumber(step_ix)
  if ix == nil then return nil end
  ix = math.floor(ix) + 1
  local st = ch.steps[ix]
  if st == nil then return nil end
  return st.rewards
end

local WALLET_TYPES = { gold = true, ore = true, matter = true }

function M.apply_rewards_to_combat_bonuses(rewards, b)
  if type(rewards) ~= "table" or b == nil then return end
  for _, r in ipairs(rewards) do
    if type(r) == "table" then
      local t = tostring(r.type or "")
      local v = tonumber(r.value) or 0
      if t == "hp_flat" then b.flat_hp = (b.flat_hp or 0) + v
      elseif t == "damage_flat" then b.flat_dmg = (b.flat_dmg or 0) + v
      elseif t == "armor_flat" then b.flat_armor = (b.flat_armor or 0) + v
      elseif t == "heal_flat" then b.flat_heal = (b.flat_heal or 0) + v
      elseif t == "crit_flat" then b.flat_crit = (b.flat_crit or 0) + v
      elseif t == "hp_pct" then b.pct_hp = (b.pct_hp or 0) + v
      elseif t == "damage_pct" then b.pct_dmg = (b.pct_dmg or 0) + v
      elseif t == "armor_pct" then b.pct_armor = (b.pct_armor or 0) + v
      elseif t == "heal_pct" then b.pct_heal = (b.pct_heal or 0) + v
      end
    end
  end
end

function M.apply_wallet_rewards(rewards, progress)
  if type(rewards) ~= "table" or type(progress) ~= "table" then return false end
  local changed = false
  for _, r in ipairs(rewards) do
    if type(r) == "table" then
      local t = tostring(r.type or "")
      local v = math.max(0, math.floor(tonumber(r.value) or 0))
      if v > 0 and WALLET_TYPES[t] then
        progress[t] = math.max(0, tonumber(progress[t]) or 0) + v
        changed = true
      end
    end
  end
  return changed
end

function M.rpc_achievement_catalog_get(ctx, payload)
  local ok, result = pcall(function()
    if payload ~= nil and payload ~= "" then
      local p = nk.json_decode(payload)
      if type(p) == "table" and p.force_refresh == true then
        M.invalidate_cache()
      end
    end
    local cat, source = M.get_catalog()
    return nk.json_encode({
      ok = true,
      catalog_source = source,
      schema_version = tonumber(cat.schema_version) or 1,
      updated_at = tonumber(cat.updated_at) or 0,
      categories = cat.categories or {},
      chains = cat.chains or {},
    })
  end)
  if not ok then
    nk.logger_error("achievement_catalog_get: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

return M
