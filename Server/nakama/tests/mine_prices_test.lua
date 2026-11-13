-- Runtime prices, Storage CAS migration, and immutable admission quotes.
package.path = "Server/nakama/?.lua;Server/nakama/modules/?.lua;Server/nakama/tests/?.lua;" .. package.path
local Fixture = require("storage_fault_fixture")
local fixture = Fixture.new()
fixture.install()
local nk = require("nakama")
local CFG = require("modules.duel_match3_config")
local Prices = require("modules.duel_mine_prices")
local PveEntry = require("modules.duel_match3_pve_entry")

local function old_price(amount, matter)
  local cost = {{resource = "energy", amount = amount}}
  if matter then cost[2] = {resource = "matter", amount = matter} end
  return cost
end

local function legacy_catalog()
  local bots = {}
  for floor = 1, 12 do
    local id = "mine_" .. floor
    bots[id] = { id = id, floor = floor, custom = "kept", reward_gold = 10 + floor,
      cost_attack = old_price(floor == 12 and 80 or floor <= 7 and 15 or 25, floor == 12 and 5 or nil),
      cost_banish = old_price(floor == 12 and 40 or floor <= 7 and 5 or 15, floor == 12 and 3 or nil) }
  end
  return { version = 1, bots = bots, operator_note = "kept" }
end

local owner, collection, keys = CFG.BOTS_STORAGE_USER_ID, CFG.BOTS_COLLECTION, CFG.BOTS_KEYS_BY_DIFFICULTY
for _, key in pairs(keys) do fixture.put(collection, owner, key, legacy_catalog()) end
local function upgrade()
  return Prices.upgrade_catalogs(collection, owner, keys)
end
local function catalog(tier)
  return fixture.get(collection, owner, keys[tier])
end
local function one_energy(price)
  assert(#price == 1 and price[1].resource == "energy" and price[1].amount > 0)
  return price[1].amount
end

fixture.seed("player", { energy = 100, matter = 10 })
local progress = fixture.read_progress("player")
progress.energy, progress.matter = 100, 10
fixture.put("progress", "player", "profile", progress)
local entry = PveEntry.new({ read_pve_progress = fixture.read_progress,
  build_pve_progress_write = fixture.build_progress, now = function() return fixture.clock end })
local metadata = { request_key = "easy:12:mine_12", floor = 12, difficulty = "easy" }
local paid, _, receipt = entry.reserve("player", catalog("easy").bots.mine_12.cost_attack, metadata)
assert(paid and receipt.costs[1].amount == 80 and receipt.costs[2].amount == 5)
assert(fixture.read_progress("player").energy == 20 and fixture.read_progress("player").matter == 5)

local ok, count = upgrade()
assert(ok and count == 3 and #fixture.writes[#fixture.writes] == 3)
local previous_tier = {}
for _, tier in ipairs({ "easy", "medium", "hard" }) do
  local v2 = catalog(tier)
  assert(v2.version == Prices.VERSION and v2.operator_note == "kept")
  local previous_normal_attack, previous_normal_banish, previous_boss_attack, previous_boss_banish = 0, 0, 0, 0
  for floor = 1, 12 do
    local bot = v2.bots["mine_" .. floor]
    local attack, banish = Prices.for_floor(tier, floor)
    local a, b = one_energy(attack), one_energy(banish)
    assert(a == one_energy(bot.cost_attack) and b == one_energy(bot.cost_banish))
    assert(b < a and a <= CFG.PVE_ENERGY_REGEN_CAP and bot.custom == "kept" and bot.reward_gold == 10 + floor)
    if floor % 4 == 0 then
      assert(a > previous_boss_attack and b > previous_boss_banish and a > previous_normal_attack and b > previous_normal_banish)
      previous_boss_attack, previous_boss_banish = a, b
    else
      assert(a > previous_normal_attack and b >= previous_normal_banish)
      previous_normal_attack, previous_normal_banish = a, b
    end
    local p = previous_tier[floor]
    assert(not p or (a > p.attack and b > p.banish))
    previous_tier[floor] = { attack = a, banish = b }
  end
end
assert(select(2, upgrade()) == 0)
print("PASS all 36 tier/floor prices, three-object CAS write, non-price fields and idempotence")

local replayed, _, same = entry.reserve("player", catalog("easy").bots.mine_12.cost_attack, metadata)
assert(replayed and same.token == receipt.token and same.costs[1].amount == 80 and same.costs[2].amount == 5)
assert(entry.refund_launch("player", receipt.token, "fixture_before_start"))
assert(fixture.read_progress("player").energy == 100 and fixture.read_progress("player").matter == 10)
local new_paid, _, fresh = entry.reserve("player", catalog("easy").bots.mine_12.cost_attack, metadata)
assert(new_paid and fresh.token ~= receipt.token and one_energy(fresh.costs) == 45)
assert(fixture.read_progress("player").energy == 55 and fixture.read_progress("player").matter == 10)
print("PASS old reservation keeps 80 energy + 5 matter, refunds the paid amount, then next entry charges 45 energy")

local override = legacy_catalog()
override.bots.mine_12.cost_attack = old_price(70, 3)
fixture.put(collection, owner, keys.medium, override)
fixture.put(collection, owner, keys.easy, legacy_catalog())
local before = #fixture.writes
local migrated, err = upgrade()
assert(not migrated and err == "catalog_prices_changed:" .. keys.medium and #fixture.writes == before)
assert(catalog("medium").bots.mine_12.cost_attack[1].amount == 70 and catalog("easy").version == 1)
print("PASS customized price aborts the whole migration without modifying any catalog")

fixture.put(collection, owner, keys.medium, legacy_catalog())
local raw_write = nk.storage_write
local writes = 0
nk.storage_write = function(batch)
  writes = writes + 1
  if writes == 1 then error("Storage write rejected - version check failed") end
  return raw_write(batch)
end
assert(select(2, upgrade()) == 2 and writes == 2 and catalog("hard").version == 2)
print("PASS catalog CAS conflict re-reads and retries the bounded batch")
