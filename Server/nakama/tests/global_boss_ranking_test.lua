package.path='Server/nakama/?.lua;'..package.path
local Fixture=dofile('Server/nakama/tests/storage_fault_fixture.lua');local F=Fixture.new();F.install()
local copy=Fixture.copy;local SYSTEM='00000000-0000-0000-0000-000000000000'
local boards,profiles={},{}
local writes,native_fail_after,list_limits=0,false,{}
F.nk.leaderboard_create=function(id,auth,order,operator,schedule,metadata,ranks)
 assert(auth==true and order=='desc'and operator=='best'and ranks==true,'ranking must be authoritative best with ranks enabled')
 boards[id]=boards[id]or{}
end
F.nk.leaderboard_record_write=function(id,uid,name,score,subscore,metadata,operator)
 assert(operator=='best'and next(metadata)==nil,'native public metadata must not contain private participant data')
 writes=writes+1;local b=assert(boards[id]);local r=b[uid]
 if not r or score>r.score then b[uid]={owner_id=uid,score=score,metadata=copy(metadata)}end
 if native_fail_after then native_fail_after=false;error('native reply lost after commit')end
 return copy(b[uid])
end
F.nk.leaderboard_records_list=function(id,owners,limit,cursor)
 assert(limit<=50,'unbounded leaderboard list');local sorted={}
 for _,r in pairs(assert(boards[id]))do sorted[#sorted+1]=copy(r)end
 table.sort(sorted,function(a,b)return a.score==b.score and a.owner_id<b.owner_id or a.score>b.score end)
 local own={};for n,r in ipairs(sorted)do r.rank=n;for _,uid in ipairs(owners or{})do if uid==r.owner_id then own[#own+1]=copy(r)end end end
 local first=cursor and cursor~=''and assert(tonumber(cursor:match('^page%-(%d+)$')),'invalid cursor')or 0
 local page={};for n=first+1,math.min(first+limit,#sorted)do page[#page+1]=copy(sorted[n])end
 return page,own,first+limit<#sorted and'page-'..(first+limit)or'',first>0 and'page-'..math.max(0,first-limit)or''
end
F.nk.users_get_id=function(ids)
 local out={};for _,uid in ipairs(ids)do if profiles[uid]then out[#out+1]=copy(profiles[uid])end end;return out
end
F.nk.account_get_id=function()error('ranking must not read private account objects')end
F.nk.storage_list=function(uid,collection,limit,cursor)
 list_limits[#list_limits+1]=limit;local rows={}
 for _,r in pairs(F.db)do if r.collection==collection then rows[#rows+1]=copy(r)end end
 table.sort(rows,function(a,b)return(a.user_id..a.key)<(b.user_id..b.key)end)
 local start=tonumber(cursor)or 0;local page={}
 for n=start+1,math.min(start+limit,#rows)do page[#page+1]=rows[n]end
 return page,start+limit<#rows and tostring(start+limit)or''
end
local R=require('modules.duel_global_boss_ranking');local spawned=0
F.nk.match_create=function(handler,p)
 assert(handler=='duel_global_boss_rank_dispatcher');spawned=spawned+1
 local mid='rank-worker-'..spawned;F.matches[mid]={match_id=mid};assert(R.bind(p.event_id,p.nonce,mid));return mid
end
local event='week-1000';F.put(R.EVENT,SYSTEM,event,{id=event,revision=1,hp=550000})
local function player(uid,total,attempts)
 profiles[uid]={user_id=uid,username='Name '..uid,display_name='ignored',avatar_url='https://untrusted.invalid/private',metadata={secret='never expose'}}
 F.put(R.PLAYER,uid,event,{event_id=event,total_damage=total,attempts_used=attempts or 1,active_attempt_id='SECRET_ATTEMPT',requests={SECRET_REQUEST=true},reward_mail_id='SECRET_REWARD'})
end
local count=0;local function check(v,n)assert(v,n);count=count+1;print('PASS '..n)end
player('a',100,1);assert(R.sync('a',event))
local a=F.get(R.PLAYER,'a',event);a.total_damage=350;a.attempts_used=2;F.put(R.PLAYER,'a',event,a);assert(R.sync('a',event));assert(R.sync('a',event))
check(boards[R.id(event)].a.score==350,'multiple attempts become one native record containing durable weekly total')
F.nk.leaderboard_record_write(R.id(event),'a',nil,100,0,{},'best')
check(boards[R.id(event)].a.score==350,'delayed older native write cannot reduce or duplicate aggregate damage')
player('b',250,1);player('c',100,3);player('zero',0,3)
assert(R.sync('b',event));assert(R.sync('c',event));assert(R.sync('zero',event))
F.put('duel_account_identity','c','gender_v1',{version=1,gender='female',finalized=true})
F.put('duel_player_cosmetics','c','appearance_v1',{schema_version=1,selected_theme='phoenix',unlocked={phoenix=true}})
local page=R.list('c',event,2)
check(#page.entries==2 and page.entries[1].user_id=='a'and page.entries[1].damage==350 and page.entries[1].attempts_used==2,
 'one sorted row per account exposes summed damage and current weekly attempt count')
check(page.self_entry.user_id=='c'and page.self_entry.rank==3 and page.self_entry.is_self,
 'caller position remains available outside the current page')
local second=R.list('c',event,2,page.next_cursor)
check(#second.entries==1 and second.entries[1].avatar_id=='phoenix-female'and second.entries[1].display_name=='Name c',
 'bounded next page resolves current public display name and matching immutable-gender avatar')
local zero=R.list('zero',event,2)
check(zero.self_entry.rank==0 and zero.self_entry.damage==0 and zero.self_entry.attempts_used==3,
 'zero-damage caller remains unranked while its own spent attempts are shown correctly')
local allowed={rank=true,user_id=true,display_name=true,avatar_id=true,damage=true,attempts_used=true,is_self=true}
for _,row in ipairs(page.entries)do for key in pairs(row)do assert(allowed[key],'private field leaked:'..key)end end
check(true,'rankingDTO excludes admission receipts, snapshots, request IDs and reward IDs')
local p=F.get(R.PLAYER,'b',event);p.total_damage=400;F.put(R.PLAYER,'b',event,p)
F.fail_after=true;assert(R.sync('b',event))
check(boards[R.id(event)].b.score==400 and F.get(R.PLAYER,'b',event).ranked_total==400,
 'lost storage acknowledgement after projection checkpoint cannot double native score')
p=F.get(R.PLAYER,'c',event);p.total_damage=500;F.put(R.PLAYER,'c',event,p);native_fail_after=true
local ok=pcall(R.sync,'c',event)
check(not ok and boards[R.id(event)].c.score==500 and F.get(R.PLAYER,'c',event).ranked_total==100,
 'lost native acknowledgement leaves durable source dirty for safe repair')
assert(R.ensure_worker(event));local control=F.get(R.COLLECTION,SYSTEM,event)
check(R.page(event,control.worker_match_id)and F.get(R.PLAYER,'c',event).ranked_total==500,
 'ephemeral recovery worker repairs a lost native acknowledgement using best(total)')
-- Simulate process loss after damage CAS but before any native write.
p=F.get(R.PLAYER,'a',event);p.total_damage=700;F.put(R.PLAYER,'a',event,p)
F.put(R.EVENT,SYSTEM,event,{id=event,revision=2,hp=549000})
F.matches[control.worker_match_id]=nil
F.put(R.EVENT,SYSTEM,'index',{current=event,pending={}})
local Boss=require('modules.duel_global_boss');assert(Boss.recover_workers())
control=F.get(R.COLLECTION,SYSTEM,event);assert(R.page(event,control.worker_match_id))
check(boards[R.id(event)].a.score==700,'startup repairs durable damage committed before the leaderboard write')
for n=1,120 do player('bulk-'..n,n,1)end
F.put(R.EVENT,SYSTEM,event,{id=event,revision=3,hp=548000});F.matches[control.worker_match_id]=nil
assert(R.ensure_worker(event));control=F.get(R.COLLECTION,SYSTEM,event)
local before=writes;check(not R.page(event,control.worker_match_id)and writes-before<=100,'recovery work is bounded to100 source rows per tick')
check(R.page(event,control.worker_match_id),'paginated recovery continues to remaining players without dropping rows')
for _,n in ipairs(list_limits)do assert(n==100)end
local forged=Boss.rpc_leaderboard({user_id='a'},{event_id=event,limit=1000})
check(forged.err=='bad_payload','client cannot request an unbounded ranking page')
forged=Boss.rpc_leaderboard({user_id='a'},{event_id=event,user_id='c',damage=999999})
check(forged.err=='bad_payload','ranking RPC rejects target-user and score injection')
-- A failed cluster probe is not proof of absence. Keep the live actor/control.
local previous=F.get(R.COLLECTION,SYSTEM,event)
F.matches[previous.worker_match_id]={match_id=previous.worker_match_id}
F.put(R.EVENT,SYSTEM,event,{id=event,revision=4,hp=547000})
local old_spawned=spawned;F.probe_failure=true
check(R.ensure_worker(event)==false and spawned==old_spawned and
 F.get(R.COLLECTION,SYSTEM,event).worker_match_id==previous.worker_match_id,
 'thrown liveness probe fails closed without replacing the existing repair worker')
F.probe_failure=false;local original_probe=F.nk.match_get
F.nk.match_get=function()return nil,'probe unavailable'end
check(R.ensure_worker(event)==false and spawned==old_spawned,
 'explicit probe error cannot be interpreted as a missing service actor')
F.nk.match_get=original_probe
assert(R.ensure_worker(event)and spawned==old_spawned)
-- A proven absent actor may be replaced. Its obsolete delayed loop then exits.
F.matches[previous.worker_match_id]=nil;assert(R.ensure_worker(event)and spawned==old_spawned+1)
local dispatcher=dofile('Server/nakama/modules/duel_global_boss_rank_dispatcher.lua')
local old_state={event_id=event,mid=previous.worker_match_id};local warnings=0
F.nk.logger_warn=function()warnings=warnings+1 end
check(dispatcher.match_loop({}, {},1,old_state)==nil and warnings==0,
 'known replaced service actor terminates instead of logging and living forever')
local current=F.get(R.COLLECTION,SYSTEM,event)
local current_state={event_id=event,mid=current.worker_match_id};F.read_failure=true
check(dispatcher.match_loop({}, {},1,current_state)==current_state and warnings==1,
 'transient storage failure preserves the current worker for retry')
F.read_failure=false
print('BOSS RANKING '..count..' CHECKS PASSED')
