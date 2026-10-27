-- The actual mine recipe drop table, shared by PvE loot and Blacksmith rewards.
local M = {}
local DROPS = {
  {floor=1,slot="Helmet",chance=.50}, {floor=2,slot="Chest",chance=.45},
  {floor=3,slot="Gloves",chance=.40}, {floor=5,slot="WeaponLeft",chance=.35},
  {floor=6,slot="WeaponRight",chance=.30}, {floor=7,slot="Legs",chance=.25},
  {floor=9,slot="Shoulders",chance=.20}, {floor=11,slot="Feet",chance=.15},
}
local DIFFICULTY = {green="easy",blue="medium",purple="hard"}
function M.chance(floor)
  for _,drop in ipairs(DROPS) do if drop.floor == floor then return drop.chance end end
  return 0
end
function M.recipe_id(floor,color,tier)
  if not DIFFICULTY[color] then color="green" end
  tier=math.max(1,math.min(3,math.floor(tonumber(tier) or 1)))
  for _,drop in ipairs(DROPS) do
    if drop.floor == floor then return "recipe_drop_t"..tier.."_"..color.."_"..drop.slot end
  end
  return ""
end
function M.eligible(progress,color)
  local difficulty = assert(DIFFICULTY[color],"invalid_recipe_rarity")
  local mine=type(progress) == "table" and progress.mine or nil
  mine=type(mine) == "table" and mine or {}
  local unlocked=type(mine.unlocked) == "table" and tonumber(mine.unlocked[difficulty]) or (difficulty == "easy" and 1 or 0)
  unlocked=unlocked or 0
  local floors=type(mine.floor_states) == "table" and mine.floor_states or {}
  local out={}
  for _,drop in ipairs(DROPS) do
    local state=floors[difficulty..":"..drop.floor]
    if drop.floor <= unlocked and type(state) == "table" and (tonumber(state.wins) or 0) > 0 then
      out[#out+1]=M.recipe_id(drop.floor,color,1)
    end
  end
  return out
end
return M
