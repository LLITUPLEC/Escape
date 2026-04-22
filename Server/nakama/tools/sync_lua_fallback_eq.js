/**
 * Печатает в stdout UTF-8 строки для вставки в ITEM_DEFS_FALLBACK (только eq_t1_* из каталога).
 * Запуск: node sync_lua_fallback_eq.js > ../modules/_eq_snippet.lua (не используйте PowerShell >)
 */
const fs = require("fs");
const path = require("path");
const p = path.join(__dirname, "..", "data", "duel_match3_item_catalog.example.json");
const j = JSON.parse(fs.readFileSync(p, "utf8"));
const QUAL_ORDER = ["normal", "rare", "epic", "legendary"];
const SLOT_SHORT = ["helmet", "shoulders", "chest", "gloves", "legs", "feet", "weapon_l", "weapon_r"];
const lines = [
  '  -- T1-only: craft_recipe_id = recipe_drop_{цвет}_{Slot} / recipe_gold_{Slot} (каталог v3).',
];
for (const q of QUAL_ORDER) {
  for (const s of SLOT_SHORT) {
    const id = `eq_t1_${q}_${s}`;
    const it = j.items[id];
    if (!it) continue;
    const a = [];
    a.push('kind = "equipment"');
    a.push(`slot = "${it.slot}"`);
    a.push(`tier = ${it.tier}`);
    a.push(`quality = "${it.quality}"`);
    a.push(`craft_recipe_id = "${it.craft_recipe_id}"`);
    if (it.hp) a.push(`hp = ${it.hp}`);
    if (it.armor) a.push(`armor = ${it.armor}`);
    if (it.damage) a.push(`damage = ${it.damage}`);
    if (it.healing) a.push(`healing = ${it.healing}`);
    if (it.crit_chance) a.push(`crit_chance = ${it.crit_chance}`);
    a.push(`craft_ore = ${it.craft_ore}`);
    a.push(`craft_gold = ${it.craft_gold}`);
    a.push(`craft_ingot_def = "${it.craft_ingot_def}"`);
    a.push(`craft_ingot_n = ${it.craft_ingot_n}`);
    a.push(`craft_tesseract_n = ${it.craft_tesseract_n}`);
    lines.push(`  ${id} = { ${a.join(", ")} },`);
  }
}
const out = path.join(__dirname, "..", "modules", "_eq_snippet.lua");
const snipText = lines.join("\n");
fs.writeFileSync(out, snipText.replace(/\n/g, "\r\n") + "\r\n", "utf8");
console.error("Wrote", out);

const luaPath = path.join(__dirname, "..", "modules", "duel_match3.lua");
let lua = fs.readFileSync(luaPath, "utf8").replace(/\r\n/g, "\n");
const startMark =
  "  -- Зелёный T1 (normal): craft_recipe_id = id предмета-рецепта в learned_recipes (§4.3: recipe_drop_t{mine_tier}_{color}_{Slot})\n";
const endMark = "  eq_t2_normal_helmet = ";
const si = lua.indexOf(startMark);
const ei = lua.indexOf(endMark);
if (si === -1 || ei === -1) throw new Error("duel_match3.lua: T1 equipment block markers not found");
lua = lua.slice(0, si) + snipText + "\n" + lua.slice(ei);

lua = lua.replace(
  /ITEM_DEFS_FALLBACK\[id\] = \{ kind = "recipe", tier = 1, quality = qual, max_stack = 1, recipe_slot = s \}/g,
  'ITEM_DEFS_FALLBACK[id] = { kind = "recipe", tier = 1, quality = qual, max_stack = 1, recipe_slot = s, recipe_target_slot = s }'
);

const tierBlockNeedle =
  "\n\n-- §4.3: recipe_drop_t{тир_шахты}_{цвет}_{Slot} и золотые recipe_gold_t{тир}_{Slot} (совпадает с duel_match3_item_catalog.example.json).\ndo";
if (!lua.includes("recipe_gold_\" .. s")) {
  lua = lua.replace(
    tierBlockNeedle,
    "\n\n-- Каталог v3: recipe_gold_{Slot} (без тира) для craft_recipe_id легендарки.\ndo\n  local slots_gold = { \"Helmet\", \"Chest\", \"Gloves\", \"WeaponLeft\", \"WeaponRight\", \"Legs\", \"Shoulders\", \"Feet\" }\n  for _, s in ipairs(slots_gold) do\n    local id = \"recipe_gold_\" .. s\n    ITEM_DEFS_FALLBACK[id] = { kind = \"recipe\", tier = 1, quality = \"legendary\", max_stack = 1, recipe_slot = s, recipe_target_slot = s }\n  end\nend" +
      tierBlockNeedle
  );
}

fs.writeFileSync(luaPath, lua.replace(/\n/g, "\r\n"), "utf8");
console.error("Patched", luaPath);
