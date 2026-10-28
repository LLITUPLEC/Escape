-- Recovery descriptors preserve tournament identities independently of the client menu snapshot.
package.path = "Server/nakama/modules/?.lua;" .. package.path
local account_reads,bot_reads=0,0
package.preload.nakama=function()return{account_get_id=function(uid)
  account_reads=account_reads+1;return{user={display_name="Current account "..uid}}
end}end
local resume=require("duel_match3_resume").new({bot_profile=function()
  bot_reads=bot_reads+1;return{name="Mine floor one"}
end})
local count=0
local function check(name,fn)fn();count=count+1;print("PASS "..name)end
local function arena(a,b)
  return{mode="pve",match_id="match",owner_user_id="player",bot_user_id="zz-bot-slot",
    bot_name="Bracket bot",players_sorted={"player","zz-bot-slot"},pve_run={floor=1},
    arena_mirror={tournament_id="tournament",round="qf",uid_a=a,uid_b=b,
      display_a=a=="player"and"Bracket player"or"Bracket bot",
      display_b=b=="player"and"Bracket player"or"Bracket bot"}}
end
check("tournament bot on either bracket side retains its bracket name",function()
  for _,s in ipairs({arena("player","zz-bot-slot"),arena("zz-bot-slot","player")})do
    local d=resume.describe(s,"player")
    assert(d.opponent_name=="Bracket bot"and d.opponent_user_id=="zz-bot-slot"and d.opponent_is_bot)
    assert(d.arena_tournament_id=="tournament"and d.mode=="pve")
  end
  assert(bot_reads==0)
end)
check("cold restore keeps the frozen bracket name instead of a changed account nickname",function()
  local s=arena("player","human");s.mode="pvp";s.players_sorted={"player","human"};s.arena_mirror.display_b="Bracket human"
  local d=resume.describe(s,"player");assert(d.opponent_name=="Bracket human"and not d.opponent_is_bot and account_reads==0)
end)
check("tournament bot without mirrored names still uses its launch name",function()
  local s=arena("player","zz-bot-slot");s.arena_mirror.uid_a=nil;s.arena_mirror.uid_b=nil
  assert(resume.describe(s,"player").opponent_name=="Bracket bot"and bot_reads==0)
end)
check("Race bot does not inherit a mine profile",function()
  local s=arena("player","zz-bot-slot");s.arena_mirror=nil;s.pvp_race=true;s.bot_name="Race bot"
  local d=resume.describe(s,"player");assert(d.opponent_name=="Race bot"and d.pvp_race and bot_reads==0)
end)
check("mine still resolves its monster profile",function()
  local s=arena("player","zz-bot-slot");s.arena_mirror=nil;s.bot_name=nil
  assert(resume.describe(s,"player").opponent_name=="Mine floor one"and bot_reads==1)
end)
check("ordinary human duel still resolves the account nickname",function()
  local s={mode="pvp",players_sorted={"player","human"}}
  assert(resume.describe(s,"player").opponent_name=="Current account human"and account_reads==1)
end)
print("match_recovery_identity_test: "..count.." passed")
