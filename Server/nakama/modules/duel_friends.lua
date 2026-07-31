local nk = require("nakama")

local function sanitize_username(raw)
  local s = tostring(raw or "")
  s = string.gsub(s, "^%s+", "")
  s = string.gsub(s, "%s+$", "")
  return s
end

local function row_field(row, key, index)
  if row == nil then
    return nil
  end
  local v = row[key]
  if v == nil then
    v = row[index]
  end
  if v == nil or v == "" then
    return nil
  end
  return tostring(v)
end

-- Резолв username без учёта регистра → точный user_id / username.
local function duel_friends_resolve_username(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local p = {}
    if payload ~= nil and payload ~= "" then
      local dec_ok, body = pcall(nk.json_decode, payload)
      if dec_ok and type(body) == "table" then
        p = body
      end
    end

    local name = sanitize_username(p.username or p.name or "")
    if name == "" then
      return nk.json_encode({ ok = false, err = "empty_username" })
    end

    local q_ok, rows = pcall(function()
      return nk.sql_query(
        "SELECT id, username FROM users WHERE lower(username) = lower($1) LIMIT 1",
        { name }
      )
    end)
    if not q_ok then
      nk.logger_error("duel_friends_resolve_username sql: " .. tostring(rows))
      return nk.json_encode({ ok = false, err = "server_error" })
    end
    if rows == nil or #rows == 0 then
      return nk.json_encode({ ok = false, err = "user_not_found" })
    end

    local row = rows[1]
    local found_id = row_field(row, "id", 1)
    local found_name = row_field(row, "username", 2)
    if found_id == nil or found_name == nil then
      return nk.json_encode({ ok = false, err = "user_not_found" })
    end

    return nk.json_encode({
      ok = true,
      user_id = found_id,
      username = found_name,
    })
  end)

  if not ok then
    nk.logger_error("duel_friends_resolve_username: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

nk.register_rpc(duel_friends_resolve_username, "duel_friends_resolve_username")
