-- Permanent collection bonuses and one-time unlock entitlements, schema 2.
-- Basis points represent fractions of a stat; critical chance is additive.
local M={VERSION=2}
M.fields={"hp_basis_points","armor_basis_points","damage_basis_points","heal_basis_points","crit_basis_points","heal_flat"}
M.catalog={
  dawnwarden={matter=0},
  voidranger={matter=15,hp_basis_points=100},
  stormforged={matter=30,crit_basis_points=100},
  emberoracle={matter=30,damage_basis_points=100},
  frostwraith={matter=50,heal_basis_points=200},
  phoenix={matter=100,hp_basis_points=200,armor_basis_points=200},
  bookmaker={matter=50,hp_basis_points=200},
  railwarden={matter=40,hp_basis_points=200,armor_basis_points=200},
  beastbreaker={matter=300,hp_basis_points=500,armor_basis_points=500,heal_basis_points=500,damage_basis_points=500},
  alchemist={matter=50,tesseract=1,heal_flat=300},
}
function M.validate(id,receipt)
  assert(id~="dawnwarden"and M.catalog[id]and type(receipt)=="table","cosmetics_invalid_reward_receipt")
  if receipt.version==1 then
    assert(receipt.matter==5 and receipt.hp_basis_points==50,"cosmetics_invalid_reward_receipt")
    for _,key in ipairs({"tesseract","armor_basis_points","damage_basis_points","heal_basis_points","crit_basis_points","heal_flat"})do
      assert((receipt[key]or 0)==0,"cosmetics_invalid_reward_receipt")
    end
  else
    assert(receipt.version==M.VERSION,"cosmetics_invalid_reward_receipt")
    for _,key in ipairs({"matter","tesseract","hp_basis_points","armor_basis_points","damage_basis_points","heal_basis_points","crit_basis_points","heal_flat"})do
      assert((receipt[key]or 0)==(M.catalog[id][key]or 0),"cosmetics_invalid_reward_receipt")
    end
  end
  return receipt
end
function M.receipt(id,previous,now)
  local receipt={version=M.VERSION,granted_at=previous and previous.granted_at or now,updated_at=now}
  for key,value in pairs(assert(M.catalog[id]))do receipt[key]=value end
  if previous then receipt.previous_version=previous.version;receipt.previously_paid_matter=previous.matter end
  return receipt
end
function M.empty()
  return {hp_basis_points=0,armor_basis_points=0,damage_basis_points=0,heal_basis_points=0,crit_basis_points=0,heal_flat=0,matter=0,tesseract=0}
end
function M.add(total,receipt)
  for _,key in ipairs(M.fields)do total[key]=total[key]+(receipt[key]or 0)end
  total.matter=total.matter+(receipt.matter or 0);total.tesseract=total.tesseract+(receipt.tesseract or 0)
end
function M.fractions(total)
  return {hp=total.hp_basis_points/10000,armor=total.armor_basis_points/10000,damage=total.damage_basis_points/10000,
    healing=total.heal_basis_points/10000,crit_chance=total.crit_basis_points/10000,healing_flat=total.heal_flat}
end
function M.bonus_text(reward)
  local parts={}
  for _,row in ipairs({{"hp_basis_points","здоровью"},{"armor_basis_points","броне"},{"damage_basis_points","урону"},{"heal_basis_points","лечению"}})do
    if (reward[row[1]]or 0)>0 then parts[#parts+1]="+"..string.format("%g",reward[row[1]]/100).."% к "..row[2] end
  end
  if (reward.crit_basis_points or 0)>0 then parts[#parts+1]="+"..string.format("%g",reward.crit_basis_points/100).." п.п. к шансу крита"end
  if (reward.heal_flat or 0)>0 then parts[#parts+1]="+"..reward.heal_flat.." к лечению"end
  return table.concat(parts,", ").." навсегда"
end
return M
