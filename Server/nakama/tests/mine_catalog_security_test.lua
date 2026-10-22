-- Exercise the actual barrier initialization and catalog RPC together.
-- Run from repository root: lua Server/nakama/tests/mine_catalog_security_test.lua
package.path = "Server/nakama/?.lua;" .. package.path
local nk = { json_encode = function(v) return v end, logger_error = function() end }
package.preload.nakama = function() return nk end

local file = assert(io.open("Server/nakama/modules/duel_match3.lua", "r"))
local source = file:read("*a"); file:close()
local start = assert(source:find("function get_unlocked_floor(progress, diff)", 1, true))
local finish = assert(source:find("local function try_match_create", start, true))
local catalog = assert(source:find("local function duel_match3_pve_catalog_get", 1, true))
local barriers = assert(source:find("local MineBarriers = runtime_lua_require", 1, true))
assert(barriers < catalog, "barrier instance must exist before catalog callback is defined")
assert(not source:find("local MineBarriers\n", 1, true), "catalog must not capture an uninitialized forward reference")

local difficulty, read_failure, reads = "easy", false, 0
local cfg = {
  PVE_MAX_LEVEL = 12, MINE_DIFFICULTY_DEFAULT = "easy", LEVEL_XP = { 0, 10, 100 },
  MINE_BARRIER_REQUIREMENTS = { [2] = { level = 2, gold = 10, ore = 5, matter = 1, key_id = "miner_key", key_amount = 1 } },
  clamp_int = function(v, lo, hi) return math.max(lo or 0, math.min(hi or math.huge, math.floor(tonumber(v) or 0))) end,
}
local function normalize(v)
  if v == "medium" or v == "hard" then return v end
  return "easy"
end
local env = setmetatable({
  nk = nk, CFG = cfg, Guard = {},
  runtime_lua_require = function(nested) return require(nested) end,
  normalize_mine_difficulty = normalize,
  normalize_mine_unlocked = function(map) return map or { easy = 1, medium = 0, hard = 0 } end,
  make_floor_state_key = function(diff, floor) return diff .. ":" .. tostring(floor) end,
  read_pve_progress = function()
    reads = reads + 1
    if read_failure then error("storage unavailable") end
    return { level = 1, mine = { current_difficulty = difficulty, unlocked = { easy = 1, medium = 0, hard = 0 }, floor_states = {} } }
  end,
  write_pve_progress = function() error("catalog must not write progress") end,
  build_progression_payload_auto = function() return { level = 1 } end,
  build_resource_payload = function() return {} end,
  random_affix_for_floor = function() return "normal" end,
  empty_key_items = function() return {} end,
  Metrics = { bot_dps_ehp_product = function() return 6, 2, 3 end },
  PveMineCost = { DEFAULT_ATTACK = {}, DEFAULT_BANISH = {} },
  get_merged_bots = function() return {
    boss = { id = "mine_4", floor = 4, is_boss = true },
    regular = { id = "mine_1", floor = 1, is_boss = false },
  } end,
  floor_respawn_left_seconds = function() return 0 end,
}, { __index = _G })
local chunk = assert(loadstring(source:sub(start, finish - 1) .. "\nreturn duel_match3_pve_catalog_get"))
setfenv(chunk, env)
local get_catalog = chunk()

for _, pair in ipairs({ { "easy", 1 }, { "medium", 3 }, { "hard", 5 } }) do
  difficulty = pair[1]
  local result = get_catalog({ user_id = "player" }, "{}")
  assert(result.ok and result.mine_difficulty == difficulty and result.barrier_cost_multiplier == pair[2])
  local req = result.barrier_requirements[2]
  assert(req.gold == 10 * pair[2] and req.ore == 5 * pair[2] and req.matter == pair[2])
  assert(req.level == 2 and req.key_amount == 1)
  assert(#result.bots == 2 and result.bots[1].id == "mine_1" and result.bots[2].is_boss)
  assert(#result.mine_floors == 2 and result.mine_floors[1].unlocked)
end
assert(reads == 3)
assert(get_catalog({}, "{}").err == "unauthorized" and reads == 3)
read_failure = true
assert(get_catalog({ user_id = "player" }, "{}").err == "server_error")
print("mine catalog security: 5 cases passed")
