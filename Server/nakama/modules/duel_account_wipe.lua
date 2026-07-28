--[[
  Удаление прогресса текущего игрока: storage (character/progress/stats) + опционально account_delete.
]]
local nk = require("nakama")

local function runtime_lua_require_nested(name_nested, name_root)
  local ok, mod = pcall(require, name_nested)
  if ok and mod ~= nil then return mod end
  return require(name_root)
end

local CFG = runtime_lua_require_nested("modules.duel_match3_config", "duel_match3_config")

local function delete_user_storage_key(user_id, collection, key)
  local ok, err = pcall(function()
    nk.storage_delete({
      {
        collection = collection,
        key = key,
        user_id = user_id,
      },
    })
  end)
  if not ok then
    nk.logger_warn(string.format("account_wipe storage_delete %s/%s: %s", tostring(collection), tostring(key), tostring(err)))
  end
end

local function wipe_user_objects(user_id)
  -- Известные ключи прогресса.
  delete_user_storage_key(user_id, CFG.CHARACTER_SHEET_COLLECTION, CFG.CHARACTER_SHEET_KEY)
  delete_user_storage_key(user_id, CFG.PVE_PROGRESS_COLLECTION, CFG.PVE_PROGRESS_KEY)
  delete_user_storage_key(user_id, CFG.STATS_COLLECTION, CFG.STATS_KEY)

  -- На всякий случай вычищаем все объекты пользователя в этих коллекциях.
  local collections = {
    CFG.CHARACTER_SHEET_COLLECTION,
    CFG.PVE_PROGRESS_COLLECTION,
    CFG.STATS_COLLECTION,
  }
  for _, coll in ipairs(collections) do
    local ok, objects = pcall(function()
      return nk.storage_list(user_id, coll, 100, "")
    end)
    if ok and type(objects) == "table" then
      local to_del = {}
      for _, obj in ipairs(objects) do
        if type(obj) == "table" and obj.key ~= nil then
          to_del[#to_del + 1] = {
            collection = coll,
            key = tostring(obj.key),
            user_id = user_id,
          }
        end
      end
      if #to_del > 0 then
        pcall(function()
          nk.storage_delete(to_del)
        end)
      end
    end
  end
end

local function duel_account_wipe(ctx, payload)
  local ok, result = pcall(function()
    local user_id = ctx and ctx.user_id or ""
    if user_id == nil or user_id == "" then
      return nk.json_encode({ ok = false, err = "unauthorized" })
    end

    wipe_user_objects(user_id)

    -- Полное удаление аккаунта Nakama (email/device ids), если runtime поддерживает.
    local deleted = false
    local ok_del, err_del = pcall(function()
      if type(nk.account_delete_id) == "function" then
        nk.account_delete_id(user_id)
        deleted = true
      end
    end)
    if not ok_del then
      nk.logger_warn("account_wipe account_delete_id: " .. tostring(err_del))
    end

    return nk.json_encode({
      ok = true,
      account_deleted = deleted,
    })
  end)

  if not ok then
    nk.logger_error("duel_account_wipe: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  return result
end

nk.register_rpc(duel_account_wipe, "duel_account_wipe")
