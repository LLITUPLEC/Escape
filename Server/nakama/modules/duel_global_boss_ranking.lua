-- Native ranking is a derived projection of the protected weekly TOTAL.
-- 'best(total)' is safe after lost acknowledgements and out-of-order retries.
local nk=require('nakama')
local M={COLLECTION='duel_global_boss_rank',PLAYER='duel_global_boss_player',EVENT='duel_global_boss'}
local SYSTEM='00000000-0000-0000-0000-000000000000'
local ensured={}
local THEMES={dawnwarden=true,voidranger=true,stormforged=true,emberoracle=true,frostwraith=true,
 phoenix=true,bookmaker=true,railwarden=true,beastbreaker=true,alchemist=true}
local function read(uid,c,k)
 local rows=nk.storage_read({{user_id=uid,collection=c,key=k}});assert(type(rows)=='table','ranking_storage_unavailable')
 local r=rows[1];if r and type(r.value)=='string'then r.value=nk.json_decode(r.value)end
 return r and r.value,r and r.version
end
local function op(key,v,ver)return{user_id=SYSTEM,collection=M.COLLECTION,key=key,value=v,version=ver or'*',permission_read=0,permission_write=0}end
local function enabled()return type(nk.leaderboard_create)=='function'and type(nk.leaderboard_record_write)=='function'end
function M.id(event_id)return'escape_boss_'..event_id end
function M.ensure(event_id)
 local id=M.id(event_id)
 if not ensured[id]then nk.leaderboard_create(id,true,'desc','best','',{event_id=event_id},true);ensured[id]=true end
 return id
end
function M.sync(uid,event_id)
 if not enabled()then return true end -- Pure engine/test runtimes have no native ranking API.
 local id=M.ensure(event_id)
 for _=1,4 do
  local p,pv=read(uid,M.PLAYER,event_id)
  if not p or(tonumber(p.total_damage)or 0)<=0 then return true end
  local total=math.floor(p.total_damage)
  if p.ranked_total==total then return true end
  nk.leaderboard_record_write(id,uid,nil,total,0,{},'best')
  p.ranked_total=total
  local ok=pcall(nk.storage_write,{{user_id=uid,collection=M.PLAYER,key=event_id,value=p,version=pv,permission_read=0,permission_write=0}})
  if ok then return true end
 end
 return false
end
function M.ensure_worker(event_id)
 if not enabled()or not event_id then return true end
 local e=read(SYSTEM,M.EVENT,event_id);if not e then return true end
 for _=1,4 do
  local s,sv=read(SYSTEM,M.COLLECTION,event_id);s=s or{schema=1,done_revision=-1}
  if(tonumber(s.done_revision)or-1)>=(tonumber(e.revision)or 0)then return true end
  if s.worker_match_id then
   local ok,live,probe_error=pcall(nk.match_get,s.worker_match_id)
   if not ok or probe_error~=nil then return false end -- Unknown liveness never replaces an actor.
   if live then return true end
  end
  if(s.lease_until or 0)>os.time()then return false end
  s.worker_nonce=nk.uuid_v4();s.lease_until=os.time()+20;s.cursor=nil;s.scan_revision=e.revision or 0
  local ok=pcall(nk.storage_write,{op(event_id,s,sv)})
  if ok then nk.match_create('duel_global_boss_rank_dispatcher',{event_id=event_id,nonce=s.worker_nonce});return true end
 end
 return false
end
function M.bind(event_id,nonce,mid)
 for _=1,4 do
  local s,sv=read(SYSTEM,M.COLLECTION,event_id)
  if s and s.worker_match_id==mid then return true end
  assert(s and s.worker_nonce==nonce,'ranking_worker_claim_invalid')
  s.worker_match_id=mid;s.worker_nonce=nil;s.lease_until=nil
  if pcall(nk.storage_write,{op(event_id,s,sv)})then return true end
 end
 return false
end
function M.page(event_id,mid)
 local s,sv=read(SYSTEM,M.COLLECTION,event_id);assert(s and s.worker_match_id==mid,'ranking_worker_replaced')
 local rows,cursor=nk.storage_list('',M.PLAYER,100,s.cursor or'')
 for _,r in ipairs(rows or{})do
  if r.key==event_id then
   local v=r.value;if type(v)=='string'then v=nk.json_decode(v)end
   if type(v)=='table'and(tonumber(v.total_damage)or 0)>0 and v.ranked_total~=v.total_damage then
    assert(M.sync(r.user_id,event_id),'ranking_sync_deferred')
   end
  end
 end
 -- A new action may commit during this page. One further scan finds its dirty
 -- owner; the projection is never allowed to acknowledge a newer revision.
 local e=read(SYSTEM,M.EVENT,event_id);s.cursor=cursor and cursor~=''and cursor or nil
 if not s.cursor then
  if(e.revision or 0)==s.scan_revision then s.done_revision=s.scan_revision;s.completed_at=os.time()
  else s.scan_revision=e.revision or 0 end
 end
 local ok=pcall(nk.storage_write,{op(event_id,s,sv)})
 if not ok then return false end
 return not s.cursor and s.done_revision==s.scan_revision
end
local function enrich(records,caller,event_id)
 local ids,seen,keys={},{},{}
 for _,r in ipairs(records)do local uid=r.owner_id or r.ownerId;if uid and not seen[uid]then
  seen[uid]=true;ids[#ids+1]=uid
  keys[#keys+1]={user_id=uid,collection=M.PLAYER,key=event_id}
  keys[#keys+1]={user_id=uid,collection='duel_player_cosmetics',key='appearance_v1'}
  keys[#keys+1]={user_id=uid,collection='duel_account_identity',key='gender_v1'}
 end end
 local profiles={};if #ids>0 then for _,u in ipairs(nk.users_get_id(ids)or{})do profiles[u.user_id or u.id]=u end end
 local sources={};if #keys>0 then for _,r in ipairs(nk.storage_read(keys)or{})do
  local v=r.value;if type(v)=='string'then v=nk.json_decode(v)end
  sources[r.user_id..':'..r.collection]=v
 end end
 local out={}
 for _,r in ipairs(records)do
  local uid=r.owner_id or r.ownerId;local user=profiles[uid]
  if user then
   local p=sources[uid..':'..M.PLAYER]or{};local appearance=sources[uid..':duel_player_cosmetics']or{}
   local identity=sources[uid..':duel_account_identity']or{};local female=identity.gender=='female'
   local theme=appearance.selected_theme
   if not THEMES[theme]or not(appearance.unlocked or{})[theme]then theme='default'end
   local avatar=theme=='default'and(female and'dawnwarden-female'or'')or(theme..(female and'-female'or'-male'))
   out[#out+1]={rank=tonumber(r.rank)or 0,user_id=uid,display_name=user.username or user.display_name or'Игрок',avatar_id=avatar,
    damage=math.max(0,tonumber(p.total_damage)or tonumber(r.score)or 0),attempts_used=math.min(3,math.max(0,tonumber(p.attempts_used)or 0)),is_self=uid==caller}
  end
 end
 return out
end
function M.list(uid,event_id,limit,cursor)
 assert(enabled(),'ranking_unavailable')
 local id=M.ensure(event_id);local ok,synced=pcall(M.sync,uid,event_id)
 local repair_ok,repair=pcall(M.ensure_worker,event_id)
 local records,owners,next_cursor,prev_cursor=nk.leaderboard_records_list(id,{uid},limit,cursor or'')
 records=records or{};owners=owners or{}
 local combined={};for _,r in ipairs(records)do combined[#combined+1]=r end;for _,r in ipairs(owners)do combined[#combined+1]=r end
 if #owners==0 then combined[#combined+1]={owner_id=uid,score=0,rank=0}end
 local mapped=enrich(combined,uid,event_id);local entries,self_entry={},nil
 local by_uid={};for _,r in ipairs(mapped)do by_uid[r.user_id]=r;if r.user_id==uid then self_entry=r end end
 for _,r in ipairs(records)do local row=by_uid[r.owner_id or r.ownerId];if row then entries[#entries+1]=row end end
 if not self_entry then self_entry={rank=0,user_id=uid,damage=0,attempts_used=0,is_self=true}end
 local s=read(SYSTEM,M.COLLECTION,event_id);local e=read(SYSTEM,M.EVENT,event_id)
 return{ok=true,event_id=event_id,entries=#entries>0 and entries or nil,self_entry=self_entry,
  next_cursor=next_cursor~=''and next_cursor or nil,prev_cursor=prev_cursor~=''and prev_cursor or nil,
  updating=not(ok and synced and repair_ok and repair)or(tonumber(s and s.done_revision)or-1)<(tonumber(e and e.revision)or 0)}
end
return M
