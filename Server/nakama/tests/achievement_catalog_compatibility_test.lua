package.path = "Server/nakama/?.lua;" .. package.path
local fixture = dofile("Server/nakama/tests/storage_fault_fixture.lua").new()
fixture.install()
local CFG = require("modules.duel_match3_config")
local Catalog = require("modules.duel_match3_achievement_catalog")

local definition = {
  categories = {{id="obsession"}, {id="resources", min_journal_version=2}, {id="future", min_journal_version=3}},
  chains = {
    {id="obs.old", counter_key="obs.old", steps={{threshold_delta=1}, {threshold_delta=4}}},
    {id="res.new", counter_key="res.new", min_journal_version=2, threshold_mode="absolute_total",
      steps={{threshold_delta=1}, {threshold_delta=5}}},
    {id="res.future", counter_key="res.future", min_journal_version=3, steps={{threshold_delta=1}}},
  },
}
local function seed()
  fixture.put(CFG.ACHIEVEMENT_DEFS_COLLECTION, CFG.ACHIEVEMENT_DEFS_STORAGE_USER_ID, CFG.ACHIEVEMENT_DEFS_KEY, definition)
end
seed()

local function catalog(payload)
  local result = Catalog.rpc_achievement_catalog_get({user_id="player"}, payload)
  assert(result.ok and result.catalog_source == "storage")
  return result
end

for _, request in ipairs({{}, {journal_version=1}, {journal_version="bad"}, {journal_version=1.5}}) do
  local legacy = catalog(request)
  assert(#legacy.categories == 1 and legacy.categories[1].id == "obsession")
  assert(#legacy.chains == 1 and legacy.chains[1].id == "obs.old")
end
print("PASS legacy requests return only the categories and chains their journal supports")

local current = catalog({journal_version=2, force_refresh=true})
assert(#current.categories == 2 and current.categories[2].id == "resources")
assert(#current.chains == 2 and current.chains[2].id == "res.new")
assert(#catalog().chains == 1 and #catalog({journal_version=3}).chains == 3)
print("PASS current clients receive new rows without mutating the shared cache for other clients")

assert(#Catalog.all_chains() == 3)
assert(Catalog.cumulative_need(Catalog.chain_by_id("obs.old"), 1) == 5)
assert(Catalog.cumulative_need(Catalog.chain_by_id("res.new"), 1) == 5)
print("PASS server claim lookups retain both cumulative-delta and absolute totals")

definition.categories = {obsession = {label_ru="Одержимость"}, resources = {label_ru="Ресурсы", min_journal_version=2}}
seed()
assert(catalog({force_refresh=true}).categories.obsession.label_ru == "Одержимость")
assert(catalog().categories.resources == nil and catalog({journal_version=2}).categories.resources.label_ru == "Ресурсы")
print("PASS category objects keep existing keys and visibility rules")
