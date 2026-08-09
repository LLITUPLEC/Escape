-- Цели / вход / награды состязаний (Storage).
-- collection=duel_match3_contest_goals, key=config
-- Сейчас: race; позже — другие режимы в том же документе.
return function(deps)
  local nk = deps.nk
  local CFG = deps.CFG
  local decode_storage_value = deps.decode_storage_value

  local _cache_doc = nil
  local _cache_t = 0

  local function default_entry()
    return {
      { resource = "matter", amount = math.max(1, math.floor(tonumber(CFG.RACE_ENTRY_MATTER) or 2)) },
    }
  end

  local function default_rewards()
    return {
      { resource = "xp", amount = math.max(0, math.floor(tonumber(CFG.RACE_WIN_XP) or 200)) },
      { resource = "matter", amount = math.max(0, math.floor(tonumber(CFG.RACE_WIN_MATTER) or 10)) },
    }
  end

  local function default_doc()
    return {
      race = {
        goal_mana = tonumber(CFG.RACE_GOAL_MANA) or 200,
        entry = default_entry(),
        rewards = default_rewards(),
      },
    }
  end

  local function normalize_resource_name(raw)
    local r = string.lower(tostring(raw or ""))
    if r == "exp" then return "xp" end
    if r == "ingot" then return "ingots" end
    if r == "key" then return "keys" end
    if r == "blueprints" then return "blueprint" end
    if r == "recipes" then return "recipe" end
    if r == "tesseracts" then return "tesseract" end
    return r
  end

  --- Нормализует список {resource, amount, id?} — пропускает нулевые/битые.
  local function normalize_lines(raw_list, fallback)
    local out = {}
    if type(raw_list) == "table" then
      for _, row in ipairs(raw_list) do
        if type(row) == "table" then
          local resource = normalize_resource_name(row.resource or row.type or row.kind)
          local amount = math.floor(tonumber(row.amount or row.count or row.qty) or 0)
          local id = tostring(row.id or row.key_id or row.item_id or row.blueprint or "")
          if resource ~= "" and amount > 0 then
            out[#out + 1] = { resource = resource, amount = amount, id = id }
          end
        end
      end
    end
    if #out == 0 and type(fallback) == "table" then
      for _, row in ipairs(fallback) do
        out[#out + 1] = {
          resource = tostring(row.resource),
          amount = math.max(0, math.floor(tonumber(row.amount) or 0)),
          id = tostring(row.id or ""),
        }
      end
    end
    return out
  end

  local function read_doc()
    local now = os.time()
    local ttl = math.max(5, tonumber(CFG.CONTEST_GOALS_CACHE_TTL_SEC) or 30)
    if _cache_doc ~= nil and (now - _cache_t) < ttl then
      return _cache_doc
    end

    local ok_read, rows = pcall(function()
      return nk.storage_read({
        {
          collection = CFG.CONTEST_GOALS_COLLECTION,
          key = CFG.CONTEST_GOALS_KEY,
          user_id = CFG.CONTEST_GOALS_STORAGE_USER_ID,
        },
      })
    end)
    _cache_t = now
    if not ok_read or rows == nil or #rows == 0 then
      _cache_doc = default_doc()
      return _cache_doc
    end

    local doc = decode_storage_value(rows[1])
    if type(doc) ~= "table" then
      _cache_doc = default_doc()
      return _cache_doc
    end
    if type(doc.race) ~= "table" then
      doc.race = default_doc().race
    else
      local race = doc.race
      if tonumber(race.goal_mana) == nil then
        race.goal_mana = tonumber(CFG.RACE_GOAL_MANA) or 200
      end
      -- Совместимость: старый формат только с goal_mana / плоские поля.
      if type(race.entry) ~= "table" then
        if tonumber(race.entry_matter) ~= nil then
          race.entry = { { resource = "matter", amount = math.floor(tonumber(race.entry_matter)) } }
        else
          race.entry = default_entry()
        end
      end
      if type(race.rewards) ~= "table" then
        local rw = {}
        if tonumber(race.reward_xp) ~= nil or tonumber(race.win_xp) ~= nil then
          rw[#rw + 1] = { resource = "xp", amount = math.floor(tonumber(race.reward_xp or race.win_xp) or 0) }
        end
        if tonumber(race.reward_matter) ~= nil or tonumber(race.win_matter) ~= nil then
          rw[#rw + 1] = { resource = "matter", amount = math.floor(tonumber(race.reward_matter or race.win_matter) or 0) }
        end
        if tonumber(race.reward_gold) ~= nil then
          rw[#rw + 1] = { resource = "gold", amount = math.floor(tonumber(race.reward_gold) or 0) }
        end
        if tonumber(race.reward_ore) ~= nil then
          rw[#rw + 1] = { resource = "ore", amount = math.floor(tonumber(race.reward_ore) or 0) }
        end
        race.rewards = #rw > 0 and rw or default_rewards()
      end
      race.entry = normalize_lines(race.entry, default_entry())
      race.rewards = normalize_lines(race.rewards, default_rewards())
    end
    _cache_doc = doc
    return doc
  end

  local M = {}

  function M.race_goal_mana()
    local doc = read_doc()
    local g = tonumber(doc.race and doc.race.goal_mana) or tonumber(CFG.RACE_GOAL_MANA) or 200
    return math.max(1, math.floor(g))
  end

  function M.race_max_mana(goal)
    local g = math.max(1, math.floor(tonumber(goal) or M.race_goal_mana()))
    local mult = tonumber(CFG.RACE_MAX_MANA_MULT) or 1.2
    return math.max(g, math.floor(g * mult + 1e-9))
  end

  function M.race_entry_costs()
    local doc = read_doc()
    return normalize_lines(doc.race and doc.race.entry, default_entry())
  end

  function M.race_rewards()
    local doc = read_doc()
    return normalize_lines(doc.race and doc.race.rewards, default_rewards())
  end

  --- Публичный конфиг для клиента (модалка входа / HUD).
  function M.race_public_config()
    return {
      goal_mana = M.race_goal_mana(),
      entry = M.race_entry_costs(),
      rewards = M.race_rewards(),
      mana_bonus_every = math.max(1, math.floor(tonumber(CFG.RACE_MANA_BONUS_EVERY_ACTIONS) or 5)),
    }
  end

  --- Свернуть список наград в плоские поля (совместимость с award_victory / GAME_OVER).
  function M.flatten_reward_lines(lines)
    local out = {
      xp = 0, gold = 0, ore = 0, matter = 0, ingots = 0, tesseract = 0,
      key_id = "", key_amount = 0, blueprint = "", recipe_item_id = "",
    }
    if type(lines) ~= "table" then return out end
    for _, row in ipairs(lines) do
      local r = tostring(row.resource or "")
      local amt = math.max(0, math.floor(tonumber(row.amount) or 0))
      local id = tostring(row.id or "")
      if r == "xp" then out.xp = out.xp + amt
      elseif r == "gold" then out.gold = out.gold + amt
      elseif r == "ore" then out.ore = out.ore + amt
      elseif r == "matter" then out.matter = out.matter + amt
      elseif r == "ingots" then out.ingots = out.ingots + amt
      elseif r == "tesseract" then out.tesseract = out.tesseract + amt
      elseif r == "keys" or r == "key" then
        if id ~= "" then
          out.key_id = id
          out.key_amount = out.key_amount + amt
        end
      elseif r == "blueprint" then
        if id ~= "" then out.blueprint = id end
      elseif r == "recipe" then
        if id ~= "" then out.recipe_item_id = id end
      end
    end
    return out
  end

  return M
end
