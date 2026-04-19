/**
 * Генерация duel_match3_bots_catalog_{easy,medium,hard}.json
 *
 * Одна глобальная монотонная лестница из 36 ступеней (Т1 этаж 1 … Т3 этаж 12):
 *   — easy:   глобальные индексы 0..11
 *   — medium: 12..23  (монстр 1 средней сильнее монстра 11 лёгкой)
 *   — hard:   24..35
 *
 * Якоря (total HP, урон, броня, лечение — как в каталоге: total HP = MAX_HP + hp_bonus):
 *   Т1×1:  150 HP, 0 / 0 / 0
 *   Т3×12: ~12 500 HP, урон 1 200 (вилка ТЗ 1 150–1 250), броня 850, лечение 600 (вилка 550–650)
 *
 * Боссы: те же формулы, что и у обычных этажей; точечное ×2 к боссам — вручную в JSON.
 *
 * Запуск: node gen_mine_bots.js
 */
const fs = require("fs");

const MAX_HP = 150;
const NAMES = [
  "Острый скол",
  "Ржавый гвоздь",
  "Соляной клещ",
  "Корунд",
  "Шлаковый червь",
  "Кремень",
  "Пыльный коготь",
  "Сталеплав",
  "Липкий ломун",
  "Тихий шорох",
  "Тихий шорох",
  "Жила",
];

/** [totalHp, damage, armor, heal] — конечный якорь: босс 12-го этажа Т3 */
const ANCHOR_START = [150, 0, 0, 0];
const ANCHOR_END = [12500, 1200, 850, 600];

function hpBonus(totalHp) {
  return Math.max(0, Math.round(totalHp - MAX_HP));
}

function lerp(a, b, t) {
  return a + (b - a) * t;
}

function lerpRow(a, b, t) {
  return [lerp(a[0], b[0], t), lerp(a[1], b[1], t), lerp(a[2], b[2], t), lerp(a[3], b[3], t)];
}

/** 36 строк [totalHp, dmg, armor, heal], строго выше предыдущей по HP; остальное неубывающее */
function buildGlobalRows36() {
  const rows = [];
  for (let k = 0; k < 36; k++) {
    const t = k / 35;
    const [thp, td, ta, th] = lerpRow(ANCHOR_START, ANCHOR_END, t);
    rows.push([
      Math.round(thp),
      Math.round(td),
      Math.round(ta),
      Math.round(th),
    ]);
  }
  for (let k = 1; k < 36; k++) {
    if (rows[k][0] <= rows[k - 1][0]) rows[k][0] = rows[k - 1][0] + 1;
    for (let j = 1; j < 4; j++) {
      if (rows[k][j] < rows[k - 1][j]) rows[k][j] = rows[k - 1][j];
    }
  }
  return rows;
}

function sliceTier(globalRows, tierIndex) {
  const start = tierIndex * 12;
  return globalRows.slice(start, start + 12);
}

function buildFromRows(rows12, critBoss4, critBoss8, critBoss12, rewardScale, cross) {
  const bots = {};
  for (let i = 1; i <= 12; i++) {
    const key = "mine_" + i;
    const isBoss = i === 4 || i === 8 || i === 12;
    const [thp, td, ta, th] = rows12[i - 1];
    let cr = 0;
    if (i === 1) cr = 0;
    else if (i <= 11) cr = 0.01 + Math.max(0, i - 2) * 0.0035;
    else cr = critBoss12;
    if (isBoss && i < 12) cr = i === 8 ? critBoss8 : critBoss4;

    const hb = hpBonus(thp);
    const rxp = Math.round((110 + (i - 1) * 22) * rewardScale);
    const rgold = Math.round((55 + (i - 1) * 11) * rewardScale);
    const rore = Math.round((51 + (i - 1) * 17) * rewardScale);
    const ingGreen = Math.max(1, Math.round(2 * rewardScale));
    const ingBlue = Math.max(2, Math.round(4 * rewardScale));
    const ingPurple = Math.max(3, Math.round(7 * rewardScale));
    let ing = 0,
      bp = "",
      rmmn = 0,
      rmmx = 0,
      tch = 0;
    if (i <= 3) {
      ing = ingGreen;
    } else if (i === 4) {
      bp = "green";
      ing = ingGreen;
    } else if (i <= 7) {
      ing = ingBlue;
    } else if (i === 8) {
      bp = "blue";
      ing = ingBlue;
    } else if (i <= 11) {
      ing = ingPurple;
    } else if (i === 12) {
      bp = "purple";
      ing = ingPurple;
      rmmn = Math.round(11 * rewardScale);
      rmmx = Math.round(22 * rewardScale);
      tch = 0.05;
    }

    let eat, ban;
    if (i <= 3) {
      eat = 15;
      ban = 5;
    } else if (i <= 7) {
      eat = 15;
      ban = 5;
    } else if (i <= 11) {
      eat = 25;
      ban = 15;
    } else {
      eat = 80;
      ban = 40;
    }
    const cost_attack = [{ amount: eat, resource: "energy" }];
    const cost_banish = [{ amount: ban, resource: "energy" }];
    if (i === 12) {
      cost_attack.push({ amount: 5, resource: "matter" });
      cost_banish.push({ amount: 3, resource: "matter" });
    }

    const ai = Math.min(0.5, Math.round((0.12 + i * 0.03) * 100) / 100);

    bots[key] = {
      id: key,
      name: NAMES[i - 1],
      floor: i,
      is_boss: isBoss,
      hp_bonus: hb,
      base_crit: Math.round(cr * 10000) / 10000,
      base_heal: Math.round(th * 100) / 100,
      reward_xp: rxp,
      base_armor: Math.round(ta),
      cross_bias: cross,
      difficulty: i,
      reward_ore: rore,
      start_mana: Math.min(950, Math.max(0, hb * 0.11 + i * 18)) | 0,
      base_damage: Math.round(td),
      cost_attack,
      cost_banish,
      petard_bias: Math.round((1 - 2 * cross) * 100) / 100,
      reward_gold: rgold,
      square_bias: cross,
      reward_ingots: ing,
      reward_key_id: "",
      reward_blueprint: bp,
      ai_ability_chance: ai,
      reward_key_amount: 0,
      reward_matter_max: rmmx,
      reward_matter_min: rmmn,
      reward_tesseract_chance: tch,
    };
  }
  return { version: 1, bots };
}

function assertMono12(rows, label) {
  for (let i = 1; i < 12; i++) {
    const a = rows[i - 1],
      b = rows[i];
    for (let k = 0; k < 4; k++) {
      if (b[k] < a[k] - 0.01) {
        throw new Error(`${label}: этаж ${i + 1} < этаж ${i} по полю ${k}`);
      }
    }
    if (b[0] <= a[0]) throw new Error(`${label}: HP не растёт на этаже ${i}→${i + 1}`);
  }
}

function assertCrossTier(easy12, medRows, hardRows, label) {
  const e11 = easy12[10];
  const m1 = medRows[0];
  if (m1[0] <= e11[0] || m1[1] < e11[1] || m1[2] < e11[2] || m1[3] < e11[3]) {
    throw new Error(`${label}: medium₁ слабее или равен easy₁₁ по одной из осей`);
  }
  const m11 = medRows[10];
  const h1 = hardRows[0];
  if (h1[0] <= m11[0] || h1[1] < m11[1] || h1[2] < m11[2] || h1[3] < m11[3]) {
    throw new Error(`${label}: hard₁ слабее или равен medium₁₁ по одной из осей`);
  }
}

function assertGlobal36(rows) {
  for (let k = 1; k < 36; k++) {
    const a = rows[k - 1],
      b = rows[k];
    if (b[0] <= a[0]) throw new Error(`global: HP на шаге ${k}`);
    for (let j = 1; j < 4; j++) {
      if (b[j] < a[j]) throw new Error(`global: поле ${j} упало на шаге ${k}`);
    }
  }
}

function main() {
  const globalRows = buildGlobalRows36();
  assertGlobal36(globalRows);

  const easyRows = sliceTier(globalRows, 0);
  const medRows = sliceTier(globalRows, 1);
  const hardRows = sliceTier(globalRows, 2);

  assertMono12(easyRows, "easy");
  assertMono12(medRows, "medium");
  assertMono12(hardRows, "hard");
  assertCrossTier(easyRows, medRows, hardRows, "cross");

  const easy = buildFromRows(easyRows, 0.06, 0.1, 0.15, 1.0, 0.33);
  const medium = buildFromRows(medRows, 0.12, 0.2, 0.3, 1.5, 0.4);
  const hard = buildFromRows(hardRows, 0.18, 0.3, 0.45, 2.0, 0.49);

  const base = __dirname;
  for (const [name, obj] of [
    ["duel_match3_bots_catalog_easy.json", easy],
    ["duel_match3_bots_catalog_medium.json", medium],
    ["duel_match3_bots_catalog_hard.json", hard],
  ]) {
    fs.writeFileSync(base + "/" + name, JSON.stringify(obj, null, 2), "utf8");
    console.log("wrote", name);
  }

  const h12 = hard.bots.mine_12;
  console.log(
    "anchors check | easy₁ total HP",
    MAX_HP + easy.bots.mine_1.hp_bonus,
    "| hard₁₂ total HP",
    MAX_HP + h12.hp_bonus,
    "dmg",
    h12.base_damage,
    "armor",
    h12.base_armor,
    "heal",
    h12.base_heal
  );
  console.log(
    "cross | easy₁₁ total HP",
    MAX_HP + easy.bots.mine_11.hp_bonus,
    "| med₁ total HP",
    MAX_HP + medium.bots.mine_1.hp_bonus,
    "| med₁₁ total HP",
    MAX_HP + medium.bots.mine_11.hp_bonus,
    "| hard₁ total HP",
    MAX_HP + hard.bots.mine_1.hp_bonus
  );
}

main();
