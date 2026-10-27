"""Offline deterministic calibration against the shipping Lua board resolver and AI.

No player data, network, rewards or Storage writes. The reference loadout uses
level12, the eight unmodified epic pieces and two claimed ranks outside DNN.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import statistics
import sys
import time

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / '.cache/lua-test-runtime'))
from lupa.lua51 import LuaRuntime

P = argparse.ArgumentParser()
P.add_argument('--seeds', type=int, default=128)
P.add_argument('--damage', type=int, default=3500)
P.add_argument('--armor', type=int, default=1000)
P.add_argument('--crit', type=float, default=.05)
P.add_argument('--turns', type=int, default=30)
P.add_argument('--hp', type=int)
P.add_argument('--output', default='.cache/qa/boss-balance.json')
A = P.parse_args()
assert 1 <= A.seeds <= 5000 and A.damage > 0 and A.damage % 500 == 0
lua = LuaRuntime(unpack_returned_tuples=True)
def table(v):
    if isinstance(v, dict): return lua.table_from({k: table(x) for k, x in v.items()})
    if isinstance(v, list): return lua.table_from([table(x) for x in v])
    return v
items_path = ROOT / 'Server/nakama/data/duel_match3_item_defs.json'
ach_path = ROOT / 'Server/nakama/data/achievement_catalog.example.json'
items = json.loads(items_path.read_text(encoding='utf-8-sig'))['items']
ach = json.loads(ach_path.read_text(encoding='utf-8-sig'))
lua.globals().ITEMS = table(items)
lua.globals().ACH = table(ach)
lua.globals().BOSS_DAMAGE = A.damage
lua.globals().BOSS_ARMOR = A.armor
lua.globals().BOSS_CRIT = A.crit
lua.globals().TURN_CAP = A.turns
lua.execute(r'''
E=assert(loadfile("Server/nakama/tests/global_boss_engine_fixture.lua"))()
local nk=package.loaded.nakama
nk.storage_read=function(req)
 if req[1].collection==E.CFG.ACHIEVEMENT_DEFS_COLLECTION then return{{value=ACH}}end
 return{}
end
local f=assert(io.open("Server/nakama/modules/duel_match3.lua"));local src=f:read("*a");f:close()
local a=assert(src:find("local SET_BONUS_FULL_EQUIP_STATS",1,true))
local b=assert(src:find("local function character_sheet_payload_arrays",a,true))
local env=setmetatable({CFG=E.CFG,get_merged_item_defs=function()return ITEMS end,
 item_def_is_equipment=function(d)return d and d.kind=="equipment"end},{__index=_G})
local fn=assert(loadstring(src:sub(a,b-1).."\nreturn sum_equipment_bonuses,merge_stats_with_equipment"));setfenv(fn,env)
local sum,merge=fn();local equipment={};local ids={}
local slots={Helmet=1,Shoulders=2,Chest=3,Gloves=4,Legs=5,Feet=6,WeaponLeft=7,WeaponRight=8}
for id,d in pairs(ITEMS)do if d.kind=="equipment"and d.quality=="epic"and d.craft_recipe_id then
 assert(slots[d.slot]and not equipment[slots[d.slot]],"ambiguous_epic_loadout");equipment[slots[d.slot]]=id;ids[#ids+1]=id end end
assert(#equipment==8,"need_eight_epic_slots")
local claimed={};local chains=0
for _,ch in ipairs(ACH.chains)do if ch.category~="dnn"then
 chains=chains+1;for i=0,math.min(1,#ch.steps-1)do claimed[#claimed+1]=ch.id..":"..i end end end
REFERENCE=merge(E.CFG.character_stats_base_for_level(12),sum({equipment=equipment}))
require("modules.duel_match3_combat_bonuses").apply_to_merged_stats(REFERENCE,claimed)
REFERENCE.max_hp=REFERENCE.hp;REFERENCE.initial_hp=REFERENCE.hp
REFERENCE.base_damage=REFERENCE.damage;REFERENCE.base_armor=REFERENCE.armor
REFERENCE.base_crit=REFERENCE.crit_chance;REFERENCE.base_heal=REFERENCE.healing
REFERENCE.equipment=equipment;REFERENCE.claimed=claimed;REFERENCE.chains=chains
RULES=require("modules.duel_global_boss_rules")
function trial(seed)
 math.randomseed(seed)
 local s=E.init_state(REFERENCE,{max_hp=1000000000,base_damage=BOSS_DAMAGE,base_armor=BOSS_ARMOR,base_crit=BOSS_CRIT,base_heal=0})
 local actor,opponent="hero","zz-bot-global";local new_turn=true;local damage=0;local actions=0;local turns=0;local ability_counts={0,0,0,0,0,0}
 while s.stats.hero.hp>0 and actions<400 do
  if new_turn then
   if actor=="zz-bot-global"then RULES.begin_turn(s);if s.stats.hero.hp<=0 then break end
   else turns=turns+1;if turns>TURN_CAP then break end end
  end
  local action=E.choose(s,actor,opponent)
  if not action then break end
  local stats=s.stats[actor];local kind=action.actionType
  if kind>=2 and kind<=6 then
   stats.mana=math.max(0,stats.mana-E.action_cost(s,kind))
   local cd=({[2]="cross_cd",[3]="square_cd",[4]="petard_cd",[5]="shield_cd",[6]="fury_cd"})[kind]
   local amount=({[2]=E.CFG.CROSS_ABILITY_COOLDOWN,[3]=E.CFG.SQUARE_ABILITY_COOLDOWN,[4]=E.CFG.PETARD_ABILITY_COOLDOWN,[5]=E.CFG.SHIELD_ABILITY_COOLDOWN,[6]=E.CFG.FURY_ABILITY_COOLDOWN})[kind]
   stats[cd]=amount
  end
  local before=s.stats["zz-bot-global"].hp
  local ok,err,extra,keep=E.resolve(s,action,actor,opponent)
  assert(ok,tostring(err));actions=actions+1
  if actor=="hero"then damage=damage+math.max(0,before-s.stats["zz-bot-global"].hp);ability_counts[kind]=ability_counts[kind]+1 end
  if s.stats["zz-bot-global"].hp>before then error("boss_healing_detected")end
  if keep then new_turn=false
  elseif extra then new_turn=true
  else E.tick_buffs(s.stats[actor]);actor,opponent=opponent,actor;E.tick_cooldowns(s.stats[actor]);new_turn=true end
  s.active_user_id=actor;E.update_cheat_rows(s)
 end
 return{damage=damage,hero_hp=s.stats.hero.hp,player_turns=math.min(turns,TURN_CAP),boss_turns=s.global_boss.boss_own_turn,actions=actions,abilities=ability_counts}
end
''')
reference = lua.globals().REFERENCE
ref = {k: reference[k] for k in ['hp', 'damage', 'armor', 'healing', 'crit_chance', 'chains']}
ref['equipment'] = [reference.equipment[i] for i in range(1,9)]
ref['claimedRanks'] = len(reference.claimed)
rows=[]
started=time.monotonic()
for seed in range(A.seeds):
    row=lua.globals().trial(313000+seed)
    rows.append({k: row[k] for k in ['damage','hero_hp','player_turns','boss_turns','actions']})
    rows[-1]['abilities']=[row.abilities[i] for i in range(1,7)]
damages=sorted(r['damage'] for r in rows)
mean=statistics.mean(damages)
boss_hp=A.hp or math.ceil(mean*100/50000)*50000
assert boss_hp>0 and boss_hp%50000==0
out={'status':'measured_actual_engine','seeds':A.seeds,'seconds':round(time.monotonic()-started,3),
 'reference':ref,'assumptions':{'level':12,'equipment':'all eight tier1 epic crafted items, unmodified','achievementRanks':2,'excludedCategory':'dnn','aura':False,'elixir':False,'cosmeticHpBonus':False,'playerPolicy':'shipping deterministic legal-move AI; human skill changes the distribution','playerTurnCap':A.turns},
 'boss':{'hp':boss_hp,'damage':A.damage,'armor':A.armor,'crit_chance':A.crit,'healing':0},
 'measured':{'meanDamage':mean,'medianDamage':statistics.median(damages),'p10Damage':damages[int(.1*(len(damages)-1))],'p90Damage':damages[int(.9*(len(damages)-1))],'meanBossHpPercent':mean/boss_hp*100,'meanPlayerTurns':statistics.mean(r['player_turns'] for r in rows),'meanBossTurns':statistics.mean(r['boss_turns'] for r in rows),'maxActions':max(r['actions'] for r in rows)},
 'sourceSha256':{p.as_posix():hashlib.sha256((ROOT/p).read_bytes()).hexdigest() for p in [Path('Server/nakama/modules/duel_match3.lua'),Path('Server/nakama/modules/duel_global_boss_rules.lua'),Path('Server/nakama/tests/global_boss_engine_fixture.lua'),items_path.relative_to(ROOT),ach_path.relative_to(ROOT)]},'trials':rows}
target=ROOT/A.output;target.parent.mkdir(parents=True,exist_ok=True);target.write_text(json.dumps(out,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in out.items() if k!='trials'},ensure_ascii=False))
