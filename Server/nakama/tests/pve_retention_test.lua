package.path = "Server/nakama/modules/?.lua;" .. package.path

local NOW, PVE, JOURNAL = 10000, "duel_pve_entry", "duel_match3_journal"
local passed = 0

local function fixture()
  local f = { rows = {}, matches = {}, lists = 0, reads = 0, probes = 0 }
  local nk = {
    json_decode = function() error("invalid JSON") end,
    storage_list = function(uid, collection, limit, cursor)
      f.lists = f.lists + 1
      assert(uid ~= "" and collection == PVE and limit == 100 and cursor == "")
      if f.list_error then error("storage_list unavailable") end
      local rows = {}
      for _, row in ipairs(f.rows) do
        if row.user_id == uid and row.collection == collection then rows[#rows + 1] = row end
      end
      table.sort(rows, function(a, b) return a.key < b.key end)
      while #rows > limit do table.remove(rows) end
      return rows
    end,
    storage_read = function(identities)
      f.reads = f.reads + 1
      assert(#identities == 4)
      if f.read_error then error("storage_read unavailable") end
      local rows = {}
      for _, identity in ipairs(identities) do
        for _, row in ipairs(f.rows) do
          if row.user_id == identity.user_id and row.collection == identity.collection and row.key == identity.key then
            rows[#rows + 1] = row
          end
        end
      end
      return rows
    end,
    match_get = function(id)
      f.probes = f.probes + 1
      local state = f.matches[id]
      if state == "error" then error("match probe unavailable") end
      if state == "indeterminate" then return nil, "match probe unavailable" end
      return state
    end,
  }
  package.loaded.nakama = nk
  package.loaded.duel_pve_retention = nil
  local module = require("duel_pve_retention")

  function f.put(uid, collection, key, value, updated)
    local row = { user_id = uid, collection = collection, key = key, value = value, update_time = updated or 1000,
      version = key .. "-version", permission_read = 0, permission_write = 0 }
    f.rows[#f.rows + 1] = row
    return row
  end
  function f.launch(uid, id, time, fields)
    local value = { token = id, status = "consumed", match_id = "match-" .. id, run_id = "run-" .. id,
      reserved_at = time - 5, consumed_at = time }
    for key, field in pairs(fields or {}) do value[key] = field end
    return f.put(uid, PVE, "launch:" .. id, value, time)
  end
  function f.scanner(budget, now)
    return module.new(now or NOW, budget or 5)
  end
  function f.remove(operations)
    for _, op in ipairs(operations) do
      local found
      for i, row in ipairs(f.rows) do
        if row.user_id == op.user_id and row.collection == op.collection and row.key == op.key then
          assert(row.version == op.version)
          table.remove(f.rows, i)
          found = true
          break
        end
      end
      assert(found)
    end
  end
  return f
end

local function keys(operations)
  local result = {}
  for _, op in ipairs(operations) do result[#result + 1] = op.key:sub(8) end
  return table.concat(result, ",")
end

local function test(name, fn)
  fn(fixture())
  passed = passed + 1
  print("PASS " .. name)
end

test("retains two terminal receipts and distinguishes equal timestamps deterministically", function(f)
  local a = f.launch("player", "a", 1000)
  f.launch("player", "b", 1000)
  assert(#f.scanner().deletions(a) == 0)
  f.launch("player", "c", 1000)
  f.launch("player", "d", 1000)
  local ops = f.scanner().deletions(a)
  assert(keys(ops) == "a,b" and ops[1].version == a.version)
end)

test("one owner visit batches up to eight old versioned rows and obeys owner budget", function(f)
  local first, latest
  for i = 1, 12 do
    latest = f.launch("first", string.format("%02d", i), 1000 + i)
    first = first or latest
  end
  local second = f.launch("second", "a", 1000)
  f.launch("second", "b", 1001)
  f.launch("second", "c", 1002)
  local scan = f.scanner(1)
  local ops = scan.deletions(latest)
  assert(#ops == 8 and keys(ops) == "01,02,03,04,05,06,07,08")
  assert(#scan.deletions(first) == 0 and #scan.deletions(second) == 0 and scan.remaining_owners() == 0)
  assert(f.lists == 1 and f.reads == 1)
  f.remove(ops)
  scan = f.scanner(2)
  assert(keys(scan.deletions(latest)) == "09,10")
  assert(keys(scan.deletions(second)) == "a")
end)

test("protects active, pending, open tickets, credits, malformed records and live matches", function(f)
  local old = f.launch("player", "old", 1000)
  f.launch("player", "active", 1001)
  f.launch("player", "journal", 1002)
  f.launch("player", "open", 1003)
  f.launch("player", "credit", 1004)
  f.launch("player", "remaining", 1005, { remaining_refund = { { amount = 1 } } })
  f.launch("player", "live", 1006)
  f.launch("player", "error", 1007)
  f.launch("player", "indeterminate", 1008)
  f.launch("player", "legacy", 1009, { match_id = false })
  f.launch("player", "duplicate-key", 1010, { token = "different-key" })
  f.launch("player", "nonterminal", 1011, { status = "refund_pending" })
  f.launch("player", "last", 1100)
  f.launch("player", "penultimate", 1099)
  f.put("player", PVE, "active", { token = "active", run_id = "run-active", status = "consumed" })
  f.put("player", PVE, "refunds", { tokens = { credit = true } })
  f.put("player", JOURNAL, "pending", { entries = { ["run-journal:pve"] = {} } })
  f.put("player", JOURNAL, "retention_v2", { schema = 2, open = { { id = "run-open:stats" } } })
  f.matches["match-live"] = { match_id = "match-live" }
  f.matches["match-error"] = "error"
  f.matches["match-indeterminate"] = "indeterminate"
  assert(keys(f.scanner().deletions(old)) == "old")
end)

test("journal origins and the exact consumable-bind channel protect otherwise terminal receipts", function(f)
  local old = f.launch("player", "old", 1000)
  f.launch("player", "bind", 1001)
  f.launch("player", "origin-run", 1002)
  f.launch("player", "origin-match", 1003)
  f.launch("player", "attached", 1004, { journal_tickets = { ["extra-channel"] = {} } })
  f.launch("player", "last", 1100)
  f.launch("player", "penultimate", 1099)
  f.put("player", JOURNAL, "pending", { entries = { ["consumable-bind:match-bind"] = {}, ["extra-channel"] = {} } })
  f.put("player", JOURNAL, "retention_v2", { schema = 2, open = {
    { id = "unrelated-id-a", origin = { root_result = "run-origin-run" } },
    { id = "unrelated-id-b", origin = { match_id = "match-origin-match" } },
  } })
  assert(keys(f.scanner().deletions(old)) == "old")
end)

test("refunded reservations qualify while a live prestart cancellation remains visible", function(f)
  local reservation = f.put("player", PVE, "launch:reservation", {
    token = "reservation", status = "refunded", refunded_at = 1000, remaining_refund = {},
  })
  f.launch("player", "prestart", 1001, { status = "refunded", refunded_at = 1001 })
  f.launch("player", "last", 1004)
  f.launch("player", "penultimate", 1003)
  f.matches["match-prestart"] = { match_id = "match-prestart" }
  assert(keys(f.scanner().deletions(reservation)) == "reservation")
  f.matches["match-prestart"] = nil
  assert(keys(f.scanner().deletions(reservation)) == "reservation,prestart")
end)

test("a recent terminal update and completion must both clear the grace period", function(f)
  local old = f.launch("player", "old", 1000)
  local recent_update = f.launch("player", "recent-update", 1001)
  recent_update.update_time = NOW - 119
  local recent_terminal = f.launch("player", "recent-terminal", NOW - 119)
  recent_terminal.update_time = 1002
  assert(#f.scanner().deletions(old) == 0)
  assert(keys(f.scanner(1, NOW + 1).deletions(old)) == "old")
end)

test("a partial owner page proves deletion only with two distinct newer witnesses", function(f)
  local outside = f.launch("player", "z-old", 1000)
  for i = 1, 98 do f.put("player", PVE, string.format("a-%03d", i), {}) end
  f.launch("player", "b-new", 1100)
  f.launch("player", "c-new", 1099)
  assert(keys(f.scanner().deletions(outside)) == "z-old")
  f.put("player", PVE, "a-other", {})
  assert(#f.scanner().deletions(outside) == 0)
end)

test("storage failures and malformed controls preserve the whole owner", function(f)
  local old = f.launch("player", "old", 1000)
  f.launch("player", "last", 1100)
  f.launch("player", "penultimate", 1099)
  f.list_error = true
  assert(#f.scanner().deletions(old) == 0)
  f.list_error, f.read_error = false, true
  assert(#f.scanner().deletions(old) == 0)
  f.read_error = false
  f.put("player", JOURNAL, "pending", { entries = "broken" })
  assert(#f.scanner().deletions(old) == 0 and f.probes == 0)
end)

test("private versioned terminal receipt proof cannot exhaust unbounded match probes", function(f)
  local first
  for i = 1, 50 do
    local row = f.launch("player", string.format("%02d", i), 1000 + i)
    first = first or row
    f.matches[row.value.match_id] = "error"
  end
  assert(#f.scanner().deletions(first) == 0 and f.probes == 16)
  first.permission_read = 1
  assert(#f.scanner().deletions(first) == 0 and f.lists == 1)
  first.permission_read, first.version = 0, ""
  assert(#f.scanner().deletions(first) == 0 and f.lists == 1)
end)

print("pve_retention_test: " .. passed .. " passed")
