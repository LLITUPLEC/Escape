-- Bounded durable admission/retirement for journal receipts. UUIDs alone are
-- deliberately insufficient to admit a missing receipt after compaction.
local nk = require("nakama")
local M = { COLLECTION = "duel_match3_journal", KEY = "retention_v2", KEEP = 4, MAX_OPEN = 128 }
local function copy(v) if type(v) ~= "table" then return v end; local o = {}; for k,x in pairs(v) do o[k]=copy(x) end; return o end
local function decode(row)
  if not row then return nil end
  local v = type(row.value) == "table" and row.value or nk.json_decode(row.value)
  assert(type(v) == "table", "invalid_retention_record"); return v
end
local function read(uid, key)
  local rows = nk.storage_read({{user_id=uid,collection=M.COLLECTION,key=key}})
  return rows and rows[1]
end
function M.load(uid)
  local row = read(uid, M.KEY)
  local v = decode(row) or {schema=2,next_seq=0,retired_through=0,open={},closed_ranges={},recent={},completion_order=0}
  assert(v.schema == 2 and type(v.open)=="table" and type(v.recent)=="table", "invalid_retention_control")
  return {uid=uid,value=v,version=row and row.version,dirty=false}
end
function M.write(ctx)
  return {user_id=ctx.uid,collection=M.COLLECTION,key=M.KEY,value=ctx.value,version=ctx.version or "*",permission_read=0,permission_write=0}
end
local function sequence(ticket)
  local n = type(ticket)=="table" and tonumber(ticket.sequence)
  if not n or n < 1 or n ~= math.floor(n) or n == math.huge then return nil end
  return n
end
function M.find(ctx, id)
  for seq,entry in pairs(ctx.value.open) do if entry.id==id then return {sequence=tonumber(seq),nonce=entry.nonce} end end
  for _,entry in ipairs(ctx.value.recent) do if entry.id==id and entry.ticket then return copy(entry.ticket) end end
end
function M.issue(ctx, id, kind, origin, terminal_only)
  local existing = M.find(ctx,id); if existing then return existing end
  local count=0; for _ in pairs(ctx.value.open) do count=count+1 end
  if count >= M.MAX_OPEN and not terminal_only then return nil,"recovery_backlog_full" end
  local seq=(tonumber(ctx.value.next_seq) or 0)+1
  local ticket={sequence=seq,nonce=nk.uuid_v4()}
  ctx.value.next_seq=seq;ctx.value.open[tostring(seq)]={id=id,kind=kind,nonce=ticket.nonce,issued_at=os.time(),origin=copy(origin)};ctx.dirty=true
  return ticket
end
function M.authorize(ctx,id,kind,ticket)
  local seq=sequence(ticket); if not seq then return false,"result_ticket_required" end
  local entry=ctx.value.open[tostring(seq)]
  if not entry then
    if seq <= (tonumber(ctx.value.next_seq) or 0) then return false,"result_compacted" end
    return false,"invalid_result_ticket"
  end
  if entry.id~=id or entry.kind~=kind or entry.nonce~=ticket.nonce then return false,"invalid_result_ticket" end
  return true
end
local function close(ctx,ticket)
  local seq=assert(sequence(ticket),"invalid_result_ticket")
  ctx.value.open[tostring(seq)]=nil
  local holes={};for key in pairs(ctx.value.open) do holes[#holes+1]=assert(tonumber(key),"invalid_open_sequence") end;table.sort(holes)
  local last=tonumber(ctx.value.next_seq) or 0
  ctx.value.retired_through=#holes>0 and holes[1]-1 or last
  local ranges={};local from=ctx.value.retired_through+1
  for _,hole in ipairs(holes) do if from<hole then ranges[#ranges+1]={from,hole-1} end;from=hole+1 end
  if from<=last then ranges[#ranges+1]={from,last} end
  ctx.value.closed_ranges=ranges;ctx.dirty=true
end
function M.adopt_pending(ctx,record)
  if record.ticket then return M.authorize(ctx,record.result_id,record.kind,record.ticket) end
  local pending=decode(read(ctx.uid,"pending"))
  if not (pending and type(pending.entries)=="table" and pending.entries[record.result_id]) then return false,"legacy_result_not_pending" end
  local ticket,err=M.issue(ctx,record.result_id,record.kind);if not ticket then return false,err end
  record.ticket=ticket;record.schema=2;return true
end
function M.finish(ctx,record)
  local ok,err=M.authorize(ctx,record.result_id,record.kind,record.ticket);if not ok then return nil,err end
  close(ctx,record.ticket)
  ctx.value.completion_order=(tonumber(ctx.value.completion_order) or 0)+1
  local recent={};for _,entry in ipairs(ctx.value.recent) do if entry.id~=record.result_id then recent[#recent+1]=entry end end
  recent[#recent+1]={id=record.result_id,ticket=copy(record.ticket),completed_at=record.completed_at or os.time(),order=ctx.value.completion_order,protocol=2}
  local evicted={};while #recent>M.KEEP do evicted[#evicted+1]=table.remove(recent,1).id end
  ctx.value.recent=recent;ctx.dirty=true;return evicted
end
function M.cancel_unused(ctx,id,kind,ticket)
  local seq=sequence(ticket)
  if seq and not ctx.value.open[tostring(seq)] and seq <= (tonumber(ctx.value.next_seq) or 0) then return true end
  local ok,err=M.authorize(ctx,id,kind,ticket);if not ok then return false,err end
  if read(ctx.uid,"result:"..id) then return false,"result_pending" end
  local pending=decode(read(ctx.uid,"pending"))
  if pending and pending.entries and pending.entries[id] then return false,"result_pending" end
  close(ctx,ticket);return true
end
local function retained(ctx,id) for _,entry in ipairs(ctx.value.recent) do if entry.id==id then return true end end;return false end
function M.can_prune(row,now)
  if type(row)~="table" or row.collection~=M.COLLECTION or type(row.key)~="string" or row.key:sub(1,7)~="result:"
      or row.permission_read~=0 or row.permission_write~=0 or type(row.version)~="string" or row.version=="" then return false end
  local record=decode(row)
  local finished=record and tonumber(record.completed_at)
  now=tonumber(now) or os.time()
  if not record or (record.status~="complete" and record.status~="aborted") or not finished or finished~=finished or finished<=0 or finished>now then return false end
  local id=row.key:sub(8)
  local pending=decode(read(row.user_id,"pending"))
  if pending and pending.entries and pending.entries[id] then return false end
  for attempt=1,5 do
    local ctx=M.load(row.user_id)
    if retained(ctx,id) then return false end
    if record.ticket then
      local seq=sequence(record.ticket)
      -- An open sequence or unknown future ticket can never be discarded.
      return seq~=nil and seq <= (tonumber(ctx.value.next_seq) or 0) and ctx.value.open[tostring(seq)]==nil
    end
    -- Legacy UUID records are observed by actual completion timestamp. New
    -- protocol completions always outrank pre-upgrade records. No missing
    -- legacy UUID may be freshly prepared in the strict journal API.
    local recent=copy(ctx.value.recent)
    recent[#recent+1]={id=id,completed_at=tonumber(record.completed_at),protocol=1}
    table.sort(recent,function(a,b)
      if (a.protocol or 1)~=(b.protocol or 1) then return (a.protocol or 1)<(b.protocol or 1) end
      if (a.protocol or 1)==2 then return (a.order or 0)<(b.order or 0) end
      if a.completed_at~=b.completed_at then return (a.completed_at or 0)<(b.completed_at or 0) end
      return a.id<b.id -- legacy timestamps have only one-second precision
    end)
    while #recent>M.KEEP do table.remove(recent,1) end
    ctx.value.recent=recent
    local ok,err=pcall(nk.storage_write,{M.write(ctx)})
    if ok then return not retained(ctx,id) end
    if not tostring(err):find("version",1,true) or attempt==5 then return false end
  end
  return false
end
function M.prune_ids(uid,ids)
  -- Cleanup is best-effort after the committed award. Failure never rolls back
  -- an earned outcome; paginated maintenance retries the immutable victims.
  pcall(function()
    for _,id in ipairs(ids or {}) do
      local row=read(uid,"result:"..id)
      if row and M.can_prune(row) then nk.storage_delete({{user_id=uid,collection=M.COLLECTION,key=row.key,version=row.version}}) end
    end
  end)
end
function M.sweep_unused(uid)
  if type(nk.match_get)~="function" then return end
  local ctx=M.load(uid);local probes,checked={},0
  for seq,entry in pairs(copy(ctx.value.open)) do
    local mid=entry.origin and entry.origin.match_id
    if checked<8 and type(mid)=="string" and mid~="" and os.time()-(tonumber(entry.issued_at) or os.time())>=120
        and not read(uid,"result:"..entry.id) then
      checked=checked+1
      local protected=false
      if entry.origin.root_result then
        local rows=nk.storage_read({{user_id=uid,collection="duel_race_entry",key="run:"..entry.origin.root_result}})
        local receipt=decode(rows and rows[1])
        protected=receipt and receipt.status=="bound" and type(receipt.finish_intent)=="table"
      end
      local proof=probes[mid]
      if proof==nil then local ok,match,err=pcall(nk.match_get,mid);proof=ok and not err and match==nil;probes[mid]=proof end
      if proof and not protected then M.cancel_unused(ctx,entry.id,entry.kind,{sequence=tonumber(seq),nonce=entry.nonce}) end
    end
  end
  if ctx.dirty then nk.storage_write({M.write(ctx)}) end
end
return M
