-- One displayed round covers the first combatant's turn and the response.
-- Only a real control transfer back to that first combatant advances it.
local M = {}
local MAX = 2147483647
local function valid_id(uid) return type(uid)=="string" and uid~="" end
function M.number(state)
  local value=tonumber(state.round_number)
  if not value or value~=value or value<1 then return 0 end
  return math.min(MAX,math.floor(value))
end
function M.start(state,first)
  assert(valid_id(first),"round_first_player_required")
  state.round_first_user_id=first;state.round_number=1;state.active_user_id=first
end
function M.restore(state,first)
  -- Older durable attempts did not save control-transfer counts. Their action
  -- counters include extra turns, so an exact historical round is unknowable.
  if not valid_id(state.round_first_user_id) or M.number(state)==0 then
    state.round_first_user_id=first;state.round_number=0
  end
end
function M.set_active(state,next_player)
  local previous=state.active_user_id
  if state.round_number==nil then M.start(state,previous or next_player) end
  local number=M.number(state)
  if number>0 and previous~=nil and previous~=next_player and next_player==state.round_first_user_id then
    state.round_number=math.min(MAX,number+1)
  end
  state.active_user_id=next_player
end
return M
