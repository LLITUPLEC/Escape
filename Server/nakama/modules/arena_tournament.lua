local nk = require("nakama")

--- Турнир арены (отдельный chunk — не упирается в лимит локалей duel_match3.lua).
return function(deps)
  local try_match_create = deps.try_match_create
  local make_bot_user_id = deps.make_bot_user_id
  local guard_read_metadata_epoch = deps.guard_read_metadata_epoch
  local guard_assert_client_epoch_matches = deps.guard_assert_client_epoch_matches
  local read_pve_progress = deps.read_pve_progress
  local write_pve_progress = deps.write_pve_progress
  local read_character_sheet = deps.read_character_sheet
  local write_character_sheet = deps.write_character_sheet
  local ensure_sheet_inventory_counts = deps.ensure_sheet_inventory_counts
  local inventory_remove_def_total = deps.inventory_remove_def_total
  local inventory_try_add = deps.inventory_try_add
  local stats_inc_arena_tournament_played = deps.stats_inc_arena_tournament_played

--- ═══════════════════════════════════════════════════════════════════════════
--- Арена: турнир на 8 участников (очередь + сетка + интеграция с duel_match3).
--- Отключение ботов: см. arena_runtime.allow_bot_fill ниже.
--- ═══════════════════════════════════════════════════════════════════════════

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

-- Persisted state (RPC and match loops can run in different Lua contexts).
local STORAGE_COLL_TOURN = "arena_tournament"
local STORAGE_COLL_USER_TID = "arena_user_tid"
local STORAGE_USER_TID_KEY = "tid"
local STORAGE_SYSTEM_USER_ID = "00000000-0000-0000-0000-000000000000"

local arena_runtime = {
  kinds = {
    [ARENA_KIND_SMITH] = { queues = {}, allow_bot_fill = true },
    [ARENA_KIND_ORE] = { queues = {}, allow_bot_fill = true },
    [ARENA_KIND_GOLD] = { queues = {}, allow_bot_fill = true },
  },
  tournaments = {},
  -- user_tid[user_id] = { tid = "...", kind = "smith|ore|gold" }
  user_tid = {},
  last_tourn_storage_sweep = 0,
}

local function kind_state(kind)
  local k = normalize_kind(kind)
  local ks = arena_runtime.kinds[k]
  if ks == nil then
    ks = { queues = {}, allow_bot_fill = true }
    arena_runtime.kinds[k] = ks
  end
  return ks, k
end

--- Миграция со старого формата { queue, queue_bet_tier, next_bot_at } → queues[bet].
local function ensure_kind_queues(ks)
  if type(ks.queues) ~= "table" then
    ks.queues = {}
  end
  if type(ks.queue) == "table" and #ks.queue > 0 then
    local old_bet = tostring(ks.queue_bet_tier or "")
    if old_bet == "" then
      local first = ks.queue[1]
      old_bet = (first ~= nil and tostring(first.bet_tier or "")) or "green"
    end
    if old_bet == "" then old_bet = "green" end
    local bq = ks.queues[old_bet]
    if bq == nil then
      bq = { queue = {}, next_bot_at = 0 }
      ks.queues[old_bet] = bq
    end
    if type(bq.queue) ~= "table" then
      bq.queue = {}
    end
    for i = 1, #ks.queue do
      bq.queue[#bq.queue + 1] = ks.queue[i]
    end
    if (tonumber(ks.next_bot_at) or 0) > 0 then
      bq.next_bot_at = tonumber(ks.next_bot_at) or 0
    end
  end
  ks.queue = nil
  ks.queue_bet_tier = nil
  ks.next_bot_at = nil
end

local function bet_queue_state(kind, bet)
  local ks, k = kind_state(kind)
  ensure_kind_queues(ks)
  local b = string.lower(tostring(bet or ""))
  if b == "" then b = "green" end
  local bq = ks.queues[b]
  if bq == nil then
    bq = { queue = {}, next_bot_at = 0 }
    ks.queues[b] = bq
  elseif type(bq.queue) ~= "table" then
    bq.queue = {}
  end
  return bq, ks, k, b
end

local function find_user_in_any_queue(user_id)
  for _, kk in ipairs({ ARENA_KIND_SMITH, ARENA_KIND_ORE, ARENA_KIND_GOLD }) do
    local ks = arena_runtime.kinds[kk]
    if ks ~= nil then
      ensure_kind_queues(ks)
      for bet_key, bq in pairs(ks.queues) do
        local q = bq and bq.queue
        if type(q) == "table" then
          for i = 1, #q do
            local e = q[i]
            if e ~= nil and e.bot ~= true and e.uid == user_id then
              return kk, tostring(bet_key), bq, i, e
            end
          end
        end
      end
    end
  end
  return nil, nil, nil, nil, nil
end

local function storage_read_one(user_id, collection, key)
  local ok, rows = pcall(function()
    return nk.storage_read({ { user_id = user_id, collection = collection, key = key } })
  end)
  if not ok then
    nk.logger_error("arena storage_read failed: " .. tostring(rows))
    return nil
  end
  if rows == nil or #rows == 0 or rows[1] == nil then
    return nil
  end
  return rows[1].value
end

local function storage_write_one(user_id, collection, key, value)
  local ok, err = pcall(function()
    nk.storage_write({ { user_id = user_id, collection = collection, key = key, value = value } })
  end)
  if not ok then
    nk.logger_error("arena storage_write failed: " .. tostring(err))
    return false
  end
  return true
end

local function storage_delete_one(user_id, collection, key)
  local ok, err = pcall(function()
    nk.storage_delete({ { user_id = user_id, collection = collection, key = key } })
  end)
  if not ok then
    nk.logger_error("arena storage_delete failed: " .. tostring(err))
    return false
  end
  return true
end

local function arena_save_tournament(T)
  if T == nil or T.id == nil or T.id == "" then return end
  storage_write_one(STORAGE_SYSTEM_USER_ID, STORAGE_COLL_TOURN, tostring(T.id), T)
end

local function arena_delete_tournament(tid)
  if tid == nil or tid == "" then return end
  arena_runtime.tournaments[tid] = nil
  storage_delete_one(STORAGE_SYSTEM_USER_ID, STORAGE_COLL_TOURN, tostring(tid))
end

local function arena_load_tournament(tid)
  if tid == nil or tid == "" then return nil end
  -- Always refresh from storage: RPC and match loops can update in different Lua contexts.
  local v = storage_read_one(STORAGE_SYSTEM_USER_ID, STORAGE_COLL_TOURN, tostring(tid))
  if v ~= nil then
    if v.phase == "done" then
      arena_delete_tournament(tid)
      return nil
    end
    arena_runtime.tournaments[tid] = v
    return v
  end
  return arena_runtime.tournaments[tid]
end

--- Удаление по TTL работало только при следующем read по tid; без обхода коллекции записи «done» копились.
local function arena_sweep_expired_done_storage()
  local cursor = ""
  while true do
    local ok, objects, next_cursor = pcall(function()
      return nk.storage_list(STORAGE_SYSTEM_USER_ID, STORAGE_COLL_TOURN, 100, cursor)
    end)
    if not ok then
      nk.logger_error("arena storage_list failed: " .. tostring(objects))
      break
    end
    if objects == nil then break end
    for _, row in ipairs(objects) do
      local v = row.value
      if v ~= nil and v.phase == "done" then
        arena_delete_tournament(row.key)
      end
    end
    if next_cursor == nil or next_cursor == "" then break end
    cursor = next_cursor
  end
end

local function arena_save_user_tid(user_id, tid)
  if user_id == nil or user_id == "" then return end
  if tid == nil or tid == "" then
    storage_delete_one(user_id, STORAGE_COLL_USER_TID, STORAGE_USER_TID_KEY)
  else
    local kind = nil
    if type(tid) == "table" then
      kind = normalize_kind(tid.kind)
      tid = tid.tid
    end
    storage_write_one(user_id, STORAGE_COLL_USER_TID, STORAGE_USER_TID_KEY, { tid = tid, kind = kind })
  end
end

local function arena_load_user_tid(user_id)
  if user_id == nil or user_id == "" then return nil end
  -- Always refresh from storage: user_tid is modified by match context on elimination.
  local v = storage_read_one(user_id, STORAGE_COLL_USER_TID, STORAGE_USER_TID_KEY)
  if v ~= nil and v.tid ~= nil and v.tid ~= "" then
    local kind = normalize_kind(v.kind)
    arena_runtime.user_tid[user_id] = { tid = v.tid, kind = kind }
    return v.tid, kind
  end
  arena_runtime.user_tid[user_id] = nil
  return nil, nil
end

local ARENA_BOT_DISPLAY_NAMES = {
  "Грейвен",
  "Фестер",
  "Морозко",
  "Шипогрыз",
  "Кремнебород",
  "Пепельный",
  "Туманный",
  "Жнец",
  "Осколок",
  "Шутиха",
}

local function arena_shuffle_inplace(arr)
  for i = #arr, 2, -1 do
    local j = math.random(i)
    arr[i], arr[j] = arr[j], arr[i]
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

local function arena_random_bot_display()
  local names = ARENA_BOT_DISPLAY_NAMES
  local idx = math.random(1, #names)
  local name = names[idx]
  table.remove(names, idx) -- no duplicates within one tournament fill batch
  if #names == 0 then
    -- reset pool
    for _, n in ipairs({
      "Грейвен","Фестер","Морозко","Шипогрыз","Кремнебород",
      "Пепельный","Туманный","Жнец","Осколок","Шутиха",
    }) do
      names[#names + 1] = n
    end
  end
  return name
end

local function arena_uid_is_bot(T, uid)
  local p = T.parts[uid]
  return p == nil or p.bot == true
end

local function arena_clear_human_tid(user_id)
  arena_runtime.user_tid[user_id] = nil
  arena_save_user_tid(user_id, nil)
end

--- Синхронизирует eliminated с сеткой: hp=0 у человека ⇒ выбыл (если on_match_finished не дошёл).
local function arena_reconcile_eliminated_from_bracket(T)
  if T == nil or type(T.parts) ~= "table" then
    return
  end
  if type(T.eliminated) ~= "table" then
    T.eliminated = {}
  end
  for _, rk in ipairs({ "qf", "sf", "final" }) do
    local plist = T[rk]
    if type(plist) ~= "table" then
      goto next_round
    end
    for i = 1, #plist do
      local pr = plist[i]
      local function mark_out(uid, hp_key, opp_uid)
        if uid == nil or uid == "" then return end
        if T.parts[uid] == nil or T.parts[uid].bot == true then return end
        if (tonumber(pr[hp_key]) or 0) > 0 then return end
        T.eliminated[uid] = true
        arena_clear_human_tid(uid)
        if pr.status ~= "done" then
          pr.status = "done"
          if pr.winner_uid == nil or pr.winner_uid == "" then
            pr.winner_uid = opp_uid or ""
          end
          pr.match_id = ""
        end
      end
      mark_out(pr.uid_a, "hp_a", pr.uid_b)
      mark_out(pr.uid_b, "hp_b", pr.uid_a)
    end
    ::next_round::
  end
end

local function arena_tournament_count_remaining_humans(T)
  if T == nil or type(T.parts) ~= "table" then
    return 0
  end
  arena_reconcile_eliminated_from_bracket(T)
  local eliminated = (type(T.eliminated) == "table") and T.eliminated or {}
  local n = 0
  for uid, meta in pairs(T.parts) do
    if meta ~= nil and meta.bot ~= true and eliminated[uid] ~= true then
      n = n + 1
    end
  end
  return n
end

local function arena_clear_all_human_tids_from_tournament(T)
  if T == nil or type(T.parts) ~= "table" then
    return
  end
  for uid, meta in pairs(T.parts) do
    if meta ~= nil and meta.bot ~= true then
      arena_clear_human_tid(uid)
    end
  end
end

--- Нет активных людей — не держим запись в storage (боты доигрывают сами себе не нужны).
local function arena_abort_if_no_humans(T)
  if T == nil or T.id == nil or T.id == "" then
    return true
  end
  if arena_tournament_count_remaining_humans(T) > 0 then
    return false
  end
  arena_clear_all_human_tids_from_tournament(T)
  arena_delete_tournament(T.id)
  return true
end

local function arena_persist_or_abort_if_no_humans(T)
  if T == nil or T.id == nil or T.id == "" then return end
  if arena_tournament_count_remaining_humans(T) <= 0 then
    arena_abort_if_no_humans(T)
    return
  end
  arena_save_tournament(T)
end

--- Удаляем «мертвые» турниры: нет ни одного активного реального игрока.
local function arena_sweep_dead_tournaments_storage()
  local cursor = ""
  while true do
    local ok, objects, next_cursor = pcall(function()
      return nk.storage_list(STORAGE_SYSTEM_USER_ID, STORAGE_COLL_TOURN, 100, cursor)
    end)
    if not ok then
      nk.logger_error("arena storage_list failed: " .. tostring(objects))
      break
    end
    if objects == nil then break end
    for _, row in ipairs(objects) do
      local v = row.value
      if v ~= nil and v.phase ~= "done" then
        arena_reconcile_eliminated_from_bracket(v)
        arena_abort_if_no_humans(v)
      end
    end
    if next_cursor == nil or next_cursor == "" then break end
    cursor = next_cursor
  end
end

local function mirror_commit(state)
  local am = state.arena_mirror
  if am == nil then
    return
  end
  local tid = am.tournament_id
  local T = arena_load_tournament(tid)
  if T == nil then
    nk.logger_warn("arena mirror_commit: tournament not found tid=" .. tostring(tid))
    return
  end
  local rk = am.round or "qf"
  local idx = tonumber(am.slot_index) or 1
  local plist = T[rk]
  if plist == nil then
    nk.logger_warn("arena mirror_commit: round list missing tid=" .. tostring(tid) .. " rk=" .. tostring(rk))
    return
  end
  local pr = plist[idx]
  if pr == nil then
    nk.logger_warn("arena mirror_commit: pair missing tid=" .. tostring(tid) .. " rk=" .. tostring(rk) .. " idx=" .. tostring(idx))
    return
  end
  if state.players_sorted == nil or #state.players_sorted < 2 then
    return
  end
  local sa = state.players_sorted[1]
  local sb = state.players_sorted[2]
  local st = state.stats
  if st == nil then
    return
  end
  local hpa = math.max(0, tonumber(st[sa].hp) or 0)
  local hpb = math.max(0, tonumber(st[sb].hp) or 0)
  if pr.uid_a == sa then
    pr.hp_a = hpa
    pr.hp_b = hpb
  elseif pr.uid_a == sb then
    pr.hp_a = hpb
    pr.hp_b = hpa
  else
    pr.hp_a = hpa
    pr.hp_b = hpb
  end
  arena_persist_or_abort_if_no_humans(T)
end

--- Награды финала турнира (золото/руда) — та же шкала, что в arena_grant_final_progress и в OP_GAME_OVER.
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

local function arena_grant_final_progress(user_id, kind, bet_tier, bet_score)
  local g, o = arena_apply_bet_score_to_prize(kind, bet_tier, bet_score)
  local max_retries = 5
  for attempt = 1, max_retries do
    local progress, version = read_pve_progress(user_id)
    progress.ore = math.max(0, (tonumber(progress.ore) or 0) + (tonumber(o) or 0))
    progress.gold = math.max(0, (tonumber(progress.gold) or 0) + (tonumber(g) or 0))
    local ok, err = pcall(function()
      write_pve_progress(user_id, progress, version)
    end)
    if ok then
      return true, g, o
    end
    local err_text = tostring(err)
    if string.find(err_text, "version", 1, true) == nil or attempt == max_retries then
      nk.logger_error("arena_grant_final_progress: " .. err_text)
      return false, g, o
    end
  end
  return false, g, o
end

--- Раунд, на который сейчас принимают side-bet: qf / sf / nil (финал и бои — нельзя).
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

--- После завершения раунда: ±1 в bet_score и счётчики win/loss за каждую ставку игрока.
local function arena_resolve_side_bets(T, rk)
  arena_ensure_side_bet_tables(T)
  local plist = T[rk]
  if plist == nil then
    return
  end
  for uid, bag in pairs(T.side_bets) do
    if type(bag) == "table" then
      local picks = bag[rk]
      if type(picks) == "table" then
        for slot_key, pick_uid in pairs(picks) do
          local pr = plist[tonumber(slot_key)]
          if pr ~= nil and pick_uid ~= nil and pick_uid ~= "" then
            local w = tostring(pr.winner_uid or "")
            if w ~= "" then
              local cur = tonumber(T.bet_score[uid]) or 0
              if w == tostring(pick_uid) then
                T.bet_score[uid] = cur + 1
                T.bet_wins[uid] = (tonumber(T.bet_wins[uid]) or 0) + 1
              else
                T.bet_score[uid] = cur - 1
                T.bet_losses[uid] = (tonumber(T.bet_losses[uid]) or 0) + 1
              end
            end
          end
        end
      end
    end
  end
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

--- Приз финала с учётом side-bet счёта победителя (для UI GAME_OVER).
local function final_prize_for_winner(tournament_id, user_id, kind, bet_tier)
  local score = 0
  local T = arena_load_tournament(tournament_id)
  if T ~= nil then
    arena_ensure_side_bet_tables(T)
    score = tonumber(T.bet_score[user_id]) or 0
  end
  return arena_apply_bet_score_to_prize(kind, bet_tier, score)
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

local arena_on_pair_completed

local function arena_all_pairs_done(T, rk)
  local plist = T[rk]
  if plist == nil then
    return false
  end
  for i = 1, #plist do
    if plist[i].status ~= "done" then
      return false
    end
  end
  return true
end

local function arena_collect_winners(T, rk)
  local out = {}
  local plist = T[rk]
  if plist == nil then
    return out
  end
  for i = 1, #plist do
    local pr = plist[i]
    local w = pr.winner_uid
    if w ~= nil and w ~= "" then
      local meta = T.parts[w]
      local display = meta ~= nil and meta.display or "?"
      local bot = meta ~= nil and meta.bot == true
      out[#out + 1] = { uid = w, display = display, bot = bot }
    end
  end
  return out
end

local function arena_spawn_round_if_pending(T, rk)
  local plist = T[rk]
  if plist == nil then
    return
  end
  -- Бот vs бот — без матча; отдельно, чтобы не вызывать advance посередине одного прохода.
  for i = 1, #plist do
    local pr = plist[i]
    if pr.status == "pending" and pr.bot_a and pr.bot_b then
      pr.winner_uid = math.random() < 0.5 and pr.uid_a or pr.uid_b
      pr.status = "done"
    end
  end
  if arena_all_pairs_done(T, rk) then
    arena_on_pair_completed(T, rk)
    return
  end
  for i = 1, #plist do
    local pr = plist[i]
    if pr.status ~= "pending" then
      -- skip
    elseif pr.bot_a or pr.bot_b then
      local human_uid = pr.bot_a and pr.uid_b or pr.uid_a
      local epoch = guard_read_metadata_epoch(human_uid)
      local bot_uid = pr.bot_a and pr.uid_a or pr.uid_b
      local mid = try_match_create({
        mode = "pve",
        owner_user_id = human_uid,
        bot_id = "arena_bot",
        bot_user_id = bot_uid,
        owner_level = 1,
        owner_session_epoch = epoch,
        arena_pvp_style = true,
        pve_run = {
          floor = 1,
          difficulty = "easy",
          affix = "",
          stat_mul = 1,
          reward_mul = 0,
          arena_suppress_all = true,
        },
        arena_mirror = {
          tournament_id = T.id,
          round = rk,
          slot_index = i,
          bet_tier = T.bet_tier,
          kind = T.kind,
        },
      })
      if mid ~= nil and mid ~= "" then
        pr.match_id = mid
        pr.status = "fighting"
      else
        nk.logger_error("arena_spawn_round_if_pending: pve match_create failed idx=" .. tostring(i))
        pr.winner_uid = human_uid
        pr.status = "done"
      end
    else
      local mid = try_match_create({
        mode = "pvp",
        invited = { { user_id = pr.uid_a }, { user_id = pr.uid_b } },
        arena_mirror = {
          tournament_id = T.id,
          round = rk,
          slot_index = i,
          bet_tier = T.bet_tier,
          kind = T.kind,
        },
      })
      if mid ~= nil and mid ~= "" then
        pr.match_id = mid
        pr.status = "fighting"
      else
        nk.logger_error("arena_spawn_round_if_pending: pvp match_create failed idx=" .. tostring(i))
        pr.winner_uid = pr.uid_a
        pr.status = "done"
      end
    end
  end
  if arena_all_pairs_done(T, rk) then
    arena_on_pair_completed(T, rk)
  end
end

local function arena_begin_next_round(T, rk_next, slots)
  arena_shuffle_inplace(slots)
  T[rk_next] = arena_build_round_from_slots(slots)
  T.phase = rk_next
  arena_spawn_round_if_pending(T, rk_next)
  arena_save_tournament(T)
end

arena_on_pair_completed = function(T, rk)
  if not arena_all_pairs_done(T, rk) then
    return
  end
  if arena_abort_if_no_humans(T) then
    return
  end
  if rk == "qf" then
    local winners = arena_collect_winners(T, "qf")
    if #winners ~= 4 then
      return
    end
    arena_resolve_side_bets(T, "qf")
    -- Prepare next bracket immediately (no history in UI).
    arena_shuffle_inplace(winners)
    T.sf = arena_build_round_from_slots(winners)
    T.qf = {}
    T.phase = "countdown"
    T.countdown_until = os.time() + ARENA_COUNTDOWN_SEC
    T.next_round = "sf"
    T.next_slots = nil
    arena_save_tournament(T)
    return
  end
  if rk == "sf" then
    local winners = arena_collect_winners(T, "sf")
    if #winners ~= 2 then
      return
    end
    arena_resolve_side_bets(T, "sf")
    arena_shuffle_inplace(winners)
    T.final = arena_build_round_from_slots(winners)
    T.sf = {}
    T.qf = {}
    T.phase = "countdown"
    T.countdown_until = os.time() + ARENA_COUNTDOWN_SEC
    T.next_round = "final"
    T.next_slots = nil
    arena_save_tournament(T)
    return
  end
  if rk == "final" then
    local plist = T.final
    local pr = plist ~= nil and plist[1] or nil
    if pr == nil then
      return
    end
    local w = pr.winner_uid
    if w ~= nil and w ~= "" and not arena_uid_is_bot(T, w) then
      arena_ensure_side_bet_tables(T)
      arena_grant_final_progress(w, T.kind, T.bet_tier, T.bet_score[w] or 0)
    end
    T.phase = "done"
    T.done_at = os.time()
    for uid, meta in pairs(T.parts) do
      if meta.bot ~= true then
        arena_clear_human_tid(uid)
      end
    end
    arena_delete_tournament(T.id)
  end
end

local function on_match_finished(state, winner_uid)
  local am = state.arena_mirror
  if am == nil then
    -- Обычный PvP/тренировочный бой без турнира: хук вызывается из duel_match3 всегда — без зеркала нечего обновлять.
    return
  end
  local tid = am.tournament_id
  local T = arena_load_tournament(tid)
  if T == nil then
    nk.logger_warn("arena on_match_finished: tournament not found tid=" .. tostring(tid) .. " winner=" .. tostring(winner_uid))
    return
  end
  local rk = am.round or "qf"
  local idx = tonumber(am.slot_index) or 1
  local plist = T[rk]
  if plist == nil then
    nk.logger_warn("arena on_match_finished: round list missing tid=" .. tostring(tid) .. " rk=" .. tostring(rk))
    return
  end
  local pr = plist[idx]
  if pr == nil then
    nk.logger_warn("arena on_match_finished: pair missing tid=" .. tostring(tid) .. " rk=" .. tostring(rk) .. " idx=" .. tostring(idx))
    return
  end
  nk.logger_info("arena on_match_finished: tid=" .. tostring(tid) .. " rk=" .. tostring(rk) .. " idx=" .. tostring(idx) ..
    " winner=" .. tostring(winner_uid) .. " a=" .. tostring(pr.uid_a) .. " b=" .. tostring(pr.uid_b))

  local loser_uid = nil
  if pr.uid_a == winner_uid then
    loser_uid = pr.uid_b
  elseif pr.uid_b == winner_uid then
    loser_uid = pr.uid_a
  else
    -- Fallback: sometimes duel_match3 may report a bot user id that doesn't match stored uid
    -- (older tournaments / stale state). If one side is a bot participant, assume bot is winner.
    local a_is_bot = arena_uid_is_bot(T, pr.uid_a)
    local b_is_bot = arena_uid_is_bot(T, pr.uid_b)
    if a_is_bot and not b_is_bot and string.find(tostring(winner_uid or ""), "zz%-bot%-", 1) == 1 then
      winner_uid = pr.uid_a
      loser_uid = pr.uid_b
    elseif b_is_bot and not a_is_bot and string.find(tostring(winner_uid or ""), "zz%-bot%-", 1) == 1 then
      winner_uid = pr.uid_b
      loser_uid = pr.uid_a
    end
  end
  pr.status = "done"
  pr.winner_uid = winner_uid
  -- Match is over and may be deleted; never offer join to a finished match.
  pr.match_id = ""
  -- Ensure HP looks correct in bracket even if last mirror_commit didn't arrive.
  -- We persist final HP snapshot from match state, then force loser HP to 0 for clarity.
  if state ~= nil and type(state.stats) == "table" then
    local function hp_of(uid)
      if uid == nil or uid == "" then return nil end
      local s = state.stats[uid]
      if s == nil then return nil end
      return math.max(0, tonumber(s.hp) or 0)
    end
    local ha = hp_of(pr.uid_a)
    local hb = hp_of(pr.uid_b)
    if ha ~= nil then pr.hp_a = ha end
    if hb ~= nil then pr.hp_b = hb end
  end
  if loser_uid ~= nil then
    if pr.uid_a == loser_uid then pr.hp_a = 0 end
    if pr.uid_b == loser_uid then pr.hp_b = 0 end
  end
  if loser_uid ~= nil and T.parts[loser_uid] ~= nil and T.parts[loser_uid].bot ~= true then
    T.eliminated[loser_uid] = true
    arena_clear_human_tid(loser_uid)
  end
  arena_on_pair_completed(T, rk)
  arena_persist_or_abort_if_no_humans(T)
end

local function arena_tick_countdowns()
  local now = os.time()
  for tid, T in pairs(arena_runtime.tournaments) do
    if T == nil then
      -- skip
    elseif arena_abort_if_no_humans(T) then
      -- skip
    elseif T.phase == "countdown" and tonumber(T.countdown_until) ~= nil and now >= tonumber(T.countdown_until) then
      if T.next_round ~= nil and T.next_round ~= "" then
        local rk_next = T.next_round
        T.next_round = nil
        T.next_slots = nil
        T.phase = rk_next
        arena_spawn_round_if_pending(T, rk_next)
      else
        T.phase = "qf"
        arena_spawn_round_if_pending(T, "qf")
      end
      arena_save_tournament(T)
    end
  end
end

local function arena_start_tournament_from_entries(kind, entries)
  local k = normalize_kind(kind)
  local tid = nk.uuid_v4()
  local bet = entries[1] ~= nil and entries[1].bet_tier or "green"

  -- Если в пачке 8 только боты — не создаём турнир/сетку и не пишем в storage.
  local has_human = false
  for _, p in ipairs(entries or {}) do
    if p ~= nil and p.bot ~= true then
      has_human = true
      break
    end
  end
  if not has_human then
    return
  end

  local T = {
    id = tid,
    kind = k,
    bet_tier = bet,
    phase = "countdown",
    countdown_until = os.time() + ARENA_COUNTDOWN_SEC,
    next_round = nil,
    next_slots = nil,
    parts = {},
    qf = {},
    sf = {},
    final = {},
    eliminated = {},
    side_bets = {},
    bet_score = {},
    bet_wins = {},
    bet_losses = {},
    created_at = os.time(),
  }
  arena_shuffle_inplace(entries)
  for _, p in ipairs(entries) do
    T.parts[p.uid] = { display = p.display, bot = p.bot == true }
    if p.bot ~= true then
      arena_runtime.user_tid[p.uid] = { tid = tid, kind = k }
      arena_save_user_tid(p.uid, { tid = tid, kind = k })
      if type(stats_inc_arena_tournament_played) == "function" then
        pcall(stats_inc_arena_tournament_played, p.uid, k)
      end
    end
  end
  local slots = {}
  for _, p in ipairs(entries) do
    slots[#slots + 1] = { uid = p.uid, display = p.display, bot = p.bot == true }
  end
  T.qf = arena_build_round_from_slots(slots)
  arena_runtime.tournaments[tid] = T
  arena_save_tournament(T)
end

local function arena_try_pop_queue_full(kind, bet)
  local bq, _ks, k, bet_key = bet_queue_state(kind, bet)
  local q = bq.queue
  if #q < ARENA_QUEUE_MAX then
    return
  end
  local batch = {}
  for i = 1, ARENA_QUEUE_MAX do
    batch[#batch + 1] = q[i]
  end
  for _ = 1, ARENA_QUEUE_MAX do
    table.remove(q, 1)
  end
  bq.next_bot_at = 0
  arena_start_tournament_from_entries(k, batch)
end

local function arena_maybe_fill_bot(kind, bet)
  local bq, ks, k, bet_key = bet_queue_state(kind, bet)
  if ks.allow_bot_fill ~= true then
    return
  end
  local q = bq.queue
  if #q >= ARENA_QUEUE_MAX or #q == 0 then
    return
  end
  -- Если в этой ставке не осталось людей — очищаем очередь от ботов.
  local has_human = false
  for i = 1, #q do
    local e = q[i]
    if e ~= nil and e.bot ~= true then
      has_human = true
      break
    end
  end
  if not has_human then
    bq.queue = {}
    bq.next_bot_at = 0
    return
  end
  local now = os.time()
  if bq.next_bot_at <= 0 then
    bq.next_bot_at = now + math.random(5, 8)
    return
  end
  if now < bq.next_bot_at then
    return
  end
  bq.next_bot_at = now + math.random(5, 8)
  q[#q + 1] = {
    uid = arena_make_bot_uid(),
    display = arena_random_bot_display(),
    bot = true,
    bet_tier = bet_key,
  }
  arena_try_pop_queue_full(k, bet_key)
end

local function arena_maybe_fill_bots_for_kind(kind)
  local ks, k = kind_state(kind)
  ensure_kind_queues(ks)
  for bet_key, _bq in pairs(ks.queues) do
    arena_maybe_fill_bot(k, bet_key)
  end
end

local function arena_json_for_user(user_id)
  arena_tick_countdowns()
  local tid, kind = arena_load_user_tid(user_id)
  if tid == nil then
    return nil
  end
  arena_maybe_fill_bots_for_kind(kind)
  local T = arena_load_tournament(tid)
  if T == nil then
    arena_clear_human_tid(user_id)
    return nil
  end
  if T.eliminated[user_id] == true then
    return {
      active = true,
      id = tid,
      eliminated = true,
      phase = T.phase,
      bet_tier = T.bet_tier,
    }
  end
  local join_mid = ""
  local join_opponent_is_bot = false
  local function scan_round(rk)
    local plist = T[rk]
    if plist == nil then
      return
    end
    for i = 1, #plist do
      local pr = plist[i]
      if pr.status == "fighting" and (pr.uid_a == user_id or pr.uid_b == user_id) then
        join_mid = pr.match_id or ""
        local opp_uid = pr.uid_a == user_id and pr.uid_b or pr.uid_a
        join_opponent_is_bot = arena_uid_is_bot(T, opp_uid)
        break
      end
    end
  end
  scan_round("qf")
  if join_mid == "" then
    scan_round("sf")
  end
  if join_mid == "" then
    scan_round("final")
  end

  local function pack_pairs(rk)
    local out = {}
    local plist = T[rk]
    if plist == nil then
      return out
    end
    for i = 1, #plist do
      local pr = plist[i]
      out[#out + 1] = {
        slot = i,
        match_id = pr.match_id or "",
        uid_a = pr.uid_a,
        uid_b = pr.uid_b,
        display_a = pr.display_a,
        display_b = pr.display_b,
        hp_a = pr.hp_a,
        hp_b = pr.hp_b,
        status = pr.status,
        winner_uid = pr.winner_uid,
      }
    end
    return out
  end

  local countdown_left = 0
  if T.phase == "countdown" and tonumber(T.countdown_until) ~= nil then
    countdown_left = math.max(0, tonumber(T.countdown_until) - os.time())
  end

  arena_ensure_side_bet_tables(T)
  local open_rk = arena_open_side_bet_round(T)
  local active_rk = arena_active_side_bet_round(T)
  local score = tonumber(T.bet_score[user_id]) or 0
  local wins = tonumber(T.bet_wins[user_id]) or 0
  local losses = tonumber(T.bet_losses[user_id]) or 0
  local base_g, base_o = final_prize_for_kind_and_bet(T.kind, T.bet_tier)
  local prev_g, prev_o = arena_apply_bet_score_to_prize(T.kind, T.bet_tier, score)

  return {
    active = true,
    id = tid,
    eliminated = false,
    phase = T.phase,
    bet_tier = T.bet_tier,
    kind = T.kind,
    countdown_left = countdown_left,
    next_round = T.next_round,
    join_match_id = join_mid,
    join_opponent_is_bot = join_opponent_is_bot,
    qf = pack_pairs("qf"),
    sf = pack_pairs("sf"),
    final_pairs = pack_pairs("final"),
    bets_open = open_rk ~= nil,
    betting_round = active_rk or "",
    my_bets = arena_pack_my_side_bets(T, user_id, active_rk),
    bet_score = score,
    bet_wins = wins,
    bet_losses = losses,
    prize_base_gold = base_g,
    prize_base_ore = base_o,
    prize_preview_gold = prev_g,
    prize_preview_ore = prev_o,
  }
end

local function duel_arena_queue_join(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end
    local ok_epoch, err_epoch = guard_assert_client_epoch_matches(user_id, payload)
    if not ok_epoch then
      return nk.json_encode({ ok = false, err = err_epoch })
    end
    local p = {}
    if payload ~= nil and payload ~= "" then
      p = nk.json_decode(payload) or {}
    end
    local kind = normalize_kind(p.arena_kind)
    local bet = string.lower(tostring(p.bet_tier or "green"))
    local k = kind
    if k == ARENA_KIND_SMITH then
      if bet ~= "green" and bet ~= "blue" and bet ~= "purple" then
        bet = "green"
      end
    else
      local cfg, normalized = resource_tier_cfg(k, bet)
      if cfg == nil then
        return nk.json_encode({ ok = false, err = "bad_bet_tier" })
      end
      bet = normalized
    end
    -- Один игрок — одна активная запись в любой очереди арены.
    local already_kind = find_user_in_any_queue(user_id)
    if already_kind ~= nil then
      return nk.json_encode({ ok = false, err = "already_in_queue" })
    end
    if arena_load_user_tid(user_id) ~= nil then
      return nk.json_encode({ ok = false, err = "already_in_tournament" })
    end
    local bq, _ks, _, bet_key = bet_queue_state(k, bet)
    bet = bet_key
    if k == ARENA_KIND_SMITH then
      local sheet = read_character_sheet(user_id)
      ensure_sheet_inventory_counts(sheet)
      local ingot_def = arena_bet_to_ingot_def(bet)
      if inventory_remove_def_total(sheet, ingot_def, ARENA_QUEUE_BET_INGOTS) ~= true then
        return nk.json_encode({ ok = false, err = "not_enough_ingots", ingot_def = ingot_def })
      end
      write_character_sheet(user_id, sheet)
    else
      local cfg = resource_tier_cfg(k, bet)
      local need = cfg ~= nil and (tonumber(cfg.bet) or 0) or 0
      if need <= 0 then
        return nk.json_encode({ ok = false, err = "bad_bet_tier" })
      end
      local max_retries = 5
      for attempt = 1, max_retries do
        local progress, version = read_pve_progress(user_id)
        local ore = tonumber(progress.ore) or 0
        local gold = tonumber(progress.gold) or 0
        if k == ARENA_KIND_ORE then
          if ore < need then
            return nk.json_encode({ ok = false, err = "not_enough_ore", required = need, have = ore })
          end
          progress.ore = math.max(0, ore - need)
        else
          if gold < need then
            return nk.json_encode({ ok = false, err = "not_enough_gold", required = need, have = gold })
          end
          progress.gold = math.max(0, gold - need)
        end
        local okw, erw = pcall(function()
          write_pve_progress(user_id, progress, version)
        end)
        if okw then
          break
        end
        local err_text = tostring(erw)
        if string.find(err_text, "version", 1, true) == nil or attempt == max_retries then
          nk.logger_error("arena join spend progress failed: " .. err_text)
          return nk.json_encode({ ok = false, err = "server_error" })
        end
      end
    end

    local disp = "Игрок"
    local ok_acc, acc = pcall(function()
      return nk.account_get_id(user_id)
    end)
    if ok_acc and acc ~= nil and acc.user ~= nil and acc.user.username ~= nil then
      disp = tostring(acc.user.username)
    end
    if disp == "" then
      disp = "Игрок"
    end

    bq.queue[#bq.queue + 1] = {
      uid = user_id,
      display = disp,
      bot = false,
      bet_tier = bet,
    }
    if bq.next_bot_at <= 0 then
      bq.next_bot_at = os.time() + math.random(5, 8)
    end
    arena_maybe_fill_bot(k, bet)
    arena_try_pop_queue_full(k, bet)

    local in_tournament = arena_load_user_tid(user_id) ~= nil
    local q_count = #bq.queue
    if in_tournament then
      -- Игрок уже вытолкнут в турнир — счётчик для его ставки.
      local bq_after = select(1, bet_queue_state(k, bet))
      q_count = #(bq_after.queue or {})
    end

    return nk.json_encode({
      ok = true,
      queue_count = q_count,
      queue_max = ARENA_QUEUE_MAX,
      bet_tier = bet,
      in_tournament = in_tournament,
      arena_kind = k,
    })
  end)
  if not ok then
    nk.logger_error("duel_arena_queue_join: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

local function duel_arena_queue_leave(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end
    local ok_epoch, err_epoch = guard_assert_client_epoch_matches(user_id, payload)
    if not ok_epoch then
      return nk.json_encode({ ok = false, err = err_epoch })
    end
    if arena_load_user_tid(user_id) ~= nil then
      return nk.json_encode({ ok = false, err = "in_tournament" })
    end
    local rk, removed_bet, bq, idx = find_user_in_any_queue(user_id)
    if rk == nil or bq == nil or idx == nil then
      return nk.json_encode({ ok = false, err = "not_in_queue" })
    end
    table.remove(bq.queue, idx)
    if #bq.queue == 0 then
      bq.next_bot_at = 0
    end

    if rk == ARENA_KIND_SMITH then
      local sheet = read_character_sheet(user_id)
      ensure_sheet_inventory_counts(sheet)
      local ingot_def = arena_bet_to_ingot_def(removed_bet)
      inventory_try_add(sheet, ingot_def, ARENA_QUEUE_BET_INGOTS)
      write_character_sheet(user_id, sheet)
    else
      local cfg = resource_tier_cfg(rk, removed_bet)
      local refund = cfg ~= nil and (tonumber(cfg.bet) or 0) or 0
      if refund > 0 then
        local max_retries = 5
        for attempt = 1, max_retries do
          local progress, version = read_pve_progress(user_id)
          if rk == ARENA_KIND_ORE then
            progress.ore = math.max(0, (tonumber(progress.ore) or 0) + refund)
          else
            progress.gold = math.max(0, (tonumber(progress.gold) or 0) + refund)
          end
          local okw, erw = pcall(function()
            write_pve_progress(user_id, progress, version)
          end)
          if okw then
            break
          end
          local err_text = tostring(erw)
          if string.find(err_text, "version", 1, true) == nil or attempt == max_retries then
            nk.logger_error("arena leave refund progress failed: " .. err_text)
            return nk.json_encode({ ok = false, err = "server_error" })
          end
        end
      end
    end
    return nk.json_encode({
      ok = true,
      queue_count = #bq.queue,
      queue_max = ARENA_QUEUE_MAX,
      bet_tier = removed_bet,
      arena_kind = rk,
    })
  end)
  if not ok then
    nk.logger_error("duel_arena_queue_leave: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

local function duel_arena_place_bet(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end
    local ok_epoch, err_epoch = guard_assert_client_epoch_matches(user_id, payload)
    if not ok_epoch then
      return nk.json_encode({ ok = false, err = err_epoch })
    end
    local p = {}
    if payload ~= nil and payload ~= "" then
      p = nk.json_decode(payload) or {}
    end
    local tid = arena_load_user_tid(user_id)
    if tid == nil then
      return nk.json_encode({ ok = false, err = "not_in_tournament" })
    end
    local T = arena_load_tournament(tid)
    if T == nil then
      arena_clear_human_tid(user_id)
      return nk.json_encode({ ok = false, err = "no_tournament" })
    end
    if T.eliminated[user_id] == true then
      return nk.json_encode({ ok = false, err = "eliminated" })
    end
    local rk = arena_open_side_bet_round(T)
    if rk == nil then
      return nk.json_encode({ ok = false, err = "bets_closed" })
    end
    local slot = tonumber(p.slot) or 0
    local plist = T[rk]
    if plist == nil or slot < 1 or slot > #plist then
      return nk.json_encode({ ok = false, err = "bad_slot" })
    end
    local pr = plist[slot]
    if pr.uid_a == user_id or pr.uid_b == user_id then
      return nk.json_encode({ ok = false, err = "own_pair" })
    end

    arena_ensure_side_bet_tables(T)
    local bag = T.side_bets[user_id]
    if type(bag) ~= "table" then
      bag = {}
      T.side_bets[user_id] = bag
    end
    local picks = bag[rk]
    if type(picks) ~= "table" then
      picks = {}
      bag[rk] = picks
    end

    local slot_key = tostring(slot)
    if p.clear == true then
      picks[slot_key] = nil
      arena_save_tournament(T)
      return nk.json_encode({
        ok = true,
        cleared = true,
        round = rk,
        slot = slot,
        my_bets = arena_pack_my_side_bets(T, user_id, rk),
        bet_score = tonumber(T.bet_score[user_id]) or 0,
      })
    end

    local side = string.lower(tostring(p.side or ""))
    local pick_uid = ""
    if side == "a" then
      pick_uid = tostring(pr.uid_a or "")
    elseif side == "b" then
      pick_uid = tostring(pr.uid_b or "")
    else
      return nk.json_encode({ ok = false, err = "bad_side" })
    end
    if pick_uid == "" then
      return nk.json_encode({ ok = false, err = "bad_side" })
    end
    picks[slot_key] = pick_uid
    arena_save_tournament(T)
    return nk.json_encode({
      ok = true,
      round = rk,
      slot = slot,
      side = side,
      pick_uid = pick_uid,
      my_bets = arena_pack_my_side_bets(T, user_id, rk),
      bet_score = tonumber(T.bet_score[user_id]) or 0,
    })
  end)
  if not ok then
    nk.logger_error("duel_arena_place_bet: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

local function duel_arena_queue_poll(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end
    local ok_epoch, err_epoch = guard_assert_client_epoch_matches(user_id, payload)
    if not ok_epoch then
      return nk.json_encode({ ok = false, err = err_epoch })
    end
    arena_tick_countdowns()
    local p = {}
    if payload ~= nil and payload ~= "" then
      p = nk.json_decode(payload) or {}
    end
    local requested_kind = normalize_kind(p.arena_kind)
    local requested_bet = string.lower(tostring(p.bet_tier or ""))

    local tnow = os.time()
    arena_runtime.last_tourn_storage_sweep = tnow
    arena_sweep_expired_done_storage()
    arena_sweep_dead_tournaments_storage()

    local in_queue = false
    local queue_kind = ""
    local queue_bet = ""
    local queue_count = 0
    local queue_max = ARENA_QUEUE_MAX

    local found_kind, found_bet, found_bq = find_user_in_any_queue(user_id)
    if found_kind ~= nil then
      in_queue = true
      queue_kind = found_kind
      queue_bet = found_bet
      queue_count = #(found_bq.queue or {})
    else
      queue_kind = requested_kind
      if requested_bet ~= "" then
        if requested_kind == ARENA_KIND_SMITH then
          if requested_bet ~= "green" and requested_bet ~= "blue" and requested_bet ~= "purple" then
            requested_bet = "green"
          end
        else
          local _cfg, normalized = resource_tier_cfg(requested_kind, requested_bet)
          requested_bet = normalized
        end
        local bq = select(1, bet_queue_state(requested_kind, requested_bet))
        queue_bet = requested_bet
        queue_count = #(bq.queue or {})
      else
        queue_bet = ""
        queue_count = 0
      end
    end

    local tournament = arena_json_for_user(user_id)
    if tournament == nil then
      tournament = { active = false, id = "" }
    end

    if queue_bet ~= "" then
      arena_maybe_fill_bot(queue_kind ~= "" and queue_kind or requested_kind, queue_bet)
    else
      arena_maybe_fill_bots_for_kind(queue_kind ~= "" and queue_kind or requested_kind)
    end

    return nk.json_encode({
      ok = true,
      queue_count = queue_count,
      queue_max = queue_max,
      in_queue = in_queue,
      queue_bet_tier = queue_bet,
      queue_kind = queue_kind,
      tournament = tournament,
    })
  end)
  if not ok then
    nk.logger_error("duel_arena_queue_poll: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end
  return {
    mirror_commit = mirror_commit,
    on_match_finished = on_match_finished,
    final_prize_for_kind_and_bet = final_prize_for_kind_and_bet,
    final_prize_for_winner = final_prize_for_winner,
    duel_arena_queue_join = duel_arena_queue_join,
    duel_arena_queue_leave = duel_arena_queue_leave,
    duel_arena_queue_poll = duel_arena_queue_poll,
    duel_arena_place_bet = duel_arena_place_bet,
  }
end
