const fs = require("fs");
const path = require("path");

const bodyPath = path.join(__dirname, "../modules/_arena_body.tmp.lua");
let body = fs.readFileSync(bodyPath, "utf8");

body = body.replace(
  /^arena_mirror_commit = function\(state\)/m,
  "local function mirror_commit(state)"
);
body = body.replace(
  /^arena_on_match_finished = function\(state, winner_uid\)/m,
  "local function on_match_finished(state, winner_uid)"
);

const header = `local nk = require("nakama")

--- Турнир арены (отдельный chunk — не упирается в лимит локалей duel_match3.lua).
return function(deps)
  local try_match_create = deps.try_match_create
  local make_bot_user_id = deps.make_bot_user_id
  local guard_read_metadata_epoch = deps.guard_read_metadata_epoch
  local guard_assert_client_epoch_matches = deps.guard_assert_client_epoch_matches
  local read_pve_progress = deps.read_pve_progress
  local write_pve_progress = deps.write_pve_progress
  local read_character_sheet = deps.read_character_sheet
  local write_character_sheet = deps.write_character_sheet
  local ensure_sheet_inventory_counts = deps.ensure_sheet_inventory_counts
  local inventory_remove_def_total = deps.inventory_remove_def_total
  local inventory_try_add = deps.inventory_try_add

`;

const footer = `
  return {
    mirror_commit = mirror_commit,
    on_match_finished = on_match_finished,
    duel_arena_queue_join = duel_arena_queue_join,
    duel_arena_queue_leave = duel_arena_queue_leave,
    duel_arena_queue_poll = duel_arena_queue_poll,
  }
end
`;

const out = path.join(__dirname, "../modules/arena_tournament.lua");
fs.writeFileSync(out, header + body + footer, "utf8");
console.log("Wrote", out, "bytes", header.length + body.length + footer.length);
