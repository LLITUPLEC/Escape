-- Test actual early callbacks against dispatch fields filled after definition.
-- Run from repository root: lua Server/nakama/tests/runtime_forward_security_test.lua
package.path = "Server/nakama/?.lua;" .. package.path
local nk = {
  json_encode = function(v) return v end,
  logger_error = function() end, logger_warn = function() end, logger_info = function() end,
  register_rpc = function() end,
  register_req_before = function() end, register_req_after = function() end,
}
package.preload.nakama = function() return nk end
local file = assert(io.open("Server/nakama/modules/duel_match3.lua", "r"))
local source = file:read("*a"); file:close()
local forward_names = {
  "Pvp", "EconomyRpc", "ContestGoals", "StatsApi", "award_pve_victory", "award_pve_defeat",
  "find_matches", "enumerate_valid_swaps", "arena_mirror_commit", "arena_on_match_finished",
  "arena_final_prize_gold_ore", "arena_final_prize_for_winner",
}
for _, name in ipairs(forward_names) do
  assert(not source:find("\nlocal " .. name .. "\r?\n"), "uninitialized forward local: " .. name)
  assert(source:find("Runtime." .. name .. " =", 1, true), "missing runtime dispatch assignment: " .. name)
end

-- This prefix defines the real safety shuffle and game-over callback before
-- enumeration and module factories have been initialized.
local prefix_end = assert(source:find("local function parse_action", 1, true))
local enum_start = assert(source:find("Runtime.enumerate_valid_swaps = function", 1, true))
local enum_end = assert(source:find("local function choose_bot_action", enum_start, true))
local code = source:sub(1, prefix_end - 1) .. source:sub(enum_start, enum_end - 1)
  .. "\nreturn Runtime, ensure_active_board_has_valid_swaps, init_board, broadcast_pvp_game_over, race_goal_mana"
local runtime, ensure_board, init_board, finish_match, goal = assert(loadstring(code))()
math.randomseed(73)
for _ = 1, 20 do
  local state = { board = init_board() }
  ensure_board(state)
  assert(#runtime.enumerate_valid_swaps(state.board) > 0)
end

local reward_calls, stats_calls, broadcasts = 0, 0, 0
runtime.Pvp = {
  apply_game_over_rewards = function() reward_calls = reward_calls + 1 end,
  apply_race_draw_rewards = function() reward_calls = reward_calls + 1 end,
}
runtime.StatsApi = { record_match_finish = function(state, winner, draw)
  assert(state.pvp_race and winner == "alice" and not draw)
  stats_calls = stats_calls + 1
end }
runtime.ContestGoals = { race_goal_mana = function() return 123 end }
assert(goal(nil) == 123)
finish_match({ broadcast_message = function(opcode, payload)
  assert(opcode == 11 and payload.winnerUserId == "alice")
  broadcasts = broadcasts + 1
end }, { pvp_race = true }, "alice", nil, nil, 0, false)
assert(reward_calls == 1 and stats_calls == 1 and broadcasts == 1)
print("runtime forward security: 12 initialized dispatch fields, 20 real boards, late reward/stats/goal callbacks passed")
