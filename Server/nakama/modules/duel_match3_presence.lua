-- One authoritative socket per user, with independent reconnect deadlines.
local M = {}

function M.is_current(state, presence)
  if not presence or not presence.user_id or not presence.session_id then return false end
  local current = state.presences and state.presences[presence.user_id]
  return current ~= nil and current.session_id == presence.session_id
    and (current.node == nil or presence.node == nil or current.node == presence.node)
end

function M.can_rejoin(state, presence, tick)
  if not presence or not presence.user_id or not presence.session_id then return false, "invalid_presence" end
  if state.started then
    local participant = false
    for _, uid in ipairs(state.players_sorted or {}) do
      if uid == presence.user_id then participant = true; break end
    end
    if not participant then return false, "not_participant" end
  end
  local deadline = (state.reconnect_deadlines or {})[presence.user_id]
  if deadline ~= nil and tick >= deadline and not (state.presences or {})[presence.user_id] then
    return false, "reconnect_timeout"
  end
  return true
end

function M.join(state, presences, dispatcher)
  state.presences = state.presences or {}
  state.reconnect_deadlines = state.reconnect_deadlines or {}
  for _, presence in ipairs(presences) do
    local old = state.presences[presence.user_id]
    state.presences[presence.user_id] = presence
    state.reconnect_deadlines[presence.user_id] = nil
    if old and old.session_id ~= presence.session_id and dispatcher and dispatcher.match_kick then
      -- Publish replacement first: the resulting old leave must be a no-op.
      dispatcher.match_kick({old})
    end
  end
end

function M.leave(state, presences, tick, grace_ticks)
  state.reconnect_deadlines = state.reconnect_deadlines or {}
  local left = {}
  for _, presence in ipairs(presences) do
    if M.is_current(state, presence) then
      state.presences[presence.user_id] = nil
      left[#left + 1] = presence.user_id
      if state.started and not state.ended then
        -- A delayed duplicate leave cannot extend the original grace period.
        state.reconnect_deadlines[presence.user_id] = state.reconnect_deadlines[presence.user_id]
          or (tick + grace_ticks)
      end
    end
  end
  return left
end

function M.expired(state, tick)
  local expired, absent = {}, 0
  for uid, deadline in pairs(state.reconnect_deadlines or {}) do
    if not (state.presences or {})[uid] then
      absent = absent + 1
      if tick >= deadline then expired[#expired + 1] = uid end
    end
  end
  table.sort(expired, function(a, b)
    local da, db = state.reconnect_deadlines[a], state.reconnect_deadlines[b]
    return da < db or (da == db and a < b)
  end)
  return expired, absent
end

function M.filter_messages(state, messages)
  local current = {}
  for _, message in ipairs(messages or {}) do
    if M.is_current(state, message.sender) then current[#current + 1] = message end
  end
  return current
end

return M
