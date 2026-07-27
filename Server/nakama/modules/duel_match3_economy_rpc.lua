local nk = require("nakama")

--- Character / workshop / resources RPCs (вынесено из duel_match3.lua из‑за лимита локалей).
return function(deps)
  local CFG = deps.CFG
  local Guard = deps.Guard
  local EQUIP_ORDER = deps.EQUIP_ORDER
  local read_character_sheet = deps.read_character_sheet
  local write_character_sheet = deps.write_character_sheet
  local ensure_sheet_inventory_counts = deps.ensure_sheet_inventory_counts
  local ensure_character_sheet_initialized = deps.ensure_character_sheet_initialized
  local ensure_sheet_workshop = deps.ensure_sheet_workshop
  local get_merged_item_defs = deps.get_merged_item_defs
  local item_def_is_equipment = deps.item_def_is_equipment
  local item_def_is_recipe = deps.item_def_is_recipe
  local item_max_stack = deps.item_max_stack
  local encode_character_ok_response = deps.encode_character_ok_response
  local sheet_has_learned = deps.sheet_has_learned
  local sheet_has_learned_for_craft = deps.sheet_has_learned_for_craft
  local workshop_craft_cost_from_def = deps.workshop_craft_cost_from_def
  local workshop_has_legendary_fodder = deps.workshop_has_legendary_fodder
  local workshop_consume_legendary_fodder = deps.workshop_consume_legendary_fodder
  local workshop_has_quality_fodder = deps.workshop_has_quality_fodder
  local workshop_consume_quality_fodder = deps.workshop_consume_quality_fodder
  local inventory_count_def = deps.inventory_count_def
  local inventory_try_add = deps.inventory_try_add
  local inventory_remove_def_total = deps.inventory_remove_def_total
  local inventory_can_fit = deps.inventory_can_fit
  local read_pve_progress = deps.read_pve_progress
  local write_pve_progress = deps.write_pve_progress
  local build_resource_payload = deps.build_resource_payload
  local build_progression_payload_auto = deps.build_progression_payload_auto
  local pve_energy_max_for_user = deps.pve_energy_max_for_user
  local empty_key_items = deps.empty_key_items

  local M = {}

  function M.duel_character_item_move(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local ok_epoch, err_epoch = Guard.assert_client_epoch_matches(user_id, payload)
    if not ok_epoch then
      return nk.json_encode({ ok = false, err = err_epoch })
    end

    local p = {}
    if payload ~= nil and payload ~= "" then
      p = nk.json_decode(payload) or {}
    end
    local op = tostring(p.op or "")
    local sheet = read_character_sheet(user_id)
    ensure_sheet_inventory_counts(sheet)
    local defs = get_merged_item_defs()

    local function json_fail(err)
      return nk.json_encode({ ok = false, err = err })
    end

    if op == "inv_to_equip" then
      local inv_index = tonumber(p.inv_index)
      local slot_index = tonumber(p.slot_index)
      if inv_index == nil or slot_index == nil then return json_fail("bad_indices") end
      inv_index = math.floor(inv_index)
      slot_index = math.floor(slot_index)
      if inv_index < 0 or inv_index > 24 then return json_fail("bad_inv_index") end
      if slot_index < 0 or slot_index > 7 then return json_fail("bad_slot_index") end
      local i = inv_index + 1
      local s = slot_index + 1
      local item = sheet.inventory[i]
      local cnt = tonumber(sheet.inventory_counts[i]) or 0
      if item == nil or item == "" or cnt < 1 then return json_fail("empty_source") end
      local def = defs[item]
      if def == nil then return json_fail("unknown_item") end
      if not item_def_is_equipment(def) then return json_fail("not_equipment") end
      if def.slot ~= EQUIP_ORDER[s] then return json_fail("wrong_slot") end
      local cur = sheet.equipment[s] or ""
      sheet.inventory[i] = cur
      if cur ~= nil and cur ~= "" then
        sheet.inventory_counts[i] = 1
      else
        sheet.inventory_counts[i] = 0
      end
      sheet.equipment[s] = item
    elseif op == "equip_to_inv" then
      local slot_index = tonumber(p.slot_index)
      local inv_index = tonumber(p.inv_index)
      if inv_index == nil or slot_index == nil then return json_fail("bad_indices") end
      inv_index = math.floor(inv_index)
      slot_index = math.floor(slot_index)
      if inv_index < 0 or inv_index > 24 then return json_fail("bad_inv_index") end
      if slot_index < 0 or slot_index > 7 then return json_fail("bad_slot_index") end
      local i = inv_index + 1
      local s = slot_index + 1
      local item = sheet.equipment[s]
      if item == nil or item == "" then return json_fail("empty_source") end
      local cur_inv = sheet.inventory[i] or ""
      if cur_inv == "" then
        sheet.equipment[s] = ""
        sheet.inventory[i] = item
        sheet.inventory_counts[i] = 1
      else
        local def_inv = defs[cur_inv]
        if def_inv == nil then return json_fail("unknown_item") end
        if not item_def_is_equipment(def_inv) then return json_fail("cannot_swap") end
        if def_inv.slot ~= EQUIP_ORDER[s] then return json_fail("cannot_swap") end
        sheet.equipment[s] = cur_inv
        sheet.inventory[i] = item
        sheet.inventory_counts[i] = 1
      end
    elseif op == "inv_swap" then
      local a = tonumber(p.inv_a)
      local b = tonumber(p.inv_b)
      if a == nil or b == nil then return json_fail("bad_indices") end
      a = math.floor(a)
      b = math.floor(b)
      if a < 0 or a > 24 or b < 0 or b > 24 then return json_fail("bad_inv_index") end
      if a ~= b then
        local ia, ib = a + 1, b + 1
        local id_a = sheet.inventory[ia] or ""
        local id_b = sheet.inventory[ib] or ""
        local ca = tonumber(sheet.inventory_counts[ia]) or 0
        local cb = tonumber(sheet.inventory_counts[ib]) or 0
        -- Одинаковый стакаемый предмет → слияние в цель (до max_stack), иначе обмен.
        if id_a ~= "" and id_a == id_b and ca > 0 and cb > 0 then
          local max_s = item_max_stack(defs[id_a])
          if max_s > 1 then
            local space = max_s - cb
            if space > 0 then
              local move = math.min(space, ca)
              cb = cb + move
              ca = ca - move
              sheet.inventory_counts[ib] = cb
              if ca <= 0 then
                sheet.inventory[ia] = ""
                sheet.inventory_counts[ia] = 0
              else
                sheet.inventory_counts[ia] = ca
              end
            else
              -- Цель уже полная — обычный swap.
              sheet.inventory[ia], sheet.inventory[ib] = id_b, id_a
              sheet.inventory_counts[ia], sheet.inventory_counts[ib] = cb, ca
            end
          else
            sheet.inventory[ia], sheet.inventory[ib] = id_b, id_a
            sheet.inventory_counts[ia], sheet.inventory_counts[ib] = cb, ca
          end
        else
          sheet.inventory[ia], sheet.inventory[ib] = id_b, id_a
          sheet.inventory_counts[ia], sheet.inventory_counts[ib] = cb, ca
        end
      end
    elseif op == "equip_swap" then
      local a = tonumber(p.slot_a)
      local b = tonumber(p.slot_b)
      if a == nil or b == nil then return json_fail("bad_indices") end
      a = math.floor(a)
      b = math.floor(b)
      if a < 0 or a > 7 or b < 0 or b > 7 then return json_fail("bad_slot_index") end
      if a ~= b then
        local sa, sb = a + 1, b + 1
        local item_a = sheet.equipment[sa] or ""
        local item_b = sheet.equipment[sb] or ""
        -- Каждый предмет может лежать только в своём слоте (как inv_to_equip).
        if item_a ~= "" then
          local def_a = defs[item_a]
          if def_a == nil then return json_fail("unknown_item") end
          if not item_def_is_equipment(def_a) then return json_fail("not_equipment") end
          if def_a.slot ~= EQUIP_ORDER[sb] then return json_fail("wrong_slot") end
        end
        if item_b ~= "" then
          local def_b = defs[item_b]
          if def_b == nil then return json_fail("unknown_item") end
          if not item_def_is_equipment(def_b) then return json_fail("not_equipment") end
          if def_b.slot ~= EQUIP_ORDER[sa] then return json_fail("wrong_slot") end
        end
        sheet.equipment[sa], sheet.equipment[sb] = item_b, item_a
      end
    else
      return json_fail("unknown_op")
    end

    write_character_sheet(user_id, sheet)

    local progress = read_pve_progress(user_id)
    return encode_character_ok_response(sheet, progress, user_id)
  end)

  if not ok then
    nk.logger_error("duel_character_item_move: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

  function M.duel_character_get(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local progress = read_pve_progress(user_id)
    local sheet = read_character_sheet(user_id)
    return encode_character_ok_response(sheet, progress, user_id)
  end)

  if not ok then
    nk.logger_error("duel_character_get: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

  function M.duel_character_recipe_learn(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local ok_epoch, err_epoch = Guard.assert_client_epoch_matches(user_id, payload)
    if not ok_epoch then
      return nk.json_encode({ ok = false, err = err_epoch })
    end

    local p = {}
    if payload ~= nil and payload ~= "" then
      p = nk.json_decode(payload) or {}
    end
    local inv_index = tonumber(p.inv_index)
    if inv_index == nil then
      return nk.json_encode({ ok = false, err = "bad_indices" })
    end
    inv_index = math.floor(inv_index)
    if inv_index < 0 or inv_index > 24 then
      return nk.json_encode({ ok = false, err = "bad_inv_index" })
    end

    local sheet = read_character_sheet(user_id)
    ensure_sheet_inventory_counts(sheet)
    local defs = get_merged_item_defs()
    local i = inv_index + 1
    local item_id = sheet.inventory[i] or ""
    local cnt = tonumber(sheet.inventory_counts[i]) or 0
    if item_id == "" or cnt < 1 then
      return nk.json_encode({ ok = false, err = "empty_source" })
    end

    local def = defs[item_id]
    if def == nil then
      return nk.json_encode({ ok = false, err = "unknown_item" })
    end
    if not item_def_is_recipe(def) then
      return nk.json_encode({ ok = false, err = "not_recipe" })
    end

    local lr = sheet.learned_recipes or {}
    for j = 1, #lr do
      if lr[j] == item_id then
        return nk.json_encode({ ok = false, err = "already_learned" })
      end
    end

    cnt = cnt - 1
    if cnt <= 0 then
      sheet.inventory[i] = ""
      sheet.inventory_counts[i] = 0
    else
      sheet.inventory_counts[i] = cnt
    end

    lr[#lr + 1] = item_id
    sheet.learned_recipes = lr
    write_character_sheet(user_id, sheet)

    local progress = read_pve_progress(user_id)
    return encode_character_ok_response(sheet, progress, user_id)
  end)

  if not ok then
    nk.logger_error("duel_character_recipe_learn: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

  function M.duel_workshop_craft_start(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local ok_epoch, err_epoch = Guard.assert_client_epoch_matches(user_id, payload)
    if not ok_epoch then
      return nk.json_encode({ ok = false, err = err_epoch })
    end

    local p = {}
    if payload ~= nil and payload ~= "" then
      p = nk.json_decode(payload) or {}
    end
    local slot_index = tonumber(p.slot_index)
    if slot_index == nil then
      return nk.json_encode({ ok = false, err = "bad_slot_index" })
    end
    slot_index = math.floor(slot_index)
    if slot_index < 0 or slot_index > 7 then
      return nk.json_encode({ ok = false, err = "bad_slot_index" })
    end

    local output_def_id = tostring(p.output_def_id or "")
    if output_def_id == "" then
      return nk.json_encode({ ok = false, err = "bad_output" })
    end

    ensure_character_sheet_initialized(user_id)
    local defs = get_merged_item_defs()
    local out_def = defs[output_def_id]
    if out_def == nil or not item_def_is_equipment(out_def) then
      return nk.json_encode({ ok = false, err = "unknown_item" })
    end

    local expected_slot = EQUIP_ORDER[slot_index + 1]
    if tostring(out_def.slot or "") ~= expected_slot then
      return nk.json_encode({ ok = false, err = "wrong_workshop_slot" })
    end

    local craft_recipe_id = tostring(out_def.craft_recipe_id or "")
    if craft_recipe_id == "" then
      return nk.json_encode({ ok = false, err = "not_craftable" })
    end

    local sheet = read_character_sheet(user_id)
    ensure_sheet_inventory_counts(sheet)
    ensure_sheet_workshop(sheet)
    local wslot = sheet.workshop_slots[slot_index + 1]
    if wslot.output_def_id ~= nil and wslot.output_def_id ~= "" then
      local wend = tonumber(wslot.ends_at) or 0
      if wend > os.time() then
        return nk.json_encode({ ok = false, err = "workshop_busy" })
      end
      return nk.json_encode({ ok = false, err = "claim_first" })
    end

    if not sheet_has_learned_for_craft(sheet, craft_recipe_id) then
      return nk.json_encode({ ok = false, err = "recipe_not_learned" })
    end

    local tier = CFG.clamp_int(tonumber(out_def.tier) or 1, 1, 3)
    local quality = tostring(out_def.quality or "normal")
    if quality ~= "normal" and quality ~= "rare" and quality ~= "epic" and quality ~= "legendary" then
      return nk.json_encode({ ok = false, err = "unsupported_craft_quality" })
    end

    local cost = workshop_craft_cost_from_def(out_def, tier, quality)
    local ore_c = cost.ore
    local gold_c = cost.gold
    local ingot_def = cost.ingot_def
    local ingot_n = cost.ingot_n
    local tess_n = cost.tesseract_n

    if quality == "normal" then
      if tier == 2 and not workshop_has_legendary_fodder(sheet, defs, slot_index, 1) then
        return nk.json_encode({ ok = false, err = "missing_legend_fodder_t1" })
      end
      if tier == 3 and not workshop_has_legendary_fodder(sheet, defs, slot_index, 2) then
        return nk.json_encode({ ok = false, err = "missing_legend_fodder_t2" })
      end
    elseif quality == "rare" then
      if not workshop_has_quality_fodder(sheet, defs, slot_index, tier, "normal") then
        return nk.json_encode({ ok = false, err = "missing_normal_fodder" })
      end
    elseif quality == "epic" then
      if not workshop_has_quality_fodder(sheet, defs, slot_index, tier, "rare") then
        return nk.json_encode({ ok = false, err = "missing_rare_fodder" })
      end
    elseif quality == "legendary" then
      if not workshop_has_quality_fodder(sheet, defs, slot_index, tier, "epic") then
        return nk.json_encode({ ok = false, err = "missing_epic_fodder" })
      end
    end

    if ingot_n > 0 and ingot_def == "" then
      return nk.json_encode({ ok = false, err = "bad_craft_cost" })
    end
    if ingot_n > 0 and inventory_count_def(sheet, ingot_def) < ingot_n then
      return nk.json_encode({ ok = false, err = "not_enough_ingots" })
    end
    if tess_n > 0 and inventory_count_def(sheet, "tesseract") < tess_n then
      return nk.json_encode({ ok = false, err = "not_enough_tesseract" })
    end

    local dur_tbl = CFG.WORKSHOP_CRAFT_DURATION_SEC_BY_TIER
    local dur = dur_tbl and dur_tbl[tier] or (60 * 60)

    local max_retries = 5
    for attempt = 1, max_retries do
      local progress, version = read_pve_progress(user_id)
      if (tonumber(progress.ore) or 0) < ore_c then
        return nk.json_encode({ ok = false, err = "not_enough_ore" })
      end
      if (tonumber(progress.gold) or 0) < gold_c then
        return nk.json_encode({ ok = false, err = "not_enough_gold" })
      end

      progress.ore = (tonumber(progress.ore) or 0) - ore_c
      progress.gold = (tonumber(progress.gold) or 0) - gold_c

      local w_ok, w_err = pcall(function()
        write_pve_progress(user_id, progress, version)
      end)
      if w_ok then
        if ingot_n > 0 then
          if not inventory_remove_def_total(sheet, ingot_def, ingot_n) then
            nk.logger_error("workshop_craft_start: не удалось списать слитки")
            return nk.json_encode({ ok = false, err = "server_error" })
          end
        end
        if tess_n > 0 then
          if not inventory_remove_def_total(sheet, "tesseract", tess_n) then
            nk.logger_error("workshop_craft_start: не удалось списать тессеракты")
            return nk.json_encode({ ok = false, err = "server_error" })
          end
        end
        if quality == "normal" then
          if tier == 2 then
            if not workshop_consume_legendary_fodder(sheet, defs, slot_index, 1) then
              nk.logger_error("workshop_craft_start: не удалось поглотить легенду T1")
              return nk.json_encode({ ok = false, err = "server_error" })
            end
          elseif tier == 3 then
            if not workshop_consume_legendary_fodder(sheet, defs, slot_index, 2) then
              nk.logger_error("workshop_craft_start: не удалось поглотить легенду T2")
              return nk.json_encode({ ok = false, err = "server_error" })
            end
          end
        elseif quality == "rare" then
          if not workshop_consume_quality_fodder(sheet, defs, slot_index, tier, "normal") then
            nk.logger_error("workshop_craft_start: не удалось поглотить normal для rare")
            return nk.json_encode({ ok = false, err = "server_error" })
          end
        elseif quality == "epic" then
          if not workshop_consume_quality_fodder(sheet, defs, slot_index, tier, "rare") then
            nk.logger_error("workshop_craft_start: не удалось поглотить rare для epic")
            return nk.json_encode({ ok = false, err = "server_error" })
          end
        elseif quality == "legendary" then
          if not workshop_consume_quality_fodder(sheet, defs, slot_index, tier, "epic") then
            nk.logger_error("workshop_craft_start: не удалось поглотить epic для legendary")
            return nk.json_encode({ ok = false, err = "server_error" })
          end
        end
        wslot.output_def_id = output_def_id
        wslot.ends_at = os.time() + dur
        write_character_sheet(user_id, sheet)
        return encode_character_ok_response(sheet, progress, user_id)
      end

      local err_text = tostring(w_err)
      if string.find(err_text, "version", 1, true) == nil or attempt == max_retries then
        error(w_err)
      end
    end

    return nk.json_encode({ ok = false, err = "retry_exhausted" })
  end)

  if not ok then
    nk.logger_error("duel_workshop_craft_start: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

  function M.duel_workshop_craft_claim(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local ok_epoch, err_epoch = Guard.assert_client_epoch_matches(user_id, payload)
    if not ok_epoch then
      return nk.json_encode({ ok = false, err = err_epoch })
    end

    local p = {}
    if payload ~= nil and payload ~= "" then
      p = nk.json_decode(payload) or {}
    end
    local slot_index = tonumber(p.slot_index)
    if slot_index == nil then
      return nk.json_encode({ ok = false, err = "bad_slot_index" })
    end
    slot_index = math.floor(slot_index)
    if slot_index < 0 or slot_index > 7 then
      return nk.json_encode({ ok = false, err = "bad_slot_index" })
    end

    ensure_character_sheet_initialized(user_id)
    local sheet = read_character_sheet(user_id)
    ensure_sheet_inventory_counts(sheet)
    ensure_sheet_workshop(sheet)
    local wslot = sheet.workshop_slots[slot_index + 1]
    local oid = tostring(wslot.output_def_id or "")
    if oid == "" then
      return nk.json_encode({ ok = false, err = "empty_workshop_slot" })
    end
    local wend = tonumber(wslot.ends_at) or 0
    if wend > os.time() then
      return nk.json_encode({ ok = false, err = "craft_not_ready" })
    end

    if inventory_try_add(sheet, oid, 1) ~= true then
      return nk.json_encode({ ok = false, err = "inventory_full" })
    end

    wslot.output_def_id = ""
    wslot.ends_at = 0
    write_character_sheet(user_id, sheet)

    local progress = read_pve_progress(user_id)
    return encode_character_ok_response(sheet, progress, user_id)
  end)

  if not ok then
    nk.logger_error("duel_workshop_craft_claim: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

  function M.duel_player_resources_get(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local progress = read_pve_progress(user_id)
    local resources = build_resource_payload(progress, user_id)
    resources.ok = true
    return nk.json_encode(resources)
  end)

  if not ok then
    nk.logger_error("duel_player_resources_get: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

  function M.duel_player_resources_spend(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local ok_epoch, err_epoch = Guard.assert_client_epoch_matches(user_id, payload)
    if not ok_epoch then
      return nk.json_encode({ ok = false, err = err_epoch })
    end

    local p = {}
    if payload ~= nil and payload ~= "" then
      p = nk.json_decode(payload) or {}
    end

    local resource = tostring(p.resource or "")
    local amount = CFG.clamp_int(p.amount, 0, nil)
    local reason = tostring(p.reason or "")
    if resource ~= "energy" then
      return nk.json_encode({ ok = false, err = "unsupported_resource" })
    end
    if amount <= 0 then
      return nk.json_encode({ ok = false, err = "bad_amount" })
    end

    local max_retries = 5
    for i = 1, max_retries do
      local now = os.time()
      local progress, version = read_pve_progress(user_id)
      local energy_max = pve_energy_max_for_user(user_id)
      local available = CFG.clamp_int(progress.energy, 0, energy_max)
      if available < amount then
        local resources = build_resource_payload(progress, user_id)
        resources.ok = false
        resources.err = "not_enough_energy"
        resources.resource = resource
        resources.reason = reason
        resources.required = amount
        return nk.json_encode(resources)
      end

      progress.energy = available - amount
      progress.energy_updated_at = now

      local write_ok, write_err = pcall(function()
        write_pve_progress(user_id, progress, version)
      end)
      if write_ok then
        local resources = build_resource_payload(progress, user_id)
        resources.ok = true
        resources.resource = resource
        resources.reason = reason
        resources.spent = amount
        return nk.json_encode(resources)
      end

      local err_text = tostring(write_err)
      if string.find(err_text, "version", 1, true) == nil or i == max_retries then
        error(write_err)
      end
    end

    return nk.json_encode({ ok = false, err = "retry_exhausted" })
  end)

  if not ok then
    nk.logger_error("duel_player_resources_spend: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

  function M.duel_pve_energy_buy(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end
    local ok_epoch, err_epoch = Guard.assert_client_epoch_matches(user_id, payload)
    if not ok_epoch then
      return nk.json_encode({ ok = false, err = err_epoch })
    end
    local p = {}
    if payload ~= nil and payload ~= "" then
      p = nk.json_decode(payload) or {}
    end
    local mode = tostring(p.mode or "")
    if mode ~= "matter" and mode ~= "gold" then
      return nk.json_encode({ ok = false, err = "bad_mode" })
    end
    local matter_cost = math.max(1, math.floor(tonumber(CFG.PVE_ENERGY_BUY_MATTER_COST) or 1))
    local matter_grant = math.max(1, math.floor(tonumber(CFG.PVE_ENERGY_BUY_MATTER_GRANT) or 100))
    local gold_cost = math.max(1, math.floor(tonumber(CFG.PVE_ENERGY_BUY_GOLD_COST) or 1000))
    local gold_grant = math.max(1, math.floor(tonumber(CFG.PVE_ENERGY_BUY_GOLD_GRANT) or 100))
    local max_retries = 5
    for i = 1, max_retries do
      local progress, version = read_pve_progress(user_id)
      local energy_max = pve_energy_max_for_user(user_id)
      local e = CFG.clamp_int(progress.energy, 0, energy_max)
      if e >= energy_max then
        return nk.json_encode({ ok = false, err = "energy_full" })
      end
      if mode == "matter" then
        local m = math.max(0, tonumber(progress.matter) or 0)
        if m < matter_cost then
          return nk.json_encode({ ok = false, err = "not_enough_matter" })
        end
        local add = matter_grant
        if e + add > energy_max then
          return nk.json_encode({ ok = false, err = "energy_full" })
        end
        progress.matter = m - matter_cost
        progress.energy = e + add
      else
        local g = math.max(0, tonumber(progress.gold) or 0)
        if g < gold_cost then
          return nk.json_encode({ ok = false, err = "not_enough_gold" })
        end
        local add = gold_grant
        if e + add > energy_max then
          return nk.json_encode({ ok = false, err = "energy_full" })
        end
        progress.gold = g - gold_cost
        progress.energy = e + add
      end
      progress.energy = CFG.clamp_int(progress.energy, 0, energy_max)
      progress.energy_updated_at = os.time()
      local write_ok, write_err = pcall(function()
        write_pve_progress(user_id, progress, version)
      end)
      if write_ok then
        local resources = build_resource_payload(progress, user_id)
        resources.ok = true
        return nk.json_encode(resources)
      end
      local err_text = tostring(write_err)
      if string.find(err_text, "version", 1, true) == nil or i == max_retries then
        error(write_err)
      end
    end
    return nk.json_encode({ ok = false, err = "retry_exhausted" })
  end)
  if not ok then
    nk.logger_error("duel_pve_energy_buy: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

  function M.duel_workshop_craft_rush(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end
    local ok_epoch, err_epoch = Guard.assert_client_epoch_matches(user_id, payload)
    if not ok_epoch then
      return nk.json_encode({ ok = false, err = err_epoch })
    end
    local p = {}
    if payload ~= nil and payload ~= "" then
      p = nk.json_decode(payload) or {}
    end
    local slot_index = tonumber(p.slot_index)
    if slot_index == nil or slot_index < 0 or slot_index > 7 then
      return nk.json_encode({ ok = false, err = "bad_slot_index" })
    end
    slot_index = math.floor(slot_index)
    local rush_gold = math.max(1, math.floor(tonumber(CFG.WORKSHOP_CRAFT_RUSH_GOLD) or 500))
    local rush_sec = math.max(60, math.floor(tonumber(CFG.WORKSHOP_CRAFT_RUSH_SECONDS) or 1200))
    ensure_character_sheet_initialized(user_id)
    local max_retries = 5
    for attempt = 1, max_retries do
      local progress, version = read_pve_progress(user_id)
      if (tonumber(progress.gold) or 0) < rush_gold then
        return nk.json_encode({ ok = false, err = "not_enough_gold" })
      end
      local sheet = read_character_sheet(user_id)
      ensure_sheet_inventory_counts(sheet)
      ensure_sheet_workshop(sheet)
      local wslot = sheet.workshop_slots[slot_index + 1]
      if wslot == nil or tostring(wslot.output_def_id or "") == "" then
        return nk.json_encode({ ok = false, err = "empty_workshop_slot" })
      end
      local wend = tonumber(wslot.ends_at) or 0
      if wend <= os.time() then
        return nk.json_encode({ ok = false, err = "craft_already_ready" })
      end
      progress.gold = (tonumber(progress.gold) or 0) - rush_gold
      wend = wend - rush_sec
      if wend < os.time() then
        wend = os.time()
      end
      wslot.ends_at = wend
      local w_ok, w_err = pcall(function()
        write_pve_progress(user_id, progress, version)
      end)
      if w_ok then
        write_character_sheet(user_id, sheet)
        return encode_character_ok_response(sheet, progress, user_id)
      end
      local err_text = tostring(w_err)
      if string.find(err_text, "version", 1, true) == nil or attempt == max_retries then
        error(w_err)
      end
    end
    return nk.json_encode({ ok = false, err = "retry_exhausted" })
  end)
  if not ok then
    nk.logger_error("duel_workshop_craft_rush: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end


  return M
end