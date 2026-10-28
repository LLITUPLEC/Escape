-- A practice match owns its complete event/attempt in memory. No Storage,
-- ranking, rewards, consumables or real event progress are ever written here.
local M={}
function M.label(uid)return 'mode=duel_match3;practice-owner='..uid end
local function copy(v)if type(v)~='table'then return v end;local o={};for k,x in pairs(v)do o[k]=copy(x)end;return o end
function M.bind(s)
 local g=assert(s.global_boss);local c=copy(assert(g.practice_config));g.practice_config=nil
 assert(g.practice==true and type(c.max_hp)=='number'and c.max_hp>0,'invalid_boss_practice')
 s._boss_practice={event={id=g.event_id,config=c,hp=c.max_hp,max_hp=c.max_hp,status='active',ends_at=9007199254740991},
  attempt={id=g.attempt_id,status='bound',sequence=0,actual_damage=0}}
 return s._boss_practice.attempt
end
function M.api(s)
 if s._boss_practice_api then return s._boss_practice_api end
 local p=assert(s._boss_practice);local a,e=p.attempt,p.event
 local function owner(uid,id)assert(uid==s.owner_user_id and id==a.id,'practice_owner_mismatch')end
 local api={get_event=function(id)assert(id==e.id);return copy(e)end,
  get_attempt=function(uid,id)owner(uid,id);return copy(a)end,
  get_player=function(uid,id)assert(uid==s.owner_user_id and id==e.id);return{total_damage=a.actual_damage,qualified=false}end,
  ensure_worker=function()error('practice_has_no_reward_worker')end}
 api.start=function(uid,id,snap)owner(uid,id);a.snapshot=copy(snap);return copy(a)end
 api.commit=function(uid,id,sequence,damage,snap,player_action,reason)
  owner(uid,id);assert(a.status=='bound'and sequence==a.sequence+1,'practice_sequence')
  damage=math.max(0,math.floor(tonumber(damage)or 0))
  local actual=math.min(e.hp,damage);e.hp=e.hp-actual;a.actual_damage=a.actual_damage+actual
  a.sequence=sequence;a.snapshot=copy(snap)
  if reason or e.hp<=0 then a.status='finished';a.end_reason=e.hp<=0 and'boss_defeated'or reason end
  return{event=copy(e),attempt=copy(a),my_damage=a.actual_damage,qualified=false}
 end
 api.finish=function(uid,id,why,snap)
  owner(uid,id);a.status='finished';a.end_reason=why;a.snapshot=copy(snap or a.snapshot)
  return{event=copy(e),attempt=copy(a),my_damage=a.actual_damage,qualified=false}
 end
 s._boss_practice_api=api;return api
end
return M
