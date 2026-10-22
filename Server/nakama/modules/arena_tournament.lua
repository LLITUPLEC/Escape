local nk = require("nakama")

local function runtime_lua_require(name_nested, name_root)
  local ok, mod = pcall(require, name_nested)
  if ok and mod ~= nil then return mod end
  return require(name_root)
end

local FakeOnline = runtime_lua_require("modules.duel_fake_online", "duel_fake_online")
local CFG = runtime_lua_require("modules.duel_match3_config", "duel_match3_config")

--- Турнир арены (отдельный chunk — не упирается в лимит локалей duel_match3.lua).
return function(deps)
  local try_match_create = deps.try_match_create
  local make_bot_user_id = deps.make_bot_user_id
  local guard_read_metadata_epoch = deps.guard_read_metadata_epoch
  local guard_assert_client_epoch_matches = deps.guard_assert_client_epoch_matches
  local read_pve_progress = deps.read_pve_progress
  local read_character_sheet = deps.read_character_sheet
  local ensure_sheet_inventory_counts = deps.ensure_sheet_inventory_counts
  local inventory_remove_def_total = deps.inventory_remove_def_total
  local inventory_try_add = deps.inventory_try_add

local ARENA_QUEUE_MAX = 8
local ARENA_COUNTDOWN_SEC = 20
local ARENA_QUEUE_BET_INGOTS = 50

-- Турнир руды / золота: несколько ставок (очередь лочится на выбран bet_tier, как у кузнеца).
-- Ключ "fixed" — legacy-совместимость со старым клиентом (мапится на базовую ставку).
local ARENA_ORE_TIERS = {
  ["500"] = { bet = 500, prize = 2500 },
  ["fixed"] = { bet = 500, prize = 2500 },
  ["2000"] = { bet = 2000, prize = 10000 },
}
local ARENA_GOLD_TIERS = {
  ["600"] = { bet = 600, prize = 3000 },
  ["fixed"] = { bet = 600, prize = 3000 },
  ["2400"] = { bet = 2400, prize = 12000 },
}

local ARENA_KIND_SMITH = "smith" -- match3Arena (слитки)
local ARENA_KIND_ORE = "ore"     -- match3Arena_Ore
local ARENA_KIND_GOLD = "gold"   -- match3Arena_Gold

local function normalize_kind(k)
  local s = string.lower(tostring(k or ""))
  if s == ARENA_KIND_ORE then return ARENA_KIND_ORE end
  if s == ARENA_KIND_GOLD then return ARENA_KIND_GOLD end
  return ARENA_KIND_SMITH
end

local function normalize_resource_bet_tier(kind, bet)
  local b = string.lower(tostring(bet or ""))
  if kind == ARENA_KIND_ORE then
    if ARENA_ORE_TIERS[b] == nil then
      return "500"
    end
    if b == "fixed" then
      return "500"
    end
    return b
  end
  if kind == ARENA_KIND_GOLD then
    if ARENA_GOLD_TIERS[b] == nil then
      return "600"
    end
    if b == "fixed" then
      return "600"
    end
    return b
  end
  return b
end

local function resource_tier_cfg(kind, bet_tier)
  local tier = normalize_resource_bet_tier(kind, bet_tier)
  if kind == ARENA_KIND_ORE then
    return ARENA_ORE_TIERS[tier] or ARENA_ORE_TIERS["500"], tier
  end
  if kind == ARENA_KIND_GOLD then
    return ARENA_GOLD_TIERS[tier] or ARENA_GOLD_TIERS["600"], tier
  end
  return nil, tier
end

local function arena_shuffle_inplace(arr)
  for i = #arr, 2, -1 do
    local j = math.random(i)
    arr[i], arr[j] = arr[j], arr[i]
  end
end

local function arena_collect_used_displays(entries)
  local used = {}
  for _, e in ipairs(entries or {}) do
    if e ~= nil and e.display ~= nil and e.display ~= "" then
      used[tostring(e.display)] = true
    end
  end
  return used
end

--- Имя бота из текущего eligible-каталога (всегда-ники + «онлайн» из пула).
local function arena_pick_bot_display(used)
  used = used or {}
  local catalog = FakeOnline.arena_eligible_bot_names(os.time())
  local free = {}
  for i = 1, #catalog do
    local n = catalog[i]
    if used[n] ~= true then
      free[#free + 1] = n
    end
  end
  if #free > 0 then
    local name = free[math.random(1, #free)]
    used[name] = true
    return name
  end
  -- Каталог исчерпан (ночь / мало pad) — уникальный суффикс, без оффлайн-пула.
  for _ = 1, 32 do
    local name = "Боец_" .. string.sub(nk.uuid_v4(), 1, 8)
    if used[name] ~= true then
      used[name] = true
      return name
    end
  end
  local fallback = "Боец_" .. tostring(os.time())
  used[fallback] = true
  return fallback
end

--- Страховка: перед стартом турнира у ботов не должно быть одинаковых display.
local function arena_dedupe_bot_displays(entries, cooldowns)
  local used = {}
  for name, at in pairs(cooldowns or {}) do
    if (tonumber(at) or 0) + (tonumber(FakeOnline.POOL_TOURNAMENT_COOLDOWN_SEC) or 3600) > os.time() then
      used[name] = true
    end
  end
  for _, e in ipairs(entries or {}) do
    if e ~= nil and e.bot ~= true and e.display ~= nil and e.display ~= "" then
      used[tostring(e.display)] = true
    end
  end
  for _, e in ipairs(entries or {}) do
    if e ~= nil and e.bot == true then
      e.display = arena_pick_bot_display(used)
    end
  end
end

local function arena_bet_to_ingot_def(bet)
  local b = string.lower(tostring(bet or ""))
  if b == "blue" then
    return "ingot_blue"
  end
  if b == "purple" then
    return "ingot_purple"
  end
  return "ingot_green"
end

local function arena_final_prize_ore_gold(bet)
  local b = string.lower(tostring(bet or ""))
  if b == "blue" then
    return 600, 600
  end
  if b == "purple" then
    return 1200, 1200
  end
  return 300, 300
end

local function arena_make_bot_uid()
  -- Must match duel_match3 bot_user_id format (zz-bot- prefix) so that:
  -- - winner_uid matches tournament participant uid
  -- - mirror_commit maps HP correctly for bot side
  return make_bot_user_id("arena_" .. nk.uuid_v4())
end

local function final_prize_for_kind_and_bet(kind, bet_tier)
  local k = normalize_kind(kind)
  if k == ARENA_KIND_ORE or k == ARENA_KIND_GOLD then
    local cfg = resource_tier_cfg(k, bet_tier)
    local prize = cfg ~= nil and (tonumber(cfg.prize) or 0) or 0
    if k == ARENA_KIND_ORE then
      return 0, prize
    end
    return prize, 0
  end
  local ore_g, gold_g = arena_final_prize_ore_gold(bet_tier)
  return tonumber(gold_g) or 0, tonumber(ore_g) or 0
end

--- Множитель финальной награды по сумме угаданных/проигранных side-bet: ±0.2 за каждую.
local function arena_bet_prize_multiplier(bet_score)
  local mult = 1 + (tonumber(bet_score) or 0) * 0.2
  if mult < 0 then
    return 0
  end
  return mult
end

local function arena_apply_bet_score_to_prize(kind, bet_tier, bet_score)
  local g, o = final_prize_for_kind_and_bet(kind, bet_tier)
  local mult = arena_bet_prize_multiplier(bet_score)
  return math.floor((tonumber(g) or 0) * mult + 0.5), math.floor((tonumber(o) or 0) * mult + 0.5)
end

local function arena_open_side_bet_round(T)
  if T == nil or T.phase ~= "countdown" then
    return nil
  end
  local nr = tostring(T.next_round or "")
  if nr == "final" then
    return nil
  end
  if nr == "sf" then
    return "sf"
  end
  return "qf"
end

--- Раунд, чьи ставки ещё лежат в side_bets (открытый countdown или текущий бой).
local function arena_active_side_bet_round(T)
  if T == nil then
    return nil
  end
  local open = arena_open_side_bet_round(T)
  if open ~= nil then
    return open
  end
  local ph = tostring(T.phase or "")
  if ph == "qf" or ph == "sf" then
    return ph
  end
  return nil
end

local function arena_ensure_side_bet_tables(T)
  if type(T.side_bets) ~= "table" then
    T.side_bets = {}
  end
  if type(T.bet_score) ~= "table" then
    T.bet_score = {}
  end
  if type(T.bet_wins) ~= "table" then
    T.bet_wins = {}
  end
  if type(T.bet_losses) ~= "table" then
    T.bet_losses = {}
  end
end

local function arena_expected_side_bets_for_finalist(bracket_size)
  local n = tonumber(bracket_size) or ARENA_QUEUE_MAX
  if n < 4 then
    n = ARENA_QUEUE_MAX
  end
  local expected = 0
  local round_pairs = math.floor(n / 2)
  while round_pairs >= 2 do
    expected = expected + (round_pairs - 1)
    round_pairs = math.floor(round_pairs / 2)
  end
  return expected
end

local function arena_count_side_bets_placed(T, uid)
  arena_ensure_side_bet_tables(T)
  local bag = T.side_bets[uid]
  if type(bag) ~= "table" then
    return 0
  end
  local n = 0
  for _, picks in pairs(bag) do
    if type(picks) == "table" then
      for _, pick_uid in pairs(picks) do
        if pick_uid ~= nil and pick_uid ~= "" then
          n = n + 1
        end
      end
    end
  end
  return n
end

local function arena_pack_my_side_bets(T, user_id, rk)
  local out = {}
  if T == nil or rk == nil or rk == "" then
    return out
  end
  arena_ensure_side_bet_tables(T)
  local bag = T.side_bets[user_id]
  local picks = bag ~= nil and bag[rk] or nil
  if type(picks) ~= "table" then
    return out
  end
  local plist = T[rk]
  for slot_key, pick_uid in pairs(picks) do
    local slot = tonumber(slot_key) or 0
    local side = ""
    if plist ~= nil and plist[slot] ~= nil then
      if tostring(plist[slot].uid_a) == tostring(pick_uid) then
        side = "a"
      elseif tostring(plist[slot].uid_b) == tostring(pick_uid) then
        side = "b"
      end
    end
    out[#out + 1] = {
      round = rk,
      slot = slot,
      side = side,
      pick_uid = tostring(pick_uid or ""),
    }
  end
  return out
end

local function arena_pair_row_from_slots(a, b)
  return {
    uid_a = a.uid,
    uid_b = b.uid,
    display_a = a.display,
    display_b = b.display,
    bot_a = a.bot == true,
    bot_b = b.bot == true,
    hp_a = 150,
    hp_b = 150,
    status = "pending",
    match_id = "",
    winner_uid = "",
  }
end

local function arena_build_round_from_slots(slots)
  local pairs = {}
  local n = math.floor(#slots / 2)
  for i = 1, n do
    pairs[#pairs + 1] = arena_pair_row_from_slots(slots[(i - 1) * 2 + 1], slots[(i - 1) * 2 + 2])
  end
  return pairs
end

-- All authoritative state is shared across runtime VMs. A single storage batch
-- commits the state change and its money/stat effects, or commits nothing.
local SYSTEM = "00000000-0000-0000-0000-000000000000"
local TOURNAMENTS = "arena_tournament"
local QUEUES = "arena_queue"
local RETRIES = 12
local function read_row(uid, collection, key)
  local rows = nk.storage_read({ { user_id = uid, collection = collection, key = key } })
  local row = rows and rows[1]
  if row == nil then return nil, "*" end
  local value = row.value
  if type(value) == "string" then value = nk.json_decode(value) end
  if type(value) ~= "table" or row.version == nil or row.version == "" then
    error("arena invalid storage row: " .. collection .. "/" .. key)
  end
  return value, row.version
end
local function write_row(uid, collection, key, value, version, readable)
  return { user_id = uid, collection = collection, key = key, value = value,
    version = version or "*", permission_read = readable and 1 or 0, permission_write = 0 }
end
local function conflict(err)
  return string.find(string.lower(tostring(err)), "version", 1, true) ~= nil
end
local function transact(collection, key, create, mutate)
  for attempt = 1, RETRIES do
    local value, version = read_row(SYSTEM, collection, key)
    if value == nil then
      if create == nil then return nil, "missing" end
      value = create()
    end
    local writes = {}
    local changed, result = mutate(value, writes)
    if not changed then return value, result end
    table.insert(writes, 1, write_row(SYSTEM, collection, key, value, version))
    local ok, err = pcall(nk.storage_write, writes)
    if ok then return value, result end
    if not conflict(err) or attempt == RETRIES then error(err) end
  end
  error("arena transaction retry limit")
end
local function load_tournament(tid)
  if tid == nil or tid == "" then return nil end
  return read_row(SYSTEM, TOURNAMENTS, tostring(tid))
end
local function summary_write(writes, uid, mutate)
  -- Coalesce all deltas for one user into one versioned object in the batch.
  for _, wo in ipairs(writes) do
    if wo.collection == CFG.STATS_COLLECTION and wo.key == CFG.STATS_KEY and wo.user_id == uid then
      mutate(wo.value)
      return
    end
  end
  local value, version = read_row(uid, CFG.STATS_COLLECTION, CFG.STATS_KEY)
  value = value or {}
  mutate(value)
  value.updated_at = os.time()
  writes[#writes + 1] = write_row(uid, CFG.STATS_COLLECTION, CFG.STATS_KEY, value, version, true)
end
local function achievement(writes, uid, key, delta)
  summary_write(writes, uid, function(value)
    value.achievement_stats = type(value.achievement_stats) == "table" and value.achievement_stats or {}
    value.achievement_stats[key] = (tonumber(value.achievement_stats[key]) or 0) + delta
  end)
end
local function uid_is_bot(T, uid)
  return T.parts[uid] == nil or T.parts[uid].bot == true
end
local function active_humans(T)
  local n = 0
  T.eliminated = type(T.eliminated) == "table" and T.eliminated or {}
  for uid, meta in pairs(T.parts or {}) do
    if not meta.bot and T.eliminated[uid] ~= true then n = n + 1 end
  end
  return n
end
local function abort_if_empty(T)
  if T.phase ~= "done" and active_humans(T) == 0 then
    T.phase, T.done_at, T.aborted = "done", os.time(), true
    return true
  end
  return false
end
local function build_progress_write(uid, progress, version)
  if type(deps.build_pve_progress_write) ~= "function" then error("arena progress write builder missing") end
  return deps.build_pve_progress_write(uid, progress, version)
end
local function build_sheet_write(uid, sheet)
  if type(deps.build_character_sheet_write) ~= "function" then error("arena sheet write builder missing") end
  return deps.build_character_sheet_write(uid, sheet)
end
local function all_done(T, rk)
  local list = T[rk]
  if type(list) ~= "table" or #list == 0 then return false end
  for _, pr in ipairs(list) do if pr.status ~= "done" then return false end end
  return true
end
local function resolve_bets(T, rk, writes)
  arena_ensure_side_bet_tables(T)
  T.bets_resolved = type(T.bets_resolved) == "table" and T.bets_resolved or {}
  if T.bets_resolved[rk] then return end
  for uid, bag in pairs(T.side_bets) do
    local picks = type(bag) == "table" and bag[rk] or nil
    if type(picks) == "table" and not uid_is_bot(T, uid) then
      for slot, picked in pairs(picks) do
        local pr = T[rk][tonumber(slot)]
        if pr and picked ~= "" and pr.winner_uid ~= nil and pr.winner_uid ~= "" then
          local won = pr.winner_uid == picked
          T.bet_score[uid] = (tonumber(T.bet_score[uid]) or 0) + (won and 1 or -1)
          local counters = won and T.bet_wins or T.bet_losses
          counters[uid] = (tonumber(counters[uid]) or 0) + 1
          achievement(writes, uid, won and "slaughter.arena_bets_won" or "slaughter.arena_bets_lost", 1)
        end
      end
    end
  end
  T.bets_resolved[rk] = true
end
local function advance(T, rk, writes)
  if T.phase == "done" or not all_done(T, rk) then return end
  if abort_if_empty(T) then return end
  if rk == "qf" or rk == "sf" then
    local slots = {}
    for _, pr in ipairs(T[rk]) do
      local uid, meta = pr.winner_uid, T.parts[pr.winner_uid]
      if not meta then error("arena winner missing from parts") end
      slots[#slots + 1] = { uid = uid, display = meta.display, bot = meta.bot == true }
    end
    if #slots ~= (rk == "qf" and 4 or 2) then error("arena invalid bracket size") end
    resolve_bets(T, rk, writes)
    arena_shuffle_inplace(slots)
    local next_round = rk == "qf" and "sf" or "final"
    T[next_round] = arena_build_round_from_slots(slots)
    T.qf = {}
    if rk == "sf" then T.sf = {} end
    T.phase, T.next_round = "countdown", next_round
    T.countdown_until, T.next_slots = os.time() + ARENA_COUNTDOWN_SEC, nil
    return
  end
  if rk ~= "final" then return end
  local winner = T.final[1].winner_uid
  if winner and winner ~= "" and not uid_is_bot(T, winner) then
    arena_ensure_side_bet_tables(T)
    local g, o = arena_apply_bet_score_to_prize(T.kind, T.bet_tier, T.bet_score[winner] or 0)
    local progress, version = read_pve_progress(winner)
    progress.gold = math.max(0, (tonumber(progress.gold) or 0) + g)
    progress.ore = math.max(0, (tonumber(progress.ore) or 0) + o)
    writes[#writes + 1] = build_progress_write(winner, progress, version)
    local expected = arena_expected_side_bets_for_finalist(T.bracket_size)
    if (tonumber(T.bet_wins[winner]) or 0) == expected
      and (tonumber(T.bet_losses[winner]) or 0) == 0
      and arena_count_side_bets_placed(T, winner) == expected then
      achievement(writes, winner, "dnn.arena_perfect_bets_win", 1)
    end
    T.reward_receipt = { user_id = winner, gold = g, ore = o, granted_at = os.time() }
  end
  -- Keep a terminal receipt: no stale runtime snapshot may resurrect a final or
  -- reissue its prize. The receipt and progress update share the same commit.
  T.phase, T.done_at, T.next_round = "done", os.time(), nil
end
local function pair_for_state(T, state)
  local am = state and state.arena_mirror
  if not am or T.phase == "done" then return nil end
  local rk, idx = tostring(am.round or "qf"), tonumber(am.slot_index) or 1
  if T.phase ~= rk then return nil end
  local pr = type(T[rk]) == "table" and T[rk][idx] or nil
  if not pr or (pr.status ~= "fighting" and pr.status ~= "spawning") then return nil end
  if pr.spawn_token ~= am.spawn_token then return nil end
  if pr.match_id and pr.match_id ~= "" and state.match_id and state.match_id ~= ""
    and pr.match_id ~= state.match_id then return nil end
  return pr, rk
end
local function mirror_commit(state)
  local am = state and state.arena_mirror
  if not am then return end
  transact(TOURNAMENTS, tostring(am.tournament_id or ""), nil, function(T)
    local pr = pair_for_state(T, state)
    if not pr then return false end
    local changed = false
    local a = type(state.stats) == "table" and state.stats[pr.uid_a]
    local b = type(state.stats) == "table" and state.stats[pr.uid_b]
    if a and b then
      pr.hp_a, pr.hp_b = math.max(0, tonumber(a.hp) or 0), math.max(0, tonumber(b.hp) or 0)
      changed = true
    end
    if pr.status == "spawning" and state.match_id and state.match_id ~= "" then
      pr.status, pr.match_id = "fighting", state.match_id
      changed = true
    end
    return changed
  end)
end
local function on_match_created(state)
  local am = state and state.arena_mirror
  if not am then return end
  if not state.match_id or state.match_id == "" then error("arena match id missing at creation") end
  local _, bound = transact(TOURNAMENTS, tostring(am.tournament_id or ""), nil, function(T)
    local pr = pair_for_state(T, state)
    if not pr or not am.spawn_token then return false, false end
    if pr.status == "fighting" then return false, pr.match_id == state.match_id end
    pr.status, pr.match_id = "fighting", state.match_id
    return true, true
  end)
  if bound ~= true then error("arena spawn claim expired or superseded") end
end
local function clean_registry(T, writes)
  local registry, version = read_row(SYSTEM, QUEUES, "state")
  if not registry or type(registry.users) ~= "table" then return end
  local changed = false
  for uid in pairs(T.parts or {}) do
    local ref = registry.users[uid]
    if ref and ref.tid == T.id and (T.phase == "done" or T.eliminated[uid]) then
      registry.users[uid], changed = nil, true
    end
  end
  if changed then writes[#writes + 1] = write_row(SYSTEM, QUEUES, "state", registry, version) end
end
local function on_match_finished(state, winner_uid)
  local am = state and state.arena_mirror
  if not am then return end
  return transact(TOURNAMENTS, tostring(am.tournament_id or ""), nil, function(T, writes)
    local pr, rk = pair_for_state(T, state)
    if not pr or (winner_uid ~= pr.uid_a and winner_uid ~= pr.uid_b) then return false end
    local loser = winner_uid == pr.uid_a and pr.uid_b or pr.uid_a
    pr.status, pr.winner_uid, pr.match_id = "done", winner_uid, ""
    for _, side in ipairs({ "a", "b" }) do
      local uid = pr["uid_" .. side]
      local st = type(state.stats) == "table" and state.stats[uid] or nil
      if st then pr["hp_" .. side] = math.max(0, tonumber(st.hp) or 0) end
      if uid == loser then pr["hp_" .. side] = 0 end
    end
    T.eliminated = type(T.eliminated) == "table" and T.eliminated or {}
    if not uid_is_bot(T, loser) then T.eliminated[loser] = true end
    advance(T, rk, writes)
    clean_registry(T, writes)
    return true
  end)
end

local function spawn_pair(tid, rk, index)
  local token = nk.uuid_v4()
  local claimed, won_claim = transact(TOURNAMENTS, tid, nil, function(T, writes)
    local pr = T.phase == rk and T[rk] and T[rk][index]
    if not pr or pr.status ~= "pending" then return false end
    if pr.bot_a and pr.bot_b then
      pr.winner_uid = math.random() < 0.5 and pr.uid_a or pr.uid_b
      pr.status = "done"
      advance(T, rk, writes)
      return true
    end
    pr.status, pr.spawn_token, pr.spawn_started_at = "spawning", token, os.time()
    return true, true
  end)
  if not claimed or won_claim ~= true then return end
  local pr = claimed[rk][index]
  local params = { arena_mirror = { tournament_id = tid, round = rk, slot_index = index,
    bet_tier = claimed.bet_tier, kind = claimed.kind, spawn_token = token } }
  if pr.bot_a or pr.bot_b then
    local human = pr.bot_a and pr.uid_b or pr.uid_a
    local bot_display = pr.bot_a and pr.display_a or pr.display_b
    params.mode, params.owner_user_id = "pve", human
    params.bot_id, params.bot_user_id = "arena_bot", pr.bot_a and pr.uid_a or pr.uid_b
    params.owner_level, params.owner_session_epoch = 1, guard_read_metadata_epoch(human)
    params.arena_pvp_style = true
    params.arena_human_like_bot = FakeOnline.is_online_pool_name(bot_display)
    params.pve_run = { floor = 1, difficulty = "easy", affix = "", stat_mul = 1,
      reward_mul = 0, arena_suppress_all = true }
  else
    params.mode, params.invited = "pvp", { { user_id = pr.uid_a }, { user_id = pr.uid_b } }
  end
  local ok, mid = pcall(try_match_create, params)
  if not ok then mid = nil end
  transact(TOURNAMENTS, tid, nil, function(T)
    local current = T.phase == rk and T[rk] and T[rk][index]
    if not current or current.status ~= "spawning" or current.spawn_token ~= token then return false end
    if mid and mid ~= "" then
      current.status, current.match_id = "fighting", mid
    else
      -- A failed creation has no fight/result and can be retried by a later poll.
      current.status, current.spawn_token, current.spawn_started_at = "pending", nil, nil
      nk.logger_error("arena match_create failed tid=" .. tid .. " slot=" .. index)
    end
    return true
  end)
end
local function tick_tournament(tid)
  local T = transact(TOURNAMENTS, tid, nil, function(value, writes)
    if value.phase == "done" then return false end
    if abort_if_empty(value) then clean_registry(value, writes) return true end
    if value.phase ~= "countdown" then
      -- No registered match owns this expired claim. A delayed creator must
      -- pass on_match_created with its token and will be rejected after reset.
      local changed = false
      for _, pr in ipairs(value[value.phase] or {}) do
        if pr.status == "spawning" and (tonumber(pr.spawn_started_at) or 0) + 30 <= os.time() then
          pr.status, pr.spawn_token, pr.spawn_started_at = "pending", nil, nil
          changed = true
        end
      end
      return changed
    end
    if (tonumber(value.countdown_until) or math.huge) > os.time() then return false end
    value.phase = value.next_round or "qf"
    value.next_round, value.next_slots = nil, nil
    return true
  end)
  if not T or T.phase == "done" then return T end
  local rk = T.phase
  if rk ~= "qf" and rk ~= "sf" and rk ~= "final" then return T end
  for i = 1, #(T[rk] or {}) do spawn_pair(tid, rk, i) end
  return load_tournament(tid)
end

local function empty_registry() return { queues = {}, users = {} } end
local function read_registry()
  return read_row(SYSTEM, QUEUES, "state") or empty_registry()
end
local function queue_key(kind, bet) return kind .. ":" .. bet end
local function get_queue(registry, kind, bet)
  registry.queues = type(registry.queues) == "table" and registry.queues or {}
  registry.users = type(registry.users) == "table" and registry.users or {}
  local key = queue_key(kind, bet)
  registry.queues[key] = registry.queues[key] or { entries = {}, next_bot_at = 0, kind = kind, bet = bet }
  return registry.queues[key]
end
local function tidy_user(registry, uid)
  registry.users = type(registry.users) == "table" and registry.users or {}
  local ref = registry.users[uid]
  if ref and ref.tid then
    local T = load_tournament(ref.tid)
    if not T or T.phase == "done" or (T.eliminated and T.eliminated[uid]) then
      registry.users[uid] = nil
      return nil
    end
  end
  return registry.users[uid]
end
local function user_tournament(uid)
  local registry = read_registry()
  local ref = registry.users and registry.users[uid]
  if ref and ref.tid then return ref.tid, ref.kind end
  -- Compatibility for tournaments created before shared queues were deployed.
  local legacy = read_row(uid, "arena_user_tid", "tid")
  if legacy and legacy.tid and legacy.tid ~= "" then return legacy.tid, normalize_kind(legacy.kind) end
  return nil
end
local function make_tournament(kind, bet, entries, cooldowns)
  arena_dedupe_bot_displays(entries, cooldowns)
  arena_shuffle_inplace(entries)
  local T = { id = nk.uuid_v4(), kind = kind, bet_tier = bet, bracket_size = #entries,
    phase = "countdown", countdown_until = os.time() + ARENA_COUNTDOWN_SEC, created_at = os.time(),
    parts = {}, qf = {}, sf = {}, final = {}, eliminated = {}, side_bets = {},
    bet_score = {}, bet_wins = {}, bet_losses = {}, bet_counted = {}, bets_resolved = {} }
  local slots = {}
  for _, p in ipairs(entries) do
    T.parts[p.uid] = { display = p.display, bot = p.bot == true }
    slots[#slots + 1] = p
  end
  T.qf = arena_build_round_from_slots(slots)
  return T
end
local function pop_full(registry, q, writes)
  if #q.entries < ARENA_QUEUE_MAX then return end
  local entries = {}
  for _ = 1, ARENA_QUEUE_MAX do entries[#entries + 1] = table.remove(q.entries, 1) end
  registry.bot_cooldowns = type(registry.bot_cooldowns) == "table" and registry.bot_cooldowns or {}
  for name, at in pairs(registry.bot_cooldowns) do
    if (tonumber(at) or 0) + 7200 < os.time() then registry.bot_cooldowns[name] = nil end
  end
  local T = make_tournament(q.kind, q.bet, entries, registry.bot_cooldowns)
  writes[#writes + 1] = write_row(SYSTEM, TOURNAMENTS, T.id, T, "*")
  q.next_bot_at = 0
  for _, p in ipairs(entries) do
    if p.bot then
      if FakeOnline.is_online_pool_name(p.display) then registry.bot_cooldowns[p.display] = os.time() end
    else
      registry.users[p.uid] = { tid = T.id, kind = q.kind }
      summary_write(writes, p.uid, function(value)
        value.summary = type(value.summary) == "table" and value.summary or {}
        local summary = value.summary
        summary.arena_tournaments = type(summary.arena_tournaments) == "table" and summary.arena_tournaments or {}
        local bag = summary.arena_tournaments[q.kind] or {}
        bag.played, bag.updated_at = (tonumber(bag.played) or 0) + 1, os.time()
        summary.arena_tournaments[q.kind] = bag
      end)
    end
  end
end
local function fill_bot(kind, bet)
  transact(QUEUES, "state", empty_registry, function(registry, writes)
    local q = get_queue(registry, kind, bet)
    if #q.entries == 0 then return false end
    local has_human = false
    for _, p in ipairs(q.entries) do if not p.bot then has_human = true end end
    if not has_human then q.entries, q.next_bot_at = {}, 0 return true end
    if #q.entries >= ARENA_QUEUE_MAX then pop_full(registry, q, writes) return true end
    if (tonumber(q.next_bot_at) or 0) > os.time() then return false end
    q.next_bot_at = os.time() + math.random(5, 8)
    local used = arena_collect_used_displays(q.entries)
    q.entries[#q.entries + 1] = { uid = arena_make_bot_uid(), display = arena_pick_bot_display(used),
      bot = true, bet_tier = bet }
    pop_full(registry, q, writes)
    return true
  end)
end
local function canonical_bet(kind, bet)
  bet = string.lower(tostring(bet or "green"))
  if kind ~= ARENA_KIND_SMITH then return normalize_resource_bet_tier(kind, bet) end
  if bet ~= "green" and bet ~= "blue" and bet ~= "purple" then return "green" end
  return bet
end
local function checked_rpc(name, handler)
  return function(ctx, payload)
    local ok, result = pcall(function()
      local uid = ctx and ctx.user_id or ""
      if uid == "" then return { ok = false, err = "unauthorized" } end
      local allowed, err = guard_assert_client_epoch_matches(uid, payload)
      if not allowed then return { ok = false, err = err } end
      local p = payload and payload ~= "" and nk.json_decode(payload) or {}
      if type(p) ~= "table" then return { ok = false, err = "bad_payload" } end
      return handler(uid, p)
    end)
    if not ok then
      nk.logger_error(name .. ": " .. tostring(result))
      result = { ok = false, err = "server_error" }
    end
    return nk.json_encode(result)
  end
end
local duel_arena_queue_join = checked_rpc("duel_arena_queue_join", function(uid, p)
  local kind = normalize_kind(p.arena_kind)
  local bet = canonical_bet(kind, p.bet_tier)
  local account = nk.account_get_id(uid)
  local display = account and account.user and tostring(account.user.username or "Игрок") or "Игрок"
  local registry, result = transact(QUEUES, "state", empty_registry, function(value, writes)
    local ref = tidy_user(value, uid)
    if ref then return false, { ok = false, err = ref.tid and "already_in_tournament" or "already_in_queue" } end
    local legacy_tid = user_tournament(uid)
    local legacy = legacy_tid and load_tournament(legacy_tid)
    if legacy and legacy.phase ~= "done" and not (legacy.eliminated and legacy.eliminated[uid]) then
      return false, { ok = false, err = "already_in_tournament" }
    end
    local q = get_queue(value, kind, bet)
    if kind == ARENA_KIND_SMITH then
      local sheet = read_character_sheet(uid)
      ensure_sheet_inventory_counts(sheet)
      local def = arena_bet_to_ingot_def(bet)
      if inventory_remove_def_total(sheet, def, ARENA_QUEUE_BET_INGOTS) ~= true then
        return false, { ok = false, err = "not_enough_ingots", ingot_def = def }
      end
      writes[#writes + 1] = build_sheet_write(uid, sheet)
    else
      local cfg = resource_tier_cfg(kind, bet)
      local progress, version = read_pve_progress(uid)
      local resource = kind == ARENA_KIND_ORE and "ore" or "gold"
      local have, need = tonumber(progress[resource]) or 0, cfg.bet
      if have < need then return false, { ok = false, err = "not_enough_" .. resource, required = need, have = have } end
      progress[resource] = have - need
      writes[#writes + 1] = build_progress_write(uid, progress, version)
    end
    q.entries[#q.entries + 1] = { uid = uid, display = display, bot = false, bet_tier = bet }
    value.users[uid] = { kind = kind, bet = bet }
    if (tonumber(q.next_bot_at) or 0) <= 0 then q.next_bot_at = os.time() + math.random(5, 8) end
    pop_full(value, q, writes)
    return true, { ok = true }
  end)
  if not result or result.ok ~= true then return result end
  local q = get_queue(registry, kind, bet)
  return { ok = true, queue_count = #q.entries, queue_max = ARENA_QUEUE_MAX, bet_tier = bet,
    in_tournament = registry.users[uid] and registry.users[uid].tid ~= nil, arena_kind = kind }
end)
local duel_arena_queue_leave = checked_rpc("duel_arena_queue_leave", function(uid)
  local registry, result = transact(QUEUES, "state", empty_registry, function(value, writes)
    local ref = tidy_user(value, uid)
    if not ref then return false, { ok = false, err = "not_in_queue" } end
    if ref.tid then return false, { ok = false, err = "in_tournament" } end
    local q = get_queue(value, ref.kind, ref.bet)
    local found
    for i, entry in ipairs(q.entries) do if entry.uid == uid and not entry.bot then found = i break end end
    if not found then error("arena queue registry mismatch") end
    if ref.kind == ARENA_KIND_SMITH then
      local sheet = read_character_sheet(uid)
      ensure_sheet_inventory_counts(sheet)
      local added, err = inventory_try_add(sheet, arena_bet_to_ingot_def(ref.bet), ARENA_QUEUE_BET_INGOTS)
      if added ~= true then return false, { ok = false, err = err or "inventory_full" } end
      writes[#writes + 1] = build_sheet_write(uid, sheet)
    else
      local cfg = resource_tier_cfg(ref.kind, ref.bet)
      local progress, version = read_pve_progress(uid)
      local resource = ref.kind == ARENA_KIND_ORE and "ore" or "gold"
      progress[resource] = math.max(0, (tonumber(progress[resource]) or 0) + cfg.bet)
      writes[#writes + 1] = build_progress_write(uid, progress, version)
    end
    table.remove(q.entries, found)
    value.users[uid] = nil
    local human = false
    for _, entry in ipairs(q.entries) do if not entry.bot then human = true break end end
    if not human then q.entries, q.next_bot_at = {}, 0 end
    return true, { ok = true, queue_count = #q.entries, queue_max = ARENA_QUEUE_MAX,
      bet_tier = ref.bet, arena_kind = ref.kind }
  end)
  return result
end)

local duel_arena_place_bet = checked_rpc("duel_arena_place_bet", function(uid, p)
  local tid = user_tournament(uid)
  if not tid then return { ok = false, err = "not_in_tournament" } end
  local _, result = transact(TOURNAMENTS, tid, nil, function(T, writes)
    if T.phase == "done" then return false, { ok = false, err = "no_tournament" } end
    if not T.parts[uid] or uid_is_bot(T, uid) then return false, { ok = false, err = "not_in_tournament" } end
    if T.eliminated[uid] then return false, { ok = false, err = "eliminated" } end
    local rk = arena_open_side_bet_round(T)
    if not rk or (tonumber(T.countdown_until) or 0) <= os.time() then
      return false, { ok = false, err = "bets_closed" }
    end
    local slot = tonumber(p.slot) or 0
    if slot ~= math.floor(slot) or slot < 1 or slot > #(T[rk] or {}) then return false, { ok = false, err = "bad_slot" } end
    local pr = T[rk][slot]
    if pr.uid_a == uid or pr.uid_b == uid then return false, { ok = false, err = "own_pair" } end
    arena_ensure_side_bet_tables(T)
    T.side_bets[uid] = T.side_bets[uid] or {}
    local bag = T.side_bets[uid]
    bag[rk] = bag[rk] or {}
    local picks, key = bag[rk], tostring(slot)
    local response = { ok = true, round = rk, slot = slot, bet_score = tonumber(T.bet_score[uid]) or 0 }
    local changed = false
    if p.clear == true then
      changed, picks[key], response.cleared = picks[key] ~= nil, nil, true
    else
      local side = string.lower(tostring(p.side or ""))
      if side ~= "a" and side ~= "b" then return false, { ok = false, err = "bad_side" } end
      local pick = pr["uid_" .. side]
      changed, picks[key] = picks[key] ~= pick, pick
      response.side, response.pick_uid = side, pick
      T.bet_counted = T.bet_counted or {}
      T.bet_counted[uid] = T.bet_counted[uid] or {}
      T.bet_counted[uid][rk] = T.bet_counted[uid][rk] or {}
      if not T.bet_counted[uid][rk][key] then
        T.bet_counted[uid][rk][key] = true
        achievement(writes, uid, "slaughter.arena_bets_placed", 1)
        changed = true
      end
    end
    response.my_bets = arena_pack_my_side_bets(T, uid, rk)
    return changed, response
  end)
  return type(result) == "table" and result or { ok = false, err = "no_tournament" }
end)
local function tournament_json(uid, T)
  if not T or T.phase == "done" then return { active = false, id = "" } end
  if T.eliminated[uid] then return { active = true, id = T.id, eliminated = true, phase = T.phase, bet_tier = T.bet_tier } end
  local join_mid, join_bot = "", false
  local function pack(rk)
    local out = {}
    for i, pr in ipairs(T[rk] or {}) do
      if pr.status == "fighting" and (pr.uid_a == uid or pr.uid_b == uid) then
        join_mid = pr.match_id or ""
        join_bot = uid_is_bot(T, pr.uid_a == uid and pr.uid_b or pr.uid_a)
      end
      out[#out + 1] = { slot = i, match_id = pr.match_id or "", uid_a = pr.uid_a, uid_b = pr.uid_b,
        display_a = pr.display_a, display_b = pr.display_b, hp_a = pr.hp_a, hp_b = pr.hp_b,
        status = pr.status, winner_uid = pr.winner_uid }
    end
    return out
  end
  local qf, sf, final = pack("qf"), pack("sf"), pack("final")
  arena_ensure_side_bet_tables(T)
  local score, rk = tonumber(T.bet_score[uid]) or 0, arena_active_side_bet_round(T)
  local bg, bo = final_prize_for_kind_and_bet(T.kind, T.bet_tier)
  local pg, po = arena_apply_bet_score_to_prize(T.kind, T.bet_tier, score)
  return { active = true, id = T.id, eliminated = false, phase = T.phase, kind = T.kind, bet_tier = T.bet_tier,
    countdown_left = T.phase == "countdown" and math.max(0, (tonumber(T.countdown_until) or 0) - os.time()) or 0,
    next_round = T.next_round, join_match_id = join_mid, join_opponent_is_bot = join_bot,
    qf = qf, sf = sf, final_pairs = final, bets_open = arena_open_side_bet_round(T) ~= nil,
    betting_round = rk or "", my_bets = arena_pack_my_side_bets(T, uid, rk), bet_score = score,
    bet_wins = tonumber(T.bet_wins[uid]) or 0, bet_losses = tonumber(T.bet_losses[uid]) or 0,
    prize_base_gold = bg, prize_base_ore = bo, prize_preview_gold = pg, prize_preview_ore = po }
end
local duel_arena_queue_poll = checked_rpc("duel_arena_queue_poll", function(uid, p)
  local kind = normalize_kind(p.arena_kind)
  local requested = p.bet_tier ~= nil and tostring(p.bet_tier) ~= ""
  local bet = canonical_bet(kind, p.bet_tier)
  local registry = read_registry()
  local ref = registry.users and registry.users[uid]
  if ref and not ref.tid then kind, bet, requested = ref.kind, ref.bet, true end
  if requested then fill_bot(kind, bet) end
  local tid = user_tournament(uid)
  local T = tid and tick_tournament(tid) or nil
  if tid and (not T or T.phase == "done" or (T.eliminated and T.eliminated[uid])) then
    transact(QUEUES, "state", empty_registry, function(value)
      local old = value.users and value.users[uid]
      if old and old.tid == tid then value.users[uid] = nil return true end
      return false
    end)
  end
  registry = read_registry()
  ref = registry.users and registry.users[uid]
  local q = get_queue(registry, kind, bet)
  return { ok = true, queue_count = requested and #q.entries or 0, queue_max = ARENA_QUEUE_MAX,
    in_queue = ref ~= nil and ref.tid == nil, queue_bet_tier = requested and bet or "",
    queue_kind = kind, tournament = tournament_json(uid, T) }
end)
local function final_prize_for_winner(tid, uid, kind, bet)
  local T = load_tournament(tid)
  local score = T and T.bet_score and tonumber(T.bet_score[uid]) or 0
  return arena_apply_bet_score_to_prize(kind, bet, score)
end
  return { mirror_commit = mirror_commit, on_match_created = on_match_created, on_match_finished = on_match_finished,
    final_prize_for_kind_and_bet = final_prize_for_kind_and_bet, final_prize_for_winner = final_prize_for_winner,
    duel_arena_queue_join = duel_arena_queue_join, duel_arena_queue_leave = duel_arena_queue_leave,
    duel_arena_queue_poll = duel_arena_queue_poll, duel_arena_place_bet = duel_arena_place_bet }
end
