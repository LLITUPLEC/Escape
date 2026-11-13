-- Canonical prices and the private Storage v1 -> v2 transition.
local nk = require("nakama")
local M = { VERSION = 2 }
local tiers = { "easy", "medium", "hard" }
local prices = {
  easy = {
    attack = { 12, 13, 14, 22, 16, 18, 20, 32, 22, 24, 26, 45 },
    banish = { 4, 4, 5, 7, 5, 6, 6, 10, 7, 8, 8, 14 },
  },
  medium = {
    attack = { 15, 16, 18, 28, 21, 23, 25, 40, 28, 31, 34, 55 },
    banish = { 5, 5, 6, 9, 7, 7, 8, 12, 9, 10, 11, 17 },
  },
  hard = {
    attack = { 18, 21, 24, 36, 34, 39, 44, 51, 51, 55, 58, 65 },
    banish = { 6, 7, 8, 11, 10, 12, 13, 15, 15, 17, 18, 20 },
  },
}

function M.for_floor(difficulty, floor)
  local tier = prices[difficulty] or prices.easy
  return { { resource = "energy", amount = tier.attack[floor] } },
    { { resource = "energy", amount = tier.banish[floor] } }
end

local function same_price(actual, expected)
  if type(actual) ~= "table" or #actual ~= #expected then return false end
  for i, price in ipairs(expected) do
    local line = actual[i]
    if type(line) ~= "table" or line.resource ~= price.resource or line.amount ~= price.amount then return false end
    for field in pairs(line) do
      if field ~= "resource" and field ~= "amount" then return false end
    end
  end
  return true
end

local function old_prices(floor)
  if floor == 12 then
    return { { resource = "energy", amount = 80 }, { resource = "matter", amount = 5 } },
      { { resource = "energy", amount = 40 }, { resource = "matter", amount = 3 } }
  end
  return { { resource = "energy", amount = floor <= 7 and 15 or 25 } },
    { { resource = "energy", amount = floor <= 7 and 5 or 15 } }
end

local function migrate_catalog(value, difficulty)
  if type(value) ~= "table" then return nil, "invalid_catalog_value" end
  if value.version ~= 1 then return nil end
  if type(value.bots) ~= "table" then return nil, "invalid_catalog_bots" end
  local bots = {}
  for floor = 1, 12 do
    local id = "mine_" .. floor
    local old = value.bots[id]
    local attack, banish = old_prices(floor)
    if type(old) ~= "table" or old.id ~= id or old.floor ~= floor or
        not same_price(old.cost_attack, attack) or not same_price(old.cost_banish, banish) then
      return nil, "catalog_prices_changed"
    end
    local bot = {}
    for key, field in pairs(old) do bot[key] = field end
    bot.cost_attack, bot.cost_banish = M.for_floor(difficulty, floor)
    bots[id] = bot
  end
  for id in pairs(value.bots) do
    if not bots[id] then return nil, "catalog_prices_changed" end
  end
  local updated = {}
  for key, field in pairs(value) do updated[key] = field end
  updated.bots, updated.version = bots, M.VERSION
  return updated
end

function M.upgrade_catalogs(collection, owner, keys)
  local identities, difficulty_by_key = {}, {}
  for _, difficulty in ipairs(tiers) do
    local key = keys[difficulty]
    identities[#identities + 1] = { collection = collection, key = key, user_id = owner }
    difficulty_by_key[key] = difficulty
  end

  for attempt = 1, 3 do
    local read_ok, rows = pcall(nk.storage_read, identities)
    if not read_ok or type(rows) ~= "table" then return false, "catalog_read_failed" end
    local writes = {}
    for _, row in ipairs(rows) do
      if row.collection ~= collection or row.user_id ~= owner or not difficulty_by_key[row.key] or
          row.permission_read ~= 0 or row.permission_write ~= 0 or type(row.version) ~= "string" or
          row.version == "" or row.version == "*" then return false, "unexpected_catalog_row" end
      local value = row.value
      if type(value) == "string" then
        local decode_ok, decoded = pcall(nk.json_decode, value)
        if not decode_ok then return false, "invalid_catalog_json" end
        value = decoded
      end
      local updated, err = migrate_catalog(value, difficulty_by_key[row.key])
      if err then return false, err .. ":" .. row.key end
      if updated then
        writes[#writes + 1] = { collection = collection, key = row.key, user_id = owner, value = updated,
          version = row.version, permission_read = 0, permission_write = 0 }
      end
    end
    if #writes == 0 then return true, 0 end
    local written, result, err = pcall(nk.storage_write, writes)
    if written and result and err == nil then return true, #writes end
    local failure = tostring(written and err or result)
    if attempt == 3 or not failure:lower():find("version", 1, true) then return false, "catalog_write_failed" end
  end
end

return M
