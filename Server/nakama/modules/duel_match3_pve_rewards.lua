-- Freeze loot once, then atomically commit its delivery and a durable receipt.
local nk = require("nakama")
local M = {}

function M.new(d)
  local R = {}
  local function pending(err, persisted)
    return { reward_xp = 0, reward_gold = 0, reward_ore = 0, reward_matter = 0,
      reward_ingots = 0, reward_tesseract = 0, reward_recipe_item_id = "", reward_blueprint = "",
      reward_pending = true, intent_persisted = persisted == true,
      err = err or "reward_pending", delivered_items = {}, pending_items = {} }
  end
  local function add_item(plan, def_id, amount, kind)
    amount = math.max(0, math.floor(tonumber(amount) or 0))
    if amount > 0 and def_id and def_id ~= "" then
      plan.items[#plan.items + 1] = { def_id = def_id, amount = amount, kind = kind }
    end
  end

  function R.freeze(bot_id, run, run_id, uid, outcome)
    run = run or {}
    local diff = d.normalize_difficulty(run.difficulty)
    local bot = assert(d.get_bot_profile(bot_id, diff), "bot_catalog_unavailable")
    local floor = d.clamp_floor(run.floor or bot.floor or 1)
    local boss = bot.is_boss == true or d.is_boss_floor(floor)
    local mul = d.reward_multiplier(diff)
    local xp = d.apply_reward_xp(math.ceil((tonumber(bot.reward_xp) or 0) * mul))
    local plan = { schema = 1, run_id = run_id, launch_token = run.launch_token,
      bot_id = bot_id, difficulty = diff, floor = floor, is_boss = boss,
      stat_mul = d.stat_multiplier(diff), items = {}, reward_xp = xp,
      reward_gold = math.ceil((tonumber(bot.reward_gold) or 0) * mul),
      reward_ore = math.ceil((tonumber(bot.reward_ore) or 0) * mul), reward_matter = 0,
      reward_key_id = tostring(bot.reward_key_id or ""), reward_key_amount = math.max(0, tonumber(bot.reward_key_amount) or 0),
      next_spawn_at = os.time() + d.respawn_seconds(boss), next_affix = d.next_affix(floor, run.affix),
      achievement_deltas = tostring(bot_id) == "mine_2" and { ["pve.kill.mine_2"] = 1 } or {} }
    for stat, delta in pairs(outcome and outcome.achievement_deltas or {}) do
      plan.achievement_deltas[stat] = (plan.achievement_deltas[stat] or 0) + delta
    end
    plan.match_stats = outcome and outcome.match_stats or nil
    -- Trusted server snapshot from the finish event; the frozen plan survives
    -- delayed claim/recovery without extending a scroll or multiplying other rewards.
    plan.currency_gold_multiplier=math.max(1,tonumber(outcome and outcome.currency_gold_multiplier) or 1)
    plan.currency_ore_multiplier=math.max(1,tonumber(outcome and outcome.currency_ore_multiplier) or 1)
    plan.reward_gold=math.ceil(plan.reward_gold*plan.currency_gold_multiplier)
    plan.reward_ore=math.ceil(plan.reward_ore*plan.currency_ore_multiplier)
    local lo = math.max(0, tonumber(bot.reward_matter_min) or 0)
    local hi = math.max(lo, tonumber(bot.reward_matter_max) or lo)
    if hi > 0 then plan.reward_matter = math.max(0, math.ceil(math.random(lo, hi) * mul)) end
    local ingots = math.max(0, math.ceil((tonumber(bot.reward_ingots) or 0) * mul))
    if ingots > 0 then
      if boss then ingots = ingots * 2
      elseif math.random() >= d.ingot_drop_chance(floor) then ingots = 0 end
    end
    add_item(plan, d.ingot_def(diff), ingots, "ingots")
    if (tonumber(bot.reward_tesseract_chance) or 0) > 0 and math.random() < tonumber(bot.reward_tesseract_chance) then
      add_item(plan, "tesseract", 1, "tesseract")
    end
    if d.uses_recipe_drop(floor, boss) then
      -- Learned-state and random roll are read only while freezing this result.
      local recipe = d.pick_recipe(uid, floor, boss, diff)
      if recipe and recipe ~= "" then
        add_item(plan, recipe, 1, "recipe")
        plan.blueprint_rarity = d.recipe_color(diff)
      end
    else
      local rarity = tostring(bot.reward_blueprint or "")
      local recipe = ({ green = "recipe_green", blue = "recipe_blue", purple = "recipe_purple", gold = "recipe_gold" })[rarity]
      add_item(plan, recipe, recipe and 1 or 0, "recipe")
      plan.blueprint_rarity = rarity
    end
    return plan
  end

  function R.build(uid, id, plan)
    assert(plan.schema == 1 and type(plan.run_id) == "string" and type(plan.items) == "table", "invalid_pve_plan")
    local progress, version = d.read_progress(uid)
    local sheet = d.read_sheet(uid)
    local defs = d.get_defs()
    progress.xp = (tonumber(progress.xp) or 0) + plan.reward_xp
    progress.gold = (tonumber(progress.gold) or 0) + plan.reward_gold
    progress.ore = (tonumber(progress.ore) or 0) + plan.reward_ore
    progress.matter = (tonumber(progress.matter) or 0) + plan.reward_matter
    if plan.reward_key_id ~= "" and plan.reward_key_amount > 0 then
      progress.key_items = progress.key_items or d.empty_keys()
      progress.key_items[plan.reward_key_id] = (tonumber(progress.key_items[plan.reward_key_id]) or 0) + plan.reward_key_amount
    end
    if plan.blueprint_rarity and plan.blueprint_rarity ~= "" then d.add_blueprint(progress, plan.blueprint_rarity) end
    progress.level = d.level_from_xp(progress.xp)
    progress.defeated = type(progress.defeated) == "table" and progress.defeated or {}
    progress.defeated[plan.bot_id] = (tonumber(progress.defeated[plan.bot_id]) or 0) + 1
    progress.mine = type(progress.mine) == "table" and progress.mine or {}
    progress.mine.current_difficulty = plan.difficulty
    progress.mine.unlocked = d.normalize_unlocked(progress.mine.unlocked, 1)
    progress.mine.floor_states = type(progress.mine.floor_states) == "table" and progress.mine.floor_states or {}
    local floor_key = d.floor_key(plan.difficulty, plan.floor)
    local floor_state = progress.mine.floor_states[floor_key] or {}
    floor_state.next_spawn_at, floor_state.last_affix = plan.next_spawn_at, plan.next_affix
    floor_state.wins = (tonumber(floor_state.wins) or 0) + 1
    progress.mine.floor_states[floor_key] = floor_state
    if plan.floor >= d.max_floor and plan.is_boss then
      if plan.difficulty == "easy" and progress.mine.unlocked.medium <= 0 then progress.mine.unlocked.medium = 1
      elseif plan.difficulty == "medium" and progress.mine.unlocked.hard <= 0 then progress.mine.unlocked.hard = 1 end
    end
    local receipt = { reward_xp = plan.reward_xp, reward_gold = plan.reward_gold, reward_ore = plan.reward_ore,
      reward_matter = plan.reward_matter, reward_key_id = plan.reward_key_id, reward_key_amount = plan.reward_key_amount,
      reward_ingots = 0, reward_tesseract = 0, reward_recipe_item_id = "", reward_blueprint = "",
      delivered_items = {}, pending_items = {}, item_loot_pending_count = 0,
      difficulty = plan.difficulty, floor = plan.floor, stat_mul = plan.stat_mul, level = progress.level,
      xp = progress.xp, gold = progress.gold, ore = progress.ore, matter = progress.matter }
    for _, item in ipairs(plan.items) do
      local delivery = d.inventory().add_or_queue(sheet, item.def_id, item.amount, defs)
      assert(type(delivery) == "table" and delivery.delivered + delivery.pending == item.amount, "invalid_loot_delivery")
      if delivery.delivered > 0 then
        receipt.delivered_items[#receipt.delivered_items + 1] = { def_id = item.def_id, amount = delivery.delivered }
        if item.kind == "ingots" then receipt.reward_ingots = receipt.reward_ingots + delivery.delivered end
        if item.kind == "tesseract" then receipt.reward_tesseract = receipt.reward_tesseract + delivery.delivered end
        if item.kind == "recipe" then receipt.reward_recipe_item_id, receipt.reward_blueprint = item.def_id, plan.blueprint_rarity or "" end
      end
      if delivery.pending > 0 then
        receipt.pending_items[#receipt.pending_items + 1] = { def_id = item.def_id, amount = delivery.pending }
        receipt.item_loot_pending_count = receipt.item_loot_pending_count + delivery.pending
      end
    end
    d.inventory().sync_resource_counters(sheet, progress, defs)
    local writes = d.build_sheet_writes(uid, sheet, progress, version)
    if plan.launch_token and plan.launch_token ~= "" and d.consume_writes then
      for _, op in ipairs(d.consume_writes(uid, plan.launch_token, plan.run_id)) do writes[#writes + 1] = op end
    end
    return writes, receipt
  end

  function R.award(uid, bot_id, epoch, run, run_id, outcome, attempt)
    attempt = attempt or {}
    local ok, result = pcall(function()
      assert(type(run_id) == "string" and run_id ~= "", "result_id_required")
      assert((attempt.uid == nil or attempt.uid == uid) and (attempt.run_id == nil or attempt.run_id == run_id), "reward_attempt_mismatch")
      attempt.uid, attempt.run_id = uid, run_id
      local id, journal = run_id .. ":pve", d.journal()
      local existing = journal.read(uid, id)
      if not existing then
        if d.is_stale(uid, epoch) then return { reward_xp = 0, session_stale = true } end
        -- Read the original sheet once for the learned-recipe drop rule.
        local plan = attempt.plan
        if not plan then plan = R.freeze(bot_id, run, run_id, uid, outcome); attempt.plan = plan end
        local ticket=attempt.journal_ticket or (journal.ticket and journal.ticket(uid,id))
        local prepared, err = journal.prepare(uid, id, "pve", plan,ticket)
        if not prepared then
          existing = journal.read(uid, id)
          if not existing then return pending(err) end
        end
      end
      attempt.intent_persisted = true
      if existing and existing.status == "aborted" then
        local refunded = pending("launch_refunded", true)
        refunded.reward_pending = false
        return refunded
      end
      local committed, err, receipt = journal.commit(uid, id, function(plan) return R.build(uid, id, plan) end)
      if not committed then return pending(err, true) end
      local effects_ok = journal.deliver_effects(uid, id)
      receipt.achievement_sync_pending = not effects_ok
      receipt.intent_persisted = true
      return receipt
    end)
    if ok then return result end
    nk.logger_error("pve reward deferred: " .. tostring(result))
    return pending("storage_unavailable", attempt.intent_persisted)
  end
  return R
end

return M
