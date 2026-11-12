-- Record wallet activity on the same CAS write that grants or spends it.
-- Tournament entry totals live in the bracket's atomic summary writes because
-- the queue holds a refundable deposit until the bracket is formed.
local M = {}
local MAX_COUNT = 2000000000
local currencies = { "gold", "ore", "matter" }
local earned_keys = { gold = "res.gold_earned", ore = "res.ore_earned", matter = "res.matter_earned" }
local spent_keys = { gold = "res.gold_spent", ore = "res.ore_spent", matter = "res.matter_spent" }
local progress_keys = {}
for _, currency in ipairs(currencies) do
  progress_keys[earned_keys[currency]] = true
  progress_keys[spent_keys[currency]] = true
end

local function count(value)
  local n = tonumber(value)
  if not n or n ~= n or n <= 0 or n == math.huge then return 0 end
  return math.floor(math.min(n, MAX_COUNT))
end

function M.add_counter(counters, key, amount)
  local delta = count(amount)
  if delta > 0 then counters[key] = math.min(MAX_COUNT, count(counters[key]) + delta) end
end

local function record(progress, key, amount)
  local delta = count(amount)
  if not key or delta == 0 then return end
  if type(progress.resource_medals) ~= "table" then progress.resource_medals = {} end
  M.add_counter(progress.resource_medals, key, delta)
end

function M.earned(progress, gold, ore, matter)
  record(progress, earned_keys.gold, gold)
  record(progress, earned_keys.ore, ore)
  record(progress, earned_keys.matter, matter)
end

function M.spent(progress, gold, ore, matter)
  record(progress, spent_keys.gold, gold)
  record(progress, spent_keys.ore, ore)
  record(progress, spent_keys.matter, matter)
end

function M.spent_currency(progress, currency, amount)
  record(progress, spent_keys[currency], amount)
end

function M.is_currency(currency)
  return spent_keys[currency] ~= nil
end

function M.spent_costs(progress, costs)
  for _, cost in ipairs(costs) do
    M.spent_currency(progress, cost.resource, cost.amount)
  end
end

function M.uses_progress(key)
  return progress_keys[key] == true
end

function M.total(stats, progress, key)
  local wallet = progress and progress.resource_medals
  return math.min(MAX_COUNT, count(stats[key]) + (type(wallet) == "table" and count(wallet[key]) or 0))
end

function M.merged_stats(stats, progress)
  local result = {}
  for key, value in pairs(stats) do result[key] = value end
  for key in pairs(progress_keys) do
    local total = M.total(stats, progress, key)
    if total > 0 then result[key] = total end
  end
  return result
end

return M
