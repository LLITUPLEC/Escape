-- Фабрика PveMineCost для duel_match3.lua: require("modules.duel_match3_pve_mine_cost") и вызов с deps.
return function(deps)
  local clamp_int = deps.clamp_int
  local pve_energy_max_for_user = deps.pve_energy_max_for_user
  local empty_key_items = deps.empty_key_items

  local P = {
    DEFAULT_ATTACK = { { resource = "energy", amount = 15 } },
    DEFAULT_BANISH = { { resource = "energy", amount = 5 } },
  }

  function P.clone_list(list)
    local out = {}
    if type(list) ~= "table" then return out end
    for i, e in ipairs(list) do
      if type(e) == "table" then
        out[#out + 1] = {
          resource = tostring(e.resource or ""),
          amount = clamp_int(tonumber(e.amount) or 0, 0, nil),
        }
      end
    end
    return out
  end

  function P.normalize(raw, fallback)
    if type(raw) ~= "table" or #raw == 0 then
      return P.clone_list(fallback)
    end
    local out = {}
    for _, e in ipairs(raw) do
      if type(e) == "table" then
        local res = string.lower(tostring(e.resource or e.id or ""))
        local amt = clamp_int(tonumber(e.amount) or 0, 0, nil)
        if res ~= "" and amt > 0 then
          out[#out + 1] = { resource = res, amount = amt }
        end
      end
    end
    if #out == 0 then
      return P.clone_list(fallback)
    end
    return out
  end

  function P.amount(progress, user_id, res_id)
    local r = string.lower(tostring(res_id or ""))
    if r == "energy" then
      local em = pve_energy_max_for_user(user_id)
      return clamp_int(progress.energy, 0, em)
    elseif r == "gold" then
      return math.max(0, tonumber(progress.gold) or 0)
    elseif r == "ore" then
      return math.max(0, tonumber(progress.ore) or 0)
    elseif r == "matter" then
      return math.max(0, tonumber(progress.matter) or 0)
    elseif r == "ingots" then
      return math.max(0, tonumber(progress.ingots) or 0)
    elseif r == "miner_key" then
      progress.key_items = progress.key_items or empty_key_items()
      return math.max(0, tonumber(progress.key_items.miner_key) or 0)
    elseif r == "dark_key" then
      progress.key_items = progress.key_items or empty_key_items()
      return math.max(0, tonumber(progress.key_items.dark_key) or 0)
    end
    return 0
  end

  function P.spend(progress, res_id, amount)
    local r = string.lower(tostring(res_id or ""))
    amount = math.max(0, math.floor(tonumber(amount) or 0))
    if amount <= 0 then return end
    if r == "energy" then
      progress.energy = (tonumber(progress.energy) or 0) - amount
      progress.energy_updated_at = os.time()
    elseif r == "gold" then
      progress.gold = math.max(0, (tonumber(progress.gold) or 0) - amount)
    elseif r == "ore" then
      progress.ore = math.max(0, (tonumber(progress.ore) or 0) - amount)
    elseif r == "matter" then
      progress.matter = math.max(0, (tonumber(progress.matter) or 0) - amount)
    elseif r == "ingots" then
      progress.ingots = math.max(0, (tonumber(progress.ingots) or 0) - amount)
    elseif r == "miner_key" then
      progress.key_items = progress.key_items or empty_key_items()
      progress.key_items.miner_key = math.max(0, (tonumber(progress.key_items.miner_key) or 0) - amount)
    elseif r == "dark_key" then
      progress.key_items = progress.key_items or empty_key_items()
      progress.key_items.dark_key = math.max(0, (tonumber(progress.key_items.dark_key) or 0) - amount)
    end
  end

  function P.can_afford(progress, user_id, cost_list)
    if type(cost_list) ~= "table" then return true end
    for _, e in ipairs(cost_list) do
      if type(e) == "table" then
        local res = string.lower(tostring(e.resource or ""))
        local need = clamp_int(tonumber(e.amount) or 0, 0, nil)
        if res ~= "" and need > 0 then
          local have = P.amount(progress, user_id, res)
          if have < need then
            return false, res, need, have
          end
        end
      end
    end
    return true
  end

  function P.apply_list(progress, cost_list)
    if type(cost_list) ~= "table" then return end
    for _, e in ipairs(cost_list) do
      if type(e) == "table" then
        local res = string.lower(tostring(e.resource or ""))
        local need = clamp_int(tonumber(e.amount) or 0, 0, nil)
        if res ~= "" and need > 0 then
          P.spend(progress, res, need)
        end
      end
    end
  end

  function P.json_not_enough(progress, user_id, res, need, have)
    local energy_max = pve_energy_max_for_user(user_id)
    local r = string.lower(tostring(res or ""))
    if r == "energy" then
      return {
        err = "not_enough_energy",
        required = need,
        energy = have,
        energy_max = energy_max,
      }
    elseif r == "gold" then
      return { err = "not_enough_gold", required = need, gold = have }
    elseif r == "ore" then
      return { err = "not_enough_ore", required = need, ore = have }
    elseif r == "matter" then
      return { err = "not_enough_matter", required = need, matter = have }
    elseif r == "ingots" then
      return { err = "not_enough_ingots", required = need, ingots = have }
    elseif r == "miner_key" or r == "dark_key" then
      return { err = "not_enough_key_item", key_id = r, required = need, have = have }
    end
    return { err = "not_enough_resources", resource = r, required = need, have = have }
  end

  return P
end
