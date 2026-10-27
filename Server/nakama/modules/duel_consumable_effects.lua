-- Pure fixed server rules. Clients cannot supply multipliers or eligible modes.
local M = { SCHEMA = 1, BOUNTY_SECONDS = 3600 }
M.DEFS = {
  elixir_assault = { damage = 1.20, armor = 0.85, healing = 1, crit = 0 },
  elixir_stone = { damage = 0.90, armor = 1.20, healing = 1, crit = 0 },
  elixir_gambit = { damage = 1, armor = 1, healing = 0.50, crit = 0.10 },
  scroll_pve_bounty = { bounty = 1.50 },
}
function M.is_elixir(id) return M.DEFS[id] ~= nil and id ~= "scroll_pve_bounty" end
function M.eligible(state)
  if type(state) ~= "table" or state.arena_mirror ~= nil or state.arena_pvp_style == true
      or state.pvp_race == true or (state.pve_run and state.pve_run.arena_suppress_all) then return false end
  return state.mode == "pve" or (state.mode == "pvp" and state.pvp_pro == true)
end
function M.normalize(sheet)
  local input = type(sheet.consumables) == "table" and sheet.consumables or {}
  local value = { schema = 1 }
  if input.use_ledger~=nil then
    local ledger=input.use_ledger
    assert(type(ledger)=="table" and type(ledger.last_seq)=="number" and ledger.last_seq>=0 and ledger.last_seq==math.floor(ledger.last_seq)
      and ledger.last_seq<9007199254740991 and type(ledger.receipts)=="table","invalid_consumable_ledger")
    value.use_ledger={schema=1,last_seq=ledger.last_seq,receipts={}}
    for i=1,math.min(4,#ledger.receipts) do value.use_ledger.receipts[i]=ledger.receipts[i] end
  end
  local arm = input.armed
  if type(arm) == "table" and M.is_elixir(arm.item_id) and type(arm.activation_id) == "string" and arm.activation_id ~= "" then
    value.armed = { item_id=arm.item_id, activation_id=arm.activation_id, armed_at=math.max(0,math.floor(tonumber(arm.armed_at) or 0)) }
  end
  local bounty = input.bounty
  if type(bounty) == "table" and type(bounty.activation_id) == "string" and bounty.activation_id ~= "" then
    local started = math.max(0,math.floor(tonumber(bounty.started_at) or 0))
    local ends = math.max(0,math.floor(tonumber(bounty.ends_at) or 0))
    if ends == started + M.BOUNTY_SECONDS then value.bounty = {activation_id=bounty.activation_id,started_at=started,ends_at=ends} end
  end
  sheet.consumables = value
  return value
end
function M.apply(actor, receipt)
  if type(actor) ~= "table" then return end
  local id = receipt and receipt.item_id or ""
  local def = M.is_elixir(id) and M.DEFS[id] or nil
  actor.consumable_id = def and id or ""
  actor.consumable_damage_mult = def and def.damage or 1
  actor.consumable_armor_mult = def and def.armor or 1
  actor.consumable_healing_mult = def and def.healing or 1
  actor.consumable_crit_add = def and def.crit or 0
end
function M.damage(raw, actor) return math.max(0,math.floor((tonumber(raw) or 0)*(tonumber(actor and actor.consumable_damage_mult) or 1)+.5)) end
function M.armor(raw, actor) return math.max(0,math.floor((tonumber(raw) or 0)*(tonumber(actor and actor.consumable_armor_mult) or 1)+.5)) end
function M.healing(raw, actor) return math.max(0,math.floor((tonumber(raw) or 0)*(tonumber(actor and actor.consumable_healing_mult) or 1))) end
function M.crit(raw, actor) return math.max(0,math.min(1,(tonumber(raw) or 0)+(tonumber(actor and actor.consumable_crit_add) or 0))) end
function M.bounty_multiplier(snapshot, now)
  if type(snapshot) ~= "table" then return 1 end
  now = tonumber(now) or 0
  return now >= (tonumber(snapshot.started_at) or math.huge) and now < (tonumber(snapshot.ends_at) or 0) and 1.5 or 1
end
function M.currency_multiplier(snapshot, now, aura_multiplier)
  return math.max(1,tonumber(aura_multiplier) or 1,M.bounty_multiplier(snapshot,now))
end
return M
