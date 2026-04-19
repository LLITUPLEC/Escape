/**
 * Фаза 4 / §6: проверка монотонности лестницы ботов и грубая метрика DPS×EHP
 * (как duel_match3_metrics.lua: EHP = totalHp * (1 + 0.05 * armor), DPS = dmg * (1 + crit)).
 *
 * Запуск из репозитория:
 *   node Server/nakama/tools/check_mine_bots_ladder.js
 * или из data/:
 *   node ../tools/check_mine_bots_ladder.js
 */
const fs = require("fs");
const path = require("path");

const MAX_HP = 150;
const K_ARM = 0.05;

const FILES = [
  "duel_match3_bots_catalog_easy.json",
  "duel_match3_bots_catalog_medium.json",
  "duel_match3_bots_catalog_hard.json",
];

function loadBots(relPath) {
  const dir = path.join(__dirname, "../data");
  const full = path.join(dir, relPath);
  const j = JSON.parse(fs.readFileSync(full, "utf8"));
  return j.bots;
}

function totalHp(bot) {
  return MAX_HP + (bot.hp_bonus || 0);
}

function effectiveDps(bot) {
  const d = bot.base_damage || 0;
  const c = Math.max(0, Math.min(1, bot.base_crit || 0));
  return d * (1 + c);
}

function ehp(bot) {
  const hp = totalHp(bot);
  const ar = bot.base_armor || 0;
  return hp * (1 + K_ARM * ar);
}

function power(bot) {
  return effectiveDps(bot) * ehp(bot);
}

function checkFile(label, bots) {
  let prev = null;
  for (let i = 1; i <= 12; i++) {
    const b = bots["mine_" + i];
    if (!b) throw new Error(label + ": missing mine_" + i);
    const hp = totalHp(b);
    if (prev != null) {
      if (hp <= totalHp(prev)) throw new Error(`${label}: mine_${i} HP не выше mine_${i - 1}`);
      if ((b.base_damage || 0) < (prev.base_damage || 0))
        throw new Error(`${label}: урон упал mine_${i}`);
      if ((b.base_armor || 0) < (prev.base_armor || 0))
        throw new Error(`${label}: броня упала mine_${i}`);
      if ((b.base_heal || 0) < (prev.base_heal || 0))
        throw new Error(`${label}: лечение упало mine_${i}`);
    }
    prev = b;
  }
}

function main() {
  const loaded = FILES.map((f) => ({ label: f.replace(".json", ""), bots: loadBots(f) }));

  for (const { label, bots } of loaded) checkFile(label, bots);

  const easy = loaded[0].bots;
  const med = loaded[1].bots;
  const hard = loaded[2].bots;

  const e11 = easy.mine_11;
  const m1 = med.mine_1;
  if (totalHp(m1) <= totalHp(e11))
    throw new Error("cross: medium mine_1 total HP <= easy mine_11");

  const m11 = med.mine_11;
  const h1 = hard.mine_1;
  if (totalHp(h1) <= totalHp(m11))
    throw new Error("cross: hard mine_1 total HP <= medium mine_11");

  console.log("OK: монотонность внутри файлов и ступень Т2/Т3 vs предыдущий тир.\n");
  console.log("Файл        | этаж | totalHP | dmg | armor | heal | crit% | DPS×EHP (условн.)");
  console.log("-".repeat(88));

  for (const { label, bots } of loaded) {
    for (let i = 1; i <= 12; i++) {
      const b = bots["mine_" + i];
      const p = power(b);
      const short = label.replace("duel_match3_bots_catalog_", "");
      console.log(
        `${short.padEnd(11)} | ${String(i).padStart(2)}   | ${String(totalHp(b)).padStart(7)} | ${String(b.base_damage).padStart(3)} | ${String(b.base_armor).padStart(5)} | ${String(Math.round(b.base_heal)).padStart(4)} | ${String(Math.round((b.base_crit || 0) * 100)).padStart(3)} | ${Math.round(p).toLocaleString("en-US")}`
      );
    }
    console.log("");
  }
}

main();
