package.path='Server/nakama/?.lua;'..package.path
local Engine=dofile('Server/nakama/tests/global_boss_engine_fixture.lua')
local Fixture=dofile('Server/nakama/tests/storage_fault_fixture.lua')
local F=Fixture.new();local nk=package.loaded.nakama
for k,v in pairs(F.nk)do nk[k]=v end;F.nk=nk;F.install();local copy=Fixture.copy
local Boss=require('modules.duel_global_boss');local Battle=require('modules.duel_global_boss_battle')
local CFG=Engine.CFG;local SYSTEM='00000000-0000-0000-0000-000000000000'
local checks=0;local function check(v,n)assert(v,n);checks=checks+1;print('PASS '..n)end
local function same(a,b)
 if type(a)~=type(b)then return false end;if type(a)~='table'then return a==b end
 for k,v in pairs(a)do if not same(v,b[k])then return false end end
 for k in pairs(b)do if a[k]==nil then return false end end;return true
end
local created={}
Boss.configure({guard_epoch=function(_,p)return p.session_epoch==7,'session_stale'end,
 try_match_create=function(p)created[#created+1]=copy(p);return'practice-match-'..#created end})
local config=Boss.config();config.max_hp=50000;config.base_damage=3500;config.max_turns=3
F.put(Boss.COLLECTION,SYSTEM,'config',config)
F.put('duel_player_cosmetics','hero','appearance_v1',{unlock_reward_receipts={phoenix={version=1,matter=5,hp_basis_points=50}}})
F.put('progress','hero','profile',{energy=0,gold=1,level=2})
F.put('sheet','hero','v1',{inventory={},consumables={armed={item_id='elixir_assault'}}})
local before=copy(F.db);local function untouched(n)check(same(before,F.db)and#F.writes==0,n)end
check(not Boss.rpc_practice({},{}).ok,'practice requires authentication')
check(not Boss.rpc_practice({user_id='hero'},{session_epoch=6}).ok,'practice rejects stale login epoch')
for _,field in ipairs({'damage','max_hp','hp','practice_config','owner_user_id','event_id','request_id'})do
 local p={session_epoch=7};p[field]=123;check(not Boss.rpc_practice({user_id='hero'},p).ok,'practice rejects client field '..field)
end
local entry=Boss.rpc_practice({user_id='hero'},{session_epoch=7})
check(entry.ok and entry.practice and not entry.spent_token,'practice starts with zero tokens and zero energy')
untouched('practice admission cannot initialize an event or spend any account resources')

local emitted={};local player_applied=0
Battle.configure({api=Boss,CFG=CFG,new_stats=Engine.new_stats,new_board=Engine.new_board,ensure_board=Engine.ensure_board,
 init_cheat_rows=Engine.init_cheat_rows,ensure_queue=function()end,update_cheat_rows=Engine.update_cheat_rows,
 read_sheet=function()error('practice cannot read/consume armed consumables')end,
 effects={normalize=function()error('practice cannot normalize consumables')end,apply=function()error('practice cannot apply consumables')end},
 apply_player=function(actor,uid,read_only)
  assert(read_only and uid=='hero');player_applied=player_applied+1
  actor.hp=100000;actor.max_hp=100000;actor.initial_hp=100000;actor.base_damage=5000;actor.base_armor=0;actor.base_crit=0;actor.mana=100
 end,choose=Engine.choose,validate=Engine.validate,resolve=Engine.resolve,action_cost=Engine.action_cost,
 tick_buffs=Engine.tick_buffs,tick_cooldowns=Engine.tick_cooldowns,parse=function(v)return v end,
 filter_messages=function(_,m)return m end,stale=function()return false end,reject=function(_,_,why)error(why)end,
 broadcast=function()end,emit=function(_,s,p)s._terminal_delivered=true;emitted[#emitted+1]=copy(p)end})
local function launch(index)
 local p=created[index];local s={mode='pve',match_id='practice-match-'..index,owner_user_id='hero',bot_user_id=p.bot_user_id,
  global_boss=copy(p.global_boss),pve_run=p.pve_run,stats={},presences={hero={}},join_deadline_tick=200}
 assert(Boss.bind_state(s));Battle.join({},s,0);return s
end
local function message(op,data)return{{sender={user_id='hero'},op_code=op,data=data}}end
local s=launch(1)
check(s.stats[s.bot_user_id].hp==50000 and s.stats.hero.hp==s.stats.hero.max_hp,'practice starts both sides at full HP')
check(s.stats[s.bot_user_id].base_damage==3500 and s.global_boss.boss_own_turn==0 and s.global_boss.next_scream_turn==1,'practice resets adaptation and scream to original config')
check(not s._consumable_bindings,'practice leaves armed consumables and bounty completely outside the battle')
-- A real petard action uses the actual shipping resolve and mana/cooldown rules.
Battle.loop({},s,1,message(17));Battle.loop({},s,2,message(CFG.OP_ACTION_REQUEST,{actionType=4,fromX=-1,fromY=-1,toX=-1,toY=-1,cx=-1,cy=-1}))
check(s.global_boss.my_damage>0 and s.stats[s.bot_user_id].hp<50000,'actual engine damage is recorded only in the private practice HP')
check(s.stats.hero.petard_cd>0 and s._boss_sequence==1,'practice uses real ability cooldown and authoritative sequence')
local damage=s.global_boss.my_damage
Battle.join({},s,3)
check(s.global_boss.my_damage==damage,'rejoining one live practice cannot restore full HP or erase its damage')
Battle.finish({},s,4,'player_surrender')
local result=emitted[#emitted]
check(result.globalBossPractice and result.bossMyDamage==damage and result.bossGlobalMaxHp==50000 and not result.bossQualified,'practice terminal DTO contains private damage and full-HP denominator without eligibility')
untouched('actions reconnect and surrender leave every persistent row byte-for-byte unchanged')

-- Exhausted/dead/expired real event and an active paid attempt are irrelevant.
local wk=math.floor((os.time()+10800+3*86400)/604800)*604800-3*86400-10800;local id='week-'..wk
F.put(Boss.COLLECTION,SYSTEM,id,{id=id,hp=0,status='dead',ends_at=1,config=config})
F.put(Boss.PLAYER_COLLECTION,'hero',id,{attempts_used=3,total_damage=123,active_attempt_id='paid-attempt'})
F.put(Boss.ATTEMPT_COLLECTION,'hero','paid-attempt',{status='bound',match_id='paid-live',snapshot={}})
before=copy(F.db)
for i=1,8 do
 local nextEntry=Boss.rpc_practice({user_id='hero'},{session_epoch=7});check(nextEntry.ok,'unlimited practice launch '..i)
 local fresh=launch(#created)
 check(fresh.stats[fresh.bot_user_id].hp==50000 and fresh.stats.hero.hp==100000 and fresh.global_boss.my_damage==0 and fresh.stats.hero.petard_cd==0,'new practice resets all HP damage and cooldown '..i)
 if i==1 then
  local private=Boss.practice_api(fresh);local snap=Battle.snapshot(fresh)
  local killed=private.commit('hero',fresh.global_boss.attempt_id,1,90000,snap,true)
  check(killed.event.hp==0 and killed.attempt.actual_damage==50000 and not killed.qualified,'private lethal damage clamps to full HP without a real boss victory')
  Battle.finish({},fresh,5,'boss_defeated')
 elseif i==2 then
  fresh.presences={};Battle.loop({},fresh,1,{});Battle.loop({},fresh,1+20*CFG.TICK_RATE,{})
  check(fresh.ended,'practice disconnect ends locally without a refund')
 else Battle.finish({},fresh,5,'turn_limit')end
end
untouched('unlimited fresh battles lethal results turn limits and disconnect do not alter the exhausted paid event')
check(player_applied==9,'every fresh practice calculates player stats through the read-only path')

-- Actual character-stats module must not use any normalizing/rewarding reader.
local Stats=require('modules.duel_match3_character_stats')
local function forbidden()error('persistent normalizer called by practice')end
Stats.configure({CFG={MAX_HP=150,character_stats_base_for_level=function()return{hp=1000,damage=10,armor=2,healing=3,crit_chance=.1}end},
 ensure_character_sheet_initialized=forbidden,read_pve_progress=forbidden,storage_read_match3_summary_val=forbidden,cosmetics_hp_bonus=forbidden,
 read_progress_snapshot=function()return{level=2}end,read_stats_snapshot=function()return{}end,
 read_character_sheet=function()return{}end,sum_equipment_bonuses=function()return{}end,merge_stats_with_equipment=function(base)return base end,
 cosmetics_hp_bonus_read_only=require('modules.duel_player_cosmetics').passive_hp_bonus_read_only,is_human=function()return true end})
local actor={};Stats.apply_player_combat_stats_from_sheet(actor,'hero',{read_only=true})
check(actor.hp==1005 and actor.max_hp==1005 and actor.initial_hp==1005,'read-only real stats retain earned cosmetic HP bonus without reconciling rewards')
untouched('read-only character calculation cannot initialize sheets normalize old receipts or deliver new unlock rewards')
-- Exercise the actual shared terminal publisher, whose normal path writes a
-- durable lifecycle receipt even for paid boss attempts.
local file=assert(io.open('Server/nakama/modules/duel_match3.lua','r'));local source=file:read('*a');file:close()
local emitter=assert(source:match('(Runtime%.emit_game_over = function.-\nend)'))
local runtime={Lifecycle={publish=forbidden},cancel_unused_journal_tickets=forbidden}
local publish=assert(loadstring('local Runtime,CFG,nk=...;'..emitter..';return Runtime.emit_game_over'))(runtime,CFG,F.nk)
local delivered
check(publish({broadcast_message=function(_,p)delivered=p end},{match_id='practice-terminal',global_boss={practice=true}},result)and delivered.globalBossPractice,
 'actual core terminal publisher skips durable lifecycle and journal hooks for practice')
untouched('practice terminal delivery cannot write a durable result mailbox')
print('GLOBAL BOSS PRACTICE '..checks..' CHECKS PASSED')
