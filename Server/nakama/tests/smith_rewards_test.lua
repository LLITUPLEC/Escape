package.path = "Server/nakama/modules/?.lua;" .. package.path
local Smith = require("arena_smith_rewards")
local Inventory = require("duel_match3_inventory")
local MineRecipes = require("duel_mine_recipe_drops")
local defs = {junk={max_stack=1}}
for _,color in ipairs({"green","blue","purple"}) do
  defs["ingot_"..color]={max_stack=200}
  defs["recipe_drop_t1_"..color.."_Helmet"]={kind="recipe",max_stack=1}
end
local checks=0
local function pass(label) checks=checks+1 print("PASS "..label) end
local progress={mine={unlocked={easy=12,medium=12,hard=12},floor_states={
  ["easy:1"]={wins=1},["medium:1"]={wins=1},["hard:1"]={wins=1}}}}
for _,row in ipairs({{"green",10,20,2,10},{"blue",15,30,3,15},{"purple",20,40,4,20}}) do
  local color,entry,champion,loss,extra=row[1],row[2],row[3],row[4],row[5]
  assert(Smith.cost(color).amount == entry and Smith.cost(color).version == 2)
  local sheet={}
  local receipt=Smith.grant(sheet,defs,color,true,progress)
  assert(receipt.items[1].amount == champion and receipt.items[2].def_id == "recipe_drop_t1_"..color.."_Helmet")
  assert(#receipt.pending_items == 0)
  pass(color.." champion receives two entries and a guaranteed useful matching-rarity recipe")
  sheet={learned_recipes={"recipe_drop_t1_"..color.."_Helmet"}}
  receipt=Smith.grant(sheet,defs,color,true,progress)
  assert(#receipt.items == 1 and receipt.items[1].amount == champion+extra)
  pass(color.." complete recipe collection gets only bounded additional same-color ingots")
  sheet={}
  receipt=Smith.grant(sheet,defs,color,false)
  assert(#receipt.items == 1 and receipt.items[1].amount == loss and loss < entry)
  assert(8*entry > champion+extra+7*loss)
  pass(color.." loss compensation is below paid cost and even complete-collection bracket remains an ingot sink")
  sheet={inventory={},inventory_counts={}}
  for i=1,25 do sheet.inventory[i],sheet.inventory_counts[i]="junk",1 end
  receipt=Smith.grant(sheet,defs,color,true,progress)
  assert(#receipt.delivered_items == 0 and receipt.pending_items[1].amount == champion and receipt.pending_items[2].amount == 1)
  sheet.inventory[1],sheet.inventory[2],sheet.inventory_counts[1],sheet.inventory_counts[2]="","",0,0
  Inventory.drain(sheet,defs)
  assert(#sheet.inventory_pending_loot == 0)
  pass(color.." full bag retains the complete reward until two cells are freed")
end
assert(not pcall(Smith.cost,"gold"))
pass("unknown smith tier cannot create a fabricated reward")
defs.recipe_drop_t1_green_Chest={kind="recipe",max_stack=1}
defs.recipe_drop_t1_green_Fabricated={kind="recipe",max_stack=1}
local receipt=Smith.grant({},defs,"green",true,{mine={unlocked={easy=12},floor_states={}}})
assert(#receipt.items==1 and receipt.items[1].amount==30)
pass("opening floors without durable monster wins grants bounded ingot fallback, never global catalog recipes")
receipt=Smith.grant({},defs,"green",true,{mine={unlocked={easy=1},floor_states={["easy:2"]={wins=4}}}})
assert(#receipt.items==1 and receipt.items[1].amount==30)
pass("durable wins on a currently locked floor are excluded")
receipt=Smith.grant({},defs,"green",true,{mine={unlocked={easy=1},floor_states={["easy:1"]={wins=1}}}})
assert(receipt.items[2].def_id=="recipe_drop_t1_green_Helmet")
pass("early defeated floor grants only its actual Helmet drop, never later Chest or fabricated definitions")
receipt=Smith.grant({learned_recipes={"recipe_drop_t1_green_Helmet"}},defs,"green",true,
  {mine={unlocked={easy=12},floor_states={["easy:1"]={wins=1}}}})
assert(#receipt.items==1 and receipt.items[1].amount==30)
pass("learning the only eligible floor recipe falls back even when unknown later-floor recipes exist")
receipt=Smith.grant({},defs,"blue",true,{mine={unlocked={easy=12,medium=12},floor_states={["easy:1"]={wins=1}}}})
assert(#receipt.items==1 and receipt.items[1].amount==45)
pass("easy monster wins cannot authorize a blue recipe from an undefeated medium monster")
receipt=Smith.grant({},defs,"green",true,{mine={unlocked={easy=12},floor_states={
  ["easy:4"]={wins=3},["easy:8"]={wins=3},["easy:10"]={wins=3},["easy:12"]={wins=3}}}})
assert(#receipt.items==1 and receipt.items[1].amount==30)
pass("bosses and floor ten have no recipe drop table and cannot authorize scroll rewards")
receipt=Smith.grant({inventory_pending_loot={{def_id="recipe_drop_t1_green_Helmet",amount=1}}},defs,"green",true,
  {mine={unlocked={easy=1},floor_states={["easy:1"]={wins=1}}}})
assert(#receipt.items==1 and receipt.items[1].amount==30)
pass("pending ownership of the only authorized recipe receives the new full-entry ingot fallback")
assert(MineRecipes.chance(1)==.5 and MineRecipes.chance(11)==.15 and MineRecipes.chance(4)==0 and
  MineRecipes.recipe_id(2,"green",1)=="recipe_drop_t1_green_Chest" and MineRecipes.recipe_id(4,"purple",1)=="")
pass("shared PvE drop table preserves live probabilities and real floor-to-slot mapping")
print("smith_rewards_test: "..checks.." invariants passed")
