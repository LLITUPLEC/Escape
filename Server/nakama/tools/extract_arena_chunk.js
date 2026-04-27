const fs = require("fs");
const path = require("path");
const src = fs.readFileSync(
  path.join(__dirname, "../modules/duel_match3.lua"),
  "utf8"
);
const lines = src.split(/\r?\n/);
const body = lines.slice(4108, 4797).join("\n");
fs.writeFileSync(
  path.join(__dirname, "../modules/_arena_body.tmp.lua"),
  body,
  "utf8"
);
console.log("lines total", lines.length, "extracted chars", body.length);
