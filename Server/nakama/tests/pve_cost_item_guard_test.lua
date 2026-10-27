package.path = "Server/nakama/?.lua;" .. package.path
local p = require("modules.duel_match3_pve_mine_cost")({
  clamp_int = function(n, lo, hi) n = math.floor(tonumber(n) or 0); if lo and n < lo then n = lo end; if hi and n > hi then n = hi end; return n end,
  pve_energy_max_for_user = function() return 100 end,
  empty_key_items = function() return {} end,
})
local progress = { energy = 100, ingots = 999, matter = 20 }
assert(p.amount(progress, "a", "ingots") == 0)
print("PASS stale scalar ingots cannot authorize a cost")
local ok, resource = p.can_afford(progress, "a", {{ resource = "ingots", amount = 1 }})
assert(not ok and resource == "ingots" and p.json_not_enough(progress, "a", resource, 1, 0).err == "unsupported_item_cost")
print("PASS item cost is explicitly unsupported rather than falsely insufficient")
assert(not pcall(p.apply_list, progress, {{ resource = "energy", amount = 15 }, { resource = "ingots", amount = 1 }}))
assert(progress.energy == 100 and progress.ingots == 999)
print("PASS mixed unsupported cost cannot partly mutate earlier scalar resources")
assert(not pcall(p.spend, progress, "ingots", 1) and progress.ingots == 999)
print("PASS direct legacy spend cannot decrement a shadow ingot counter")
assert(p.can_afford(progress, "a", {{ resource = "energy", amount = 15 }, { resource = "matter", amount = 2 }}))
p.apply_list(progress, {{ resource = "energy", amount = 15 }, { resource = "matter", amount = 2 }})
assert(progress.energy == 85 and progress.matter == 18)
print("PASS supported scalar attack costs retain existing behavior")
print("pve_cost_item_guard_test: 5 passed")
