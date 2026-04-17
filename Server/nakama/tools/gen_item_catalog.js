/**
 * Генерирует duel_match3_item_catalog.example.json:
 * 96 экипировок (8 слотов × 4 качества × 3 тира),
 * 72 рецепта дропа (recipe_drop_t{tier}_{color}_{Slot}),
 * 24 золотых рецепта (recipe_gold_t{tier}_{Slot}),
 * материалы + черновые рецепты.
 *
 * Запуск: node Server/nakama/tools/gen_item_catalog.js
 */
const fs = require("fs");
const path = require("path");

const OUT = path.join(__dirname, "..", "data", "duel_match3_item_catalog.example.json");

const SLOTS = [
  { id: "helmet", slot: "Helmet", hp: 480, armor: 40, damage: 0, healing: 0, crit: 0 },
  { id: "shoulders", slot: "Shoulders", hp: 200, armor: 85, damage: 0, healing: 0, crit: 0 },
  { id: "chest", slot: "Chest", hp: 600, armor: 42, damage: 0, healing: 0, crit: 0 },
  { id: "gloves", slot: "Gloves", hp: 180, armor: 0, damage: 40, healing: 35, crit: 0.02 },
  { id: "legs", slot: "Legs", hp: 420, armor: 45, damage: 0, healing: 0, crit: 0 },
  { id: "feet", slot: "Feet", hp: 200, armor: 85, damage: 0, healing: 0, crit: 0 },
  { id: "weapon_l", slot: "WeaponLeft", hp: 220, armor: 50, damage: 100, healing: 0, crit: 0 },
  { id: "weapon_r", slot: "WeaponRight", hp: 200, armor: 30, damage: 260, healing: 0, crit: 0 },
];

const QUAL = ["normal", "rare", "epic", "legendary"];
const QUAL_COLOR = { normal: "green", rare: "blue", epic: "purple", legendary: "gold" };

const TIER_MUL = { 1: 1.0, 2: 1.4, 3: 1.75 };
const QUAL_MUL = { normal: 1.0, rare: 1.45, epic: 1.95, legendary: 2.6 };

/** Синхронно с duel_match3_config WORKSHOP_T*_NORMAL_COST и масштабом по качеству. */
function craftFor(tier, quality) {
  const base =
    tier === 1
      ? { ore: 40, gold: 20, ingot_def: "ingot_green", ingot_n: 3 }
      : tier === 2
        ? { ore: 80, gold: 40, ingot_def: "ingot_green", ingot_n: 6 }
        : { ore: 160, gold: 80, ingot_def: "ingot_green", ingot_n: 9 };
  const qm = Math.pow(QUAL_MUL[quality], 1.1);
  let ore = Math.round(base.ore * qm);
  let gold = Math.round(base.gold * qm);
  let ingot_n = Math.max(1, Math.round(base.ingot_n * qm));
  let ingot_def = base.ingot_def;
  if (quality === "rare") ingot_def = "ingot_blue";
  if (quality === "epic") ingot_def = "ingot_purple";
  if (quality === "legendary") {
    ingot_def = "";
    ingot_n = 0;
    ore = Math.round(ore * 1.15);
    gold = Math.round(gold * 1.15);
  }
  return {
    craft_ore: ore,
    craft_gold: gold,
    craft_ingot_def: ingot_def,
    craft_ingot_n: ingot_n,
    craft_tesseract_n: quality === "legendary" ? 1 : 0,
  };
}

function recipeDropId(tier, color, slotName) {
  return `recipe_drop_t${tier}_${color}_${slotName}`;
}

function goldRecipeId(tier, slotName) {
  return `recipe_gold_t${tier}_${slotName}`;
}

function roundStat(s) {
  return Math.round(s * 1000) / 1000;
}

function scaleBase(tier, quality, row) {
  const tm = TIER_MUL[tier];
  const qm = QUAL_MUL[quality];
  const m = tm * qm;
  return {
    hp: roundStat(row.hp * m),
    armor: roundStat(row.armor * m),
    damage: roundStat(row.damage * m),
    healing: roundStat(row.healing * m),
    crit_chance: row.crit > 0 ? roundStat(Math.min(0.25, row.crit * m)) : 0,
  };
}

function buildEquipmentId(tier, quality, shortId) {
  return `eq_t${tier}_${quality}_${shortId}`;
}

function craftRecipeForEq(tier, quality, slotName) {
  if (quality === "legendary") return goldRecipeId(tier, slotName);
  const color = ({ normal: "green", rare: "blue", epic: "purple" })[quality];
  return recipeDropId(tier, color, slotName);
}

const items = {
  version: 2,
  items: {},
};

function addItem(id, def) {
  items.items[id] = def;
}

// --- legacy demo
addItem("helm_rusty", { kind: "equipment", tier: 1, quality: "normal", slot: "Helmet", hp: 30, armor: 10, healing: 10, damage: 0, crit_chance: 0.2 });
addItem("boots_basic", { kind: "equipment", tier: 1, quality: "normal", slot: "Feet", hp: 0, armor: 2, damage: 0, healing: 0, crit_chance: 0 });
addItem("sword_basic", { kind: "equipment", tier: 1, quality: "normal", slot: "WeaponRight", hp: 0, armor: 0, damage: 35, healing: 0, crit_chance: 0 });
addItem("gloves_basic", { kind: "equipment", tier: 1, quality: "normal", slot: "Gloves", hp: 0, armor: 0, damage: 0, healing: 3, crit_chance: 0.3 });

// materials
addItem("ingot_green", { kind: "material", tier: 1, quality: "normal", max_stack: 100 });
addItem("ingot_blue", { kind: "material", tier: 1, quality: "rare", max_stack: 100 });
addItem("ingot_purple", { kind: "material", tier: 1, quality: "epic", max_stack: 100 });
addItem("tesseract", { kind: "tesseract", tier: 1, quality: "legendary", max_stack: 5 });

// legacy generic recipe ids (награды/боты)
["recipe_green", "recipe_blue", "recipe_purple", "recipe_gold"].forEach((rid) => {
  const q = rid.replace("recipe_", "");
  const quality = q === "green" ? "normal" : q === "blue" ? "rare" : q === "purple" ? "epic" : "legendary";
  addItem(rid, { kind: "recipe", tier: 1, quality, max_stack: 1, recipe_slot: "Helmet" });
});

// 96 equipment + 72 mine recipe + 24 gold recipe
for (let tier = 1; tier <= 3; tier++) {
  for (const q of QUAL) {
    for (const row of SLOTS) {
      const eqId = buildEquipmentId(tier, q, row.id);
      const st = scaleBase(tier, q, row);
      const cr = craftRecipeForEq(tier, q, row.slot);
      const craft = craftFor(tier, q);
      const eq = {
        kind: "equipment",
        tier,
        quality: q,
        slot: row.slot,
        craft_recipe_id: cr,
        hp: st.hp,
        armor: st.armor,
        damage: st.damage,
        healing: st.healing,
        crit_chance: st.crit_chance,
        ...craft,
      };
      addItem(eqId, eq);
    }
  }
}

const colq = { green: "normal", blue: "rare", purple: "epic" };
const slotsA = ["Helmet", "Chest", "Gloves", "WeaponLeft"];
const slotsB = ["WeaponRight", "Legs", "Shoulders", "Feet"];

for (let t = 1; t <= 3; t++) {
  for (const cname of Object.keys(colq)) {
    const qual = colq[cname];
    for (const s of [...slotsA, ...slotsB]) {
      const id = recipeDropId(t, cname, s);
      addItem(id, {
        kind: "recipe",
        tier: t,
        quality: qual,
        max_stack: 1,
        recipe_slot: s,
        recipe_target_slot: s,
      });
    }
  }
}

for (let t = 1; t <= 3; t++) {
  for (const row of SLOTS) {
    const id = goldRecipeId(t, row.slot);
    addItem(id, {
      kind: "recipe",
      tier: t,
      quality: "legendary",
      max_stack: 1,
      recipe_slot: row.slot,
      recipe_target_slot: row.slot,
    });
  }
}

// Старые id recipe_drop_{color}_{Slot} без тира — для совместимости с сохранениями
for (const cname of Object.keys(colq)) {
  const qual = colq[cname];
  for (const s of [...slotsA, ...slotsB]) {
    const id = `recipe_drop_${cname}_${s}`;
    if (!items.items[id]) {
      addItem(id, {
        kind: "recipe",
        tier: 1,
        quality: qual,
        max_stack: 1,
        recipe_slot: s,
        recipe_target_slot: s,
      });
    }
  }
}

// Старые id recipe_t2/t3_green_Helmet
addItem("recipe_t2_green_Helmet", items.items[recipeDropId(2, "green", "Helmet")]);
addItem("recipe_t3_green_Helmet", items.items[recipeDropId(3, "green", "Helmet")]);

fs.writeFileSync(OUT, JSON.stringify(items, null, 2), "utf8");
console.log("Wrote", OUT, "items:", Object.keys(items.items).length);
