local nk = require("nakama")

local function try_create(setup, names)
  for _, name in ipairs(names) do
    local ok, match_id_or_err = pcall(nk.match_create, name, setup)
    if ok and match_id_or_err then
      return match_id_or_err
    end
  end
  return nil
end

local function detect_mode(matched_users)
  for _, u in ipairs(matched_users or {}) do
    local p = u.properties
    if p and p.mode ~= nil then
      return tostring(p.mode)
    end
    local sp = u.string_properties
    if sp and sp.mode ~= nil then
      return tostring(sp.mode)
    end
  end
  return ""
end

--- §14 фаза 5: очередь match3 с string_properties.rules = "pro".
local function detect_pvp_pro(matched_users)
  for _, u in ipairs(matched_users or {}) do
    local sp = u.string_properties or u.properties
    if type(sp) == "table" and string.lower(tostring(sp.rules or "")) == "pro" then
      return true
    end
  end
  return false
end

local function on_matchmaker_matched(context, matched_users)
  local mode = detect_mode(matched_users)
  local setup = { invited = matched_users, mode = "pvp" }

  if mode == "match3" then
    if detect_pvp_pro(matched_users) then
      setup.pvp_pro = true
    end
    local match_id = try_create(setup, { "duel_match3", "modules/duel_match3", "modules.duel_match3" })
    if match_id then return match_id end
    error("cannot create match3 authoritative handler (duel_match3)")
  end

  nk.logger_warn("duel_matchmaker: unsupported mode='" .. tostring(mode) .. "' (only match3 is supported)")
  error("unsupported matchmaker mode: " .. tostring(mode))
end

nk.register_matchmaker_matched(on_matchmaker_matched)
