local function count_map(t)
  local n = 0
  for _, _ in pairs(t) do n = n + 1 end
  return n
end

local function match_init(context, params)
  local invited = {}
  if params and params.invited then
    for _, u in ipairs(params.invited) do
      local p = u.presence or u
      if p and p.user_id then invited[p.user_id] = true end
    end
  end

  local state = {
    invited = invited,
    presences = {},
  }
  return state, 10, "mode=duel_relay"
end

local function match_join_attempt(context, dispatcher, tick, state, presence, metadata)
  if count_map(state.presences) >= 2 and state.presences[presence.user_id] == nil then
    return state, false, "full"
  end

  if next(state.invited) ~= nil and not state.invited[presence.user_id] and state.presences[presence.user_id] == nil then
    return state, false, "not_invited"
  end

  return state, true
end

local function match_join(context, dispatcher, tick, state, presences)
  for _, p in ipairs(presences) do
    state.presences[p.user_id] = p
  end
  return state
end

local function match_leave(context, dispatcher, tick, state, presences)
  for _, p in ipairs(presences) do
    state.presences[p.user_id] = nil
  end
  if count_map(state.presences) == 0 then return nil end
  return state
end

local function match_loop(context, dispatcher, tick, state, messages)
  for _, m in ipairs(messages) do
    dispatcher.broadcast_message(m.op_code, m.data, nil, m.sender)
  end
  return state
end

local function match_terminate(context, dispatcher, tick, state, grace_seconds)
  return state
end

local function match_signal(context, dispatcher, tick, state, data)
  return state, "ok"
end

return {
  match_init = match_init,
  match_join_attempt = match_join_attempt,
  match_join = match_join,
  match_leave = match_leave,
  match_loop = match_loop,
  match_terminate = match_terminate,
  match_signal = match_signal,
}
