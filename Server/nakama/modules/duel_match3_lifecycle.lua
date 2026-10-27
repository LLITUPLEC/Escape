-- Bounded admission/reconnect policy and acknowledged, authenticated surrender.
-- A fixed four-result mailbox per account makes lost HTTP acknowledgements safe
-- after the authoritative match itself has already terminated.
local nk = require("nakama")
local M = { COLLECTION = "duel_match3_terminal", KEY = "v1", LIMIT = 4 }
local function human(uid) return type(uid)=="string" and uid~="" and uid:sub(1,7)~="zz-bot-" end
function M.participant(state,uid)
  if not human(uid) then return false end
  for _,id in ipairs(state.players_sorted or {}) do if id==uid then return true end end
  if state.started then return false end
  return state.owner_user_id==uid or (state.invited or {})[uid]==true
end
local function read(uid)
  local rows=nk.storage_read({{collection=M.COLLECTION,key=M.KEY,user_id=uid}})
  assert(type(rows)=="table","terminal_storage_unavailable")
  local row=rows[1]
  if row and type(row.value)=="string" then row.value=nk.json_decode(row.value) end
  assert(not row or type(row.value)=="table" and row.value.schema==1 and type(row.value.receipts)=="table","invalid_terminal_mailbox")
  return row
end
local function response(receipt)
  return {ok=true,accepted=true,terminal=true,winner_user_id=receipt.winner_user_id or "",
    match_cancelled=receipt.match_cancelled==true,end_reason=receipt.end_reason or "finished"}
end
function M.lookup(uid,match_id)
  local row=read(uid)
  for _,r in ipairs(row and row.value.receipts or {}) do if r.match_id==match_id then return response(r) end end
end
function M.publish(state,payload)
  local mid=state.match_id
  if type(mid)~="string" or mid=="" then return true end
  local users,seen={},{}
  local function add(uid) if human(uid) and not seen[uid] then users[#users+1]=uid;seen[uid]=true end end
  for _,uid in ipairs(state.players_sorted or {}) do add(uid) end
  if #users==0 then add(state.owner_user_id);for uid in pairs(state.invited or {}) do add(uid) end end
  local receipt={match_id=mid,result_id=state.stats_result_id,winner_user_id=payload.winnerUserId or "",
    match_cancelled=payload.matchCancelled==true,end_reason=payload.endReason or "finished",finished_at=os.time()}
  for _=1,5 do
    local writes={}
    for _,uid in ipairs(users) do
      local row=read(uid);local old=row and row.value.receipts or {};local found=false
      for _,r in ipairs(old) do if r.match_id==mid then
        assert(r.winner_user_id==receipt.winner_user_id and r.match_cancelled==receipt.match_cancelled,"terminal_result_conflict")
        found=true;break
      end end
      if not found then
        local entries={receipt};for _,r in ipairs(old) do if #entries<M.LIMIT then entries[#entries+1]=r end end
        writes[#writes+1]={collection=M.COLLECTION,key=M.KEY,user_id=uid,value={schema=1,receipts=entries},
          version=row and row.version or "*",permission_read=0,permission_write=0}
      end
    end
    if #writes==0 or pcall(nk.storage_write,writes) then return true end
  end
  return false
end
function M.new(deps)
  local L={}
  function L.rpc(ctx,payload)
    local uid=ctx and ctx.user_id
    if not human(uid) then return nk.json_encode({ok=false,err="unauthorized"}) end
    local p=payload
    if type(p)=="string" and #p<=512 then local ok,v=pcall(nk.json_decode,p);p=ok and v or nil end
    if type(p)~="table" or type(p.match_id)~="string" or #p.match_id<1 or #p.match_id>160 then return nk.json_encode({ok=false,err="bad_payload"}) end
    for k in pairs(p) do if k~="match_id" and k~="session_epoch" then return nk.json_encode({ok=false,err="bad_payload"}) end end
    local allowed,err=deps.guard(uid,payload)
    if not allowed then return nk.json_encode({ok=false,err=err}) end
    local ok,result=pcall(function()
      local previous=M.lookup(uid,p.match_id);if previous then return previous end
      local signaled,answer=pcall(nk.match_signal,p.match_id,nk.json_encode({kind="duel_forfeit",user_id=uid}))
      if signaled and type(answer)=="string" then local decoded,v=pcall(nk.json_decode,answer);if decoded and type(v)=="table" then return v end end
      previous=M.lookup(uid,p.match_id);if previous then return previous end
      return {ok=false,accepted=false,terminal=false,err="match_unavailable"}
    end)
    if not ok then nk.logger_error("forfeit acknowledgment deferred: "..tostring(result));result={ok=false,err="result_pending",terminal=false} end
    return nk.json_encode(result)
  end
  function L.signal(dispatcher,state,data)
    local ok,p=pcall(nk.json_decode,data)
    if not ok or type(p)~="table" or p.kind~="duel_forfeit" then return state,nk.json_encode({ok=false,err="bad_signal"}) end
    if not M.participant(state,p.user_id) then return state,nk.json_encode({ok=false,err="not_participant"}) end
    if not state.ended then deps.forfeit(dispatcher,state,p.user_id,"player_surrender") end
    if state._terminal_delivered then return state,nk.json_encode(assert(M.lookup(p.user_id,state.match_id))) end
    return state,nk.json_encode({ok=false,accepted=true,terminal=false,err="result_pending"})
  end
  return L
end
return M
