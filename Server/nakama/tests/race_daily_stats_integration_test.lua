-- Real Pvp + RaceEntry + StatsApi + Journal, with the shared atomic storage fixture.
package.path="Server/nakama/?.lua;Server/nakama/modules/?.lua;Server/nakama/tests/?.lua;"..package.path
local Fixture=require("storage_fault_fixture")
local passed,now=0,1792886400
local real_time=os.time;os.time=function()return now end
local function test(name,fn)
  local f=Fixture.new();f.install();f.seed("a");f.seed("b")
  package.loaded.duel_match3_stats_api=nil
  package.loaded.duel_mail=nil
  local mail=require("duel_mail")
  local j=require("tests.journal_fixture_admission").wrap(require("duel_match3_journal").new({stats_collection="stats",stats_key="summary"}),f)
  local stats=require("duel_match3_stats_api")({CFG={STATS_COLLECTION="stats",STATS_KEY="summary"},finalization_journal=j,
    Ach={is_human=function(uid)return uid:sub(1,7)~="zz-bot-"end,storage_read_match3_summary_val=function(uid)local v,ver=f.get("stats",uid,"summary");return v or {},ver end}})
  local module=require("duel_match3_race_entry")
  local function build()
    local entry=module.new({read_pve_progress=f.read_progress,build_pve_progress_write=f.build_progress,finalization_journal=j,
      append_level_up_writes=mail.append_level_up_writes})
    local pvp=require("duel_match3_pvp")({nk=f.nk,CFG={RACE_WIN_XP=200,RACE_WIN_MATTER=4,RACE_DRAW_XP=200},
      read_pve_progress=f.read_progress,current_level_from_xp=function(x)return math.floor(x/100)+1 end,
      get_race_entry=function()return entry end,build_match_finish_effects=stats.build_match_finish_effects})
    entry.configure_recovery(pvp.resume_race_result);return entry,pvp
  end
  local entry,pvp=build()
  local function bind(users)
    for _,uid in ipairs(users)do if uid:sub(1,7)~="zz-bot-"then assert(entry.pay(uid,{{resource="matter",amount=2}}))end end
    f.matches.mid={match_id="mid"};assert(entry.claim(users,"run","mid"))
    return {started=true,ended=true,pvp_race=true,mode=users[2]:sub(1,7)=="zz-bot-"and"pve"or"pvp",players_sorted=users,stats_result_id="run",
      _ach_counters={a={["uses.cross"]=2,["uses.shield"]=3},b={["uses.petard"]=1}},_daily_player_actions={a=0,b=3}}
  end
  local ok,err=pcall(fn,f,j,stats,entry,pvp,bind,build)
  assert(ok,name..": "..tostring(err));passed=passed+1;print("PASS "..name)
end
test("human Race settles and immediately delivers the identical daily stats plan once",function(f,j,stats,entry,pvp,bind)
  local s=bind({"a","b"});assert(pvp.apply_game_over_rewards(s,"a",{}));assert(stats.record_match_finish(s,"a",false))
  local a=f.get("stats","a","summary");local b=f.get("stats","b","summary")
  assert(a.played==1 and a.wins==1 and b.played==1 and b.losses==1)
  assert(a.daily.ability_uses==5 and a.daily.pvp_played==1 and b.daily.ability_uses==1 and b.daily.pvp_played==1)
  assert(j.read("a","run:stats").status=="complete")
  assert(pvp.apply_game_over_rewards(s,"a",{}));assert(stats.record_match_finish(s,"a",false));assert(entry.cancel("a"))
  assert(f.get("stats","a","summary").wins==1 and f.get("stats","b","summary").losses==1)
  assert(f.read_progress("a").matter==102 and f.read_progress("b").matter==98)
  local winner=f.read_progress("a").resource_medals
  local loser=f.read_progress("b").resource_medals
  assert(winner["res.matter_earned"]==4 and winner["res.matter_spent"]==2)
  assert(loser["res.matter_earned"]==nil and loser["res.matter_spent"]==2)
end)
test("Race XP and level mail settle once for each participant",function(f,j,stats,entry,pvp,bind)
  f.seed("b",{xp=90,level=1})
  local s=bind({"a","b"})
  assert(pvp.apply_game_over_rewards(s,"a",{}))
  assert(pvp.apply_game_over_rewards(s,"a",{}))
  local mail=require("duel_mail")
  local winner,loser=mail.get("a"),mail.get("b")
  assert(f.read_progress("a").level==3 and f.read_progress("b").level==2)
  assert(winner.unread_count==1 and loser.unread_count==1)
  assert(winner.messages[1].attachments[1].amount==3 and loser.messages[1].attachments[1].amount==1)
end)
for _,won in ipairs({true,false})do
  test("bot Race stats, XP and paid refund survive repeated forfeit: humanwin="..tostring(won),function(f,j,stats,entry,pvp,bind)
    local bot="zz-bot-race";local s=bind({"a",bot});local winner=won and"a"or bot
    assert(pvp.apply_game_over_rewards(s,winner,{}));assert(stats.record_match_finish(s,winner,false))
    assert(pvp.apply_game_over_rewards(s,winner,{}));assert(stats.record_match_finish(s,winner,false));assert(entry.cancel("a"));assert(entry.cancel("a"))
    local a=f.get("stats","a","summary");assert(a.played==1 and a.wins==(won and 1 or 0) and a.losses==(won and 0 or 1))
    assert(a.daily.pvp_played==(won and 1 or 0) and a.daily.ability_uses==5 and not f.get("stats",bot,"summary"))
    assert(f.read_progress("a").matter==100 and f.read_progress("a").xp==(won and 200 or 0))
    assert(f.read_progress("a").resource_medals==nil)
  end)
end
test("human Race draw refunds both stakes without resource progress",function(f,j,stats,entry,pvp,bind)
  local s=bind({"a","b"})
  assert(pvp.apply_race_draw_rewards(s,{}))
  assert(pvp.apply_race_draw_rewards(s,{}))
  for _,uid in ipairs({"a","b"})do
    local p=f.read_progress(uid)
    assert(p.matter==100 and p.resource_medals==nil)
  end
end)
test("restart after frozen intent preserves the original daily plan across midnight",function(f,j,stats,entry,pvp,bind,restart)
  local s=bind({"a","b"});local day=math.floor((now+10800)/86400)
  f.before_write=function()f.fail_after=true end;assert(not pvp.apply_game_over_rewards(s,"a",{}))
  local intent=f.get("duel_race_entry","a","run:run").finish_intent
  assert(intent.spec.finish_effects.a.match_stats.daily_day==day)
  now=now+86400;entry,pvp=restart();f.matches.mid=nil
  assert(entry.recover("a"));assert(entry.recover("b"));assert(j.recover("a",{}));assert(j.recover("b",{}))
  local a=f.get("stats","a","summary");assert(a.wins==1 and a.daily.day==day and a.daily.ability_uses==5)
  assert(f.read_progress("a").matter==102 and f.read_progress("b").matter==98)
end)
test("legacy prepared Race intent delivers original stats without rewriting or inventing daily counters",function(f,j,stats,entry,pvp,bind)
  local s=bind({"a","b"});local old={mode="pvp",against_bot=false,draw_xp=200,loss_xp=10,win_rewards={xp=200,matter=4}}
  assert(entry.prepare_finish(s.players_sorted,"run","a",false,old));assert(pvp.apply_game_over_rewards(s,"a",{}))
  assert(j.read("a","run:stats").plan.match_stats.daily_day==nil)
  assert(stats.record_match_finish(s,"a",false));assert(stats.record_match_finish(s,"a",false))
  local a=f.get("stats","a","summary");assert(a.played==1 and a.wins==1 and a.daily==nil)
  assert(j.read("a","run:stats").plan.match_stats.daily_day==nil and j.read("a","run:stats").status=="complete")
end)
os.time=real_time
print("race daily stats integration: "..passed.." passed")
