-- Historical unit fixtures start at an already-admitted authoritative action.
-- Model that NEW admission once, retaining its original ticket across journal
-- reconstruction. Runtime strict/missing-ticket adversarial tests do not use
-- this helper. Fault flags/callbacks belong to the subsequent result boundary.
local M={}
function M.wrap(j,f)
  local nk=require("nakama")
  f=f or {};f.admitted_journal_tickets=f.admitted_journal_tickets or {}
  local serial=0
  if not nk.uuid_v4 then nk.uuid_v4=function()serial=serial+1;return "fixture-admission-"..serial end end
  local function ticket(uid,id,kind,explicit)
    if explicit then return explicit end
    local k=uid..":"..id
    if f.admitted_journal_tickets[k]then
      local ok,rows=pcall(nk.storage_read,{{user_id=uid,collection="duel_match3_journal",key="retention_v2"}})
      if not ok or rows and rows[1] then return f.admitted_journal_tickets[k] end
      f.admitted_journal_tickets[k]=nil -- this fixture reset its entire database
    end
    local read_ok,existing=pcall(j.read,uid,id)
    if not read_ok or existing then return nil end
    local before,after,callback=f.fail_before,f.fail_after,f.before_write
    f.fail_before,f.fail_after,f.before_write=false,false,nil
    local ok,err,tickets=j.issue_many({{uid=uid,id=id,kind=kind}})
    f.fail_before,f.fail_after,f.before_write=before,after,callback
    assert(ok,err);f.admitted_journal_tickets[k]=tickets[1];return tickets[1]
  end
  local prepare,prepare_writes,atomic,atomic_many=j.prepare,j.prepare_writes,j.atomic,j.atomic_many
  j.prepare=function(uid,id,kind,plan,t)return prepare(uid,id,kind,plan,ticket(uid,id,kind,t))end
  j.prepare_writes=function(uid,id,kind,plan,t)return prepare_writes(uid,id,kind,plan,ticket(uid,id,kind,t))end
  j.atomic=function(uid,id,kind,plan,build,t)return atomic(uid,id,kind,plan,build,ticket(uid,id,kind,t))end
  j.atomic_many=function(specs)for _,s in ipairs(specs)do s.ticket=ticket(s.uid,s.id,s.kind,s.ticket)end;return atomic_many(specs)end
  return j
end
return M
