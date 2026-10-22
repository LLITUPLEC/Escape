-- Runtime storage writes bypass client request hooks. Reserve game state for
-- server code, including the very first create (permissions alone cannot do it).
local nk = require("nakama")

local function server_collection(collection)
  if type(collection) ~= "string" then return false end
  return string.sub(collection, 1, 5) == "duel_"
      or string.sub(collection, 1, 6) == "arena_"
end

local function guard_objects(payload, field)
  if type(payload) ~= "table" or type(payload[field]) ~= "table" then
    return nil
  end
  for _, object in pairs(payload[field]) do
    if type(object) ~= "table" or type(object.collection) ~= "string"
        or server_collection(object.collection) then
      -- Reject the entire batch, so a protected mutation is never hidden among
      -- unrelated client objects. Do not trust user IDs, versions or permissions.
      return nil
    end
  end
  return payload
end

nk.register_req_before(function(ctx, payload)
  return guard_objects(payload, "objects")
end, "WriteStorageObjects")

nk.register_req_before(function(ctx, payload)
  return guard_objects(payload, "object_ids")
end, "DeleteStorageObjects")

return { is_server_collection = server_collection }
