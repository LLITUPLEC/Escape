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
local ARENA_COUNTDOWN_SEC = 60

local arena_runtime = {
  queue = {},
  queue_bet_tier = "",
  next_bot_at = 0,
  tournaments = {},
  user_tid = {},
  allow_bot_fill = true,
}

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
    return 1200, 1200
  end
  if b == "purple" then
    return 2400, 2400
  end
  return 600, 600
end

local function arena_make_bot_uid()
  return "arena-bot-" .. nk.uuid_v4()
end

local function arena_random_bot_display()
  local names = ARENA_BOT_DISPLAY_NAMES
  return names[math.random(1, #names)]
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
  local T = arena_runtime.tournaments[tid]
  if T == nil then
    return
  end
  local rk = am.round or "qf"
  local idx = tonumber(am.slot_index) or 1
  local plist = T[rk]
  if plist == nil then
    return
  end
  local pr = plist[idx]
  if pr == nil then
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
      local mid = try_match_create({
        mode = "pve",
        owner_user_id = human_uid,
        bot_id = "arena_bot",
        bot_user_id = make_bot_user_id("arena_bot"),
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
    arena_begin_next_round(T, "sf", winners)
    return
  end
  if rk == "sf" then
    local winners = arena_collect_winners(T, "sf")
    if #winners ~= 2 then
      return
    end
    arena_begin_next_round(T, "final", winners)
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
    for uid, meta in pairs(T.parts) do
      if meta.bot ~= true then
        arena_clear_human_tid(uid)
      end
    end
  end
end

local function on_match_finished(state, winner_uid)
  local am = state.arena_mirror
  if am == nil then
    return
  end
  local tid = am.tournament_id
  local T = arena_runtime.tournaments[tid]
  if T == nil then
    return
  end
  local rk = am.round or "qf"
  local idx = tonumber(am.slot_index) or 1
  local plist = T[rk]
  if plist == nil then
    return
  end
  local pr = plist[idx]
  if pr == nil then
    return
  end
  local loser_uid = nil
  if pr.uid_a == winner_uid then
    loser_uid = pr.uid_b
  elseif pr.uid_b == winner_uid then
    loser_uid = pr.uid_a
  end
  pr.status = "done"
  pr.winner_uid = winner_uid
  if loser_uid ~= nil and T.parts[loser_uid] ~= nil and T.parts[loser_uid].bot ~= true then
    T.eliminated[loser_uid] = true
    arena_clear_human_tid(loser_uid)
  end
  arena_on_pair_completed(T, rk)
end

local function arena_tick_countdowns()
  local now = os.time()
  for _, T in pairs(arena_runtime.tournaments) do
    if T.phase == "countdown" and tonumber(T.countdown_until) ~= nil and now >= tonumber(T.countdown_until) then
      T.phase = "qf"
      arena_spawn_round_if_pending(T, "qf")
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
    end
  end
  local slots = {}
  for _, p in ipairs(entries) do
    slots[#slots + 1] = { uid = p.uid, display = p.display, bot = p.bot == true }
  end
  T.qf = arena_build_round_from_slots(slots)
  arena_runtime.tournaments[tid] = T
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
  local tid = arena_runtime.user_tid[user_id]
  if tid == nil then
    return nil
  end
  local T = arena_runtime.tournaments[tid]
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
    if arena_runtime.user_tid[user_id] ~= nil then
      return nk.json_encode({ ok = false, err = "already_in_tournament" })
    end
    if arena_runtime.queue_bet_tier ~= "" and arena_runtime.queue_bet_tier ~= bet then
      return nk.json_encode({ ok = false, err = "bet_tier_mismatch" })
    end
    local sheet = read_character_sheet(user_id)
    ensure_sheet_inventory_counts(sheet)
    local ingot_def = arena_bet_to_ingot_def(bet)
    if inventory_remove_def_total(sheet, ingot_def, 100) ~= true then
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
    local in_tournament = arena_runtime.user_tid[user_id] ~= nil

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
    if arena_runtime.user_tid[user_id] ~= nil then
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
    inventory_try_add(sheet, ingot_def, 100)
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
    duel_arena_queue_join = duel_arena_queue_join,
    duel_arena_queue_leave = duel_arena_queue_leave,
    duel_arena_queue_poll = duel_arena_queue_poll,
  }
end
