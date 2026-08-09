-- Друзья → «Предложить Спуск». Вынесено из duel_match3.lua (лимит локалей Lua).
local nk = require("nakama")

return function(deps)
  local CFG = deps.CFG
  local ContestGoals = deps.ContestGoals
  local Guard = deps.Guard
  local decode_storage_value = deps.decode_storage_value
  local try_match_create = deps.try_match_create
  local read_pve_progress = deps.read_pve_progress
  local write_pve_progress = deps.write_pve_progress
  local race_progress_balance = deps.race_progress_balance
  local race_progress_spend = deps.race_progress_spend
  local race_progress_grant = deps.race_progress_grant
  local race_normalize_entry_costs = deps.race_normalize_entry_costs

  local NOTIFY_CODE_FRIENDS_RACE_INVITE = 10020
  local NOTIFY_CODE_FRIENDS_RACE_UPDATE = 10021
  local FRIENDS_RACE_INVITE_COLLECTION = "duel_friends_race"
  local FRIENDS_RACE_INVITE_TTL_SEC = 60
  local FRIENDS_RACE_PREP_SECONDS = 5
  local FRIENDS_RACE_STORAGE_USER_ID = "00000000-0000-0000-0000-000000000000"

  local function friends_race_username(user_id)
    if user_id == nil or user_id == "" then return "" end
    local ok, account = pcall(function()
      return nk.account_get_id(user_id)
    end)
    if not ok or account == nil or account.user == nil then return "" end
    local name = tostring(account.user.username or "")
    if name == "" then name = tostring(account.user.display_name or "") end
    return name
  end

  local function friends_race_is_mutual(a, b)
    if a == nil or b == nil or a == "" or b == "" or a == b then return false end
    local list = nil
    local ok = pcall(function()
      local friends, _cursor = nk.friends_list(a, 100, 0, "")
      if type(friends) == "table" and friends.friends ~= nil then
        list = friends.friends
      else
        list = friends
      end
    end)
    if not ok or type(list) ~= "table" then return false end
    for _, f in ipairs(list) do
      if type(f) == "table" then
        local u = f.user or f
        local uid = tostring((u and (u.user_id or u.id)) or "")
        if uid == b then return true end
      end
    end
    return false
  end

  local function friends_race_storage_read(key)
    local ok, rows = pcall(function()
      return nk.storage_read({
        {
          collection = FRIENDS_RACE_INVITE_COLLECTION,
          key = key,
          user_id = FRIENDS_RACE_STORAGE_USER_ID,
        },
      })
    end)
    if not ok or rows == nil or #rows == 0 then return nil, nil end
    return decode_storage_value(rows[1]), rows[1].version
  end

  local function friends_race_storage_write(key, value, version)
    local obj = {
      collection = FRIENDS_RACE_INVITE_COLLECTION,
      key = key,
      user_id = FRIENDS_RACE_STORAGE_USER_ID,
      value = value,
      permission_read = 0,
      permission_write = 0,
    }
    if version ~= nil and version ~= "" then
      obj.version = version
    end
    nk.storage_write({ obj })
  end

  local function friends_race_storage_delete(key)
    pcall(function()
      nk.storage_delete({
        {
          collection = FRIENDS_RACE_INVITE_COLLECTION,
          key = key,
          user_id = FRIENDS_RACE_STORAGE_USER_ID,
        },
      })
    end)
  end

  local function friends_race_clear_invite(invite)
    if type(invite) ~= "table" then return end
    local iid = tostring(invite.invite_id or "")
    local from_id = tostring(invite.from_user_id or "")
    local to_id = tostring(invite.to_user_id or "")
    if iid ~= "" then friends_race_storage_delete("id:" .. iid) end
    if from_id ~= "" then friends_race_storage_delete("out:" .. from_id) end
    if to_id ~= "" then friends_race_storage_delete("in:" .. to_id) end
  end

  --- Одна коллекция, три ключа-индекса (id / outbox / inbox). Чистим просроченные.
  local function friends_race_gc_expired()
    local now = os.time()
    local cursor = ""
    while true do
      local ok_list, objects, next_cursor = pcall(function()
        return nk.storage_list(FRIENDS_RACE_STORAGE_USER_ID, FRIENDS_RACE_INVITE_COLLECTION, 100, cursor)
      end)
      if not ok_list then
        nk.logger_error("friends_race_gc storage_list: " .. tostring(objects))
        break
      end
      if objects == nil then break end
      for _, row in ipairs(objects) do
        if type(row) == "table" then
          local key = tostring(row.key or "")
          local val = decode_storage_value(row)
          local expired = type(val) ~= "table" or (tonumber(val.expires_at) or 0) < now
          if expired then
            if type(val) == "table" then
              friends_race_clear_invite(val)
            else
              friends_race_storage_delete(key)
            end
          end
        end
      end
      if next_cursor == nil or next_cursor == "" then break end
      cursor = next_cursor
    end
  end

  local function race_ensure_entry_paid(user_id)
    local costs = race_normalize_entry_costs(ContestGoals.race_entry_costs())
    if #costs == 0 then
      costs = { { resource = "matter", amount = math.max(1, math.floor(tonumber(CFG.RACE_ENTRY_MATTER) or 2)) } }
    end
    local max_retries = 5
    for i = 1, max_retries do
      local progress, version = read_pve_progress(user_id)
      if progress.race_entry_pending == true then
        return true, nil, type(progress.race_entry_costs) == "table" and progress.race_entry_costs or costs
      end
      for _, line in ipairs(costs) do
        local res = tostring(line.resource or "")
        local need = math.max(0, math.floor(tonumber(line.amount) or 0))
        local have = race_progress_balance(progress, res)
        if have == nil then
          return false, "unsupported_entry_resource", costs
        end
        if have < need then
          return false, "not_enough_" .. res, costs
        end
      end
      for _, line in ipairs(costs) do
        race_progress_spend(progress, line.resource, line.amount)
      end
      progress.race_entry_pending = true
      progress.race_entry_costs = costs
      local write_ok, write_err = pcall(function()
        write_pve_progress(user_id, progress, version)
      end)
      if write_ok then
        return true, nil, costs
      end
      local err_text = tostring(write_err)
      if string.find(err_text, "version", 1, true) == nil or i == max_retries then
        return false, "storage_write_failed", costs
      end
    end
    return false, "retry_exhausted", costs
  end

  local function race_refund_entry_paid(user_id)
    local max_retries = 5
    for i = 1, max_retries do
      local progress, version = read_pve_progress(user_id)
      if progress.race_entry_pending ~= true then
        return true
      end
      local costs = race_normalize_entry_costs(progress.race_entry_costs)
      if #costs == 0 then
        costs = race_normalize_entry_costs(ContestGoals.race_entry_costs())
      end
      if #costs == 0 then
        costs = { { resource = "matter", amount = math.max(1, math.floor(tonumber(CFG.RACE_ENTRY_MATTER) or 2)) } }
      end
      for _, line in ipairs(costs) do
        race_progress_grant(progress, line.resource, line.amount)
      end
      progress.race_entry_pending = false
      progress.race_entry_costs = nil
      local write_ok, write_err = pcall(function()
        write_pve_progress(user_id, progress, version)
      end)
      if write_ok then return true end
      local err_text = tostring(write_err)
      if string.find(err_text, "version", 1, true) == nil or i == max_retries then
        return false
      end
    end
    return false
  end

  local function friends_race_notify(user_id, subject, content, code)
    pcall(function()
      nk.notification_send(user_id, subject, content, code, "", true)
    end)
  end

  --- Удаляет invite из storage и уведомляет второго игрока (status=cancelled).
  local function friends_race_cancel_invite(invite, by_user_id, reason)
    if type(invite) ~= "table" then return false end
    local invite_id = tostring(invite.invite_id or "")
    local from_id = tostring(invite.from_user_id or "")
    local to_id = tostring(invite.to_user_id or "")
    friends_race_clear_invite(invite)

    local peer_id = ""
    if tostring(by_user_id or "") == from_id then
      peer_id = to_id
    elseif tostring(by_user_id or "") == to_id then
      peer_id = from_id
    else
      -- На всякий случай уведомим обе стороны, кроме by_user_id.
      if from_id ~= "" and from_id ~= tostring(by_user_id or "") then
        friends_race_notify(from_id, "friends_race_cancelled", {
          invite_id = invite_id,
          status = "cancelled",
          by_user_id = by_user_id,
          reason = reason or "cancelled",
          kind = "race",
        }, NOTIFY_CODE_FRIENDS_RACE_UPDATE)
      end
      if to_id ~= "" and to_id ~= tostring(by_user_id or "") then
        friends_race_notify(to_id, "friends_race_cancelled", {
          invite_id = invite_id,
          status = "cancelled",
          by_user_id = by_user_id,
          reason = reason or "cancelled",
          kind = "race",
        }, NOTIFY_CODE_FRIENDS_RACE_UPDATE)
      end
      return true
    end

    if peer_id ~= nil and peer_id ~= "" then
      friends_race_notify(peer_id, "friends_race_cancelled", {
        invite_id = invite_id,
        status = "cancelled",
        by_user_id = by_user_id,
        by_username = friends_race_username(by_user_id),
        reason = reason or "cancelled",
        kind = "race",
      }, NOTIFY_CODE_FRIENDS_RACE_UPDATE)
    end
    return true
  end

  local function duel_friends_race_invite(ctx, payload)
    local ok, result = pcall(function()
      local user_id = ctx and ctx.user_id or ""
      if user_id == nil or user_id == "" then
        return nk.json_encode({ ok = false, err = "unauthorized" })
      end
      local ok_epoch, err_epoch = Guard.assert_client_epoch_matches(user_id, payload)
      if not ok_epoch then
        return nk.json_encode({ ok = false, err = err_epoch })
      end

      friends_race_gc_expired()

      local p = {}
      if payload ~= nil and payload ~= "" then
        local dec_ok, body = pcall(nk.json_decode, payload)
        if dec_ok and type(body) == "table" then p = body end
      end
      local target_id = tostring(p.target_user_id or p.user_id or "")
      if target_id == "" then
        return nk.json_encode({ ok = false, err = "empty_target" })
      end
      if target_id == user_id then
        return nk.json_encode({ ok = false, err = "cannot_invite_self" })
      end
      if not friends_race_is_mutual(user_id, target_id) then
        return nk.json_encode({ ok = false, err = "not_friends" })
      end

      local now = os.time()
      local existing_out = friends_race_storage_read("out:" .. user_id)
      if type(existing_out) == "table" and (tonumber(existing_out.expires_at) or 0) > now then
        return nk.json_encode({ ok = false, err = "invite_already_sent" })
      elseif type(existing_out) == "table" then
        friends_race_clear_invite(existing_out)
      end
      local existing_in = friends_race_storage_read("in:" .. target_id)
      if type(existing_in) == "table" and (tonumber(existing_in.expires_at) or 0) > now then
        return nk.json_encode({ ok = false, err = "target_busy" })
      elseif type(existing_in) == "table" then
        friends_race_clear_invite(existing_in)
      end

      local costs = race_normalize_entry_costs(ContestGoals.race_entry_costs())
      if #costs == 0 then
        costs = { { resource = "matter", amount = math.max(1, math.floor(tonumber(CFG.RACE_ENTRY_MATTER) or 2)) } }
      end
      local progress = read_pve_progress(user_id)
      if progress.race_entry_pending ~= true then
        for _, line in ipairs(costs) do
          local have = race_progress_balance(progress, line.resource)
          local need = math.max(0, math.floor(tonumber(line.amount) or 0))
          if have == nil then
            return nk.json_encode({ ok = false, err = "unsupported_entry_resource", resource = line.resource })
          end
          if have < need then
            return nk.json_encode({
              ok = false,
              err = "not_enough_" .. tostring(line.resource),
              required = need,
              resource = line.resource,
            })
          end
        end
      end

      local invite_id = nk.uuid_v4()
      local from_name = friends_race_username(user_id)
      local to_name = friends_race_username(target_id)
      local invite = {
        invite_id = invite_id,
        from_user_id = user_id,
        to_user_id = target_id,
        from_username = from_name,
        to_username = to_name,
        created_at = now,
        expires_at = now + FRIENDS_RACE_INVITE_TTL_SEC,
        kind = "race",
      }
      friends_race_storage_write("id:" .. invite_id, invite, nil)
      friends_race_storage_write("out:" .. user_id, invite, nil)
      friends_race_storage_write("in:" .. target_id, invite, nil)

      friends_race_notify(target_id, "friends_race_invite", {
        invite_id = invite_id,
        from_user_id = user_id,
        from_username = from_name,
        expires_at = invite.expires_at,
        kind = "race",
        prep_seconds = FRIENDS_RACE_PREP_SECONDS,
      }, NOTIFY_CODE_FRIENDS_RACE_INVITE)

      return nk.json_encode({
        ok = true,
        invite_id = invite_id,
        target_user_id = target_id,
        target_username = to_name,
        expires_at = invite.expires_at,
      })
    end)
    if not ok then
      nk.logger_error("duel_friends_race_invite: " .. tostring(result))
      return nk.json_encode({ ok = false, err = "server_error" })
    end
    return result
  end

  local function duel_friends_race_respond(ctx, payload)
    local ok, result = pcall(function()
      local user_id = ctx and ctx.user_id or ""
      if user_id == nil or user_id == "" then
        return nk.json_encode({ ok = false, err = "unauthorized" })
      end
      local ok_epoch, err_epoch = Guard.assert_client_epoch_matches(user_id, payload)
      if not ok_epoch then
        return nk.json_encode({ ok = false, err = err_epoch })
      end

      friends_race_gc_expired()

      local p = {}
      if payload ~= nil and payload ~= "" then
        local dec_ok, body = pcall(nk.json_decode, payload)
        if dec_ok and type(body) == "table" then p = body end
      end
      local invite_id = tostring(p.invite_id or "")
      local accept = p.accept == true or p.accept == 1 or tostring(p.accept) == "true"
      if invite_id == "" then
        return nk.json_encode({ ok = false, err = "empty_invite" })
      end

      local invite = friends_race_storage_read("id:" .. invite_id)
      if type(invite) ~= "table" then
        return nk.json_encode({ ok = false, err = "invite_not_found" })
      end
      local now = os.time()
      if (tonumber(invite.expires_at) or 0) < now then
        friends_race_clear_invite(invite)
        return nk.json_encode({ ok = false, err = "invite_expired" })
      end
      if tostring(invite.to_user_id or "") ~= user_id then
        return nk.json_encode({ ok = false, err = "not_invitee" })
      end

      local from_id = tostring(invite.from_user_id or "")
      local from_name = tostring(invite.from_username or friends_race_username(from_id))
      local to_name = tostring(invite.to_username or friends_race_username(user_id))

      if not accept then
        friends_race_clear_invite(invite)
        friends_race_notify(from_id, "friends_race_declined", {
          invite_id = invite_id,
          by_user_id = user_id,
          by_username = to_name,
          status = "declined",
          kind = "race",
        }, NOTIFY_CODE_FRIENDS_RACE_UPDATE)
        return nk.json_encode({ ok = true, status = "declined" })
      end

      local ok_to, err_to = race_ensure_entry_paid(user_id)
      if not ok_to then
        return nk.json_encode({ ok = false, err = err_to or "not_enough_matter" })
      end
      local ok_from, err_from = race_ensure_entry_paid(from_id)
      if not ok_from then
        race_refund_entry_paid(user_id)
        friends_race_notify(from_id, "friends_race_failed", {
          invite_id = invite_id,
          status = "charge_failed",
          err = err_from,
          kind = "race",
        }, NOTIFY_CODE_FRIENDS_RACE_UPDATE)
        return nk.json_encode({ ok = false, err = "inviter_" .. tostring(err_from or "charge_failed") })
      end

      local match_id = try_match_create({
        mode = "pvp",
        pvp_race = true,
        invited = {
          { user_id = from_id },
          { user_id = user_id },
        },
      })
      if match_id == nil or match_id == "" then
        race_refund_entry_paid(user_id)
        race_refund_entry_paid(from_id)
        return nk.json_encode({ ok = false, err = "match_create_failed" })
      end

      friends_race_clear_invite(invite)

      local ready = {
        invite_id = invite_id,
        match_id = match_id,
        status = "match_ready",
        kind = "race",
        prep_seconds = FRIENDS_RACE_PREP_SECONDS,
        from_user_id = from_id,
        from_username = from_name,
        to_user_id = user_id,
        to_username = to_name,
      }
      friends_race_notify(from_id, "friends_race_match_ready", ready, NOTIFY_CODE_FRIENDS_RACE_UPDATE)
      friends_race_notify(user_id, "friends_race_match_ready", ready, NOTIFY_CODE_FRIENDS_RACE_UPDATE)

      return nk.json_encode({
        ok = true,
        status = "match_ready",
        match_id = match_id,
        prep_seconds = FRIENDS_RACE_PREP_SECONDS,
        opponent_user_id = from_id,
        opponent_username = from_name,
        invite_id = invite_id,
      })
    end)
    if not ok then
      nk.logger_error("duel_friends_race_respond: " .. tostring(result))
      return nk.json_encode({ ok = false, err = "server_error" })
    end
    return result
  end

  --- Аннулирует активные исходящие/входящие предложения игрока (вход в другой матч и т.п.).
  local function duel_friends_race_clear(ctx, payload)
    local ok, result = pcall(function()
      local user_id = ctx and ctx.user_id or ""
      if user_id == nil or user_id == "" then
        return nk.json_encode({ ok = false, err = "unauthorized" })
      end
      local ok_epoch, err_epoch = Guard.assert_client_epoch_matches(user_id, payload)
      if not ok_epoch then
        return nk.json_encode({ ok = false, err = err_epoch })
      end

      friends_race_gc_expired()

      local p = {}
      if payload ~= nil and payload ~= "" then
        local dec_ok, body = pcall(nk.json_decode, payload)
        if dec_ok and type(body) == "table" then p = body end
      end
      local reason = tostring(p.reason or "match_enter")
      if reason == "" then reason = "match_enter" end

      local cleared = 0
      local seen = {}
      local out_invite = friends_race_storage_read("out:" .. user_id)
      if type(out_invite) == "table" then
        local iid = tostring(out_invite.invite_id or "")
        if iid ~= "" then seen[iid] = true end
        if friends_race_cancel_invite(out_invite, user_id, reason) then
          cleared = cleared + 1
        end
      end
      local in_invite = friends_race_storage_read("in:" .. user_id)
      if type(in_invite) == "table" then
        local iid = tostring(in_invite.invite_id or "")
        if iid == "" or not seen[iid] then
          if friends_race_cancel_invite(in_invite, user_id, reason) then
            cleared = cleared + 1
          end
        end
      end

      return nk.json_encode({ ok = true, cleared = cleared, reason = reason })
    end)
    if not ok then
      nk.logger_error("duel_friends_race_clear: " .. tostring(result))
      return nk.json_encode({ ok = false, err = "server_error" })
    end
    return result
  end

  return {
    duel_friends_race_invite = duel_friends_race_invite,
    duel_friends_race_respond = duel_friends_race_respond,
    duel_friends_race_clear = duel_friends_race_clear,
  }
end
