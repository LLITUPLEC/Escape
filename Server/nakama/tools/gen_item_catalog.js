/**
 * Генерирует duel_match3_item_catalog.example.json:
 * 32 экипировки (8 слотов × 4 качества, только ТИР-1).
 * Рецепты крафта: 24 из шахты (8 зелёных / 8 синих / 8 фиолетовых по слотам) + 8 золотых recipe_gold_{Slot}
 * (выдача с глобальных боссов — отдельный контент; в JSON заготовки для крафта легендарки).
 * Материалы + устаревшие generic recipe_* (совместимость).
 *
 * Бонус полного комплекта (8 вещей, один tier+quality): ×1.25 к hp/damage/armor/healing с экипа
 * на сервере в sum_equipment_bonuses (duel_match3.lua); крит с вещей без этого множителя.
 * Статы экипа калибруются по duel_match3_bots_catalog_{easy,medium,hard}.json:
 * игрок 12 ур., полный комплект, сет ×1.25 — цель: ~1.6× к hp_bonus / base_* у mine_11 «своей» линейки
 * (normal→easy, rare→medium, epic→hard). Legendary — от hard mine_12 с тем же ×1.6.
 * Сырой бюджет по слотам: (цель − база персонажа) / 1.25, распределение пропорционально шаблону SLOTS.
 *
 * Запуск после gen_mine_bots.js: node Server/nakama/tools/gen_item_catalog.js
 * Затем при необходимости синхронизировать ITEM_DEFS_FALLBACK в duel_match3.lua:
 *   node Server/nakama/tools/sync_lua_fallback_eq.js
 */
const fs = require("fs");
const path = require("path");

const OUT = path.join(__dirname, "..", "data", "duel_match3_item_catalog.example.json");

const SLOTS = [
  { id: "helmet", slot: "Helmet", hp: 420, armor: 35, damage: 0, healing: 0, crit: 0 },
  { id: "shoulders", slot: "Shoulders", hp: 175, armor: 72, damage: 0, healing: 0, crit: 0 },
  { id: "chest", slot: "Chest", hp: 520, armor: 36, damage: 0, healing: 0, crit: 0 },
  { id: "gloves", slot: "Gloves", hp: 155, armor: 0, damage: 35, healing: 30, crit: 0.02 },
  { id: "legs", slot: "Legs", hp: 365, armor: 38, damage: 0, healing: 0, crit: 0 },
  { id: "feet", slot: "Feet", hp: 175, armor: 72, damage: 0, healing: 0, crit: 0 },
  { id: "weapon_l", slot: "WeaponLeft", hp: 190, armor: 42, damage: 88, healing: 0, crit: 0 },
  { id: "weapon_r", slot: "WeaponRight", hp: 175, armor: 26, damage: 228, healing: 0, crit: 0 },
];

const QUAL = ["normal", "rare", "epic", "legendary"];
/** Множители только для стоимости крафта (статы экипа калибруются по ботам). */
const QUAL_MUL = { normal: 1.0, rare: 1.45, epic: 1.95, legendary: 2.6 };

const DATA_DIR = path.join(__dirname, "..", "data");

/** База персонажа 12 ур. (duel_match3.lua). */
const PLAYER_BASE_L12 = { hp: 480, damage: 11, armor: 11, healing: 11, crit: 0.06 };
const SET_BONUS_EQ = 1.25;
/** Итог игрока vs монстр: ×1.6 к hp_bonus и base_damage / base_armor / base_heal / base_crit. */
const VS_MONSTER_MUL = 1.6;

function loadBot(diff, id) {
  const p = path.join(DATA_DIR, `duel_match3_bots_catalog_${diff}.json`);
  const j = JSON.parse(fs.readFileSync(p, "utf8"));
  return j.bots[id];
}

function roundStat(s) {
  return Math.round(s * 1000) / 1000;
}

function eqInt(x) {
  return Math.max(0, Math.round(x));
}

function mergedTargetsFromBot(bot) {
  if (!bot) {
    return { hp: 0, damage: 0, armor: 0, healing: 0, crit: 0 };
  }
  const hb = bot.hp_bonus || 0;
  return {
    hp: VS_MONSTER_MUL * hb,
    damage: VS_MONSTER_MUL * (bot.base_damage || 0),
    armor: VS_MONSTER_MUL * (bot.base_armor || 0),
    healing: VS_MONSTER_MUL * (bot.base_heal || 0),
    crit: VS_MONSTER_MUL * (bot.base_crit || 0),
  };
}

/**
 * Разбить целое total на части пропорционально weights (сумма = total), метод наибольших дробных частей.
 */
function distributeIntegerBudget(total, weights) {
  const sumW = weights.reduce((a, b) => a + b, 0);
  if (total <= 0 || sumW <= 0) return weights.map(() => 0);
  const exact = weights.map((w) => (total * w) / sumW);
  const floors = exact.map((x) => Math.floor(x));
  let rem = total - floors.reduce((a, b) => a + b, 0);
  const order = exact
    .map((x, i) => i)
    .sort((i, j) => exact[j] - Math.floor(exact[j]) - (exact[i] - Math.floor(exact[i])));
  for (let k = 0; k < rem; k++) floors[order[k]]++;
  return floors;
}

function slotStatsArrayForBot(refBot) {
  const tgt = mergedTargetsFromBot(refBot);
  const raw = {
    hp: (tgt.hp - PLAYER_BASE_L12.hp) / SET_BONUS_EQ,
    damage: (tgt.damage - PLAYER_BASE_L12.damage) / SET_BONUS_EQ,
    armor: (tgt.armor - PLAYER_BASE_L12.armor) / SET_BONUS_EQ,
    healing: (tgt.healing - PLAYER_BASE_L12.healing) / SET_BONUS_EQ,
    crit: tgt.crit - PLAYER_BASE_L12.crit,
  };
  const hpB = eqInt(Math.round(Math.max(0, raw.hp)));
  const dmgB = eqInt(Math.round(Math.max(0, raw.damage)));
  const armB = eqInt(Math.round(Math.max(0, raw.armor)));
  const healB = eqInt(Math.round(Math.max(0, raw.healing)));
  const hpParts = distributeIntegerBudget(hpB, SLOTS.map((r) => r.hp));
  const dmgParts = distributeIntegerBudget(dmgB, SLOTS.map((r) => r.damage));
  const armParts = distributeIntegerBudget(armB, SLOTS.map((r) => r.armor));
  const healParts = distributeIntegerBudget(healB, SLOTS.map((r) => r.healing));
  const critRaw = Math.max(0, raw.crit);
  const critGloves = critRaw > 0 ? roundStat(Math.min(0.25, critRaw)) : 0;
  return SLOTS.map((row, i) => ({
    hp: hpParts[i],
    armor: armParts[i],
    damage: dmgParts[i],
    healing: healParts[i],
    crit_chance: row.crit > 0 ? critGloves : 0,
  }));
}

function mergedFromSlotStats(arr) {
  const sum = arr.reduce(
    (s, r) => ({
      hp: s.hp + r.hp,
      armor: s.armor + r.armor,
      damage: s.damage + r.damage,
      healing: s.healing + r.healing,
      crit: s.crit + r.crit_chance,
    }),
    { hp: 0, armor: 0, damage: 0, healing: 0, crit: 0 }
  );
  return {
    hp: PLAYER_BASE_L12.hp + sum.hp * SET_BONUS_EQ,
    armor: PLAYER_BASE_L12.armor + sum.armor * SET_BONUS_EQ,
    damage: PLAYER_BASE_L12.damage + sum.damage * SET_BONUS_EQ,
    healing: PLAYER_BASE_L12.healing + sum.healing * SET_BONUS_EQ,
    crit: Math.min(1, PLAYER_BASE_L12.crit + sum.crit),
  };
}

function logEquipVerify(label, refBot, arr) {
  const m = mergedFromSlotStats(arr);
  const t = mergedTargetsFromBot(refBot);
  console.log(
    `${label} | merged hp/dmg/arm/heal/crit=${m.hp.toFixed(0)}/${m.damage.toFixed(1)}/${m.armor.toFixed(1)}/${m.healing.toFixed(1)}/${m.crit.toFixed(4)} | target ${t.hp.toFixed(1)}/${t.damage.toFixed(1)}/${t.armor.toFixed(1)}/${t.healing.toFixed(1)}/${t.crit.toFixed(4)}`
  );
}

/** Базовая стоимость зелёного T1 — упор на фарм (синхронизировать с duel_match3_config WORKSHOP_T1_NORMAL_COST). */
function craftFor(quality) {
  const base = { ore: 120, gold: 80, ingot_def: "ingot_green", ingot_n: 8 };
  const qm = Math.pow(QUAL_MUL[quality], 1.12);
  let ore = Math.round(base.ore * qm);
  let gold = Math.round(base.gold * qm);
  let ingot_n = Math.max(1, Math.round(base.ingot_n * qm));
  let ingot_def = base.ingot_def;
  if (quality === "rare") ingot_def = "ingot_blue";
  if (quality === "epic") ingot_def = "ingot_purple";
  if (quality === "legendary") {
    ingot_def = "";
    ingot_n = 0;
    ore = Math.round(ore * 1.2);
    gold = Math.round(gold * 1.2);
  }
  return {
    craft_ore: ore,
    craft_gold: gold,
    craft_ingot_def: ingot_def,
    craft_ingot_n: ingot_n,
    craft_tesseract_n: quality === "legendary" ? 1 : 0,
  };
}

function recipeDropId(color, slotName) {
  return `recipe_drop_${color}_${slotName}`;
}

function goldRecipeId(slotName) {
  return `recipe_gold_${slotName}`;
}

function buildEquipmentId(quality, shortId) {
  return `eq_t1_${quality}_${shortId}`;
}

function craftRecipeForEq(quality, slotName) {
  if (quality === "legendary") return goldRecipeId(slotName);
  const color = { normal: "green", rare: "blue", epic: "purple" }[quality];
  return recipeDropId(color, slotName);
}

const items = {
  version: 3,
  items: {},
};

function addItem(id, def) {
  items.items[id] = def;
}

addItem("helm_rusty", {
  kind: "equipment",
  tier: 1,
  quality: "normal",
  slot: "Helmet",
  hp: 30,
  armor: 10,
  healing: 10,
  damage: 0,
  crit_chance: 0.2,
});
addItem("boots_basic", {
  kind: "equipment",
  tier: 1,
  quality: "normal",
  slot: "Feet",
  hp: 0,
  armor: 2,
  damage: 0,
  healing: 0,
  crit_chance: 0,
});
addItem("sword_basic", {
  kind: "equipment",
  tier: 1,
  quality: "normal",
  slot: "WeaponRight",
  hp: 0,
  armor: 0,
  damage: 35,
  healing: 0,
  crit_chance: 0,
});
addItem("gloves_basic", {
  kind: "equipment",
  tier: 1,
  quality: "normal",
  slot: "Gloves",
  hp: 0,
  armor: 0,
  damage: 0,
  healing: 3,
  crit_chance: 0.3,
});

addItem("ingot_green", { kind: "material", tier: 1, quality: "normal", max_stack: 100 });
addItem("ingot_blue", { kind: "material", tier: 1, quality: "rare", max_stack: 100 });
addItem("ingot_purple", { kind: "material", tier: 1, quality: "epic", max_stack: 100 });
addItem("tesseract", { kind: "tesseract", tier: 1, quality: "legendary", max_stack: 5 });

["recipe_green", "recipe_blue", "recipe_purple", "recipe_gold"].forEach((rid) => {
  const q = rid.replace("recipe_", "");
  const quality = q === "green" ? "normal" : q === "blue" ? "rare" : q === "purple" ? "epic" : "legendary";
  addItem(rid, { kind: "recipe", tier: 1, quality, max_stack: 1, recipe_slot: "Helmet" });
});

const easy11 = loadBot("easy", "mine_11");
const med11 = loadBot("medium", "mine_11");
const hard11 = loadBot("hard", "mine_11");
const hard12 = loadBot("hard", "mine_12");
const REF_BOT = { normal: easy11, rare: med11, epic: hard11, legendary: hard12 };

console.log(
  "equip: игрок 12 ур., полный сет ×1.25; цель — ×1.6 к hp_bonus / base_* реф-бота (legendary → hard mine_12):"
);
const statsByQuality = {};
for (const q of QUAL) {
  statsByQuality[q] = slotStatsArrayForBot(REF_BOT[q]);
  logEquipVerify(q, REF_BOT[q], statsByQuality[q]);
}

for (const q of QUAL) {
  const arr = statsByQuality[q];
  for (let si = 0; si < SLOTS.length; si++) {
    const row = SLOTS[si];
    const eqId = buildEquipmentId(q, row.id);
    const st = arr[si];
    const cr = craftRecipeForEq(q, row.slot);
    const craft = craftFor(q);
    addItem(eqId, {
      kind: "equipment",
      tier: 1,
      quality: q,
      slot: row.slot,
      craft_recipe_id: cr,
      hp: st.hp,
      armor: st.armor,
      damage: st.damage,
      healing: st.healing,
      crit_chance: st.crit_chance,
      ...craft,
    });
  }
}

const colq = { green: "normal", blue: "rare", purple: "epic" };
for (const cname of Object.keys(colq)) {
  const qual = colq[cname];
  for (const row of SLOTS) {
    const id = recipeDropId(cname, row.slot);
    addItem(id, {
      kind: "recipe",
      tier: 1,
      quality: qual,
      max_stack: 1,
      recipe_slot: row.slot,
      recipe_target_slot: row.slot,
    });
  }
}

for (const row of SLOTS) {
  const id = goldRecipeId(row.slot);
  addItem(id, {
    kind: "recipe",
    tier: 1,
    quality: "legendary",
    max_stack: 1,
    recipe_slot: row.slot,
    recipe_target_slot: row.slot,
  });
}

fs.writeFileSync(OUT, JSON.stringify(items, null, 2), "utf8");
console.log("Wrote", OUT, "items:", Object.keys(items.items).length);
