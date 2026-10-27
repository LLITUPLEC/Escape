-- Server-only inventory state. No storage/RPC work: callers commit the mutated
-- sheet, wallet and outcome journal in one version-checked storage batch.
local M = { BASE_CAPACITY = 25, MAX_CAPACITY = 50, SLOT_PRICE_MATTER = 3, SALE_RECEIPT_LIMIT = 64 }
local MAX_SAFE_INTEGER = 9007199254740991
local function integer(value, fallback)
  local n = tonumber(value)
  if not n or n ~= n or n == math.huge or n == -math.huge then return fallback or 0 end
  return math.max(0, math.min(MAX_SAFE_INTEGER, math.floor(n)))
end
local function extra(sheet)
  return math.min(25, integer(sheet and sheet.inventory_extra_slots))
end
function M.capacity(sheet) return M.BASE_CAPACITY + extra(sheet) end
function M.observe_recipes(sheet, defs)
  if sheet.first_recipe_received == true then return true end
  local function recipe(id) return type(defs)=="table" and type(defs[id])=="table" and defs[id].kind=="recipe" end
  for _,id in pairs(type(sheet.inventory)=="table" and sheet.inventory or {}) do
    if recipe(id) then sheet.first_recipe_received=true;return true end
  end
  for _,id in pairs(type(sheet.learned_recipes)=="table" and sheet.learned_recipes or {}) do
    if recipe(id) then sheet.first_recipe_received=true;return true end
  end
  for _,row in ipairs(type(sheet.inventory_pending_loot)=="table" and sheet.inventory_pending_loot or {}) do
    if type(row)=="table" and recipe(row.def_id) then sheet.first_recipe_received=true;return true end
  end
  for _,row in ipairs(type(sheet.inventory_sale_receipts)=="table" and sheet.inventory_sale_receipts or {}) do
    if type(row)=="table" and recipe(row.sold_item_id or row.expected_def_id) then sheet.first_recipe_received=true;return true end
  end
  for _,row in ipairs(type(sheet.workshop_slots)=="table" and sheet.workshop_slots or {}) do
    if type(row)=="table" and type(defs)=="table" and type(defs[row.output_def_id])=="table" then
      sheet.first_recipe_received=true;return true -- An already-started legacy craft is trusted proof.
    end
  end
  sheet.first_recipe_received=false;return false
end
local function append_pending(pending, id, amount)
  if type(id) ~= "string" or id == "" or amount <= 0 then return end
  for _, row in ipairs(pending) do
    if row.def_id == id then
      assert(row.amount <= MAX_SAFE_INTEGER - amount, "pending_loot_overflow")
      row.amount = row.amount + amount
      return
    end
  end
  pending[#pending + 1] = { def_id = id, amount = amount }
end
function M.normalize(sheet)
  sheet.inventory = type(sheet.inventory) == "table" and sheet.inventory or {}
  sheet.inventory_counts = type(sheet.inventory_counts) == "table" and sheet.inventory_counts or {}
  local inv, counts = sheet.inventory, sheet.inventory_counts
  local unlocked = extra(sheet)
  -- Existing populated cells are never truncated by a legacy/missing capacity.
  for i = 26, 50 do if type(inv[i]) == "string" and inv[i] ~= "" then unlocked = math.max(unlocked, i - 25) end end
  sheet.inventory_extra_slots = unlocked
  local pending = {}
  for _, row in ipairs(type(sheet.inventory_pending_loot) == "table" and sheet.inventory_pending_loot or {}) do
    if type(row) == "table" then append_pending(pending, row.def_id, integer(row.amount)) end
  end
  local overflow = {}
  for k, id in pairs(inv) do
    if type(k) == "number" and k > 50 and k == math.floor(k) and type(id) == "string" and id ~= "" then overflow[#overflow + 1] = k end
  end
  table.sort(overflow)
  for _, i in ipairs(overflow) do
    append_pending(pending, inv[i], math.max(1, integer(counts[i], 1)))
    inv[i], counts[i] = nil, nil
  end
  for i = 1, M.capacity(sheet) do
    local id = inv[i]
    if id == nil or id == false then id = "" elseif type(id) ~= "string" then id = tostring(id) end
    inv[i] = id
    counts[i] = id == "" and 0 or math.max(1, integer(counts[i], 1))
  end
  sheet.inventory_pending_loot = pending
  local receipts = {}
  for k, row in pairs(type(sheet.inventory_slot_receipts) == "table" and sheet.inventory_slot_receipts or {}) do
    local index = tonumber(k)
    if index and index == math.floor(index) and index >= 25 and index < M.capacity(sheet)
        and type(row) == "table" and type(row.request_id) == "string" then
      receipts[tostring(index)] = { request_id = row.request_id }
    end
  end
  sheet.inventory_slot_receipts = receipts
  local sales, input = {}, sheet.inventory_sale_receipts
  if type(input) == "table" then
    for i = math.max(1, #input - M.SALE_RECEIPT_LIMIT + 1), #input do
      local row = input[i]
      if type(row) == "table" and type(row.request_id) == "string" and #row.request_id <= 80
          and type(row.expected_def_id) == "string" and (row.source == "inventory" or row.source == "equipment")
          and type(row.index) == "number" and row.index == math.floor(row.index) and row.index >= 0 and row.index < 50 then
        sales[#sales + 1] = { request_id = row.request_id, expected_def_id = row.expected_def_id,
          source = row.source, index = row.index, sold_item_id = row.sold_item_id,
          sold_count = integer(row.sold_count), gold_gained = integer(row.gold_gained) }
      end
    end
  end
  sheet.inventory_sale_receipts = sales
  return sheet
end
local function stack_size(defs, id)
  local def = type(defs) == "table" and defs[id]
  if type(def) ~= "table" then return nil end
  return math.max(1, integer(def.max_stack, 1))
end
function M.free_amount(sheet, id, defs)
  local stack = stack_size(defs, id)
  if not stack then return 0, "unknown_item" end
  local space = 0
  for i = 1, M.capacity(sheet) do
    local current = sheet.inventory[i] or ""
    if current == "" then space = space + stack
    elseif current == id then space = space + math.max(0, stack - integer(sheet.inventory_counts[i])) end
  end
  return space
end
local function add_exact(sheet, id, amount, stack)
  for i = 1, M.capacity(sheet) do
    if sheet.inventory[i] == id then
      local n = integer(sheet.inventory_counts[i])
      local add = math.min(math.max(0, stack - n), amount)
      sheet.inventory_counts[i], amount = n + add, amount - add
      if amount == 0 then return end
    end
  end
  for i = 1, M.capacity(sheet) do
    if (sheet.inventory[i] or "") == "" then
      local add = math.min(stack, amount)
      sheet.inventory[i], sheet.inventory_counts[i], amount = id, add, amount - add
      if amount == 0 then return end
    end
  end
  assert(amount == 0, "inventory_preflight_mismatch")
end
function M.try_add(sheet, id, amount, defs)
  amount = integer(amount)
  if amount == 0 then return true end
  M.normalize(sheet)
  local room, err = M.free_amount(sheet, id, defs)
  if err then return false, err end
  if room < amount then return false, "inventory_full" end
  add_exact(sheet, id, amount, stack_size(defs, id))
  if defs[id].kind == "recipe" then sheet.first_recipe_received = true end
  return true
end
function M.add_or_queue(sheet, id, amount, defs)
  assert(type(id) == "string" and id ~= "", "bad_loot_id")
  amount = integer(amount)
  M.normalize(sheet)
  local room = M.free_amount(sheet, id, defs)
  local delivered = math.min(amount, room)
  if delivered > 0 then add_exact(sheet, id, delivered, stack_size(defs, id)) end
  local pending = amount - delivered
  append_pending(sheet.inventory_pending_loot, id, pending)
  if amount > 0 and type(defs[id]) == "table" and defs[id].kind == "recipe" then sheet.first_recipe_received = true end
  return { def_id = id, amount = amount, delivered = delivered, pending = pending }
end
function M.pending_payload(sheet)
  local rows, total = {}, 0
  for _, row in ipairs(sheet.inventory_pending_loot or {}) do
    rows[#rows + 1] = { def_id = row.def_id, amount = row.amount }
    total = total + row.amount
  end
  return rows, total
end
function M.drain(sheet, defs)
  M.normalize(sheet)
  local pending, delivered, items = {}, 0, {}
  for _, row in ipairs(sheet.inventory_pending_loot) do
    local room = M.free_amount(sheet, row.def_id, defs)
    local n = math.min(row.amount, room)
    if n > 0 then add_exact(sheet, row.def_id, n, stack_size(defs, row.def_id)); delivered = delivered + n end
    append_pending(pending, row.def_id, row.amount - n)
    if n > 0 then items[#items + 1] = { def_id = row.def_id, amount = n } end
  end
  sheet.inventory_pending_loot = pending
  local _, total = M.pending_payload(sheet)
  return { delivered = delivered, total_pending = total, items = items }
end
-- Spendable balances exclude pending loot and already consumed items.
function M.resource_totals(sheet, defs)
  local totals = { ingots = 0, tesseracts = 0, blueprint_green = 0, blueprint_blue = 0, blueprint_purple = 0, blueprint_gold = 0 }
  for i = 1, M.capacity(sheet) do
    local id, amount = sheet.inventory[i] or "", integer(sheet.inventory_counts[i])
    if id == "ingot_green" or id == "ingot_blue" or id == "ingot_purple" then totals.ingots = totals.ingots + amount
    elseif id == "tesseract" then totals.tesseracts = totals.tesseracts + amount
    elseif id ~= "" then
      local def = type(defs) == "table" and defs[id]
      local color = id:match("^recipe_drop_t%d+_(%a+)_") or id:match("^recipe_(%a+)$")
      if not color and type(def) == "table" and def.kind == "recipe" then
        color = ({uncommon="green",rare="blue",epic="purple",legendary="gold"})[def.quality] or def.quality
      end
      local key = color and "blueprint_" .. color
      if key and totals[key] ~= nil then totals[key] = totals[key] + amount end
    end
  end
  return totals
end
function M.sync_resource_counters(sheet, progress, defs)
  local totals = M.resource_totals(sheet, defs)
  for key, value in pairs(totals) do progress[key] = value end
  return totals
end
function M.sale_receipt(sheet, request_id)
  for _, receipt in ipairs(sheet.inventory_sale_receipts or {}) do
    if receipt.request_id == request_id then return receipt end
  end
end
function M.remember_sale(sheet, receipt)
  local rows = sheet.inventory_sale_receipts or {}
  rows[#rows + 1] = receipt
  while #rows > M.SALE_RECEIPT_LIMIT do table.remove(rows, 1) end
  sheet.inventory_sale_receipts = rows
end
function M.unlock_slot(sheet, progress, index, request_id)
  if type(index) ~= "number" or index ~= math.floor(index) or index < 25 or index >= 50 then return false, "bad_slot_index" end
  if type(request_id) ~= "string" or #request_id < 8 or #request_id > 80 or not request_id:match("^[%w_%-]+$") then return false, "bad_request_id" end
  M.normalize(sheet)
  for k, row in pairs(sheet.inventory_slot_receipts) do
    if row.request_id == request_id and tonumber(k) ~= index then return false, "request_id_reused" end
  end
  -- Absolute slot identity makes a retry safe even after an ambiguous response,
  -- or when another request already bought this same cell.
  if index < M.capacity(sheet) then return true, "already_unlocked" end
  if index ~= M.capacity(sheet) then return false, "unlock_previous_slot" end
  local balance = integer(progress.matter)
  if balance < M.SLOT_PRICE_MATTER then return false, "not_enough_matter" end
  progress.matter = balance - M.SLOT_PRICE_MATTER
  sheet.inventory_extra_slots = extra(sheet) + 1
  sheet.inventory[index + 1], sheet.inventory_counts[index + 1] = "", 0
  sheet.inventory_slot_receipts[tostring(index)] = { request_id = request_id }
  return true, "purchased"
end
return M
