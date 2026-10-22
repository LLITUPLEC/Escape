/*
 * Read-only numerical audit of economy/catalogue content.
 * node Server/nakama/tests/balance/audit.js [--storage-dir DIRECTORY]
 *
 * --storage-dir accepts the sanitized system-record export (one JSON value per
 * collection__key.json). It never reads credentials or writes Nakama Storage.
 * Without it, audits the full duel_match3_item_defs.json (not the user's small
 * experimental duel_match3_item_catalog.example.json) and other checked-in data.
 * Combat examples deliberately exclude achievements, affixes, aura and AI skill;
 * they are diagnostics, not measured win rates.
 */
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const root = path.resolve(__dirname, '../..');
const args = process.argv.slice(2);
const ix = args.indexOf('--storage-dir');
if (ix >= 0 && !args[ix + 1]) throw new Error('--storage-dir requires a directory');
const liveDir = ix >= 0 ? path.resolve(args[ix + 1]) : null;
const inputs = {};
function read(collection, key, localFile) {
  const filename = liveDir ? path.join(liveDir, `${collection}__${key}.json`)
    : path.join(root, 'data', localFile);
  const raw = fs.readFileSync(filename, 'utf8').replace(/^\uFEFF/, '');
  inputs[`${collection}/${key}`] = crypto.createHash('sha256').update(raw).digest('hex');
  const parsed = JSON.parse(raw);
  return parsed.value && parsed.collection ? parsed.value : parsed;
}
const items = read('duel_match3_item_defs', 'catalog', 'duel_match3_item_defs.json').items;
const bots = {};
for (const diff of ['easy', 'medium', 'hard']) {
  bots[diff] = read('duel_match3_bot_defs', `catalog_${diff}`, `duel_match3_bots_catalog_${diff}.json`).bots;
}
const achievements = read('duel_match3_achievement_defs', 'catalog', 'achievement_catalog.example.json');
const contest = read('duel_match3_contest_goals', 'config', 'contest_goals.example.json');
const cfg = fs.readFileSync(path.join(root, 'modules', 'duel_match3_config.lua'), 'utf8');
const main = fs.readFileSync(path.join(root, 'modules', 'duel_match3.lua'), 'utf8');
const levelXp = cfg.match(/LEVEL_XP\s*=\s*\{([^}]+)\}/)[1].split(',').map(x => Number(x.trim()));
const barrierSource = cfg.match(/MINE_BARRIER_REQUIREMENTS\s*=\s*\{([\s\S]*?)\n\s*\},/)[1];
const barriers = [...barrierSource.matchAll(/\[(\d+)\]\s*=\s*\{([^}]+)\}/g)].map(m => {
  const row = {floor: Number(m[1])};
  for (const kv of m[2].matchAll(/(\w+)\s*=\s*(\d+)/g)) row[kv[1]] = Number(kv[2]);
  return row;
});
function constant(name) {
  const m = cfg.match(new RegExp(`\\b${name}\\s*=\\s*(\\d+)`));
  if (!m) throw new Error(`Missing numeric constant ${name}`);
  return Number(m[1]);
}
// Fail visibly if the runtime formula changes; do not silently reuse this model.
if (!/if d == "medium" then return 1\.5 end\s+if d == "hard" then return 2\.0 end/.test(main)) {
  throw new Error('Review reward multipliers: runtime changed');
}
const rewardMul = {easy: 1, medium: 1.5, hard: 2};
const findings = [];
const errors = [];
const round = n => Math.round(n * 1000) / 1000;
const sum = (rows, field) => rows.reduce((acc, r) => acc + (Number(r[field]) || 0), 0);
const equipment = Object.entries(items).filter(([, d]) => d.kind === 'equipment');
const crafts = equipment.filter(([, d]) => d.craft_recipe_id);
const recipeFloors = {Helmet:1, Chest:2, Gloves:3, WeaponLeft:5, WeaponRight:6, Legs:7, Shoulders:9, Feet:11};
const slotOrder = ['Helmet','Shoulders','Chest','Gloves','Legs','Feet','WeaponLeft','WeaponRight'];
const qualityOrder = ['normal','rare','epic','legendary'];
const qualitySets = Object.fromEntries(qualityOrder.map(q => [q,
  crafts.filter(([, d]) => d.quality === q).map(([id, d]) => ({id, ...d}))]));
function stats(rows, level=12) {
  const bonus = rows.length === 8 && new Set(rows.map(r => `${r.tier}:${r.quality}`)).size === 1 ? 1.25 : 1;
  return {
    hp: Math.round(150 + (level-1)*30 + sum(rows,'hp')*bonus),
    damage: Math.round(level-1 + sum(rows,'damage')*bonus),
    armor: Math.round(level-1 + sum(rows,'armor')*bonus),
    healing: Math.round(level-1 + sum(rows,'healing')*bonus),
    crit_chance: round(Math.min(1, .005 + (level-1)*.005 + sum(rows,'crit_chance'))),
  };
}
for (const [id, def] of Object.entries(items)) {
  for (const field of ['hp','damage','armor','healing','crit_chance','sale_price','craft_ore','craft_gold','craft_ingot_n','craft_tesseract_n']) {
    if (def[field] != null && (!Number.isFinite(Number(def[field])) || Number(def[field]) < 0)) errors.push(`${id}.${field}: invalid`);
  }
  if ((def.crit_chance || 0) > 1) errors.push(`${id}: crit_chance exceeds 1`);
  if (def.craft_recipe_id && !items[def.craft_recipe_id]) errors.push(`${id}: missing recipe ${def.craft_recipe_id}`);
  if (def.craft_recipe_id && items[def.craft_recipe_id] && items[def.craft_recipe_id].recipe_slot !== def.slot) errors.push(`${id}: recipe slot mismatch`);
  if (def.craft_item_id && !items[def.craft_item_id]) errors.push(`${id}: missing fodder ${def.craft_item_id}`);
  if (def.craft_item_id && items[def.craft_item_id] && items[def.craft_item_id].slot !== def.slot) errors.push(`${id}: fodder slot mismatch`);
  if (def.craft_ingot_n > 0 && !items[def.craft_ingot_def]) errors.push(`${id}: missing ingot ${def.craft_ingot_def}`);
}
for (const [quality, rows] of Object.entries(qualitySets)) {
  if (rows.length!==8 || new Set(rows.map(r=>r.slot)).size!==8) errors.push(`${quality}: expected exactly one craftable item in each of 8 slots`);
}
for (const [diff, defs] of Object.entries(bots)) {
  for (let floor=1; floor<=12; floor++) {
    const b = defs[`mine_${floor}`];
    if (!b) { errors.push(`${diff}: missing floor ${floor}`); continue; }
    if (b.floor !== floor) errors.push(`${diff}/mine_${floor}: incorrect floor`);
    for (const field of ['hp_bonus','base_damage','base_armor','base_heal','base_crit','reward_xp','reward_gold','reward_ore','reward_ingots','reward_matter_min','reward_matter_max','reward_tesseract_chance']) {
      if (b[field]!=null && (!Number.isFinite(Number(b[field])) || Number(b[field])<0)) errors.push(`${diff}/mine_${floor}.${field}: invalid`);
    }
    for (const field of ['base_crit','reward_tesseract_chance']) if ((b[field]||0)>1) errors.push(`${diff}/mine_${floor}.${field}: exceeds 1`);
    if ((b.reward_matter_min||0)>(b.reward_matter_max||0)) errors.push(`${diff}/mine_${floor}: matter range inverted`);
  }
}
const walletRewards = (achievements.chains || []).flatMap(ch => ch.steps.flatMap((st, step) =>
  (st.rewards || []).filter(r => ['gold','ore','matter'].includes(r.type)).map(r => ({chain:ch.id,counter:ch.counter_key,step,
    required:ch.threshold_mode==='absolute_total'?st.threshold_delta:sum(ch.steps.slice(0,step+1),'threshold_delta'),...r}))));
const accessibleMineMatter = Object.values(bots.easy).filter(b => b.floor <=11 && (b.reward_matter_max || 0)>0);
const raceEntryMatter = sum((contest.race.entry || []).filter(r => r.resource === 'matter'),'amount');
if (!accessibleMineMatter.length && !walletRewards.some(r => r.type === 'matter') && raceEntryMatter>0 && (barriers.find(b=>b.floor===12).matter || 0)>0) {
  findings.push({id:'MATTER_BOOTSTRAP', severity:'blocker', evidence:{initial:0, mineFloors1to11:0, raceEntry:raceEntryMatter, floor12Barrier:barriers.find(b=>b.floor===12).matter}, note:'No configured gameplay source for the first matter. Leaderboard reward rows are display-only.'});
}
const raceRecipeRewards = (contest.race.rewards || []).filter(r => r.resource==='recipe').map(r => r.id);
const goldRecipes = crafts.filter(([,d])=>d.quality==='legendary').map(([,d])=>d.craft_recipe_id);
if (goldRecipes.length && !goldRecipes.some(id=>raceRecipeRewards.includes(id))) {
  findings.push({id:'LEGENDARY_RECIPE_SOURCE', severity:'blocker', count:goldRecipes.length,
    note:'Craft requires exact gold scroll IDs; mine only drops green/blue/purple, tournament only grants ore/gold, achievements have no item reward path. Generic recipe_gold does not satisfy these recipes.'});
}
const thresholdMismatches = [];
const rewardTextMismatches = [];
const bonusTotals = {};
for (const ch of achievements.chains || []) {
  let accumulated = 0;
  ch.steps.forEach((st, step) => {
    accumulated += st.threshold_delta || 0;
    const required = ch.threshold_mode === 'absolute_total' ? st.threshold_delta : accumulated;
    const numberInDescription = String(st.description_ru || '').match(/(?:^|\s)(\d+)\s+(?:раз|побед|турнир|боя|боёв|боев|матч)/i);
    if (numberInDescription && Number(numberInDescription[1]) !== required) thresholdMismatches.push({chain:ch.id,step,description:Number(numberInDescription[1]),actual:required});
    for (const reward of st.rewards || []) bonusTotals[reward.type]=(bonusTotals[reward.type]||0)+Number(reward.value||0);
    if ((st.rewards || []).length===1) {
      const r=st.rewards[0];
      const desc=String(st.reward_text_ru || '').match(/\+([\d.]+)(%?)/);
      if (desc && !desc[2] && Number(desc[1])!==Number(r.value)) rewardTextMismatches.push({chain:ch.id,step,text:Number(desc[1]),actual:r.value,type:r.type});
    }
  });
}
if (thresholdMismatches.length) findings.push({id:'ACHIEVEMENT_THRESHOLD_TEXT',severity:'content_ambiguity',count:thresholdMismatches.length,
  note:'Descriptions quote per-step deltas while the numbers shown here are lifetime cumulative requirements. Unity subtracts previous steps in its progress bar, so these are not proven incorrect conditions; wording such as another 50 would remove ambiguity.',
  examples:thresholdMismatches.slice(0,8)});
if (rewardTextMismatches.length) findings.push({id:'ACHIEVEMENT_REWARD_TEXT',severity:'defect',examples:rewardTextMismatches});
const setStats = Object.fromEntries(qualityOrder.map(q=>[q,stats(qualitySets[q])]));
// Requested temporary aura: all +10%, extra damage +5%, extra armor -10%,
// critical chance +25 percentage points, XP +50%, respawn wait -50%, 2 hours.
// Runtime multiplies all_stats and per-stat factors (1.10*1.05, 1.10*.90).
function applyRequestedAura(s) {
  return {hp:Math.round(s.hp*1.1),damage:Math.round(s.damage*1.1*1.05),armor:Math.round(s.armor*1.1*.9),
    healing:Math.round(s.healing*1.1),crit_chance:round(Math.min(1,s.crit_chance+.25))};
}
const upgradeRegressions = [];
for (let qi=0; qi<qualityOrder.length-1; qi++) {
  const from=qualityOrder[qi], to=qualityOrder[qi+1], before=setStats[from];
  for (const slot of slotOrder) {
    const replacement=qualitySets[to].find(r=>r.slot===slot);
    if (!replacement) continue;
    const after=stats(qualitySets[from].map(r=>r.slot===slot?replacement:r));
    const reduced=Object.keys(before).filter(k=>after[k]<before[k]);
    if (reduced.length) upgradeRegressions.push({from,to,slot,before,after,reduced});
  }
}
if (upgradeRegressions.length) findings.push({id:'SET_BONUS_UPGRADE_CLIFF',severity:'balance',count:upgradeRegressions.length,examples:upgradeRegressions.slice(0,3)});
const mineRows = [];
const bossResets = [];
for (const [diff, defs] of Object.entries(bots)) {
  for (let floor=1; floor<=12; floor++) {
    const b=defs[`mine_${floor}`], mul=rewardMul[diff], boss=b.is_boss || floor%4===0;
    const ingotChance=boss?1:({1:.25,2:.5,3:.75}[floor%4]||1);
    const ingots=Math.ceil((b.reward_ingots||0)*mul)*(boss?2:1);
    const matterMin=b.reward_matter_min||0, matterMax=b.reward_matter_max||0;
    let matterMean=0;
    for (let value=matterMin;value<=matterMax;value++) matterMean+=Math.ceil(value*mul)/(matterMax-matterMin+1);
    const recipeSlot=Object.keys(recipeFloors).find(s=>recipeFloors[s]===floor);
    const recipeIdx=Object.values(recipeFloors).sort((a,b)=>a-b).indexOf(floor);
    const recipeChance=recipeIdx>=0?(50-5*recipeIdx)/100:0;
    const energyCost=sum((b.cost_attack||[]).filter(r=>r.resource==='energy'),'amount');
    mineRows.push({diff,floor,hp:150+(b.hp_bonus||0),damage:b.base_damage||0,armor:b.base_armor||0,heal:b.base_heal||0,
      xp:Math.ceil(b.reward_xp*mul),gold:Math.ceil(b.reward_gold*mul),ore:Math.ceil(b.reward_ore*mul),
      expectedIngots:round(ingots*ingotChance),ingotsOnDrop:ingots,ingotChance,expectedMatter:round(matterMean),
      energyCost,matterCost:sum((b.cost_attack||[]).filter(r=>r.resource==='matter'),'amount'),recipeSlot,recipeChance});
    if (floor>1 && 150+b.hp_bonus < 150+defs[`mine_${floor-1}`].hp_bonus) bossResets.push({diff,from:floor-1,to:floor,previousHp:150+defs[`mine_${floor-1}`].hp_bonus,currentHp:150+b.hp_bonus});
  }
}
if (bossResets.length) findings.push({id:'NON_MONOTONIC_POST_BOSS',severity:'balance',examples:bossResets});
const craftTotals=Object.fromEntries(qualityOrder.map(q=>[q,{
  ore:sum(qualitySets[q],'craft_ore'),gold:sum(qualitySets[q],'craft_gold'),ingots:sum(qualitySets[q],'craft_ingot_n'),
  tesseracts:sum(qualitySets[q],'craft_tesseract_n'),craftHours:round(qualitySets[q].reduce((n,r)=>n+(r.craft_minutes||60)/60,0)),
}]));
const starterCraft=qualitySets.normal.find(r=>r.slot==='Helmet');
const earlyRow=mineRows.find(r=>r.diff==='easy'&&r.floor===1);
const starterWins=Math.max(starterCraft.craft_ore/earlyRow.ore,starterCraft.craft_gold/earlyRow.gold,starterCraft.craft_ingot_n/earlyRow.expectedIngots,1/earlyRow.recipeChance);
const barriersTotal={ore:sum(barriers,'ore'),gold:sum(barriers,'gold'),matter:sum(barriers,'matter')};
const lastEasyNormal=mineRows.find(r=>r.diff==='easy'&&r.floor===11);
const lastHardBoss=mineRows.find(r=>r.diff==='hard'&&r.floor===12);
const result={
  source:liveDir?'sanitized live system Storage snapshot':'checked-in full catalogues',
  assumptions:{snapshotAura:false,achievements:false,affixes:false,combat:'ordinary bomb match=15 flat + damage; armor subtracts per action, critical doubles before armor',rewardMultiplier:rewardMul},
  inputSha256:inputs,
  structuralChecks:{items:Object.keys(items).length,equipment:equipment.length,craftable:crafts.length,achievementChains:(achievements.chains||[]).length,errors},
  findings,
  progression:{levelXp,barriersTotal,barrierMediumMultiplier:3,barrierHardMultiplier:5,
    firstHelmet:{ore:starterCraft.craft_ore,gold:starterCraft.craft_gold,ingots:starterCraft.craft_ingot_n,winsByMeanBottleneck:round(starterWins),energyByMeanBottleneck:round(starterWins*earlyRow.energyCost),note:'Resource/drop means only; not exact expected completion time or measured win rate. Simultaneous random requirements can take longer.'},
    level12FromZero:{easy11Wins:Math.ceil(levelXp.at(-1)/lastEasyNormal.xp),easy11Energy:Math.ceil(levelXp.at(-1)/lastEasyNormal.xp)*lastEasyNormal.energyCost,hard12Wins:Math.ceil(levelXp.at(-1)/lastHardBoss.xp)},
    regenEnergyPerDay:86400/constant('PVE_ENERGY_REGEN_SECONDS'),
  },
  equipment:{level12FullSets:setStats,level1FullSets:Object.fromEntries(qualityOrder.map(q=>[q,stats(qualitySets[q],1)])),
    craftTotals,upgradeRegressions},
  achievementMaximumBonusTotals:Object.fromEntries(Object.entries(bonusTotals).map(([k,v])=>[k,round(v)])),
  matterAchievementSources:walletRewards.filter(r=>r.type==='matter'),
  requestedAura:{durationHours:2,hpMultiplier:1.1,damageMultiplier:1.155,armorMultiplier:.99,healingMultiplier:1.1,critAdd:.25,xpMultiplier:1.5,respawnMultiplier:.5,
    level12FullSets:Object.fromEntries(qualityOrder.map(q=>[q,applyRequestedAura(setStats[q])])),
    mineRows:mineRows.map(r=>({diff:r.diff,floor:r.floor,xp:Math.ceil(r.xp*1.5)})),
    note:'A separate numerical scenario. The supplied catalogue snapshot was captured before this temporary aura was created. These are per-fight values while it is active, not a 2-hour completion estimate.'},
  race:{entry:contest.race.entry,rewards:contest.race.rewards,expectedNetMatterAt50PercentWin:sum((contest.race.rewards||[]).filter(r=>r.resource==='matter'),'amount')/2-raceEntryMatter,
    note:'Two humans pay 4 total matter, winner receives 10: +6 total per completed decisive race; bots add no entry.'},
  tournament:{resourcePayoutToEntryRatio:5,participants:8,unmodifiedTotalPayoutRatio:5/8,sideBetMultiplierRangeForFinalist:[.2,1.8],
    note:'With 4 predictions, champion payout can be 1x..9x own stake. 8 real humans supply 8x stake; bot-filled brackets supply less.'},
  mineRows,
};
console.log(JSON.stringify(result,null,2));
if (errors.length) process.exitCode=1;
