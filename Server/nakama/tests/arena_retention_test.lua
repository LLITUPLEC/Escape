package.path = "Server/nakama/?.lua;" .. package.path
local SYSTEM = "00000000-0000-0000-0000-000000000000"
local NOW, COLLECTION = 50 * 86400, "arena_tournament"
local db, reads

local function reset()
  db, reads = {}, 0
  db.state = { key = "state", user_id = SYSTEM, collection = "arena_queue", value = { users = {} } }
end

local function done(key, at, kind, tier)
  local row = { collection = COLLECTION, user_id = SYSTEM, key = key, version = "revision-" .. key,
    permission_read = 0, permission_write = 0, update_time = at,
    value = { id = key, phase = "done", kind = kind or "gold", bet_tier = tier or "600", done_at = at,
      parts = { winner = { bot = false }, loser = { bot = false } },
      final = { { status = "done", winner_uid = "winner", uid_a = "winner", uid_b = "loser" } },
      reward_receipt = { user_id = "winner", granted_at = at } } }
  db[key] = row
  return row
end

package.preload.nakama = function()
  return {
    storage_read = function(ids)
      reads = reads + 1
      assert(#ids <= 15, "bounded witnesses plus queue registry")
      local out = {}
      for _, id in ipairs(ids) do
        assert(id.user_id == SYSTEM)
        local row = db[id.key]
        if row and row.collection == id.collection then out[#out + 1] = row end
      end
      return out
    end,
    json_decode = function() error("invalid JSON") end,
  }
end

local retention = require("modules.duel_arena_retention")
local function deleted(result, keys)
  local expected = {}
  for _, key in ipairs(keys) do expected[key] = true end
  for _, row in ipairs(result) do
    assert(expected[row.key], "unexpected deletion " .. row.key)
    assert(row.collection == COLLECTION and row.user_id == SYSTEM and row.version == db[row.key].version)
    expected[row.key] = nil
  end
  assert(next(expected) == nil, "missing expected deletion " .. tostring(next(expected)))
end

reset()
local state = {}
local first = done("oldest", NOW - 5000)
local second = done("second", NOW - 4000)
local third = done("third", NOW - 3000)
local newest = done("newest", NOW - 2000)
deleted(retention.deletions(state, { first, second }, NOW), {})
deleted(retention.deletions(state, { third, newest }, NOW), {})
assert(state.arena_latest["gold:600"][1] == "newest" and state.arena_latest["gold:600"][2] == "third")
deleted(retention.deletions(state, { first, second }, NOW), { "oldest", "second" })
assert(reads == 3)
print("PASS paged cleanup waits for two verified witnesses and never deletes outside the current page")

reset()
state = {}
local a = done("a", NOW - 3600, "ore", "fixed")
local b = done("b", NOW - 3600, "ore", "500")
local c = done("c", NOW - 3600, "ore", "500")
local high_stake = done("high-stake", NOW - 7200, "ore", "2000")
local other_kind = done("other-kind", NOW - 7200)
deleted(retention.deletions(state, { b, c, a, high_stake, other_kind }, NOW), { "a" })
assert(state.arena_latest["ore:500"][1] == "c" and state.arena_latest["ore:500"][2] == "b")
assert(#state.arena_latest["ore:2000"] == 1 and #state.arena_latest["gold:600"] == 1)
print("PASS latest pair is separate by kind and stake, with fixed alias and stable same-second ordering")

reset()
state = {}
local old = done("old", NOW - 40 * 86400)
local later = done("later", NOW - 39 * 86400)
local most_recent = done("most-recent", NOW - 38 * 86400)
deleted(retention.deletions(state, { later, most_recent }, NOW), {})
db["most-recent"] = nil
deleted(retention.deletions(state, { old }, NOW), {})
assert(state.arena_latest["gold:600"][1] == "later" and state.arena_latest["gold:600"][2] == "old")
local replacement = done("replacement", NOW - 10)
deleted(retention.deletions(state, { old, replacement }, NOW), { "old" })
print("PASS removed witnesses cannot authorize deletion and the most recent two survive past thirty days")

reset()
state = {}
local at_cutoff = done("at-cutoff", NOW - 300)
local too_recent = done("too-recent", NOW - 299)
local edited = done("edited", NOW - 400)
edited.update_time = NOW - 299
local reward_late = done("reward-late", NOW - 500)
reward_late.value.reward_receipt.granted_at = NOW - 299
local top1, top2 = done("top1", NOW - 25), done("top2", NOW - 5)
deleted(retention.deletions(state, { at_cutoff, too_recent, edited, reward_late, top1, top2 }, NOW), { "at-cutoff" })
print("PASS bracket, payment and version all receive a five-minute grace window")

reset()
state = {}
local active = done("active", NOW - 1000)
active.value.phase = "final"
local unpaid = done("unpaid", NOW - 990)
unpaid.value.reward_receipt = nil
local wrong_winner = done("wrong-winner", NOW - 980)
wrong_winner.value.reward_receipt.user_id = "loser"
local uncounted = done("uncounted", NOW - 970)
uncounted.value.side_bets = { winner = { qf = { ["1"] = "loser" } } }
local referenced = done("referenced", NOW - 960)
db.state.value.users.winner = { tid = referenced.key }
local invalid_meta = done("public", NOW - 950)
invalid_meta.permission_read = 1
local future = done("future", NOW - 940)
future.value.done_at = NOW + 1
local legacy = done("legacy", NOW - 930)
legacy.value.bet_tier = nil
local unverified_abort = done("abort-with-human", NOW - 920)
unverified_abort.value.aborted, unverified_abort.value.reward_receipt = true, nil
local proof = done("proof", NOW - 850)
local proof2 = done("proof2", NOW - 810)
local eligible = done("eligible", NOW - 1050)
deleted(retention.deletions(state, { active, unpaid, wrong_winner, uncounted, referenced, invalid_meta, future,
  legacy, unverified_abort, proof, proof2, eligible }, NOW), { "eligible" })
db.state.value.users = false
deleted(retention.deletions(state, { eligible, proof, proof2 }, NOW), {})
print("PASS no active, unpaid, unresolved, referenced, malformed or unsupported bracket is removed")

reset()
state = {}
local smith = done("smith", NOW - 1500, "smith", "green")
smith.value.rules_version = 2
smith.value.craft_receipts = { winner = { user_id = "winner", outcome = "champion" } }
local smith_old = done("smith-old", NOW - 1700, "smith", "green")
local bot_final = done("bot-final", NOW - 1600, "smith", "green")
bot_final.value.parts.winner.bot = true
bot_final.value.reward_receipt = nil
local abort = done("aborted", NOW - 1400, "smith", "green")
abort.value.aborted, abort.value.reward_receipt = true, nil
abort.value.eliminated = { winner = true, loser = true }
abort.value.side_bets = { loser = { sf = { ["1"] = "winner" } } }
abort.value.bets_resolved = { sf = true }
deleted(retention.deletions(state, { smith, smith_old, bot_final, abort }, NOW), { "smith-old" })
assert(state.arena_latest["smith:green"][2] == "bot-final", "pending participation receipt cannot count as witness")
smith.value.craft_receipts.loser = { user_id = "loser", outcome = "participation" }
deleted(retention.deletions(state, { smith, bot_final, abort }, NOW), { "bot-final" })
assert(state.arena_latest["smith:green"][1] == "aborted" and state.arena_latest["smith:green"][2] == "smith")
print("PASS bot finals, proven aborts and Smith v2 paid craft receipts are terminal witnesses")

reset()
state = {}
local cohorts = {
  { "smith", "green" }, { "smith", "blue" }, { "smith", "purple" },
  { "ore", "500" }, { "ore", "2000" }, { "gold", "600" }, { "gold", "2400" },
}
local keys = {}
for i = 1, 1000 do
  local cohort = cohorts[(i - 1) % #cohorts + 1]
  local key = string.format("bracket-%04d", i)
  keys[i] = key
  done(key, NOW - 2000 + i, cohort[1], cohort[2])
end
for _ = 1, 2 do
  for first_index = 1, #keys, 100 do
    local page = {}
    for i = first_index, math.min(first_index + 99, #keys) do
      if db[keys[i]] then page[#page + 1] = db[keys[i]] end
    end
    for _, candidate in ipairs(retention.deletions(state, page, NOW)) do
      assert(db[candidate.key].version == candidate.version)
      db[candidate.key] = nil
    end
  end
end
local kept = 0
for _, pair in pairs(state.arena_latest) do
  assert(#pair == 2 and db[pair[1]] and db[pair[2]])
  kept = kept + #pair
end
assert(kept == 14)
for _, key in ipairs(keys) do if db[key] then kept = kept - 1 end end
assert(kept == 0, "only the latest two terminal brackets per cohort remain")
print("PASS two bounded scans reduce 1000 completed brackets to fourteen retained rows")
