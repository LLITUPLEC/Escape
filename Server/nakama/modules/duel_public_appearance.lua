-- Public render IDs only. Reading another player's outfit must never run unlock
-- reconciliation, grant rewards, finalize identity or expose protected records.
local nk = require("nakama")
local ok, RateLimit = pcall(require, "modules.duel_api_rate_limit")
if not ok then RateLimit = require("duel_api_rate_limit") end
local M = {}
local THEMES = {dawnwarden=true,voidranger=true,stormforged=true,emberoracle=true,frostwraith=true,
  phoenix=true,bookmaker=true,railwarden=true,beastbreaker=true,alchemist=true}
function M.resolve(uid)
  if type(uid) ~= "string" or uid == "" or uid:sub(1,7) == "zz-bot-" then return {avatar_id="",body_id=""} end
  local rows = nk.storage_read({
    {user_id=uid,collection="duel_player_cosmetics",key="appearance_v1"},
    {user_id=uid,collection="duel_account_identity",key="gender_v1"},
  })
  assert(type(rows)=="table", "public_appearance_unavailable")
  local appearance, identity = {}, {}
  for _,row in ipairs(rows) do
    local value = row.value
    if type(value)=="string" then value=nk.json_decode(value) end
    if type(value)=="table" then
      if row.collection=="duel_player_cosmetics" then appearance=value
      elseif row.collection=="duel_account_identity" then identity=value end
    end
  end
  local female = identity.gender=="female"
  local theme = appearance.selected_theme
  if not THEMES[theme] or type(appearance.unlocked)~="table" or appearance.unlocked[theme]~=true then theme="default" end
  local id = theme=="default" and (female and "dawnwarden-female" or "") or theme..(female and "-female" or "-male")
  return {avatar_id=id,body_id=id}
end
local function uuid(value)
  return type(value)=="string" and #value==36 and value:match("^%x%x%x%x%x%x%x%x%-%x%x%x%x%-%x%x%x%x%-%x%x%x%x%-%x%x%x%x%x%x%x%x%x%x%x%x$")~=nil
end
RateLimit.register_rpc(function(ctx,payload)
  if not ctx or type(ctx.user_id)~="string" or ctx.user_id=="" then return nk.json_encode({ok=false,err="unauthorized"}) end
  if type(payload)~="string" or #payload>256 then return nk.json_encode({ok=false,err="bad_payload"}) end
  local parsed,data=pcall(nk.json_decode,payload)
  if not parsed or type(data)~="table" or not uuid(data.user_id) then return nk.json_encode({ok=false,err="bad_payload"}) end
  local found,result=pcall(M.resolve,data.user_id)
  if not found then return nk.json_encode({ok=false,err="public_appearance_unavailable"}) end
  result.ok=true;result.user_id=data.user_id
  return nk.json_encode(result)
end,"duel_public_appearance_get")
return M
