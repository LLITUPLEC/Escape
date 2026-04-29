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

--- ═══════════════════════════════════════════════════════════════════════════
--- Арена: турнир на 8 участников (очередь + сетка + интеграция с duel_match3).
--- Отключение ботов: см. arena_runtime.allow_bot_fill ниже.
--- ═══════════════════════════════════════════════════════════════════════════

local ARENA_QUEUE_MAX = 8
local ARENA_COUNTDOWN_SEC = 20
local ARENA_QUEUE_BET_INGOTS = 50

-- Persisted state (RPC and match loops can run in different Lua contexts).
local STORAGE_COLL_TOURN = "arena_tournament"
local STORAGE_COLL_USER_TID = "arena_user_tid"
local STORAGE_USER_TID_KEY = "tid"
local STORAGE_SYSTEM_USER_ID = "00000000-0000-0000-0000-000000000000"
local TOURNAMENT_TTL_SEC = 15 * 60

local arena_runtime = {
  queue = {},
  queue_bet_tier = "",
  next_bot_at = 0,
  tournaments = {},
  user_tid = {},
  allow_bot_fill = true,
  last_tourn_storage_sweep = 0,
}

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
    if v.phase == "done" and tonumber(v.done_at) ~= nil and os.time() > tonumber(v.done_at) + TOURNAMENT_TTL_SEC then
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
  local now = os.time()
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
        local da = tonumber(v.done_at)
        if da ~= nil and now > da + TOURNAMENT_TTL_SEC then
          arena_delete_tournament(row.key)
        end
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
    storage_write_one(user_id, STORAGE_COLL_USER_TID, STORAGE_USER_TID_KEY, { tid = tid })
  end
end

local function arena_load_user_tid(user_id)
  if user_id == nil or user_id == "" then return nil end
  -- Always refresh from storage: user_tid is modified by match context on elimination.
  local v = storage_read_one(user_id, STORAGE_COLL_USER_TID, STORAGE_USER_TID_KEY)
  if v ~= nil and v.tid ~= nil and v.tid ~= "" then
    arena_runtime.user_tid[user_id] = v.tid
    return v.tid
  end
  arena_runtime.user_tid[user_id] = nil
  return nil
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
  arena_save_tournament(T)
end

--- Награды финала турнира (золото/руда) — та же шкала, что в arena_grant_final_progress и в OP_GAME_OVER.
local function final_prize_for_bet_tier(bet_tier)
  local ore_g, gold_g = arena_final_prize_ore_gold(bet_tier)
  return tonumber(gold_g) or 0, tonumber(ore_g) or 0
end

local function arena_grant_final_progress(user_id, bet_tier)
  local ore_g, gold_g = arena_final_prize_ore_gold(bet_tier)
  local max_retries = 5
  for attempt = 1, max_retries do
    local progress, version = read_pve_progress(user_id)
    progress.ore = math.max(0, (tonumber(progress.ore) or 0) + ore_g)
    progress.gold = math.max(0, (tonumber(progress.gold) or 0) + gold_g)
    local ok, err = pcall(function()
      write_pve_progress(user_id, progress, version)
    end)
    if ok then
      return true
    end
    local err_text = tostring(err)
    if string.find(err_text, "version", 1, true) == nil or attempt == max_retries then
      nk.logger_error("arena_grant_final_progress: " .. err_text)
      return false
    end
  end
  return false
end

local function arena_clear_human_tid(user_id)
  arena_runtime.user_tid[user_id] = nil
  arena_save_user_tid(user_id, nil)
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
  if rk == "qf" then
    local winners = arena_collect_winners(T, "qf")
    if #winners ~= 4 then
      return
    end
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
      arena_grant_final_progress(w, T.bet_tier)
    end
    T.phase = "done"
    T.done_at = os.time()
    for uid, meta in pairs(T.parts) do
      if meta.bot ~= true then
        arena_clear_human_tid(uid)
      end
    end
    arena_save_tournament(T)
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
  if loser_uid ~= nil then
    if pr.uid_a == loser_uid then pr.hp_a = 0 end
    if pr.uid_b == loser_uid then pr.hp_b = 0 end
  end
  if loser_uid ~= nil and T.parts[loser_uid] ~= nil and T.parts[loser_uid].bot ~= true then
    T.eliminated[loser_uid] = true
    arena_clear_human_tid(loser_uid)
  end
  arena_on_pair_completed(T, rk)
  arena_save_tournament(T)
end

local function arena_tick_countdowns()
  local now = os.time()
  for _, T in pairs(arena_runtime.tournaments) do
    if T.phase == "countdown" and tonumber(T.countdown_until) ~= nil and now >= tonumber(T.countdown_until) then
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

local function arena_start_tournament_from_entries(entries)
  local tid = nk.uuid_v4()
  local bet = entries[1] ~= nil and entries[1].bet_tier or "green"
  local T = {
    id = tid,
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
  }
  arena_shuffle_inplace(entries)
  for _, p in ipairs(entries) do
    T.parts[p.uid] = { display = p.display, bot = p.bot == true }
    if p.bot ~= true then
      arena_runtime.user_tid[p.uid] = tid
      arena_save_user_tid(p.uid, tid)
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

local function arena_try_pop_queue_full()
  local q = arena_runtime.queue
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
  arena_runtime.queue_bet_tier = ""
  arena_runtime.next_bot_at = 0
  arena_start_tournament_from_entries(batch)
end

local function arena_maybe_fill_bot()
  if arena_runtime.allow_bot_fill ~= true then
    return
  end
  local q = arena_runtime.queue
  if #q >= ARENA_QUEUE_MAX or #q == 0 then
    return
  end
  local now = os.time()
  if arena_runtime.next_bot_at <= 0 then
    arena_runtime.next_bot_at = now + math.random(5, 8)
    return
  end
  if now < arena_runtime.next_bot_at then
    return
  end
  arena_runtime.next_bot_at = now + math.random(5, 8)
  q[#q + 1] = {
    uid = arena_make_bot_uid(),
    display = arena_random_bot_display(),
    bot = true,
    bet_tier = arena_runtime.queue_bet_tier,
  }
  arena_try_pop_queue_full()
end

local function arena_json_for_user(user_id)
  arena_tick_countdowns()
  arena_maybe_fill_bot()
  local tid = arena_load_user_tid(user_id)
  if tid == nil then
    return nil
  end
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

  return {
    active = true,
    id = tid,
    eliminated = false,
    phase = T.phase,
    bet_tier = T.bet_tier,
    countdown_left = countdown_left,
    next_round = T.next_round,
    join_match_id = join_mid,
    join_opponent_is_bot = join_opponent_is_bot,
    qf = pack_pairs("qf"),
    sf = pack_pairs("sf"),
    final_pairs = pack_pairs("final"),
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
    local bet = string.lower(tostring(p.bet_tier or "green"))
    if bet ~= "green" and bet ~= "blue" and bet ~= "purple" then
      bet = "green"
    end
    for _, qe in ipairs(arena_runtime.queue) do
      if qe.bot ~= true and qe.uid == user_id then
        return nk.json_encode({ ok = false, err = "already_in_queue" })
      end
    end
    if arena_load_user_tid(user_id) ~= nil then
      return nk.json_encode({ ok = false, err = "already_in_tournament" })
    end
    if arena_runtime.queue_bet_tier ~= "" and arena_runtime.queue_bet_tier ~= bet then
      return nk.json_encode({ ok = false, err = "bet_tier_mismatch" })
    end
    local sheet = read_character_sheet(user_id)
    ensure_sheet_inventory_counts(sheet)
    local ingot_def = arena_bet_to_ingot_def(bet)
    if inventory_remove_def_total(sheet, ingot_def, ARENA_QUEUE_BET_INGOTS) ~= true then
      return nk.json_encode({ ok = false, err = "not_enough_ingots", ingot_def = ingot_def })
    end
    write_character_sheet(user_id, sheet)

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

    if arena_runtime.queue_bet_tier == "" then
      arena_runtime.queue_bet_tier = bet
    end
    arena_runtime.queue[#arena_runtime.queue + 1] = {
      uid = user_id,
      display = disp,
      bot = false,
      bet_tier = bet,
    }
    if arena_runtime.next_bot_at <= 0 then
      arena_runtime.next_bot_at = os.time() + math.random(5, 8)
    end
    arena_maybe_fill_bot()
    arena_try_pop_queue_full()

    -- После попа очередь может быть пуста — клиенту нужно различать «0 в очереди, но вы уже в турнире».
    local in_tournament = arena_load_user_tid(user_id) ~= nil

    return nk.json_encode({
      ok = true,
      queue_count = #arena_runtime.queue,
      queue_max = ARENA_QUEUE_MAX,
      bet_tier = bet,
      in_tournament = in_tournament,
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
    local removed_bet = ""
    local q = arena_runtime.queue
    for i = 1, #q do
      local e = q[i]
      if e.bot ~= true and e.uid == user_id then
        removed_bet = e.bet_tier or arena_runtime.queue_bet_tier
        table.remove(q, i)
        break
      end
    end
    if removed_bet == "" then
      return nk.json_encode({ ok = false, err = "not_in_queue" })
    end
    local sheet = read_character_sheet(user_id)
    ensure_sheet_inventory_counts(sheet)
    local ingot_def = arena_bet_to_ingot_def(removed_bet)
    inventory_try_add(sheet, ingot_def, ARENA_QUEUE_BET_INGOTS)
    write_character_sheet(user_id, sheet)
    if #q == 0 then
      arena_runtime.queue_bet_tier = ""
      arena_runtime.next_bot_at = 0
    end
    return nk.json_encode({ ok = true, queue_count = #q, queue_max = ARENA_QUEUE_MAX })
  end)
  if not ok then
    nk.logger_error("duel_arena_queue_leave: " .. tostring(result))
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
    arena_maybe_fill_bot()

    local tnow = os.time()
    if tnow - (arena_runtime.last_tourn_storage_sweep or 0) >= 60 then
      arena_runtime.last_tourn_storage_sweep = tnow
      arena_sweep_expired_done_storage()
    end

    local in_queue = false
    local bet = arena_runtime.queue_bet_tier
    for _, qe in ipairs(arena_runtime.queue) do
      if qe.bot ~= true and qe.uid == user_id then
        in_queue = true
        bet = qe.bet_tier or bet
        break
      end
    end

    local tournament = arena_json_for_user(user_id)
    if tournament == nil then
      tournament = { active = false, id = "" }
    end
    return nk.json_encode({
      ok = true,
      queue_count = #arena_runtime.queue,
      queue_max = ARENA_QUEUE_MAX,
      in_queue = in_queue,
      queue_bet_tier = arena_runtime.queue_bet_tier,
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
    final_prize_for_bet_tier = final_prize_for_bet_tier,
    duel_arena_queue_join = duel_arena_queue_join,
    duel_arena_queue_leave = duel_arena_queue_leave,
    duel_arena_queue_poll = duel_arena_queue_poll,
  }
end
