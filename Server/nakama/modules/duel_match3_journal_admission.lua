-- Called only for a newly created authoritative match, never by result retry.
local M={}
function M.admit(state,d)
  if state.global_boss then return true end
  if state._journal_tickets then return true end
  local users,seen={},{}
  local function add(uid) if d.is_human(uid) and not seen[uid] then users[#users+1]=uid;seen[uid]=true end end
  add(state.owner_user_id);for uid in pairs(state.invited or {}) do add(uid) end
  for _,uid in ipairs(state.players_sorted or {}) do add(uid) end
  local specs={}
  local function entry(uid,id,kind) specs[#specs+1]={uid=uid,id=id,kind=kind,origin={match_id=state.match_id,root_result=state.stats_result_id}} end
  for _,uid in ipairs(users) do
    entry(uid,state.stats_result_id,"effects");entry(uid,state.stats_result_id..":stats","effects")
    if state.mode=="pve" and not state.pvp_race and not state.arena_mirror and not (state.pve_run and state.pve_run.arena_suppress_all) then
      entry(uid,state.stats_result_id..":pve","pve")
    end
    if d.consumable_eligible(state) then entry(uid,"consumable-bind:"..state.match_id,"consumable_bind") end
  end
  if #specs==0 then state._journal_tickets={};return true end
  local ok,err,tickets=d.journal.issue_many(specs);if not ok then return false,err end
  state._journal_tickets={}
  for i,spec in ipairs(specs) do
    state._journal_tickets[spec.uid]=state._journal_tickets[spec.uid] or {}
    state._journal_tickets[spec.uid][spec.id]=tickets[i]
  end
  if state.mode=="pve" and not state.pvp_race and not state.arena_mirror and state.pve_run and state.pve_run.launch_token then
    local attached,why=d.pve_entry.attach_journal_tickets(state.owner_user_id,state.pve_run.launch_token,state.stats_result_id,state._journal_tickets[state.owner_user_id])
    if not attached then return false,why end
  end
  return true
end
function M.cancel_unused(state,journal)
  if state.global_boss or not state._journal_tickets then return true end
  local specs={}
  for uid,entries in pairs(state._journal_tickets) do for id,ticket in pairs(entries) do
    local kind=id:sub(1,16)=="consumable-bind:" and "consumable_bind" or id:sub(-4)==":pve" and "pve" or "effects"
    specs[#specs+1]={uid=uid,id=id,kind=kind,ticket=ticket}
  end end
  return journal.cancel_unused_many(specs)
end
return M
