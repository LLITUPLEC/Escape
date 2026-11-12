-- The weekly player aggregate owns score, reward mail and request replay.
-- Only redundant, terminal per-match snapshots can be removed here.
local loaded, Owner = pcall(require, "modules.duel_owner_receipt_retention")
if not loaded then Owner = require("duel_owner_receipt_retention") end
local M = { COLLECTION = "duel_global_boss_attempt" }
local PLAYER = "duel_global_boss_player"

local function terminal(_, key, value, cutoff)
  if value.schema ~= 1 or value.id ~= key or type(value.event_id) ~= "string"
      or not value.event_id:match("^week%-%d+$") or key:sub(1, #value.event_id + 1) ~= value.event_id .. ":"
      or type(value.request_id) ~= "string" or value.request_id == "" then return nil end
  if value.status ~= "finished" and value.status ~= "refunded" then return nil end
  if value.match_id ~= nil then
    if type(value.match_id) ~= "string" or value.match_id == "" then return nil end
  elseif value.status ~= "refunded" or value.bound_at ~= nil or value.snapshot ~= nil
      or value.player_actions ~= 0 or value.sequence ~= 0 then
    return nil -- Only an attempt refunded before match binding lacks a match ID.
  end
  local created = Owner.past(value.created_at, cutoff)
  local finished = Owner.past(value.finished_at, cutoff)
  if created and finished and created <= finished then return finished, created end
end

local function assess_owner(uid, ranked, match_gone)
  local wanted, identities = {}, {}
  for _, entry in ipairs(ranked) do
    local id = entry.value.event_id
    if not wanted[id] then
      wanted[id] = true
      identities[#identities + 1] = { user_id = uid, collection = PLAYER, key = id }
    end
  end
  local rows = Owner.read(identities)
  if not rows then return nil end
  local players = {}
  for _, row in ipairs(rows) do
    local player = Owner.value(row)
    if row.user_id == uid and row.collection == PLAYER and wanted[row.key]
        and player and player.schema == 1 and player.event_id == row.key
        and type(player.requests) == "table" then
      players[row.key] = player
    end
  end
  return function(value)
    local player = players[value.event_id]
    -- Missing attempt history must never reopen a paid entry with this request.
    if not player or player.active_attempt_id == value.id or player.requests[value.request_id] ~= value.id
        or (value.match_id and not match_gone(value.match_id)) then return false end
    return true
  end
end

local function distinct(left, right)
  return (left.event_id ~= right.event_id or left.request_id ~= right.request_id)
    and (not left.match_id or left.match_id ~= right.match_id)
end

function M.new(now, max_owners)
  return Owner.new(now, max_owners, {
    collection = M.COLLECTION, terminal = terminal, assess = assess_owner, distinct = distinct,
  })
end

return M
