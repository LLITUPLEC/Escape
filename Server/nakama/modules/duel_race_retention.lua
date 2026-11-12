local nk = require("nakama")
local loaded, Owner = pcall(require, "modules.duel_owner_receipt_retention")
if not loaded then Owner = require("duel_owner_receipt_retention") end
local M = { COLLECTION = "duel_race_entry" }
local JOURNAL = "duel_match3_journal"
local PEER_LIMIT = 16

local function nonempty(value)
  return type(value) == "string" and value ~= ""
end

local function participants(uid, value)
  if type(value) ~= "table" or #value < 1 or #value > 2 then return false end
  local seen = {}
  for _, owner in ipairs(value) do
    if not nonempty(owner) or seen[owner] then return false end
    seen[owner] = true
  end
  return seen[uid] == true
end

local function terminal(uid, key, value, cutoff)
  if not nonempty(value.run_id) or key ~= "run:" .. value.run_id or not nonempty(value.receipt_id)
      or not participants(uid, value.participants)
      or (value.match_id ~= nil and not nonempty(value.match_id))
      or (value.remaining_refund ~= nil and (type(value.remaining_refund) ~= "table" or next(value.remaining_refund))) then
    return nil
  end
  if value.journal_tickets ~= nil and type(value.journal_tickets) ~= "table" then return nil end
  local finished
  if value.status == "settled" then
    local intent = value.finish_intent
    -- A closed settlement retains its frozen intent. A bound receipt with
    -- the same intent is still recoverable work and never reaches this branch.
    if intent ~= nil and (type(intent) ~= "table" or intent.run_id ~= value.run_id
        or intent.winner ~= value.winner or intent.is_draw ~= value.is_draw) then return nil end
    finished = Owner.past(value.settled_at, cutoff)
  elseif value.status == "refunded" and value.finish_intent == nil then
    finished = Owner.past(value.refunded_at, cutoff)
  end
  return finished, Owner.past(value.paid_at, cutoff)
end

local function identity(uid, collection, key)
  return { user_id = uid, collection = collection, key = key }
end

local function related(active, value)
  return active and (active.run_id == value.run_id or active.receipt_id == value.receipt_id
    or (value.match_id and active.match_id == value.match_id))
end

local function assess_owner(uid, _, match_gone)
  local rows = Owner.read({
    identity(uid, M.COLLECTION, "active"), identity(uid, JOURNAL, "pending"),
    identity(uid, JOURNAL, "retention_v2"),
  })
  if not rows then return nil end
  local active, pending, opened, open_runs, open_matches = nil, {}, {}, {}, {}
  for _, row in ipairs(rows) do
    local value = Owner.value(row)
    if not value or row.user_id ~= uid then return nil end
    if row.collection == M.COLLECTION and row.key == "active" and nonempty(value.receipt_id) then
      active = value
    elseif row.collection == JOURNAL and row.key == "pending" and type(value.entries) == "table" then
      pending = value.entries
    elseif row.collection == JOURNAL and row.key == "retention_v2" and value.schema == 2 and type(value.open) == "table" then
      for _, entry in pairs(value.open) do
        if type(entry) ~= "table" or not nonempty(entry.id)
            or (entry.origin ~= nil and type(entry.origin) ~= "table") then return nil end
        opened[entry.id] = true
        if entry.origin then
          if nonempty(entry.origin.root_result) then open_runs[entry.origin.root_result] = true end
          if nonempty(entry.origin.match_id) then open_matches[entry.origin.match_id] = true end
        end
      end
    else
      return nil
    end
  end

  local function outstanding(id)
    return nonempty(id) and (pending[id] ~= nil or opened[id] == true)
  end

  local legacy_idle, checked = nil, 0
  local function peer_closed(peer, value)
    if checked >= PEER_LIMIT then return false end
    checked = checked + 1
    local key = "run:" .. value.run_id
    local peers = Owner.read({ identity(peer, M.COLLECTION, "active"), identity(peer, M.COLLECTION, key) })
    if not peers then return false end
    for _, row in ipairs(peers) do
      local other = Owner.value(row)
      if not other or row.user_id ~= peer or row.collection ~= M.COLLECTION then return false end
      if row.key == key then
        if other.run_id ~= value.run_id or other.status ~= value.status then return false end
      elseif row.key ~= "active" or (related(other, value) and other.status ~= value.status) then
        return false
      end
    end
    -- A peer's matching terminal receipt may have been pruned on an earlier
    -- pass. A bound or inconsistent peer is always a reason to keep this one.
    return true
  end

  return function(value)
    local run_id, match_id = value.run_id, value.match_id
    if open_runs[run_id] or outstanding(run_id) or outstanding(run_id .. ":stats")
        or outstanding(run_id .. ":pve") or outstanding(value.journal_id) or outstanding(value.result_journal_id)
        or (match_id and (open_matches[match_id] or outstanding("consumable-bind:" .. match_id))) then return false end
    for id in pairs(value.journal_tickets or {}) do if outstanding(id) then return false end end
    if match_id then
      if not match_gone(match_id) then return false end
    else
      if legacy_idle == nil then
        local ok, matches, err = pcall(nk.match_list, 1, true)
        legacy_idle = ok and err == nil and type(matches) == "table" and next(matches) == nil
      end
      if not legacy_idle then return false end
    end
    local is_active = related(active, value)
    if is_active and active.status ~= value.status then return false end
    for _, peer in ipairs(value.participants) do
      if peer ~= uid and not peer_closed(peer, value) then return false end
    end
    return true, is_active
  end
end

local function distinct(left, right)
  return left.receipt_id ~= right.receipt_id and (not left.match_id or left.match_id ~= right.match_id)
end

function M.new(now, max_owners)
  return Owner.new(now, max_owners, {
    collection = M.COLLECTION, terminal = terminal, assess = assess_owner, distinct = distinct,
  })
end

return M
