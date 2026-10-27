-- Actual match callbacks with deterministic ticks; no 300-second real wait.
package.path = "Server/nakama/?.lua;" .. package.path
local Presence = require("modules.duel_match3_presence")
local file = assert(io.open("Server/nakama/modules/duel_match3.lua", "r"))
local source = file:read("*a"); file:close()
local function presence(uid, sid) return {user_id = uid, session_id = sid, node = "node"} end
local alice, bob = presence("alice", "a1"), presence("bob", "b1")
local current_alice = presence("alice", "a2")
local finishes, snapshots, kicks = {}, {}, {}
local dispatcher = {
  broadcast_message = function(op, payload, targets) snapshots[#snapshots + 1] = {op=op, payload=payload, targets=targets} end,
  match_kick = function(rows) for _, row in ipairs(rows) do kicks[#kicks + 1] = row.session_id end end,
}
local env = setmetatable({
  CFG = { TICK_RATE=6, RECONNECT_GRACE_SECONDS=300, OP_PEER_DISCONNECT=18,
    OP_BOARD_SYNC=10, OP_PLAYER_LEFT=12, OP_ACTION_REQUEST=13, OP_SNAPSHOT_REQUEST=16, OP_SELECTION_SYNC=15 },
  Runtime = { Rounds=require("modules.duel_match3_rounds"), Presence=Presence, RateLimit={filter_match_messages=function(_, messages) return messages end} },
  nk = {json_encode=function(value) return value end, time=function() return 0 end, logger_error=function() end},
  count_present_players = function(state) local n=0; for _ in pairs(state.presences) do n=n+1 end; return n end,
  make_sync_msg = function(state) return {board=state.board, activeUserId=state.active_user_id} end,
  new_stats = function() return {hp=150, max_hp=150} end,
  other_player_id = function(state, uid)
    return state.players_sorted[1] == uid and state.players_sorted[2] or state.players_sorted[1]
  end,
  finalize_pvp_forfeit_win = function(_, state, winner, reason)
    finishes[#finishes+1] = {winner=winner, reason=reason}
  end,
  on_pve_owner_socket_gone = function() end,
  parse_action = function() error("obsolete socket reached action parser") end,
}, {__index=_G})
-- The focused session fixture keeps finalization at its existing boundary;
-- the integrated lifecycle suite below exercises the actual shared finalizer.
env.Runtime.forfeit_match=function(d,s,uid,reason)
  s.ended=true
  if not s.started then
    s.players_sorted={};for id in pairs(s.invited or {})do s.players_sorted[#s.players_sorted+1]=id end;table.sort(s.players_sorted)
  end
  env.finalize_pvp_forfeit_win(d,s,env.other_player_id(s,uid),reason)
end
local helpers_start = assert(source:find("Runtime.finish_arena_result = function", 1, true))
local helpers_end = assert(source:find("-- Board dimensions:", helpers_start, true))
local helpers = assert(loadstring(source:sub(helpers_start, helpers_end-1)))
setfenv(helpers, env); helpers()
local join_start = assert(source:find("local function match_join_attempt", 1, true))
local join_end = assert(source:find("local function augment_arena_game_over_payload", join_start, true))
local join_chunk = assert(loadstring(source:sub(join_start, join_end-1) .. "\nreturn match_join_attempt, match_join"))
setfenv(join_chunk, env)
local attempt, join = join_chunk()
local leave_start = assert(source:find("local function match_leave", join_end, true))
local loop_end = assert(source:find("local function match_terminate", leave_start, true))
local loop_chunk = assert(loadstring(source:sub(leave_start, loop_end-1) .. "\nreturn match_leave, match_loop"))
setfenv(loop_chunk, env)
local leave, loop = loop_chunk()
local function state()
  return {mode="pvp", started=true, ended=false, invited={alice=true,bob=true},
    players_sorted={"alice","bob"}, presences={alice=alice,bob=bob}, stats={}, board={1,2,3},
    reconnect_deadlines={}, turn_deadline_paused=false, turn_deadline_tick=100000,
    active_user_id="alice", join_deadline_tick=1800}
end
local cases = 0
local function check(condition, message) assert(condition, message); cases=cases+1 end

local s = state()
check(leave({}, dispatcher, 1, s, {alice}) == s, "single disconnect destroys match")
check(leave({}, dispatcher, 2, s, {bob}) == s and not s.ended, "double disconnect destroys match")
check(s.reconnect_deadlines.alice == 1801 and s.reconnect_deadlines.bob == 1802, "grace is not per participant")
join({}, dispatcher, 10, s, {bob})
check(s.reconnect_deadlines.bob == nil and s.reconnect_deadlines.alice == 1801, "one reconnect clears other grace")
join({}, dispatcher, 20, s, {current_alice})
check(next(s.reconnect_deadlines) == nil and s.board[1] == 1 and #finishes == 0, "rejoin mutated fight/results")
check(snapshots[#snapshots].targets[1].session_id == "a2", "replacement did not receive full snapshot")

s = state()
join({}, dispatcher, 1, s, {current_alice})
check(kicks[#kicks] == "a1" and s.presences.alice == current_alice, "overlap replacement not published before kick")
leave({}, dispatcher, 2, s, {alice})
check(s.presences.alice == current_alice and s.reconnect_deadlines.alice == nil, "old leave erased replacement")
loop({}, dispatcher, 3, s, {{sender=alice,op_code=12}, {sender=alice,op_code=13,data="obsolete"}})
check(not s.ended and #finishes == 0, "obsolete socket could act or forfeit")
check(loop({}, dispatcher, 4, s, {{sender=current_alice,op_code=12}}) == nil and #finishes == 1,
  "current socket cannot finish match")
loop({}, dispatcher, 5, s, {{sender=current_alice,op_code=12}})
check(#finishes == 1, "repeated completion writes another result")

s = state(); finishes = {}
leave({}, dispatcher, 1, s, {alice}); leave({}, dispatcher, 2, s, {bob})
local _, before = attempt({}, dispatcher, 1800, s, current_alice, {})
local _, at_deadline = attempt({}, dispatcher, 1801, s, current_alice, {})
check(before and not at_deadline, "expired participant can rejoin or valid grace rejects")
check(loop({}, dispatcher, 1800, s, {}) == s and #finishes == 0, "grace ends before its deadline")
check(loop({}, dispatcher, 1801, s, {}) == nil and #finishes == 1 and finishes[1].winner == "bob",
  "expired earliest disconnect does not resolve exactly once")
loop({}, dispatcher, 1802, s, {})
check(#finishes == 1, "second expiry repeats payout")

s = state(); s.presences={}; s.reconnect_deadlines={}
local _, outsider = attempt({}, dispatcher, 1, s, presence("outsider", "x"), {})
check(not outsider, "empty sockets let an outsider occupy a started match")
check(not Presence.is_current(s, {user_id="alice"}), "presence without server session is trusted")

s = state(); s.started=false; s.presences={bob=bob}; s.arena_mirror={}; finishes={}
check(loop({}, dispatcher, 1799, s, {}) == s and #finishes == 0, "arena no-show resolves prematurely")
check(loop({}, dispatcher, 1800, s, {}) == nil and finishes[1].winner == "bob", "arena no-show stalls next round")

s = state(); s.mode="pve"; s.owner_user_id="alice"; s.bot_user_id="zz-bot-test"
s.players_sorted={"alice","zz-bot-test"}; s.presences={alice=alice}
leave({}, dispatcher, 7, s, {alice})
join({}, dispatcher, 8, s, {current_alice})
check(s.presences.alice == current_alice and next(s.reconnect_deadlines) == nil, "PvE owner reconnect failed")

-- A temporary final payout error must retain the ended match until committed.
s = state(); s.ended=true; s.arena_mirror={}
local unavailable, commits = true, 0
env.Runtime.arena_on_match_finished = function()
  if unavailable then error("temporary database outage") end
  commits=commits+1
  return { reward_receipt={user_id="bob", gold=3000, ore=0} }
end
local delivered = #snapshots
check(not env.Runtime.finish_arena_result(s, "bob"), "arena storage error is swallowed as success")
env.Runtime.emit_game_over(dispatcher, s, {winnerUserId="bob"})
check(#snapshots == delivered and loop({}, dispatcher, 10, s, {}) == s, "failed final discarded or promised rewards")
unavailable=false
check(loop({}, dispatcher, 11, s, {}) == s and commits == 0, "retry backoff ignored")
check(loop({}, dispatcher, 16, s, {}) == nil and commits == 1, "recovered final not committed")
check(#snapshots == delivered+1 and snapshots[#snapshots].payload.rewardGold == 3000,
  "recovered final did not deliver its committed prize")
loop({}, dispatcher, 17, s, {})
check(commits == 1, "ended match retried an already committed prize")

s = state(); s.ended=true; s.pvp_race=true; unavailable=true; commits=0
env.Runtime.Pvp = { apply_game_over_rewards=function(_, _, payload)
  if unavailable then return false end
  commits=commits+1; payload.rewardMatter=10; return true
end }
local payload={winnerUserId="bob"}
env.Runtime.apply_result_rewards(s, "bob", false, payload)
env.Runtime.emit_game_over(dispatcher, s, payload)
check(loop({}, dispatcher, 20, s, {}) == s and commits == 0, "Race settlement failure discarded bound receipt")
unavailable=false
check(loop({}, dispatcher, 26, s, {}) == nil and commits == 1 and snapshots[#snapshots].payload.rewardMatter == 10,
  "recovered Race reward missing or duplicated")

-- Defensive HP-finish path must use the same paid Race settlement/retry gate.
local finish_start = assert(source:find("local function finish_turn_and_broadcast", 1, true))
local finish_end = assert(source:find("local function count_present_players", finish_start, true))
local finish_chunk = assert(loadstring(source:sub(finish_start, finish_end-1) .. "\nreturn finish_turn_and_broadcast"))
setfenv(finish_chunk, env)
local finish_turn = finish_chunk()
env.Ach = {snapshot_hp_was_exactly_one=function() end, flush_match_finish=function() end}
env.try_finish_race_after_action = function() return false end
env.broadcast_sync = function() end
env.Runtime.StatsApi = {record_match_finish=function() end}
s=state(); s.pvp_race=true; s.stats={alice={hp=0}, bob={hp=150}}; unavailable=true
finish_turn(dispatcher, s, nil, false, true, 30, 6, nil)
check(s.ended and s._race_pending_result ~= nil and s._pending_game_over ~= nil,
  "HP finish bypassed deferred paid Race settlement")

local forfeit_start = assert(source:find("local function finalize_pvp_forfeit_win", 1, true))
local forfeit_end = assert(source:find("local function match_leave", forfeit_start, true))
local forfeit_chunk = assert(loadstring(source:sub(forfeit_start, forfeit_end-1) .. "\nreturn finalize_pvp_forfeit_win"))
setfenv(forfeit_chunk, env)
local forfeit = forfeit_chunk()
env.is_pvp_race = function(value) return value.pvp_race == true end
env.augment_arena_game_over_payload = function() end
env.Ach.flush_match_finish = function() error("achievement storage temporarily unavailable") end
unavailable=false; s=state(); s.ended=true
check(pcall(forfeit, dispatcher, s, "bob", "test_forfeit"), "achievement error turns logging into an unhandled forfeit error")
print("reconnect lifecycle: " .. cases .. " real callback/session/deadline cases passed")
