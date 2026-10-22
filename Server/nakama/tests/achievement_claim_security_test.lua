-- Run from the repository root with Lua 5.1+:
-- lua Server/nakama/tests/achievement_claim_security_test.lua
package.path = "Server/nakama/?.lua;" .. package.path

local function copy(v)
  if type(v) ~= "table" then return v end
  local out = {}
  for k, x in pairs(v) do out[k] = copy(x) end
  return out
end

local cfg = {
  STATS_COLLECTION = "duel_match3_stats", STATS_KEY = "summary",
  PVE_PROGRESS_COLLECTION = "duel_match3_progress", PVE_PROGRESS_KEY = "profile",
  BOT_USER_ID_PREFIX = "bot____",
}
local db, writes, fail_write, before_write
local function id(c, k) return c .. "/" .. k end
local summary_id = id(cfg.STATS_COLLECTION, cfg.STATS_KEY)
local progress_id = id(cfg.PVE_PROGRESS_COLLECTION, cfg.PVE_PROGRESS_KEY)
local function put(key, value)
  local prev = db[key]
  db[key] = { value = copy(value), version = tostring((prev and tonumber(prev.version) or 0) + 1) }
end
local nk = {
  json_decode = copy, json_encode = copy,
  logger_error = function() end,
  storage_read = function(keys)
    local out = {}
    for _, key in ipairs(keys) do
      local row = db[id(key.collection, key.key)]
      if row then out[#out + 1] = copy(row) end
    end
    return out
  end,
  storage_write = function(batch)
    writes[#writes + 1] = copy(batch)
    if before_write then
      local fn = before_write
      before_write = nil
      fn()
    end
    if fail_write then error(fail_write) end
    -- Model Nakama's atomic version validation before committing any object.
    for _, op in ipairs(batch) do
      assert(op.permission_write == 0 and op.permission_read == 1)
      local row = db[id(op.collection, op.key)]
      if op.version == "*" then
        if row then error("version check failed") end
      elseif not row or op.version ~= row.version then
        error("version check failed")
      end
    end
    for _, op in ipairs(batch) do put(id(op.collection, op.key), op.value) end
  end,
}
local cat = {
  chain_by_id = function(chain)
    if chain == "currency" or chain == "combat" then return { counter_key = "wins" } end
  end,
  cumulative_need = function(_, step) if step == 0 or step == 1 then return (step + 1) * 5 end end,
  step_rewards = function(chain)
    if chain == "currency" then return { { type = "gold", value = 10 } } end
    return { { type = "hp_flat", value = 2 } }
  end,
  apply_wallet_rewards = function(rewards, progress)
    if rewards[1].type == "gold" then progress.gold = progress.gold + 10; return true end
    return false
  end,
}
package.preload.nakama = function() return nk end
package.preload["modules.duel_match3_config"] = function() return cfg end
package.preload["modules.duel_match3_achievement_catalog"] = function() return cat end
local achievements = require("modules.duel_match3_achievements")
local epoch_ok = true
achievements.configure({
  decode_storage_value = function(row) return row.value end,
  read_pve_progress = function()
    local row = db[progress_id]
    if row then return copy(row.value), row.version end
    return { gold = 0, ore = 0, matter = 0, energy = 100, mine = { selected_floor = 1 } }, nil
  end,
  guard_assert_client_epoch_matches = function() return epoch_ok, "session_stale" end,
})
local function reset(with_progress)
  db, writes, fail_write, before_write, epoch_ok = {}, {}, nil, nil, true
  put(summary_id, { achievement_stats = { wins = 10 }, achievement_claimed = {} })
  if with_progress ~= false then put(progress_id, { gold = 100, ore = 3, matter = 4, energy = 19, mine = { selected_floor = 7 }, race_entry_pending = true }) end
end
local function claim(chain, step, ctx)
  return achievements.rpc_achievement_claim_step(ctx or { user_id = "player" }, { chain_id = chain or "currency", step_index = step or 0 })
end
local function claimed_count() return #db[summary_id].value.achievement_claimed end
local passed = 0
local function test(name, run)
  reset()
  run()
  passed = passed + 1
  print("PASS " .. name)
end

test("claim and currency commit in a single CAS batch", function()
  local result = claim()
  assert(result.ok and result.progression.gold == 110)
  assert(#writes == 1 and #writes[1] == 2)
  assert(claimed_count() == 1 and db[progress_id].value.gold == 110)
  assert(db[progress_id].value.energy == 19 and db[progress_id].value.mine.selected_floor == 7)
  assert(db[progress_id].value.race_entry_pending == true)
end)
test("storage failure never persists a claim or loses its reward", function()
  fail_write = "database unavailable"
  local result = claim()
  assert(not result.ok and result.err == "storage_write_failed")
  assert(claimed_count() == 0 and db[progress_id].value.gold == 100)
  assert(#writes == 1)
end)
test("wallet conflict retries both snapshots and preserves concurrent gold", function()
  before_write = function()
    local progress = copy(db[progress_id].value)
    progress.gold = progress.gold + 23
    put(progress_id, progress)
  end
  assert(claim().ok and #writes == 2)
  assert(claimed_count() == 1 and db[progress_id].value.gold == 133)
end)
test("stats conflict retries without overwriting new match counters", function()
  before_write = function()
    local summary = copy(db[summary_id].value)
    summary.achievement_stats.wins = 11
    put(summary_id, summary)
  end
  assert(claim().ok and #writes == 2)
  assert(db[summary_id].value.achievement_stats.wins == 11)
end)
test("simultaneous duplicate claims grant currency once", function()
  local other
  before_write = function() other = claim() end
  local result = claim()
  assert(other.ok and not result.ok and result.err == "already_claimed")
  assert(claimed_count() == 1 and db[progress_id].value.gold == 110)
end)
test("missing progression uses create-only and rejects a racing create", function()
  reset(false)
  before_write = function() put(progress_id, { gold = 50, ore = 0, matter = 0 }) end
  assert(claim().ok and writes[1][2].version == "*" and #writes == 2)
  assert(db[progress_id].value.gold == 60 and claimed_count() == 1)
end)
test("retry exhaustion leaves both objects unchanged", function()
  fail_write = "version check failed"
  assert(not claim().ok and #writes == 6)
  assert(claimed_count() == 0 and db[progress_id].value.gold == 100)
end)
test("stat-only achievement does not rewrite progression", function()
  assert(claim("combat").ok and #writes == 1 and #writes[1] == 1)
  assert(db[progress_id].version == "1" and db[progress_id].value.gold == 100)
end)
test("negative fractional nonfinite steps are rejected before writing", function()
  for _, step in ipairs({ -1, 0.5, 1.5, math.huge, -math.huge, 0/0 }) do
    local result = claim("currency", step)
    assert(not result.ok and result.err == "bad_request")
  end
  assert(#writes == 0)
end)
test("threshold prerequisites duplicates auth and epoch remain enforced", function()
  assert(claim("currency", 1).err == "prerequisite_not_claimed")
  db[summary_id].value.achievement_stats.wins = 0
  assert(claim().err == "threshold_not_met")
  db[summary_id].value.achievement_stats.wins = 10
  assert(claim(nil, nil, {}).err == "unauthorized")
  epoch_ok = false
  assert(claim().err == "session_stale")
  assert(#writes == 0)
  epoch_ok = true
  assert(claim().ok and claim().err == "already_claimed" and #writes == 1)
end)
print("achievement claim security: " .. passed .. " tests passed")
