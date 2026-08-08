-- Глобальная аномалия сервера (Storage → бафы PvE match3).
-- Вынесено из duel_match3.lua из‑за лимита ~200 local в одном чанке Lua.
return function(deps)
  local nk = deps.nk
  local CFG = deps.CFG
  local decode_storage_value = deps.decode_storage_value

  local _cache_doc = nil
  local _cache_expiry = nil
  local _cache_t = 0

  local function storage_row_time_unix(row)
    if row == nil then return nil end
    local u = row.update_time or row.UpdateTime or row.create_time or row.CreateTime
    if u == nil then return nil end
    if type(u) == "number" then
      if u > 20000000000 then return math.floor(u / 1000) end
      if u > 1000000000000 then return math.floor(u / 1000000) end
      return math.floor(u)
    end
    if type(u) == "string" then
      local y, mo, d, h, mi, se = u:match("^(%d%d%d%d)%-(%d%d)%-(%d%d)[T ](%d%d):(%d%d):(%d%d)")
      if y then
        return os.time({
          year = tonumber(y), month = tonumber(mo), day = tonumber(d),
          hour = tonumber(h), min = tonumber(mi), sec = tonumber(se),
          isdst = false
        })
      end
    end
    return nil
  end

  local function compute_aura_expiry_unix(doc, row)
    if doc == nil then return nil end
    local e = tonumber(doc.ends_at_unix) or tonumber(doc.ends_at)
    if e ~= nil and e > 0 then return math.floor(e) end
    local dur_h = tonumber(doc.duration_hours)
    if dur_h == nil or dur_h <= 0 then return nil end
    local start_u = tonumber(doc.started_at_unix) or tonumber(doc.anchor_unix) or storage_row_time_unix(row) or os.time()
    return math.floor(start_u + dur_h * 3600 + 0.5)
  end

  local M = {}

  function M.get_active()
    local now = os.time()
    local ttl = math.max(5, tonumber(CFG.SERVER_AURA_CACHE_TTL_SECONDS) or 30)
    if _cache_doc ~= nil and (now - _cache_t) < ttl then
      if _cache_expiry ~= nil and now > _cache_expiry then
        return nil
      end
      return _cache_doc
    end

    local ok_read, rows = pcall(function()
      return nk.storage_read({
        {
          collection = CFG.SERVER_AURA_COLLECTION,
          key = CFG.SERVER_AURA_KEY,
          user_id = CFG.SERVER_AURA_STORAGE_USER_ID,
        },
      })
    end)
    _cache_t = now
    _cache_doc = nil
    _cache_expiry = nil

    if not ok_read or rows == nil or #rows == 0 then
      return nil
    end

    local row = rows[1]
    local doc = decode_storage_value(row) or {}
    if doc.active == false or doc.enabled == false then
      return nil
    end

    local exp = compute_aura_expiry_unix(doc, row)
    if exp ~= nil and now > exp then
      return nil
    end

    _cache_expiry = exp
    _cache_doc = doc
    return doc
  end

  function M.xp_multiplier(aura)
    if aura == nil then return 1 end
    local p = tonumber(aura.xp_bonus_pct) or tonumber(aura.xp_pct) or 0
    return math.max(0, 1 + p / 100)
  end

  function M.apply_to_pve_reward_xp(reward_xp, aura)
    local m = M.xp_multiplier(aura)
    if m == 1 then return reward_xp end
    return math.max(0, math.ceil((tonumber(reward_xp) or 0) * m))
  end

  --- mine_respawn_wait_pct: +50 → таймер короче в 2 раза (10→5 мин); −50 → длиннее (10→15 мин).
  function M.mine_respawn_duration_seconds(base_seconds, aura)
    local b = math.max(1, math.floor(tonumber(base_seconds) or 600))
    if aura == nil then return b end
    local p = tonumber(aura.mine_respawn_wait_pct) or tonumber(aura.mine_respawn_pct) or 0
    if p == 0 then return b end
    local mult = 1 - p / 100
    mult = math.max(0.05, math.min(20, mult))
    return math.max(10, math.floor(b * mult + 0.5))
  end

  --- crit_pct: аддитивные пункты шанса крита (15 → +0.15 к base_crit, в UI «+15%»).
  --- Остальные *_pct — множители: 20 → ×1.20.
  function M.apply_to_pve_player_stats(stats, aura)
    if stats == nil or aura == nil then return end
    local function pct_mul(p)
      p = tonumber(p) or 0
      return math.max(-0.95, 1 + p / 100)
    end
    local all_ex_crit = pct_mul(aura.all_stats_pct or aura.stats_bonus_pct)
    local m_hp = all_ex_crit * pct_mul(aura.hp_pct)
    local m_dmg = all_ex_crit * pct_mul(aura.damage_pct)
    local m_arm = all_ex_crit * pct_mul(aura.armor_pct)
    local m_heal = all_ex_crit * pct_mul(aura.healing_pct)
    local crit_add = (tonumber(aura.crit_pct) or 0) / 100

    local max_hp = math.max(1, math.floor((tonumber(stats.max_hp) or CFG.MAX_HP) * m_hp + 0.5))
    local hp = math.floor((tonumber(stats.hp) or max_hp) * m_hp + 0.5)
    stats.max_hp = max_hp
    stats.hp = math.min(max_hp, math.max(1, hp))
    stats.base_damage = math.max(0, math.floor((tonumber(stats.base_damage) or 0) * m_dmg + 0.5))
    stats.base_armor = math.max(0, math.floor((tonumber(stats.base_armor) or 0) * m_arm + 0.5))
    stats.base_heal = math.floor((tonumber(stats.base_heal) or 0) * m_heal + 0.5)
    stats.base_crit = math.max(0, math.min(1, (tonumber(stats.base_crit) or 0) + crit_add))
    stats.initial_hp = stats.max_hp
  end

  function M.rpc_get(ctx, payload)
    local aura = M.get_active()
    if aura == nil then
      return nk.json_encode({ ok = true, active = false })
    end
    local exp = _cache_expiry
    return nk.json_encode({
      ok = true,
      active = true,
      title = tostring(aura.title or ""),
      description = tostring(aura.description or ""),
      endsAtUnix = exp or 0,
      allStatsPct = tonumber(aura.all_stats_pct) or tonumber(aura.stats_bonus_pct) or 0,
      critPct = tonumber(aura.crit_pct) or 0,
      hpPct = tonumber(aura.hp_pct) or 0,
      damagePct = tonumber(aura.damage_pct) or 0,
      armorPct = tonumber(aura.armor_pct) or 0,
      healingPct = tonumber(aura.healing_pct) or 0,
      xpBonusPct = tonumber(aura.xp_bonus_pct) or tonumber(aura.xp_pct) or 0,
      mineRespawnWaitPct = tonumber(aura.mine_respawn_wait_pct) or tonumber(aura.mine_respawn_pct) or 0,
      durationHours = tonumber(aura.duration_hours) or 0,
    })
  end

  return M
end
