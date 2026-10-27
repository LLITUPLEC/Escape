local nk = require("nakama")
local inventory_ok, Inventory = pcall(require, "modules.duel_match3_inventory")
if not inventory_ok then Inventory = require("duel_match3_inventory") end
return function(deps)
  local M = {}
  local function execute(ctx, payload, buying)
    local uid = ctx and ctx.user_id
    if type(uid) ~= "string" or uid == "" then return nk.json_encode({ ok = false, err = "unauthorized" }) end
    local valid, why = deps.Guard.assert_client_epoch_matches(uid, payload)
    if not valid then return nk.json_encode({ ok = false, err = why }) end
    local p = payload and payload ~= "" and nk.json_decode(payload) or {}
    if type(p) ~= "table" then return nk.json_encode({ ok = false, err = "bad_payload" }) end
    local last_error
    for _ = 1, 4 do
      local sheet = deps.read_character_sheet(uid)
      local progress, version = deps.read_pve_progress(uid)
      Inventory.normalize(sheet)
      local status
      if buying then
        local ok, why = Inventory.unlock_slot(sheet, progress, p.slot_index, p.request_id)
        if not ok then return nk.json_encode({ ok = false, err = why }) end
        status = why
      end
      local defs = deps.get_merged_item_defs()
      local drained = Inventory.drain(sheet, defs)
      Inventory.sync_resource_counters(sheet, progress, defs)
      if status ~= "purchased" and drained.delivered == 0 then return deps.encode_character_ok_response(sheet, progress, uid) end
      -- The wallet and slot receipt commit together. Retry also handles a lost
      -- acknowledgement: the next read observes the receipt and never recharges.
      local written, err = pcall(deps.write_character_sheet, uid, sheet, progress, version)
      if written then return deps.encode_character_ok_response(sheet, progress, uid) end
      last_error = err
    end
    nk.logger_error("inventory transaction: " .. tostring(last_error))
    return nk.json_encode({ ok = false, err = "storage_conflict_retry" })
  end
  local function guarded(ctx, payload, buying)
    local ok, result = pcall(execute, ctx, payload, buying)
    if ok then return result end
    nk.logger_error("inventory RPC: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "server_error" })
  end
  function M.duel_character_inventory_slot_buy(ctx, payload) return guarded(ctx, payload, true) end
  function M.duel_character_loot_deliver(ctx, payload) return guarded(ctx, payload, false) end
  return M
end
