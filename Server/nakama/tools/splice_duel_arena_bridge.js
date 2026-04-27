const fs = require("fs");
const path = require("path");

const p = path.join(__dirname, "../modules/duel_match3.lua");
const lines = fs.readFileSync(p, "utf8").split(/\r?\n/);

const bridge = [
  "local arena_factory = runtime_lua_require(\"modules.arena_tournament\", \"arena_tournament\")",
  "local Arena = arena_factory({",
  "  try_match_create = try_match_create,",
  "  make_bot_user_id = make_bot_user_id,",
  "  guard_read_metadata_epoch = guard_read_metadata_epoch,",
  "  guard_assert_client_epoch_matches = guard_assert_client_epoch_matches,",
  "  read_pve_progress = read_pve_progress,",
  "  write_pve_progress = write_pve_progress,",
  "  read_character_sheet = read_character_sheet,",
  "  write_character_sheet = write_character_sheet,",
  "  ensure_sheet_inventory_counts = ensure_sheet_inventory_counts,",
  "  inventory_remove_def_total = inventory_remove_def_total,",
  "  inventory_try_add = inventory_try_add,",
  "})",
  "arena_mirror_commit = Arena.mirror_commit",
  "arena_on_match_finished = Arena.on_match_finished",
  "",
];

const outLines = lines.slice(0, 4108).concat(bridge).concat(lines.slice(4797));
fs.writeFileSync(p, outLines.join("\n"), "utf8");
console.log("spliced duel_match3.lua new lines", outLines.length);
