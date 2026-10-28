-- A craft can charge only the ingot price the new client actually displayed.
-- Legacy APKs bake these eight old prices into ItemDefinition assets. Refuse
-- changed epic prices with a readable err (old ErrToRu returns unknown err verbatim).
local M = {}
M.INVALID = "invalid_craft_quote"
M.CHANGED = "craft_quote_changed"
M.LEGACY_UPDATED = "Рецепт обновлён. Для изготовления этой вещи обновите клиент игры."
M.LEGACY_INGOTS = {
  eq_t1_epic_helmet=8, eq_t1_epic_chest=25, eq_t1_epic_gloves=50,
  eq_t1_epic_weapon_l=125, eq_t1_epic_weapon_r=150, eq_t1_epic_legs=180,
  eq_t1_epic_shoulders=220, eq_t1_epic_feet=270,
}
function M.valid_expected(value)
  return value == nil or (type(value)=="number" and value==value and value>=0 and value<=1000000000 and math.floor(value)==value)
end
function M.check(output_id, expected, actual)
  if not M.valid_expected(expected) then return false, M.INVALID end
  if expected ~= nil then
    if expected ~= actual then return false, M.CHANGED end
    return true
  end
  local previous = M.LEGACY_INGOTS[output_id]
  if previous ~= nil and actual ~= previous then return false, M.LEGACY_UPDATED end
  return true
end
return M
