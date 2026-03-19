--[[
  PIN дуэли: storage привязан к реальному user_id в БД Nakama (FK storage_user_id_fkey).
  Владелец записи — lexicographically min(user_a, user_b), оба UUID участников матча.
  Клиент передаёт user_a и user_b (уже отсортированы: user_a < user_b).

  RPC:
    duel_match_ensure_pins  {"match_id":"...","user_a":"uuid","user_b":"uuid"}
    duel_keypad_guess       {"match_id":"...","user_a":"...","user_b":"...","door_id":1,"guess":"04"}
]]

local nk = require("nakama")
local COLLECTION = "duel_keypad_pins"

math.randomseed(os.time())

local function random_digit_char()
  return tostring(math.random(0, 9))
end

local function random_pin(len)
  local s = ""
  for _ = 1, len do
    s = s .. random_digit_char()
  end
  return s
end

local function score(pin, guess)
  if string.len(pin) ~= string.len(guess) then
    return 0, 0
  end
  local counts = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }
  local bulls = 0
  local n = string.len(pin)
  for i = 1, n do
    local pc = string.sub(pin, i, i)
    local gc = string.sub(guess, i, i)
    if pc == gc then
      bulls = bulls + 1
    else
      local d = tonumber(pc)
      if d ~= nil then
        counts[d + 1] = counts[d + 1] + 1
      end
    end
  end
  local cows = 0
  for i = 1, n do
    local pc = string.sub(pin, i, i)
    local gc = string.sub(guess, i, i)
    if pc ~= gc then
      local d = tonumber(gc)
      if d ~= nil then
        local idx = d + 1
        if counts[idx] and counts[idx] > 0 then
          counts[idx] = counts[idx] - 1
          cows = cows + 1
        end
      end
    end
  end
  return bulls, cows
end

local function decode_storage_value(obj)
  if obj == nil then
    return nil
  end
  local v = obj.value
  if v == nil then
    v = obj.Value
  end
  if v == nil then
    return nil
  end
  if type(v) == "table" then
    return v
  end
  if type(v) == "string" then
    return nk.json_decode(v)
  end
  return nil
end

--- Владелец ключа в storage: min(user_a, user_b). Проверка, что RPC вызывает участник пары.
local function resolve_owner_and_verify_ctx(ctx, m)
  local ua = m.user_a
  local ub = m.user_b
  local me = ctx.user_id
  if ua == nil or ua == "" then
    return nil, "bad_user_a"
  end
  if ub == nil or ub == "" then
    if me ~= ua then
      return nil, "bad_ctx"
    end
    return ua, nil
  end
  if me ~= ua and me ~= ub then
    return nil, "bad_ctx"
  end
  if ua < ub then
    return ua, nil
  end
  return ub, nil
end

local function read_pins(match_id, owner_user_id)
  local r = nk.storage_read({ { collection = COLLECTION, key = match_id, user_id = owner_user_id } })
  if r == nil or #r == 0 then
    return nil
  end
  return decode_storage_value(r[1])
end

local function duel_match_ensure_pins(ctx, payload)
  local ok, result = pcall(function()
    if payload == nil or payload == "" then
      return nk.json_encode({ ok = false, err = "empty_payload" })
    end
    local m = nk.json_decode(payload)
    if not m or not m.match_id or m.match_id == "" then
      return nk.json_encode({ ok = false, err = "bad_payload" })
    end
    local match_id = m.match_id
    local owner, err = resolve_owner_and_verify_ctx(ctx, m)
    if owner == nil then
      return nk.json_encode({ ok = false, err = err or "bad_owner" })
    end

    if read_pins(match_id, owner) ~= nil then
      return nk.json_encode({ ok = true })
    end

    local pin_a = random_pin(2)
    local pin_b = random_pin(3)
    nk.storage_write({
      {
        collection = COLLECTION,
        key = match_id,
        user_id = owner,
        value = { pin_a = pin_a, pin_b = pin_b },
        permission_read = 0,
        permission_write = 0,
      },
    })
    return nk.json_encode({ ok = true })
  end)
  if not ok then
    nk.logger_error("duel_match_ensure_pins: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error", detail = tostring(result) })
  end
  return result
end

local function duel_keypad_guess(ctx, payload)
  local ok, result = pcall(function()
    if payload == nil or payload == "" then
      return nk.json_encode({ ok = false, err = "empty_payload" })
    end
    local m = nk.json_decode(payload)
    if not m or not m.match_id or not m.door_id or m.guess == nil then
      return nk.json_encode({ ok = false, err = "bad_payload" })
    end
    local owner, err = resolve_owner_and_verify_ctx(ctx, m)
    if owner == nil then
      return nk.json_encode({ ok = false, err = err or "bad_owner" })
    end

    local match_id = m.match_id
    local door_id = tonumber(m.door_id)
    local guess = tostring(m.guess)
    local pins = read_pins(match_id, owner)
    if pins == nil then
      return nk.json_encode({ ok = false, err = "no_pins" })
    end
    local pin
    if door_id == 1 or door_id == 3 then
      pin = pins.pin_a
    elseif door_id == 2 or door_id == 4 then
      pin = pins.pin_b
    else
      return nk.json_encode({ ok = false, err = "bad_door" })
    end
    if pin == nil then
      return nk.json_encode({ ok = false, err = "pin_missing" })
    end
    if string.len(guess) ~= string.len(pin) then
      return nk.json_encode({ ok = false, err = "bad_length" })
    end
    for i = 1, string.len(guess) do
      local c = string.sub(guess, i, i)
      if c < "0" or c > "9" then
        return nk.json_encode({ ok = false, err = "bad_chars" })
      end
    end
    local bulls, cows = score(pin, guess)
    local granted = (guess == pin)
    return nk.json_encode({ ok = true, granted = granted, bulls = bulls, cows = cows })
  end)
  if not ok then
    nk.logger_error("duel_keypad_guess: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error", detail = tostring(result) })
  end
  return result
end

nk.register_rpc(duel_match_ensure_pins, "duel_match_ensure_pins")
nk.register_rpc(duel_keypad_guess, "duel_keypad_guess")
