-- Run from the repository root: lua Server/nakama/tests/stats_security_test.lua
package.path = "Server/nakama/?.lua;" .. package.path

local function copy(v)
  if type(v) ~= "table" then return v end
  local out = {}
  for k, x in pairs(v) do out[k] = copy(x) end
  return out
end

local db, writes, before_write, fail_write
local registrations, deleted = {}, 0
local nk = {
  json_encode = function(v) return v end,
  json_decode = function() error("client result payload must never be decoded") end,
  logger_error = function() end,
  register_rpc = function(fn, name) registrations[name] = fn end,
  sql_query = function() deleted = deleted + 1; error("administrative SQL must never run") end,
  leaderboard_delete = function() deleted = deleted + 1; error("leaderboards must never be deleted") end,
  storage_write = function(objects)
    writes[#writes + 1] = copy(objects)
    if before_write then
      local fn = before_write
      before_write = nil
      fn()
    end
    if fail_write then error(fail_write) end
    for _, obj in ipairs(objects) do
      local row = db[obj.user_id]
      if obj.version == "*" then
        if row ~= nil then error("version check failed") end
      elseif row == nil or obj.version ~= row.version then
        error("version check failed")
      end
    end
    for _, obj in ipairs(objects) do
      local row = db[obj.user_id]
      db[obj.user_id] = { value = copy(obj.value), version = tostring((row and tonumber(row.version) or 0) + 1) }
      assert(obj.permission_read == 1 and obj.permission_write == 0)
    end
  end,
}
package.preload.nakama = function() return nk end

local api = require("modules.duel_match3_stats_api")({
  CFG = { STATS_COLLECTION = "duel_match3_stats", STATS_KEY = "summary" },
  Ach = {
    storage_read_match3_summary_val = function(uid)
      local row = db[uid]
      return row and copy(row.value) or {}, row and row.version or nil
    end,
    is_human = function(uid) return type(uid) == "string" and uid ~= "" and string.sub(uid, 1, 7) ~= "bot_pve" end,
  },
  read_pve_progress = function() return {} end,
  normalize_mine_difficulty = function(v) return v end,
})

local function reset()
  db, writes, before_write, fail_write = {}, {}, nil, nil
end

local function state(overrides)
  local s = {
    started = true, ended = true, stats_result_id = "server-match-1",
    players_sorted = { "alice", "bob" }, mode = "pvp", presences = { alice = {} },
  }
  for k, v in pairs(overrides or {}) do s[k] = v end
  return s
end

local passed = 0
local function test(name, fn)
  reset()
  fn()
  passed = passed + 1
  print("PASS " .. name)
end

test("retired purge module registers nothing and exposes no deletion path", function()
  local purge = require("modules.duel_leaderboard_admin_purge")
  assert(type(purge) == "table" and next(purge) == nil)
  assert(next(registrations) == nil and deleted == 0)
end)

test("legacy result RPC rejects forged results without touching storage", function()
  for _, payload in ipairs({ '{"won":true,"mode":"duel"}', '{"won":true,"mode":"arena","session_epoch":1}', "invalid json" }) do
    local result = api.duel_match3_stats_record({ user_id = "alice" }, payload)
    assert(not result.ok and result.err == "client_result_recording_disabled")
  end
  assert(api.duel_match3_stats_record(nil, "{}").err == "unauthorized")
  assert(api.duel_match3_stats_record({}, "{}").err == "unauthorized")
  assert(#writes == 0 and next(db) == nil)
end)

test("finished online result counts both participants including disconnected loser", function()
  db.alice = { version = "7", value = {
    played = 10, wins = 7, losses = 3, achievement_stats = { chain = 99 },
    achievement_claimed = { "chain:0" }, modes = { mine = { played = 3, wins = 2, losses = 1 } },
  } }
  assert(api.record_match_finish(state(), "alice", false))
  local a, b = db.alice.value, db.bob.value
  assert(a.played == 11 and a.wins == 8 and a.losses == 3)
  assert(a.achievement_stats.chain == 99 and a.achievement_claimed[1] == "chain:0")
  assert(a.modes.mine.played == 3 and a.modes.duel.wins == 1)
  assert(b.played == 1 and b.wins == 0 and b.losses == 1 and b.modes.duel.losses == 1)
  assert(writes[1][1].version == "7" and writes[2][1].version == "*")
end)

test("repeated finish calls and reconstructed state cannot double count", function()
  local s = state()
  assert(api.record_match_finish(s, "alice", false))
  assert(api.record_match_finish(s, "alice", false))
  assert(api.record_match_finish(state(), "alice", false))
  assert(#writes == 2 and db.alice.value.wins == 1 and db.bob.value.losses == 1)
end)

test("unfinished invalid and draw matches cannot write stats", function()
  for _, s in ipairs({ state({ started = false }), state({ ended = false }),
      state({ players_sorted = { "alice" } }), state({ players_sorted = { "alice", "alice" } }),
      state({ stats_result_id = "" }) }) do
    assert(not api.record_match_finish(s, "alice", false))
  end
  assert(not api.record_match_finish(state(), "outsider", false))
  assert(not api.record_match_finish(state(), "", true))
  assert(not api.record_match_finish(state(), "alice", true))
  assert(#writes == 0)
end)

test("mine race arena and pro modes come only from match state and bots are excluded", function()
  for _, cfg in ipairs({
    { fields = { mode = "pve" }, expected = "mine" },
    { fields = { mode = "pve", pvp_race = true }, expected = "race" },
    { fields = { mode = "pve", arena_mirror = {} }, expected = "arena" },
    { fields = { pvp_pro = true }, expected = "pro" },
  }) do
    reset()
    cfg.fields.players_sorted = { "alice", "bot_pve_enemy" }
    assert(api.record_match_finish(state(cfg.fields), "bot_pve_enemy", false))
    assert(#writes == 1 and db.bot_pve_enemy == nil)
    assert(db.alice.value.losses == 1 and db.alice.value.modes[cfg.expected].losses == 1)
  end
end)

test("version conflicts reread counters and preserve concurrent achievement updates", function()
  db.alice = { version = "1", value = { played = 4, wins = 2, losses = 2 } }
  before_write = function()
    db.alice = { version = "2", value = { played = 5, wins = 3, losses = 2, achievement_claimed = { "new:0" } } }
  end
  assert(api.record_match_finish(state(), "alice", false))
  assert(#writes == 3 and db.alice.value.played == 6 and db.alice.value.wins == 4)
  assert(db.alice.value.achievement_claimed[1] == "new:0")
end)

test("create-only prevents overwriting a concurrently created summary", function()
  before_write = function()
    db.alice = { version = "1", value = { played = 12, wins = 9, losses = 3 } }
  end
  assert(api.record_match_finish(state(), "alice", false))
  assert(writes[1][1].version == "*" and db.alice.value.played == 13 and db.alice.value.wins == 10)
end)

test("retry exhaustion leaves stats unchanged and permits later internal retry", function()
  fail_write = "version check failed"
  local s = state()
  assert(not api.record_match_finish(s, "alice", false))
  assert(next(db) == nil and #writes == 10)
  fail_write = nil
  assert(api.record_match_finish(s, "alice", false))
  assert(db.alice.value.played == 1 and db.bob.value.played == 1)
end)

test("successful participants are not counted twice when another write fails", function()
  before_write = function()
    before_write = function() fail_write = "temporary storage failure" end
  end
  local s = state()
  assert(not api.record_match_finish(s, "alice", false))
  assert(db.alice.value.wins == 1 and db.bob == nil)
  fail_write = nil
  assert(api.record_match_finish(s, "alice", false))
  assert(db.alice.value.wins == 1 and db.bob.value.losses == 1)
end)

test("deduplication history stays bounded", function()
  for i = 1, 80 do
    assert(api.record_match_finish(state({ stats_result_id = "match-" .. tostring(i) }), "alice", false))
  end
  assert(db.alice.value.played == 80 and #db.alice.value.match_result_ids == 4)
  assert(db.alice.value.match_result_ids[1] == "match-77")
end)

test("daily plans use trusted completion day counters and accepted moves", function()
  local s=state({_ach_counters={alice={["uses.cross"]=2,["uses.shield"]=3},bob={["uses.petard"]=1}},_daily_player_actions={bob=2}})
  local a=api.build_match_finish_effects(s,"alice",false)
  assert(a.alice.match_stats.ability_uses==5 and a.bob.match_stats.ability_uses==1)
  assert(a.alice.match_stats.daily_pvp_eligible and not a.bob.match_stats.daily_pvp_eligible)
  local day=a.alice.match_stats.daily_day;s._daily_player_actions.bob=3
  local b=api.build_match_finish_effects(s,"alice",false)
  assert(b.bob.match_stats.daily_pvp_eligible and b.alice.match_stats.daily_day==day)
end)
test("daily plans exclude unstarted aborted invalid and drawn matches", function()
  for _,s in ipairs({state({started=false}),state({ended=false}),state({stats_result_id=""}),state({players_sorted={"alice","alice"}})}) do
    assert(next(api.build_match_finish_effects(s,"alice",false))==nil)
  end
  assert(next(api.build_match_finish_effects(state(),"alice",true))==nil)
end)
print("stats security: " .. passed .. " tests passed")
