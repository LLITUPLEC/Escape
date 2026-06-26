--[[
  Одноразовая очистка лидербордов (dev/staging).
  Вызов RPC (нужна авторизация игрока):
    payload: {"mode":"broken"}  — только ID с %GW%V или символом %
    payload: {"mode":"all_lb"}  — все lb_*

  После очистки файл можно удалить с сервера.
]]
local nk = require("nakama")

local function row_id(row)
  if row == nil then return nil end
  return row.id or row[1] or row["id"]
end

local function fetch_ids(sql, param)
  local ok, rows = pcall(function()
    if param == nil then
      return nk.sql_query(sql)
    end
    return nk.sql_query(sql, { param })
  end)
  if not ok or rows == nil then
    nk.logger_error("leaderboard_purge sql_query: " .. tostring(rows))
    return {}
  end
  local ids = {}
  for _, row in ipairs(rows) do
    local id = row_id(row)
    if id ~= nil and id ~= "" then
      ids[#ids + 1] = tostring(id)
    end
  end
  return ids
end

local function purge_ids(ids)
  local deleted, failed = 0, 0
  for _, id in ipairs(ids) do
    local ok, err = pcall(function()
      nk.leaderboard_delete(id)
    end)
    if ok then
      deleted = deleted + 1
      nk.logger_info("leaderboard_purge deleted: " .. id)
    else
      failed = failed + 1
      nk.logger_error("leaderboard_purge failed " .. id .. ": " .. tostring(err))
    end
  end
  return deleted, failed
end

local function duel_leaderboard_admin_purge(ctx, payload)
  local ok, result = pcall(function()
    if ctx == nil or ctx.user_id == nil or ctx.user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    local mode = "broken"
    if payload ~= nil and payload ~= "" then
      local dec_ok, body = pcall(nk.json_decode, payload)
      if dec_ok and body ~= nil and body.mode ~= nil then
        mode = tostring(body.mode)
      end
    end

    local ids
    if mode == "all_lb" then
      ids = fetch_ids([[SELECT id FROM leaderboard WHERE id LIKE 'lb\_%' ESCAPE '\']])
    else
      ids = fetch_ids([[SELECT id FROM leaderboard WHERE id LIKE '%\%GW\%V' ESCAPE '\']])
      local pct_ids = fetch_ids([[SELECT id FROM leaderboard WHERE id LIKE '%\%%' ESCAPE '\']])
      local seen = {}
      for _, id in ipairs(ids) do seen[id] = true end
      for _, id in ipairs(pct_ids) do
        if not seen[id] then
          ids[#ids + 1] = id
          seen[id] = true
        end
      end
    end

    local deleted, failed = purge_ids(ids)
    return nk.json_encode({
      ok = true,
      mode = mode,
      matched = #ids,
      deleted = deleted,
      failed = failed,
      ids = ids,
    })
  end)

  if not ok then
    nk.logger_error("duel_leaderboard_admin_purge: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

nk.register_rpc(duel_leaderboard_admin_purge, "duel_leaderboard_admin_purge")
