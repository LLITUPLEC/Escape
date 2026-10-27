package.path='Server/nakama/?.lua;'..package.path
local F=dofile('Server/nakama/tests/storage_fault_fixture.lua').new();F.install()
local copy=dofile('Server/nakama/tests/storage_fault_fixture.lua').copy
local Boss=require('modules.duel_global_boss');local Mail=require('modules.duel_mail');local Rules=require('modules.duel_global_boss_rules')
local SYSTEM='00000000-0000-0000-0000-000000000000'
local defs={global_boss_teleport={kind='material',max_stack=99},tesseract={kind='material',max_stack=99},
 elixir_assault={kind='consumable',max_stack=99},elixir_stone={kind='consumable',max_stack=99},elixir_gambit={kind='consumable',max_stack=99},scroll_pve_bounty={kind='consumable',max_stack=99}}
for _,slot in ipairs({'Helmet','Shoulders','Chest','Gloves','Legs','Feet','WeaponLeft','WeaponRight'})do defs['recipe_drop_t1_gold_'..slot]={kind='recipe',max_stack=1}end
local function read_sheet(uid)local s,v=F.get('sheet',uid,'v1');s._storage_version=v;return s end
local function write_sheet(uid,s)local v=s._storage_version;s=copy(s);s._storage_version=nil;return{collection='sheet',user_id=uid,key='v1',value=s,version=v or'*',permission_read=1,permission_write=0}end
local guard=function(uid,p)return p.session_epoch==7,'session_stale'end
Mail.configure({read_progress=F.read_progress,read_sheet=read_sheet,get_defs=function()return defs end,build_sheet_write=write_sheet,build_progress_write=F.build_progress,guard_epoch=guard})
local counter=0
Boss.configure({read_sheet=read_sheet,get_defs=function()return defs end,build_sheet_write=write_sheet,guard_epoch=guard,mail=Mail,
 try_match_create=function(p)
  counter=counter+1;local mid='own-match-'..counter;F.matches[mid]={match_id=mid}
  local s={owner_user_id=p.owner_user_id,match_id=mid,global_boss=p.global_boss}
  assert(Boss.bind_state(s,p.global_boss.spawn_nonce));return mid
 end})
F.nk.storage_list=function(uid,c,n,cursor)
 local rows={};for _,row in pairs(F.db)do if row.collection==c then rows[#rows+1]=copy(row)end end
 table.sort(rows,function(a,b)return(a.user_id..a.key)<(b.user_id..b.key)end)
 local out={};local start=tonumber(cursor)or 0
 for i=start+1,math.min(#rows,start+n)do out[#out+1]=rows[i]end
 return out,start+n<#rows and tostring(start+n)or''
end
F.nk.storage_delete=function(rows)for _,r in ipairs(rows)do F.db[F.id(r.collection,r.user_id,r.key)]=nil end end
F.nk.match_create=function()return'service'end
local count=0;local function check(v,n)assert(v,n);count=count+1;print('PASS '..n)end
local function seed(uid,tokens)
 F.seed(uid);F.put('sheet',uid,'v1',{equipment={},inventory={[1]='global_boss_teleport'},inventory_counts={[1]=tokens},inventory_extra_slots=0,inventory_pending_loot={},learned_recipes={}})
end
check(Boss.config().eligibility_min_damage==1,'default eligibility requires positive damage without level restrictions')
F.put(Boss.COLLECTION,SYSTEM,'config',{eligibility_min_damage=1000})
local e=assert(Boss.event());check(e.max_hp%50000==0 and e.config.base_damage%500==0,'bossHP/damage round up to required multiples')
seed('a',5);seed('b',5);seed('low',5)
local a=Boss.enter('a','request-0001',7);check(a.ok and a.spent_token and a.attempts_left==2,'all-level admission costs one token and one weekly attempt')
local again=Boss.enter('a','request-0001',7);check(again.match_id==a.match_id and Boss.token_count(read_sheet('a'))==4,'lost entry acknowledgement resumes active attempt without new charge')
F.put(Boss.COLLECTION,SYSTEM,'config',{max_hp=1000000,base_damage=9000,base_armor=9000,base_crit=.9,max_turns=60})
local shown=Boss.status('a')
check(shown.hp_max==550000 and shown.base_damage==3500 and shown.base_armor==1000 and shown.base_crit==.05
 and shown.base_heal==0 and shown.no_healing and shown.max_turns==30,
 'boss section exposes current frozen-event stats even after next-event config changes')
F.fail_after=true;local started=Boss.start('a',a.attempt_id,{value=1},{},read_sheet('a'))
check(started and started.snapshot.value==1,'first snapshot survives lost storage acknowledgement')
local b=Boss.enter('b','request-0002',7);Boss.start('b',b.attempt_id,{value=1},{},read_sheet('b'))
local c=Boss.enter('low','request-0003',7);Boss.start('low',c.attempt_id,{value=1},{},read_sheet('low'))
local hit=Boss.commit('a',a.attempt_id,1,2000,{value=2},true)
check(hit and hit.actual_damage==2000 and Boss.get_event(e.id).hp==e.max_hp-2000,'player damage commits to one globally shared HP row')
local replay=Boss.commit('a',a.attempt_id,1,2000,{value=2},true)
check(replay.replayed and Boss.get_event(e.id).hp==e.max_hp-2000,'duplicate action sequence never repeats global damage')
local other=Boss.commit('b',b.attempt_id,1,2000,{value=2},true)
check(other.event.hp==e.max_hp-4000,'second account attacks the same remaining global HP')
F.before_write=function()assert(Boss.commit('b',b.attempt_id,2,100,{value=3},true))end
local concurrent=Boss.commit('a',a.attempt_id,2,100,{value=3},true)
check(concurrent.event.hp==e.max_hp-4200,'concurrent global CAS conflict retries without losing either contribution')
F.fail_after=true;local ambiguous=Boss.commit('a',a.attempt_id,3,100,{value=4},true)
check(ambiguous and ambiguous.replayed and Boss.get_event(e.id).hp==e.max_hp-4300,'lost commit acknowledgement reads durable sequence instead of repeating damage')
local bad,err=Boss.commit('a',a.attempt_id,8,999999,{value=9},true)
check(not bad and err=='stale_action','out of order internal action sequence rejected')
Boss.commit('low',c.attempt_id,1,1,{value=2},true)
check(not Boss.get_player('low',e.id).qualified,'one tiny low-level hit does not qualify for legendary chest')
check(Mail.get('a').unread_count==0,'no boss rewards before global death')
-- Take the last global HP with a legitimate hit. Potential damage qualifies a
-- finisher even when remaining HP is below its actual ability strength.
local death=Boss.commit('a',a.attempt_id,4,e.max_hp,{value=5},true)
check(death.event.hp==0 and death.event.status=='dead','global death clamps HP at zero and persists reward outbox')
local done=Boss.dispatch_page(e.id)
check(done and Mail.get('a').unread_count==1 and Mail.get('b').unread_count==1 and Mail.get('low').unread_count==0,'offline fanout sends exactly one chest to each qualified owner')
local mail=Mail.get('a').messages[1]
check(#mail.attachments==7 and mail.attachments[1].def_id:match('^recipe_drop_t1_gold_'),'mail freezes one of eight legendary recipes plus useful supplies')
local before=F.read_progress('a');F.fail_after=true;local claim=Mail.claim('a',mail.id)
local after=F.read_progress('a')
check(claim.ok and claim.already_claimed and after.gold==before.gold+1500,'lost mail claim acknowledgement grants resources exactly once')
local r=Mail.claim('a',mail.id);check(r.ok and r.already_claimed and F.read_progress('a').gold==after.gold,'claimed mail replay cannot grant again')
check(not Mail.claim('a','global_boss:forged').ok,'claim cannot create a missing reward entitlement')
-- Inbox history keeps only four claimed rows; old missing claims stay invalid.
for i=1,5 do local id='other:'..i;assert(Mail.deliver('a',{id=id,title='x',body='x',created_at=os.time(),attachments={{resource='gold',amount=1}}}));assert(Mail.claim('a',id).ok)end
check(not Mail.claim('a',mail.id).ok,'compacted claimed mail cannot be replayed')
local ev,ver=Boss.get_event(e.id);ev.mail_done=false;ev.mail_cursor=nil
F.put(Boss.COLLECTION,SYSTEM,e.id,ev);Boss.dispatch_page(e.id)
check(Mail.get('a').unread_count==0,'durable participant admission proof prevents fanout recreating compacted claimed mail')
-- A reserved attempt whose global boss dies before any action is refunded.
local _,start,finish=e.id,e.starts_at,e.ends_at
local uid='late';seed(uid,1)
local a2={schema=1,id='late-attempt',event_id=e.id,request_id='late-request',status='bound',sequence=0,player_actions=0}
F.put(Boss.ATTEMPT_COLLECTION,uid,a2.id,a2);F.put(Boss.PLAYER_COLLECTION,uid,e.id,{attempts_used=1,active_attempt_id=a2.id,requests={}})
local refund=Boss.finish(uid,a2.id,'boss_defeated')
check(refund.refunded and Boss.token_count(read_sheet(uid))==2 and Boss.get_player(uid,e.id).attempts_used==0,'late global death before first action refunds token and weekly attempt')
Boss.finish(uid,a2.id,'boss_defeated');check(Boss.token_count(read_sheet(uid))==2,'late-death refund is idempotent')
-- Exact passive math and non-healable cry.
local s={global_boss={},bot_user_id='boss',owner_user_id='hero',stats={boss={base_damage=100,base_crit=.05},hero={hp=1000,initial_hp=1000,max_hp=2000,base_armor=99999}}}
Rules.begin_turn(s);check(s.stats.hero.hp==800 and s.stats.boss.base_damage==101 and math.abs(s.stats.boss.base_crit-.06)<1e-9,'turn1 cry uses initialHP and ignores armor; adaptation increases current values')
Rules.begin_turn(s);check(math.abs(s.stats.boss.base_damage-103.02)<1e-8 and s.stats.hero.hp==800,'turn2 retains damage fraction and cry cooldown')
Rules.begin_turn(s);check(s.stats.hero.hp==600 and s.global_boss.boss_own_turn==3,'cry fires again on third own turn')
for i=1,20 do Rules.begin_turn(s)end;check(s.stats.boss.base_crit==1 and s.stats.boss.base_heal==0,'adaptation crit caps at100percent and boss healing remainszero')
local badrpc=Boss.rpc_enter({user_id='a'},{session_epoch=7,request_id='request-9999',damage=999})
check(not badrpc.ok and badrpc.err=='bad_payload','client cannot submit damage or boss stats through entry RPC')
check(not Mail.rpc_claim({user_id='low'},{session_epoch=7,message_id=mail.id}).ok,'mail is scoped to authenticated owner only')
local changed=Boss.rpc_enter({user_id='a'},{session_epoch=7,event_id='week-previous',request_id='previous-week-request'})
check(changed.err=='event_changed','retry across weekly rollover cannot charge an entry in a different event')
-- A match init lost-ACK must accept the same already-bound actor on retry.
F.put(Boss.ATTEMPT_COLLECTION,'bind','bind-receipt',{id='bind-receipt',status='reserved',spawn_nonce='nonce',sequence=0})
F.fail_after=true;local bound=Boss.bind_state({owner_user_id='bind',match_id='same-actor',global_boss={attempt_id='bind-receipt'}},'nonce')
check(bound and bound.match_id=='same-actor','match bind survives an ambiguous committed storage acknowledgement')
-- Service VMs have no duel-core injection. They must still refund an orphan
-- which never acted when another participant killed the global boss.
local CFG=require('modules.duel_match3_config');local orphan='offline-no-action'
F.put(CFG.CHARACTER_SHEET_COLLECTION,orphan,CFG.CHARACTER_SHEET_KEY,{inventory={'global_boss_teleport'},inventory_counts={1},equipment={},inventory_pending_loot={}})
F.put(Boss.PLAYER_COLLECTION,orphan,e.id,{schema=1,event_id=e.id,attempts_used=1,active_attempt_id='orphan',qualified=false,requests={}})
F.put(Boss.ATTEMPT_COLLECTION,orphan,'orphan',{id='orphan',event_id=e.id,status='bound',player_actions=0,sequence=0})
local old=Boss.get_event(e.id);old.mail_done=false;old.mail_cursor=nil;F.put(Boss.COLLECTION,SYSTEM,e.id,old)
Boss.configure({});assert(Boss.dispatch_page(e.id))
local refunded=F.get(CFG.CHARACTER_SHEET_COLLECTION,orphan,CFG.CHARACTER_SHEET_KEY)
check(Boss.token_count(refunded)==2 and Boss.get_player(orphan,e.id).attempts_used==0,
 'dependency-free offline dispatcher refunds a never-acted orphan without loading duel core')
local Battle=require('modules.duel_global_boss_battle');local stale,finished,rejected=true,0,nil
local live_event={id='fresh',hp=10,max_hp=550000,ends_at=os.time()+1000}
local state={started=true,ended=false,owner_user_id='owner',bot_user_id='zz-bot-global',global_boss={event_id='fresh',attempt_id='fresh'},
 presences={owner={}},active_user_id='owner',stats={owner={hp=100},['zz-bot-global']={hp=10}},turn_deadline_tick=99999}
Battle.configure({CFG=CFG,api={get_event=function()return live_event end,
 finish=function()finished=finished+1;return{event=live_event,attempt={status='finished'}}end},
 filter_messages=function(s,m)return m end,stale=function()return stale end,
 reject=function(_,_,why)rejected=why end,broadcast=function()end,emit=function(_,s)s._terminal_delivered=true end})
Battle.loop({},state,1,{{sender={user_id='owner'},op_code=CFG.OP_PLAYER_LEFT}})
check(finished==0 and not state.ended and rejected=='session_stale','old logged-out socket cannot surrender a boss through legacyOP12')
stale=false;Battle.loop({},state,2,{{sender={user_id='owner'},op_code=CFG.OP_PLAYER_LEFT}})
check(finished==1 and state.ended and state._terminal_delivered,'current epoch legacy surrender still settles the own boss attempt')
print('GLOBAL BOSS '..count..' CHECKS PASSED')
