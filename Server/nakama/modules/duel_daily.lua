-- Server-owned Moscow-day rewards. No client counter, clock, item or user ID
-- is accepted. Receipt, wallet and full-inventory pending loot commit together.
local nk = require("nakama")
local function load(nested, root) local ok, m = pcall(require, nested); if ok then return m end return require(root) end
local CFG = load("modules.duel_match3_config", "duel_match3_config")
local Inventory = load("modules.duel_match3_inventory", "duel_match3_inventory")
local M = { COLLECTION = "duel_daily", KEY = "v1" }
local deps = {}
function M.configure(d) deps = assert(d) end
function M.day(now) return math.floor(((now or os.time()) + 10800) / 86400) end
local function count(n) return math.max(0, math.floor(tonumber(n) or 0)) end
local function reward(gold, ore, matter, id, amount)
  return {gold=gold or 0, ore=ore or 0, matter=matter or 0,
    items=id and {{def_id=id,amount=amount or 1}} or nil}
end
local LOGIN = {
  reward(150,100), reward(0,0,0,"ingot_green",3), reward(0,0,2),
  reward(0,0,0,"elixir_assault",1), reward(300,200),
  {gold=0,ore=0,matter=0,items={{def_id="ingot_blue",amount=3},{def_id="elixir_stone",amount=1}}},
  {gold=0,ore=0,matter=0,items={{def_id="scroll_pve_bounty",amount=1},{def_id="ingot_purple",amount=3},{def_id="elixir_gambit",amount=1},{def_id="global_boss_teleport",amount=1}}},
}
local function quests(day)
  local variant = day % 3
  return {
    {id="mine",label="Победить монстров в шахте",counter="mine_wins",target=3+variant,reward=reward(100,75)},
    {id="abilities",label="Применить способности в завершённых боях",counter="ability_uses",target=4+variant,reward=reward(75,50)},
    {id="pvp",label="Завершить PvP: победить или сделать 3 своих хода",counter="pvp_played",target=2,reward=reward(75,0,0,"ingot_green",2)},
  }
end
local function row(uid)
  local rows = nk.storage_read({{collection=M.COLLECTION,key=M.KEY,user_id=uid}})
  assert(type(rows)=="table", "daily_storage_unavailable")
  local r = rows[1]
  if r then assert(type(r.value)=="table" and r.value.schema==1, "daily_invalid_record") end
  return r and r.value or {schema=1,total_claimed=0,last_login_day=-1,quest_day=-1,quest_claimed={}}, r and r.version or "*"
end
local function stats(uid)
  local rows = nk.storage_read({{collection=CFG.STATS_COLLECTION,key=CFG.STATS_KEY,user_id=uid}})
  assert(type(rows)=="table", "daily_stats_unavailable")
  local v = rows[1] and rows[1].value or {}
  if type(v)=="string" then v=nk.json_decode(v) end
  assert(type(v)=="table", "daily_invalid_stats")
  return v
end
local function snapshot(value, summary, day, now)
  local progress = type(summary.daily)=="table" and summary.daily.day==day and summary.daily or {}
  local claimed = value.quest_day==day and value.quest_claimed or {}
  local all = true
  local rows = quests(day)
  for _, q in ipairs(rows) do
    q.progress=count(progress[q.counter]); q.claimed=claimed[q.id]==true
    q.ready=q.progress>=q.target and not q.claimed; q.counter=nil
    all=all and q.claimed
  end
  return {ok=true,day=day,now=now,reset_at=(day+1)*86400-10800,
    login={next_day=count(value.total_claimed)%7+1,total_claimed=count(value.total_claimed),
      can_claim=day>(tonumber(value.last_login_day) or -1),claimed_today=value.last_login_day==day,rewards=LOGIN},
    quests=rows,completion_reward=reward(0,0,0,"scroll_pve_bounty",1),
    completion_ready=all and value.completion_day~=day,completion_claimed=value.completion_day==day}
end
local function parse(payload, claim)
  local p = payload
  if p==nil or p=="" then p={} end
  if type(p)=="string" then
    if #p>512 then return nil end
    local ok, decoded=pcall(nk.json_decode,p); if not ok then return nil end; p=decoded
  end
  if type(p)~="table" then return nil end
  local allowed=claim and {kind=true,day=true,quest_id=true,session_epoch=true} or {}
  for k in pairs(p) do if not allowed[k] then return nil end end
  return p
end
local function grant(uid, value, version, prize, day, now, summary)
  local progress, pv = deps.read_progress(uid)
  assert(type(progress)=="table", "daily_invalid_progress")
  progress.gold=count(progress.gold)+prize.gold
  progress.ore=count(progress.ore)+prize.ore
  progress.matter=count(progress.matter)+prize.matter
  local receipt={day=day,gold=prize.gold,ore=prize.ore,matter=prize.matter,delivered_items={},pending_items={}}
  local writes={}
  if prize.items then
    local sheet=deps.read_sheet(uid); local defs=deps.get_defs()
    for _, item in ipairs(prize.items) do
      assert(type(defs[item.def_id])=="table", "daily_missing_item:"..item.def_id)
      local r=Inventory.add_or_queue(sheet,item.def_id,item.amount,defs)
      assert(r.delivered+r.pending==item.amount,"daily_inventory_mismatch")
      if r.delivered>0 then receipt.delivered_items[#receipt.delivered_items+1]={def_id=item.def_id,amount=r.delivered} end
      if r.pending>0 then receipt.pending_items[#receipt.pending_items+1]={def_id=item.def_id,amount=r.pending} end
    end
    Inventory.sync_resource_counters(sheet,progress,defs)
    writes[#writes+1]=deps.build_sheet_write(uid,sheet)
  end
  writes[#writes+1]=deps.build_progress_write(uid,progress,pv)
  -- Empty Lua tables encode as objects on Nakama; omit empty optional arrays
  -- so Unity's typed JSON parser receives arrays only when entries exist.
  if #receipt.delivered_items==0 then receipt.delivered_items=nil end
  if #receipt.pending_items==0 then receipt.pending_items=nil end
  value.updated_at=now; value.last_receipt=receipt
  writes[#writes+1]={collection=M.COLLECTION,key=M.KEY,user_id=uid,value=value,version=version,permission_read=0,permission_write=0}
  nk.storage_write(writes)
  local response=snapshot(value,summary,day,now); response.receipt=receipt
  return response
end
local function handle(ctx,payload,claim)
  local uid=ctx and ctx.user_id
  if type(uid)~="string" or uid=="" then return nk.json_encode({ok=false,err="unauthorized"}) end
  local ok,result=pcall(function()
    local p=parse(payload,claim); if not p then return {ok=false,err="bad_payload"} end
    if claim and deps.guard_epoch then
      local allowed,err=deps.guard_epoch(uid,payload)
      if not allowed then return {ok=false,err=err or "session_stale"} end
    end
    local now=os.time(); local today=M.day(now)
    if claim and (type(p.day)~="number" or p.day~=today) then return {ok=false,err="day_changed"} end
    if claim and p.kind~="login" and p.kind~="quest" and p.kind~="completion" then return {ok=false,err="bad_kind"} end
    if claim and (p.kind=="quest" and type(p.quest_id)~="string" or p.kind~="quest" and p.quest_id~=nil) then return {ok=false,err="bad_payload"} end
    for attempt=1,5 do
      local value,version=row(uid); local summary=stats(uid)
      if not claim then return snapshot(value,summary,today,now) end
      if today<(tonumber(value.quest_day) or -1) or today<(tonumber(value.last_login_day) or -1) then return {ok=false,err="clock_regression"} end
      local prize
      if p.kind=="login" then
        if value.last_login_day==today then local r=snapshot(value,summary,today,now);r.already_claimed=true;return r end
        prize=LOGIN[count(value.total_claimed)%7+1]
        value.total_claimed=count(value.total_claimed)+1; value.last_login_day=today
      else
        if value.quest_day~=today then value.quest_day=today;value.quest_claimed={} end
        if p.kind=="quest" then
          local selected
          for _,q in ipairs(quests(today)) do if q.id==p.quest_id then selected=q end end
          if not selected then return {ok=false,err="unknown_quest"} end
          if value.quest_claimed[p.quest_id]==true then local r=snapshot(value,summary,today,now);r.already_claimed=true;return r end
          local daily=type(summary.daily)=="table" and summary.daily.day==today and summary.daily or {}
          if count(daily[selected.counter])<selected.target then return {ok=false,err="quest_incomplete"} end
          prize=selected.reward;value.quest_claimed[p.quest_id]=true
        else
          if value.completion_day==today then local r=snapshot(value,summary,today,now);r.already_claimed=true;return r end
          for _,q in ipairs(quests(today)) do if not value.quest_claimed[q.id] then return {ok=false,err="quests_unclaimed"} end end
          prize=reward(0,0,0,"scroll_pve_bounty",1); value.completion_day=today
        end
      end
      local committed,response=pcall(grant,uid,value,version,prize,today,now,summary)
      if committed then return response end
      if not tostring(response):find("version",1,true) or attempt==5 then error(response) end
    end
  end)
  if not ok then nk.logger_error("duel_daily: "..tostring(result));result={ok=false,err="daily_unavailable"} end
  return nk.json_encode(result)
end
function M.rpc_get(ctx,payload) return handle(ctx,payload,false) end
function M.rpc_claim(ctx,payload) return handle(ctx,payload,true) end
return M
