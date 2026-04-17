/**
 * Генерация duel_match3_bots_catalog_{easy,medium,hard}.json
 * Лестница: линейная между якорями; medium₁ ≈ 1.6× easy₁₁ по осям; hard₁ ≈ 1.6× medium₁₁.
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

function hpBonus(totalHp) {
  return Math.max(0, Math.round(totalHp - MAX_HP));
}

function lerpRow(a, b, t) {
  return [
    lerp(a[0], b[0], t),
    lerp(a[1], b[1], t),
    lerp(a[2], b[2], t),
    lerp(a[3], b[3], t),
  ];
}
function lerp(a, b, t) {
  return a + (b - a) * t;
}

function scaleRow([hp, d, a, h], mul) {
  const s = Math.sqrt(mul);
  return [hp * s, d * s, a * s, h * s];
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
    let ing = 0,
      bp = "",
      rmmn = 0,
      rmmx = 0,
      tch = 0;
    if (i === 4) {
      bp = "green";
      ing = Math.max(1, Math.round(2 * rewardScale));
    } else if (i === 8) {
      bp = "blue";
      ing = Math.max(2, Math.round(4 * rewardScale));
    } else if (i === 12) {
      bp = "purple";
      ing = Math.max(3, Math.round(7 * rewardScale));
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

function assertMono(rows, label) {
  for (let i = 1; i < 12; i++) {
    const a = rows[i - 1],
      b = rows[i];
    for (let k = 0; k < 4; k++) {
      if (b[k] < a[k] - 0.01) {
        throw new Error(`${label}: этаж ${i + 1} < этаж ${i} по полю ${k}`);
      }
    }
  }
}

/** Easy: якорь 1 и 12; промежуточные вручную монотонны. */
const EASY = [
  [150, 0, 0, 0],
  [650, 18, 6, 0],
  [1100, 38, 14, 4],
  [1650, 58, 24, 9],
  [2100, 72, 34, 14],
  [2600, 92, 46, 22],
  [3100, 112, 58, 32],
  [3800, 138, 74, 44],
  [4300, 158, 88, 56],
  [4800, 182, 102, 68],
  [5400, 210, 118, 82],
  [8333, 792, 556, 389],
];

function linear12(start, end) {
  const o = [];
  for (let i = 0; i < 12; i++) {
    o.push(lerpRow(start, end, i / 11));
  }
  return o;
}

function main() {
  assertMono(EASY, "easy_raw");
  const easy = buildFromRows(EASY, 0.06, 0.1, 0.15, 1.0, 0.33);

  const e11 = EASY[10];
  const e12 = EASY[11];
  const mStart = scaleRow(e11, 1.6);
  const mEnd = scaleRow(e12, 1.6);
  const medRows = linear12(mStart, mEnd);
  assertMono(medRows, "medium_raw");
  const medium = buildFromRows(medRows, 0.12, 0.2, 0.3, 1.5, 0.4);

  const med1 = medRows[0];
  const hStart = scaleRow(med1, 1.6);
  const hEnd = [25000, 2375, 1667, 1167];
  const hardRows = linear12(hStart, hEnd);
  assertMono(hardRows, "hard_raw");
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
  console.log(
    "easy mine_1 HP",
    MAX_HP + easy.bots.mine_1.hp_bonus,
    "| hard mine_1 HP",
    MAX_HP + hard.bots.mine_1.hp_bonus,
    "| hard mine_12 HP",
    MAX_HP + hard.bots.mine_12.hp_bonus,
    "dmg",
    hard.bots.mine_12.base_damage
  );
}

main();
