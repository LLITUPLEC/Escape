-- Account gender is immutable server-owned identity, not client metadata/session vars.
-- Only freshly created device accounts have a pending registration choice. Existing
-- accounts without this record are permanently male, including pre-upgrade guests.
local nk = require("nakama")
local rate_ok, RateLimit = pcall(require, "modules.duel_api_rate_limit")
if not rate_ok then RateLimit = require("duel_api_rate_limit") end
local M = {}
local guidance_ok, Guidance = pcall(require, "modules.duel_player_guidance")
if not guidance_ok then Guidance = require("duel_player_guidance") end
local COLLECTION, KEY = "duel_account_identity", "gender_v1"

local function valid(value) return value == "male" or value == "female" end
local function read(uid)
  local rows = nk.storage_read({ { collection = COLLECTION, key = KEY, user_id = uid } })
  local row = rows and rows[1]
  if row and (type(row.value) ~= "table" or row.value.version ~= 1 or not valid(row.value.gender) or type(row.value.finalized) ~= "boolean") then
    error("account_gender_invalid_record")
  end
  return row
end
local function write(uid, gender, finalized, row, newly_created)
  local writes = { { collection = COLLECTION, key = KEY, user_id = uid,
    value = { version = 1, gender = gender, finalized = finalized },
    version = row and row.version or "*", permission_read = 0, permission_write = 0 } }
  if newly_created and not row then local marker=Guidance.new_account_write(uid);if marker then writes[#writes+1]=marker end end
  nk.storage_write(writes)
end
local function has_email(uid)
  local account = nk.account_get_id(uid)
  if not account then error("account_gender_missing_account") end
  return type(account.email) == "string" and account.email ~= ""
end
local function identity(uid, pending_new_device, new_email_gender, newly_created)
  for _ = 1, 4 do
    local row = read(uid)
    if row then
      if not row.value.finalized and has_email(uid) then
        -- Linking commits the last server-staged registration choice. Older
        -- clients never stage one, so their initial male default is committed.
        if pcall(write, uid, row.value.gender, true, row) then return { gender = row.value.gender, finalized = true } end
      else return row.value end
    else
      local gender = valid(new_email_gender) and new_email_gender or "male"
      local finalized = not pending_new_device
      if pcall(write, uid, gender, finalized, nil, newly_created) then return { gender = gender, finalized = finalized } end
    end
  end
  error("account_gender_storage_conflict")
end

-- Shared by the existing AuthenticateEmail after-hook, so it is registered once.
function M.after_email(ctx, session, request)
  local uid = ctx and ctx.user_id
  if type(uid) ~= "string" or uid == "" then return end
  local vars = request and request.account and request.account.vars
  local choice = session and session.created == true and type(vars) == "table" and vars.gender or nil
  identity(uid, false, choice, session and session.created == true)
end

-- Nakama Lua after-hooks receive (context, outgoing response, incoming request).
-- Logger/runtime arguments in the TypeScript examples do not exist in this ABI.
nk.register_req_after(function(ctx, session, request)
  local uid = ctx and ctx.user_id
  if type(uid) ~= "string" or uid == "" then return end
  identity(uid, session and session.created == true, nil, session and session.created == true)
end, "AuthenticateDevice")

nk.register_req_before(function(ctx, payload)
  local vars = type(payload) == "table" and type(payload.account) == "table" and payload.account.vars
  if type(vars) == "table" and vars.gender ~= nil and not valid(vars.gender) then
    error({ "invalid_account_gender", 3 })
  end
  return payload
end, "AuthenticateEmail")

nk.register_req_after(function(ctx)
  local uid = ctx and ctx.user_id
  if type(uid) == "string" and uid ~= "" then identity(uid, false, nil) end
end, "LinkEmail")

local function reply(value)
  return { ok = true, gender = value.gender, can_choose = not value.finalized }
end

-- Internal server consumers derive identity here; no client field is an authority.
function M.authoritative(uid) return identity(uid, false, nil) end
local function handler(ctx, payload, choose)
  local uid = ctx and ctx.user_id
  if type(uid) ~= "string" or uid == "" then return nk.json_encode({ ok = false, err = "unauthorized" }) end
  local ok, result = pcall(function()
    if not choose then return reply(identity(uid, false, nil)) end
    local data = payload
    if type(payload) == "string" then
      if #payload > 256 then return { ok = false, err = "bad_payload" } end
      local decoded, value = pcall(nk.json_decode, payload)
      if not decoded then return { ok = false, err = "bad_payload" } end
      data = value
    end
    if type(data) ~= "table" or not valid(data.gender) then return { ok = false, err = "invalid_account_gender" } end
    for _ = 1, 4 do
      local row = read(uid)
      if not row then
        -- Never create a pending marker from a player RPC: old accounts cannot
        -- opt into a new-account-only workflow by deleting/recreating state.
        local value = identity(uid, false, nil)
        return { ok = false, err = "account_gender_immutable", gender = value.gender, can_choose = false }
      end
      if row.value.finalized then
        if row.value.gender == data.gender then return reply(row.value) end -- Idempotent retry.
        return { ok = false, err = "account_gender_immutable", gender = row.value.gender, can_choose = false }
      end
      if has_email(uid) then
        local value = identity(uid, false, nil)
        return { ok = false, err = "account_gender_immutable", gender = value.gender, can_choose = false }
      end
      -- This is the registration draft, not an edit of a registered account.
      -- A failed email/password/link attempt can be corrected before successful
      -- registration. LinkEmail's after-hook (or the next read) freezes it.
      if pcall(write, uid, data.gender, false, row) then return reply({ gender = data.gender, finalized = false }) end
    end
    error("account_gender_storage_conflict")
  end)
  if not ok then
    nk.logger_error("duel_account_gender failed: " .. tostring(result))
    return nk.json_encode({ ok = false, err = "account_gender_unavailable" })
  end
  return nk.json_encode(result)
end

RateLimit.register_rpc(function(ctx, payload) return handler(ctx, payload, false) end, "duel_account_gender_get")
RateLimit.register_rpc(function(ctx, payload) return handler(ctx, payload, true) end, "duel_account_gender_register")
return M
