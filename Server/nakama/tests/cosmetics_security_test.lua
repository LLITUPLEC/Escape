-- Real cosmetics and achievement-catalog modules; with Lua 5.4 for interleaving.
package.path = "Server/nakama/?.lua;" .. package.path
local function copy(v) if type(v) ~= "table" then return v end local r={} for k,x in pairs(v) do r[k]=copy(x) end return r end
local db, rpcs, identities, revision, fail_read, fail_write, race = {}, {}, {}, 0, false, false, nil
local barrier, ambiguous = false, false
local nk = {
  json_encode=copy,json_decode=function(v) if type(v)=="table" then return v end error("invalid json") end,
  logger_error=function() end,logger_warn=function() end,
  storage_read=function(keys)
    if fail_read then error("storage unavailable") end
    local rows={}; for _,k in ipairs(keys) do local row=db[k.collection.."/"..k.key.."/"..k.user_id]; if row then rows[#rows+1]=copy(row) end end
    if barrier and #keys==4 then coroutine.yield("snapshot") end
    return rows
  end,
  storage_write=function(writes)
    if fail_write then error("write unavailable") end
    if race then local f=race; race=nil; f(writes[1]) end
    local seen={}
    for _,w in ipairs(writes) do
      local key=w.collection.."/"..w.key.."/"..w.user_id
      local old=db[key]
      assert(not seen[key],"duplicate key in atomic batch");seen[key]=true
      assert(w.version=="*" and not old or old and w.version==old.version,"CAS conflict")
      assert((w.collection=="duel_player_cosmetics" and w.permission_read==0 or
        w.collection=="duel_mail" and w.permission_read==0 or
        w.collection=="duel_match3_progress" and w.permission_read==1) and w.permission_write==0)
    end
    for _,w in ipairs(writes) do
      local key=w.collection.."/"..w.key.."/"..w.user_id
      revision=revision+1;db[key]={collection=w.collection,key=w.key,user_id=w.user_id,version=tostring(revision),value=copy(w.value)}
    end
    if ambiguous then ambiguous=false;error("lost commit acknowledgement") end
  end,
  storage_delete=function(keys)for _,k in ipairs(keys)do db[k.collection.."/"..k.key.."/"..k.user_id]=nil end end,
}
package.loaded.nakama=nk
package.loaded["modules.duel_api_rate_limit"]={register_rpc=function(fn,name) rpcs[name]=fn end}
package.loaded["modules.duel_account_gender"]={authoritative=function(uid) return assert(identities[uid],"identity missing") end}
local CFG=require("modules.duel_match3_config")
local Cat=require("modules.duel_match3_achievement_catalog")
local function put(collection,key,uid,val)
  revision=revision+1;db[collection.."/"..key.."/"..uid]={collection=collection,key=key,user_id=uid,version=tostring(revision),value=copy(val)}
end
local function catalog()
  local chains={}
  local function chain(id,key,thresholds)
    local steps={};for _,v in ipairs(thresholds) do steps[#steps+1]={threshold_delta=v} end
    chains[#chains+1]={id=id,counter_key=key,threshold_mode="cumulative_delta",steps=steps}
  end
  chain("obs.line6","matches.line6",{5,25,100,300})
  chain("obs.firework_lover","slaughter.finish_petard_pvp",{5,50,100,500})
  chain("dnn.win_1hp","dnn.win_at_one_hp",{1,5})
  chain("dnn.perfect_bets","dnn.arena_perfect_bets_win",{1,5,25,100})
  chain("sl.bets_won","slaughter.arena_bets_won",{8,40,200,800})
  chain("obs.nail_rusty","pve.kill.mine_2",{25,100,250,500})
  chain("obs.shield","uses.shield",{10,50,250,500})
  put(CFG.ACHIEVEMENT_DEFS_COLLECTION,CFG.ACHIEVEMENT_DEFS_KEY,CFG.ACHIEVEMENT_DEFS_STORAGE_USER_ID,{schema_version=1,chains=chains})
  Cat.invalidate_cache()
end
local Cosmetics=assert(loadfile("Server/nakama/modules/duel_player_cosmetics.lua"))()
local Mail=require("modules.duel_mail")
local get,select=rpcs.duel_player_cosmetics_get,rpcs.duel_player_cosmetics_select
local function init(female)
  db={};identities={u={gender=female and "female" or "male",finalized=true},other={gender="male",finalized=true}};revision=0;fail_read=false;fail_write=false;race=nil;barrier=false;ambiguous=false;catalog()
end
local function stats(value) put(CFG.STATS_COLLECTION,CFG.STATS_KEY,"u",value) end
local function progress(value) put(CFG.PVE_PROGRESS_COLLECTION,CFG.PVE_PROGRESS_KEY,"u",value) end
local function sheet(value) put(CFG.CHARACTER_SHEET_COLLECTION,CFG.CHARACTER_SHEET_KEY,"u",value) end
local function row() return db["duel_player_cosmetics/appearance_v1/u"] end
local function theme(reply,id) for _,t in ipairs(reply.themes or {}) do if t.id==id then return t end end error("theme missing: "..id) end
local passed=0
local function test(name,fn) init(false);fn();passed=passed+1;print("PASS "..name) end

test("authenticated caller only and strict payload never accepts client progress",function()
  assert(not get(nil,{}).ok and not select({},{}).ok)
  for _,payload in ipairs({{theme_id="phoenix",unlocked=true},{theme_id="phoenix",user_id="other"},{theme_id="phoenix",gender="female"},{theme_id="phoenix",avatar_id="phoenix-female"}}) do
    assert(select({user_id="u"},payload).err=="bad_payload")
  end
  assert(not get({user_id="u"},{wins=999999,legacy_theme_id="phoenix"}).ok)
  assert(not select({user_id="u"},string.rep("x",257)).ok and not row())
end)
test("male original and first paired starter remain free while nine prestige skins have requirements",function()
  local r=get({user_id="u"},{})
  assert(r.ok and r.gender=="male" and r.selected_theme=="default" and #r.themes==10)
  for _,t in ipairs(r.themes) do
    if t.id=="dawnwarden" then assert(t.unlocked and t.requirements==nil)
    else assert(not t.unlocked and #t.requirements>=1 and #t.requirements<=3) end
  end
  assert(select({user_id="u"},{theme_id="default"}).ok)
end)
test("female starter is the first paired skin and all others stay locked",function()
  identities.u.gender="female";local r=get({user_id="u"},{})
  assert(r.ok and theme(r,"dawnwarden").unlocked and theme(r,"dawnwarden").requirements==nil)
  assert(select({user_id="u"},{theme_id="dawnwarden"}).ok)
  assert(select({user_id="u"},{theme_id="bookmaker"}).err=="appearance_locked")
end)
test("pending registration cannot read or save cosmetic authority",function()
  identities.u.finalized=false
  assert(get({user_id="u"},{}).err=="registration_incomplete" and select({user_id="u"},{theme_id="default"}).err=="registration_incomplete")
  assert(not row())
end)
test("locked skin selection cannot bypass requirements or overwrite current choice",function()
  get({user_id="u"},{})
  local version=row().version
  assert(select({user_id="u"},{theme_id="beastbreaker"}).err=="appearance_locked")
  assert(row().value.selected_theme=="default" and row().version==version)
  assert(select({user_id="u"},{theme_id="phoenix-female"}).err=="unknown_appearance")
end)
test("retroactive durable mine progress unlocks and selection persists across sessions",function()
  progress({defeated={mine_1=30,mine_2=30},mine={floor_states={["easy:4"]={wins=1}}}})
  local r=get({user_id="u"},{})
  assert(theme(r,"dawnwarden").unlocked and theme(r,"voidranger").unlocked)
  assert(select({user_id="u"},{theme_id="voidranger"}).ok)
  assert(get({user_id="u",session_id="second-device"},{}).selected_theme=="voidranger")
end)
test("mine totals use max durable views rather than double count",function()
  progress({defeated={mine_1=6},mine={floor_states={["easy:1"]={wins=6}}}})
  local r=get({user_id="u"},{})
  assert(not theme(r,"voidranger").unlocked and theme(r,"voidranger").requirements[1].current==6)
end)
test("achievement ranks use cumulative catalog thresholds without reward claims",function()
  stats({achievement_stats={["slaughter.finish_petard_pvp"]=154,["slaughter.tournament_smith_final"]=1}})
  assert(not theme(get({user_id="u"},{}),"emberoracle").unlocked)
  stats({achievement_stats={["slaughter.finish_petard_pvp"]=155,["slaughter.tournament_smith_final"]=1}})
  assert(theme(get({user_id="u"},{}),"emberoracle").unlocked)
end)
test("Phoenix counts server PvP wins not played losses or mine wins",function()
  stats({played=99999,wins=99999,modes={mine={wins=99999},duel={wins=24}},achievement_stats={["dnn.win_at_one_hp"]=1}})
  assert(not theme(get({user_id="u"},{}),"phoenix").unlocked)
  stats({modes={duel={wins=24},pro={wins=1}},achievement_stats={["dnn.win_at_one_hp"]=1}})
  assert(theme(get({user_id="u"},{}),"phoenix").unlocked)
end)
test("bookmaker requires both actual Tote genius and second won-bets rank",function()
  stats({achievement_stats={["dnn.arena_perfect_bets_win"]=1,["slaughter.arena_bets_won"]=47}})
  assert(not theme(get({user_id="u"},{}),"bookmaker").unlocked)
  stats({achievement_stats={["dnn.arena_perfect_bets_win"]=1,["slaughter.arena_bets_won"]=48}})
  assert(theme(get({user_id="u"},{}),"bookmaker").unlocked)
end)
test("learned recipe collection counts unique server IDs only",function()
  local recipes={};for i=1,15 do recipes[#recipes+1]="recipe_"..i end;recipes[#recipes+1]="recipe_1"
  sheet({learned_recipes=recipes});stats({achievement_stats={["uses.shield"]=60}});progress({mine={floor_states={["medium:12"]={wins=1}}}})
  assert(not theme(get({user_id="u"},{}),"alchemist").unlocked)
  recipes[#recipes+1]="recipe_16";sheet({learned_recipes=recipes})
  assert(theme(get({user_id="u"},{}),"alchemist").unlocked)
end)
test("earned unlock is monotonic and old local hints cannot replace server choice",function()
  progress({defeated={mine_1=60},mine={floor_states={["easy:4"]={wins=1}}}})
  assert(get({user_id="u"},{legacy_theme_id="voidranger"}).selected_theme=="voidranger")
  progress({})
  local r=get({user_id="u"},{legacy_theme_id="dawnwarden"})
  assert(r.selected_theme=="voidranger" and theme(r,"voidranger").unlocked)
end)
test("locked legacy skin cannot migrate itself into authority",function()
  assert(get({user_id="u"},{legacy_theme_id="phoenix"}).selected_theme=="default")
end)
test("storage read and write failures fail closed",function()
  fail_read=true;assert(get({user_id="u"},{}).err=="cosmetics_unavailable");fail_read=false
  fail_write=true;assert(get({user_id="u"},{}).err=="cosmetics_unavailable");fail_write=false
  assert(not row())
end)
test("CAS retry preserves concurrent unlocked history and then commits selection",function()
  progress({defeated={mine_1=20}});get({user_id="u"},{})
  race=function(w) local old=row();old.value.unlocked.phoenix=true;revision=revision+1;old.version=tostring(revision) end
  local r=select({user_id="u"},{theme_id="dawnwarden"})
  assert(r.ok and r.selected_theme=="dawnwarden" and theme(r,"phoenix").unlocked)
end)
test("default restoration never changes gender and avatar payload is rejected",function()
  identities.u.gender="female";get({user_id="u"},{})
  assert(select({user_id="u"},{theme_id="default"}).gender=="female")
  assert(select({user_id="u"},{theme_id="dawnwarden",avatar_id="voidranger-female"}).err=="bad_payload")
end)

local function matter(uid)
  local record=db[CFG.PVE_PROGRESS_COLLECTION.."/"..CFG.PVE_PROGRESS_KEY.."/"..(uid or "u")]
  return record and record.value.matter or 0
end
local function old_unlocks(ids)
  local unlocked={dawnwarden=true};for _,id in ipairs(ids) do unlocked[id]=true end
  put("duel_player_cosmetics","appearance_v1","u",{schema_version=1,selected_theme="default",unlocked=unlocked})
end
test("old prestige unlocks retroactively pay five matter once and passive HP survives selection changes",function()
  progress({matter=7});old_unlocks({"phoenix","voidranger"})
  local r=get({user_id="u"},{})
  assert(r.ok and r.unlock_reward_matter_granted==10 and r.rewarded_matter_total==10 and r.passive_hp_bonus_percent==1 and matter()==17)
  assert(theme(r,"phoenix").reward_granted and theme(r,"phoenix").unlock_reward_matter==5)
  for _,id in ipairs({"phoenix","default","voidranger","dawnwarden"}) do assert(select({user_id="u"},{theme_id=id}).ok) end
  assert(get({user_id="u"},{}).unlock_reward_matter_granted==0 and matter()==17 and Cosmetics.passive_hp_bonus("u")==.01)
end)
test("starter and default grant no matter or passive HP",function()
  local r=get({user_id="u"},{})
  assert(r.ok and r.unlocked_prestige_count==0 and r.passive_hp_bonus_percent==0 and matter()==0)
  assert(not theme(r,"dawnwarden").reward_granted and theme(r,"dawnwarden").unlock_reward_matter==0)
end)
test("all nine prestige rewards cap at forty-five matter and four point five percent HP",function()
  old_unlocks({"voidranger","stormforged","emberoracle","frostwraith","phoenix","bookmaker","railwarden","beastbreaker","alchemist"})
  local r=Cosmetics.reconcile("u")
  assert(r.ok and r.unlocked_prestige_count==9 and r.passive_hp_bonus_percent==4.5 and matter()==45)
  assert(Cosmetics.passive_hp_bonus("u")==.045 and matter()==45)
end)
test("reward outage changes neither wallet nor unlock receipt",function()
  old_unlocks({"phoenix"});progress({matter=9});fail_write=true
  assert(get({user_id="u"},{}).err=="cosmetics_unavailable" and matter()==9 and not row().value.unlock_reward_receipts)
  fail_write=false;assert(get({user_id="u"},{}).ok and matter()==14)
end)
test("lost acknowledgement observes committed receipt and never repays",function()
  old_unlocks({"phoenix"});ambiguous=true
  local r=get({user_id="u"},{})
  assert(r.ok and matter()==5 and r.unlock_reward_matter_granted==0 and theme(r,"phoenix").reward_granted)
end)
test("progress CAS conflict preserves unrelated currency and retries one reward",function()
  old_unlocks({"phoenix"});progress({matter=3,gold=8})
  race=function() progress({matter=23,gold=99}) end
  assert(get({user_id="u"},{}).ok and matter()==28)
  local p=db[CFG.PVE_PROGRESS_COLLECTION.."/"..CFG.PVE_PROGRESS_KEY.."/u"].value
  assert(p.gold==99)
end)
test("concurrent reconciliation snapshots award only one account-scoped receipt",function()
  old_unlocks({"phoenix"});progress({matter=11})
  barrier=true
  local a=coroutine.create(function() assert(get({user_id="u"},{}).ok) end)
  local b=coroutine.create(function() assert(Cosmetics.passive_hp_bonus("u")==.005) end)
  local ok,x=coroutine.resume(a);assert(ok and x=="snapshot")
  ok,x=coroutine.resume(b);assert(ok and x=="snapshot");barrier=false
  assert(coroutine.resume(a));assert(coroutine.resume(b))
  assert(coroutine.status(a)=="dead" and coroutine.status(b)=="dead" and matter()==16)
  assert(get({user_id="other"},{}).unlocked_prestige_count==0 and matter("other")==0)
end)
test("receipt permanence retains HP when source progress is lowered",function()
  old_unlocks({"phoenix"});get({user_id="u"},{})
  local saved=row().value;saved.unlocked.phoenix=nil
  put("duel_player_cosmetics","appearance_v1","u",saved)
  assert(theme(get({user_id="u"},{}),"phoenix").unlocked and Cosmetics.passive_hp_bonus("u")==.005 and matter()==5)
end)

local function earn_scout()
  progress({matter=4,defeated={mine_1=50},mine={floor_states={["easy:4"]={wins=1}}}})
end
test("old collection baselines silently while new unlock commits one named informational letter with its reward",function()
  old_unlocks({"phoenix"});assert(get({user_id="u"},{}).ok)
  assert(Mail.get("u").unread_count==0 and row().value.unlock_mail_receipts.phoenix)
  earn_scout();local r=get({user_id="u"},{})
  local inbox=Mail.get("u");assert(r.ok and r.unlock_reward_matter_granted==5 and matter()==9)
  assert(inbox.unread_count==1 and #inbox.messages==1)
  local m=inbox.messages[1]
  assert(m.id=="appearance_unlock:voidranger" and m.kind=="appearance_unlock" and m.action=="hero" and m.avatar_id=="voidranger-male")
  assert(m.theme_id=="voidranger" and not m.attachments and row().value.unlock_mail_receipts.voidranger)
  for _=1,8 do assert(get({user_id="u"},{}).ok and Cosmetics.passive_hp_bonus("u")==.01)end
  assert(Mail.get("u").unread_count==1 and matter()==9)
end)
test("fresh female unlock uses paired avatar; reading letter changes only mail and compacted history cannot reappear",function()
  identities.u.gender="female";get({user_id="u"},{})
  earn_scout();get({user_id="u"},{})
  assert(Mail.get("u").messages[1].avatar_id=="voidranger-female")
  local wallet=copy(db[CFG.PVE_PROGRESS_COLLECTION.."/"..CFG.PVE_PROGRESS_KEY.."/u"])
  local r=Mail.claim("u","appearance_unlock:voidranger")
  assert(r.ok and r.informational and r.unread_count==0)
  assert(Mail.claim("u","appearance_unlock:voidranger").already_claimed)
  assert(db[CFG.PVE_PROGRESS_COLLECTION.."/"..CFG.PVE_PROGRESS_KEY.."/u"].version==wallet.version)
  for n=1,5 do
    local id="informational:"..n
    assert(Mail.deliver("u",{id=id,title="message",body="text",created_at=os.time()}))
    assert(Mail.claim("u",id).ok)
  end
  assert(not db["duel_mail/appearance_unlock:voidranger/u"])
  for _=1,4 do assert(get({user_id="u"},{}).ok)end
  assert(Mail.get("u").unread_count==0 and not db["duel_mail/appearance_unlock:voidranger/u"])
end)
test("unlock mail outage and lost acknowledgement preserve atomic reward, receipt, inbox and cosmetic availability",function()
  get({user_id="u"},{});earn_scout();fail_write=true
  assert(get({user_id="u"},{}).err=="cosmetics_unavailable")
  assert(not row().value.unlocked.voidranger and matter()==4 and not db["duel_mail/inbox/u"])
  fail_write=false;ambiguous=true
  assert(get({user_id="u"},{}).ok and matter()==9 and Mail.get("u").unread_count==1)
  assert(get({user_id="u"},{}).ok and matter()==9 and Mail.get("u").unread_count==1)
end)
test("concurrent new unlock reconciliation emits one account-scoped notification",function()
  get({user_id="u"},{});earn_scout();barrier=true
  local a=coroutine.create(function()assert(get({user_id="u"},{}).ok)end)
  local b=coroutine.create(function()assert(get({user_id="u"},{}).ok)end)
  local ok,stage=coroutine.resume(a);assert(ok and stage=="snapshot")
  ok,stage=coroutine.resume(b);assert(ok and stage=="snapshot");barrier=false
  assert(coroutine.resume(a));assert(coroutine.resume(b))
  assert(Mail.get("u").unread_count==1 and matter()==9 and Mail.get("other").unread_count==0)
end)
test("concurrent inbox change preserves unrelated mail and custom cosmetic metadata",function()
  get({user_id="u"},{})
  row().value.admin_note={keep="custom"};earn_scout()
  race=function() assert(Mail.deliver("u",{id="other-event",title="event",body="event",created_at=os.time()}))end
  assert(get({user_id="u"},{}).ok)
  local inbox=Mail.get("u");assert(inbox.unread_count==2 and matter()==9 and row().value.admin_note.keep=="custom")
end)

-- A real server response for mobile QA: existing progress unlocks the first
-- expedition skins, while prestige skins show authentic unmet requirements.
local function fixture(female)
  init(female)
  progress({defeated={mine_1=60,mine_2=70},mine={floor_states={["easy:4"]={wins=2},["easy:8"]={wins=1},["easy:12"]={wins=1}}}})
  stats({modes={duel={wins=12},race={wins=7}},achievement_stats={["matches.line6"]=30,["slaughter.finish_petard_pvp"]=12,["slaughter.arena_bets_won"]=10}})
  sheet({learned_recipes={"recipe_a","recipe_b","recipe_c"}})
  return get({user_id="u"},{})
end
COSMETIC_UI_FIXTURE_MALE=fixture(false)
COSMETIC_UI_SELECTED_MALE=select({user_id="u"},{theme_id="voidranger"})
COSMETIC_UI_FIXTURE_FEMALE=fixture(true)
COSMETIC_UI_SELECTED_FEMALE=select({user_id="u"},{theme_id="voidranger"})
COSMETIC_UNLOCK_MAIL_FIXTURE=Mail.get("u")
print("Cosmetics security: "..passed.." passed")
