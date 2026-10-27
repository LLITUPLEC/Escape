-- Read the actual engine functions, avoiding all economy/RPC/storage setup.
-- Both live battles and calibration use these exact resolve/AI functions.
package.path="Server/nakama/?.lua;"..package.path
local nk={json_encode=function(v)return v end,json_decode=function(v)return v end,
 logger_error=function()end,logger_warn=function()end,logger_info=function()end,
 register_rpc=function()end,register_req_before=function()end,register_req_after=function()end}
package.loaded.nakama=nk
local f=assert(io.open("Server/nakama/modules/duel_match3.lua","r"));local source=f:read("*a");f:close()
local prefix=assert(source:find("local function parse_action",1,true))
local suffix=assert(source:find("local function validate_action_basic",prefix,true))
local finish=assert(source:find("local function match_init",suffix,true))
local code=source:sub(1,prefix-1)..source:sub(suffix,finish-1)..[[
return {CFG=CFG,Runtime=Runtime,new_stats=new_stats,new_board=init_board,ensure_board=ensure_active_board_has_valid_swaps,make_sync=make_sync_msg,
 choose=choose_bot_action,resolve=resolve_action,validate=validate_action_basic,action_cost=action_mana_cost,
 tick_cooldowns=tick_cooldowns,tick_buffs=tick_buffs_end_turn,finish_turn=finish_turn_and_broadcast,
 init_cheat_rows=init_cheat_rows,update_cheat_rows=update_cheat_rows_from_board,
 init_state=function(hero,boss)
  local h,b=new_stats(),new_stats()
  for k,v in pairs(hero)do h[k]=v end;for k,v in pairs(boss)do b[k]=v end
  h.hp=h.max_hp;h.initial_hp=h.max_hp;b.hp=b.max_hp;b.initial_hp=b.max_hp;b.base_heal=0
  local s={mode="pve",started=true,ended=false,owner_user_id="hero",bot_user_id="zz-bot-global",players_sorted={"hero","zz-bot-global"},
   stats={hero=h,["zz-bot-global"]=b},active_user_id="hero",pve_run={affix="",difficulty="easy"},
   global_boss={boss_own_turn=0,next_scream_turn=1,boss_turn_prepared=false,player_turns=0},spawn_queues={},
   cheat_rows=init_cheat_rows(),board=init_board()}
  ensure_active_board_has_valid_swaps(s);update_cheat_rows_from_board(s);return s
 end}
]]
return assert(loadstring(code,"actual-global-boss-engine"))()
