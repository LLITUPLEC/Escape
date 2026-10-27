-- Exercises the same ordinary resolve/validation functions used by live fights.
local E=dofile('Server/nakama/tests/global_boss_engine_fixture.lua')
local function state()
 return E.init_state({max_hp=25000,base_damage=2022,base_armor=1458,base_crit=.076,base_heal=932},
  {max_hp=550000,base_damage=3500,base_armor=1000,base_crit=.05,base_heal=999})
end
for action_type=2,6 do
 local s=state();s.active_user_id=s.bot_user_id;s.stats[s.bot_user_id].mana=100
 local action={actionType=action_type,cx=2,cy=2}
 assert(E.validate(s,s.bot_user_id,action),'boss ordinary ability disabled: '..action_type)
 assert(E.action_cost(s,action_type)>0,'boss ordinary skill must retain its mana cost')
 local ok,err,extra,keep=E.resolve(s,action,s.bot_user_id,s.owner_user_id)
 assert(ok,err);assert((action_type>=4)==(keep==true),'ordinary ability turn semantics changed')
 assert(s.global_boss.boss_own_turn==0,'ordinary skill must not increment the boss counter itself')
 print('PASS boss retains real ordinary skill'..action_type..' with its mana and turn rules')
end
local s=state();local b=s.stats[s.bot_user_id];b.hp=500000;b.base_heal=999;b.shield_t1=3
-- Controlled legal capsule match, with a real cascade and intentionally nonzero
-- healing/shield stats to catch any accidental restoration in the core engine.
for y=0,E.CFG.HEIGHT-1 do for x=0,E.CFG.SIZE-1 do s.board[y*E.CFG.SIZE+x+1]=(x+2*y)%5+1 end end
local y=E.CFG.ACTIVE_Y_MIN
s.board[y*E.CFG.SIZE+1]=5;s.board[y*E.CFG.SIZE+2]=5;s.board[y*E.CFG.SIZE+3]=1
s.board[(y+1)*E.CFG.SIZE+3]=5
local ok,err=E.resolve(s,{actionType=1,fromX=2,fromY=0,toX=2,toY=1},s.bot_user_id,s.owner_user_id)
assert(ok,err);assert(b.hp==500000,'boss capsules or shield flat bonus restored health')
print('PASS boss capsule match cannot heal even with nonzero base heal and active shield')
