-- Transactional storage boundary model, not a game implementation. Every CAS is
-- validated before any row changes; faults may occur before OR after commit.
local M = {}
function M.copy(value)
  if type(value) ~= "table" then return value end
  local out = {}; for k, v in pairs(value) do out[k] = M.copy(v) end; return out
end
function M.new()
  local F = { db = {}, clock = 1000, sequence = 0, writes = {}, matches = {}, read_failure = false,
    fail_before = false, fail_after = false, probe_failure = false }
  function F.id(collection, uid, key) return collection .. ":" .. uid .. ":" .. key end
  function F.put(collection, uid, key, value)
    local id = F.id(collection, uid, key); local before = F.db[id]
    F.db[id] = { collection = collection, key = key, user_id = uid, value = M.copy(value), version = tostring((before and tonumber(before.version) or 0) + 1),permission_read=0,permission_write=0 }
  end
  function F.get(collection, uid, key)
    local row = F.db[F.id(collection, uid, key)]; return row and M.copy(row.value), row and row.version
  end
  F.nk = {
    json_encode = M.copy, json_decode = M.copy,
    uuid_v4 = function() F.sequence = F.sequence + 1; return "token-" .. F.sequence end,
    logger_error = function() end, logger_warn = function() end, logger_info = function() end,
    match_get = function(id) if F.probe_failure then error("cluster probe unavailable") end; return M.copy(F.matches[id]) end,
    match_list = function() if F.probe_failure then error("cluster probe unavailable") end; local out = {}; for _, row in pairs(F.matches) do out[#out + 1] = M.copy(row) end; return out end,
    storage_read = function(keys)
      if F.read_failure then error("database read unavailable") end
      local out = {}; for _, request in ipairs(keys) do
        local row = F.db[F.id(request.collection, request.user_id, request.key)]
        if row then out[#out + 1] = M.copy(row) end
      end; return out
    end,
    storage_write = function(writes)
      F.writes[#F.writes + 1] = M.copy(writes)
      if F.before_write then local callback = F.before_write; F.before_write = nil; callback(writes) end
      if F.fail_before then error("database write unavailable") end
      local seen = {}
      for _, op in ipairs(writes) do
        local id = F.id(op.collection, op.user_id, op.key)
        assert(not seen[id], "duplicate storage operation"); seen[id] = true
        assert(op.permission_write == 0 and (op.permission_read == 0 or op.permission_read == 1), "invalid storage permissions")
        local existing = F.db[id]
        if op.version == "*" then assert(existing == nil, "version check failed")
        else assert(existing ~= nil and existing.version == op.version, "version check failed") end
      end
      local ack={}
      for _, op in ipairs(writes) do F.put(op.collection, op.user_id, op.key, op.value)
        local row=F.db[F.id(op.collection,op.user_id,op.key)];row.permission_read=op.permission_read;row.permission_write=op.permission_write
        ack[#ack+1]={version=row.version}
      end
      if F.fail_after then F.fail_after = false; error("connection lost after commit") end
      return ack
    end,
    storage_delete=function(ids)
      if F.delete_failure then error("delete unavailable") end
      for _,op in ipairs(ids) do local row=F.db[F.id(op.collection,op.user_id,op.key)];assert(row and row.version==op.version,"version check failed") end
      for _,op in ipairs(ids) do F.db[F.id(op.collection,op.user_id,op.key)]=nil end
    end,
  }
  function F.seed(uid, overrides)
    local p = { matter = 100, gold = 100, ore = 100, energy = 100, xp = 0, level = 1, key_items = {} }
    for k, v in pairs(overrides or {}) do p[k] = M.copy(v) end
    F.put("progress", uid, "profile", p)
  end
  function F.read_progress(uid) return F.get("progress", uid, "profile") end
  function F.build_progress(uid, value, version)
    return { collection = "progress", key = "profile", user_id = uid, value = M.copy(value), version = version or "*", permission_read = 1, permission_write = 0 }
  end
  function F.install()
    package.loaded.nakama = F.nk
    for name in pairs(package.loaded) do
      if name:find("duel_match3_journal", 1, true) or name:find("duel_journal_retention",1,true) or name:find("duel_match3_pve_entry", 1, true)
          or name:find("duel_match3_race_entry", 1, true) or name:find("duel_match3_recovery", 1, true) then package.loaded[name] = nil end
    end
  end
  return F
end
return M
