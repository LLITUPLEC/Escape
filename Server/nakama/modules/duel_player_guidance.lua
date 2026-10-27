-- Presentation eligibility and account-scoped onboarding; never grants gameplay rewards.
local nk = require("nakama")
local function load(nested, root) local ok,m=pcall(require,nested);if ok then return m end;return require(root) end
local CFG=load("modules.duel_match3_config","duel_match3_config")
local Inventory=load("modules.duel_match3_inventory","duel_match3_inventory")
local M={COLLECTION="duel_player_guidance",KEY="v1"}
local deps={}
function M.configure(d) deps=assert(d) end
local function value(row)
  if not row then return {} end
  local v=row.value;if type(v)=="string" then v=nk.json_decode(v) end
  assert(type(v)=="table","guidance_invalid_source");return v
end
local function read(uid)
  local rows=nk.storage_read({{collection=M.COLLECTION,key=M.KEY,user_id=uid}})
  assert(type(rows)=="table","guidance_storage_unavailable")
  local row=rows[1]
  if row then
    local v=value(row)
    assert(v.schema==1 and (v.tutorial=="pending" or v.tutorial=="completed" or v.tutorial=="skipped"),"guidance_invalid_record")
    row.value=v
  end
  return row
end
-- Called only by trusted Authenticate* after-hooks when Nakama says created=true.
-- This write joins the initial identity CAS batch, so an old account never opts in.
function M.new_account_write(uid)
  if read(uid) then return nil end
  return {collection=M.COLLECTION,key=M.KEY,user_id=uid,version="*",permission_read=0,permission_write=0,
    value={schema=1,tutorial="pending",created_at=os.time()}}
end
local function count(n)
  n=tonumber(n);if not n or n~=n or n==math.huge or n==-math.huge then return 0 end
  return math.max(0,math.floor(n))
end
function M.mine_wins(stats,progress)
  local total=count(type(stats.modes)=="table" and type(stats.modes.mine)=="table" and stats.modes.mine.wins)
  local floors,defeated=0,0
  for _,row in pairs(type(progress.mine)=="table" and type(progress.mine.floor_states)=="table" and progress.mine.floor_states or {}) do
    if type(row)=="table" then floors=floors+count(row.wins) end
  end
  for id,n in pairs(type(progress.defeated)=="table" and progress.defeated or {}) do
    if type(id)=="string" and id:match("^mine_%d+$") then defeated=defeated+count(n) end
  end
  return math.max(total,floors,defeated) -- Three views of the same wins, not additive.
end
function M.workshop_unlocked(sheet,defs)
  Inventory.observe_recipes(sheet,defs)
  return sheet.first_recipe_received==true
end
function M.status(uid)
  local rows=nk.storage_read({
    {collection=CFG.STATS_COLLECTION,key=CFG.STATS_KEY,user_id=uid},
    {collection=CFG.PVE_PROGRESS_COLLECTION,key=CFG.PVE_PROGRESS_KEY,user_id=uid},
    {collection=CFG.CHARACTER_SHEET_COLLECTION,key=CFG.CHARACTER_SHEET_KEY,user_id=uid},
  });assert(type(rows)=="table","guidance_sources_unavailable")
  local sources={};for _,r in ipairs(rows) do sources[r.collection.."/"..r.key]=value(r) end
  local stats=sources[CFG.STATS_COLLECTION.."/"..CFG.STATS_KEY] or {}
  local progress=sources[CFG.PVE_PROGRESS_COLLECTION.."/"..CFG.PVE_PROGRESS_KEY] or {}
  local sheet=sources[CFG.CHARACTER_SHEET_COLLECTION.."/"..CFG.CHARACTER_SHEET_KEY] or {}
  local row=read(uid);local wins=M.mine_wins(stats,progress)
  return {ok=true,schema=1,workshop_unlocked=M.workshop_unlocked(sheet,assert(deps.get_defs,"guidance_not_configured")()),
    tournaments_unlocked=wins>=10,mine_wins=wins,tutorial_status=row and row.value.tutorial or "legacy"}
end
local function parse(payload,write)
  if payload==nil or payload=="" then payload={} end
  if type(payload)=="string" then
    if #payload>512 then return nil end
    local ok,p=pcall(nk.json_decode,payload);if not ok then return nil end;payload=p
  end
  if type(payload)~="table" then return nil end
  local allowed=write and {result=true,session_epoch=true} or {}
  for k in pairs(payload) do if not allowed[k] then return nil end end
  if write and payload.result~="completed" and payload.result~="skipped" then return nil end
  return payload
end
local function handler(ctx,payload,write)
  local uid=ctx and ctx.user_id
  if type(uid)~="string" or uid=="" then return nk.json_encode({ok=false,err="unauthorized"}) end
  local p=parse(payload,write);if not p then return nk.json_encode({ok=false,err="bad_payload"}) end
  local ok,result=pcall(function()
    if not write then return M.status(uid) end
    local allowed,err=deps.guard_epoch(uid,payload)
    if not allowed then return {ok=false,err=err} end
    for _=1,5 do
      local row=read(uid);local v=row and row.value or {schema=1}
      -- Completed is monotonic; replaying and skipping later cannot erase it.
      if v.tutorial=="completed" or v.tutorial==p.result then return M.status(uid) end
      v.tutorial=p.result;v.updated_at=os.time()
      if pcall(nk.storage_write,{{collection=M.COLLECTION,key=M.KEY,user_id=uid,value=v,
          version=row and row.version or "*",permission_read=0,permission_write=0}}) then return M.status(uid) end
    end
    return {ok=false,err="storage_conflict"}
  end)
  if not ok then nk.logger_error("player_guidance: "..tostring(result));result={ok=false,err="guidance_unavailable"} end
  return nk.json_encode(result)
end
function M.rpc_get(ctx,payload)return handler(ctx,payload,false)end
function M.rpc_set(ctx,payload)return handler(ctx,payload,true)end
return M
