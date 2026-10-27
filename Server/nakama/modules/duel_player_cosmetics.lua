-- Cosmetic unlocks and selection are server-owned. Only durable gameplay state
-- is read; payloads never supply counters, achievements, gender or unlock flags.
local nk = require("nakama")
local function load(nested, root) local ok, m = pcall(require, nested); if ok then return m end return require(root) end
local CFG = load("modules.duel_match3_config", "duel_match3_config")
local Cat = load("modules.duel_match3_achievement_catalog", "duel_match3_achievement_catalog")
local Gender = load("modules.duel_account_gender", "duel_account_gender")
local RateLimit = load("modules.duel_api_rate_limit", "duel_api_rate_limit")
local Mail = load("modules.duel_mail", "duel_mail")
local M = {}
local COLLECTION, KEY = "duel_player_cosmetics", "appearance_v1"
local REWARD_MATTER, REWARD_HP_BASIS_POINTS, MAX_SAFE_INTEGER = 5, 50, 9007199254740991

local function req(id, label, kind, goal, key, extra)
  return { id=id, label=label, kind=kind, required=goal, key=key, extra=extra }
end
local THEMES = {
  { id="dawnwarden", requirements={} },
  { id="voidranger", requirements={ req("deep_scout", "Победить 50 монстров в шахте", "mine_kills", 50), req("first_guardian", "Победить босса 4-го этажа: легко", "floor", 1, "easy:4") } },
  { id="stormforged", requirements={ req("absolute_ii", "Достижение «Абсолют»: ранг 2", "achievement", 2, "obs.line6"), req("generator_guard", "Победить босса 8-го этажа: легко", "floor", 1, "easy:8") } },
  { id="emberoracle", requirements={ req("pyrotechnic_iii", "Достижение «Пиротехник»: ранг 3", "achievement", 3, "obs.firework_lover"), req("forge_champion", "Выиграть кузнечный турнир", "counter", 1, "slaughter.tournament_smith_final") } },
  { id="frostwraith", requirements={ req("ice_depths", "Победить 300 монстров в шахте", "mine_kills", 300), req("frozen_guardian", "Победить босса 4-го этажа: сложно", "floor", 1, "hard:4") } },
  { id="phoenix", requirements={ req("reborn", "Достижение «Феникс»: ранг 1", "achievement", 1, "dnn.win_1hp"), req("survivor", "Одержать 25 побед в PvP", "pvp_wins", 25) } },
  { id="bookmaker", requirements={ req("tote_genius", "Достижение «Гений Тотализатора»: ранг 1", "achievement", 1, "dnn.perfect_bets"), req("winning_eye", "Достижение «Шарлатан»: ранг 2", "achievement", 2, "sl.bets_won") } },
  { id="railwarden", requirements={ req("rail_races", "Одержать 20 побед в режиме «Спуск»", "mode_wins", 20, "race"), req("shaft_end", "Победить босса 12-го этажа: легко", "floor", 1, "easy:12") } },
  { id="beastbreaker", requirements={ req("monster_hunter", "Победить 500 монстров в шахте", "mine_kills", 500), req("deepest_beast", "Победить босса 12-го этажа: сложно", "floor", 1, "hard:12"), req("nailer_ii", "Достижение «Пригвоздить»: ранг 2", "achievement", 2, "obs.nail_rusty") } },
  { id="alchemist", requirements={ req("recipe_collection", "Изучить 16 разных рецептов", "recipes", 16), req("shield_ii", "Достижение «Железный Занавес»: ранг 2", "achievement", 2, "obs.shield"), req("medium_depths", "Победить босса 12-го этажа: средне", "floor", 1, "medium:12") } },
}
local INDEX = {}; for _, t in ipairs(THEMES) do INDEX[t.id] = t end
local NAMES = {voidranger="Разведчик глубин",stormforged="Мастер грозы",emberoracle="Хранитель горна",
  frostwraith="Полярный следопыт",phoenix="Феникс",bookmaker="Хозяин ставок",railwarden="Страж магистрали",
  beastbreaker="Укротитель глубин",alchemist="Алхимик шахты"}
local function number(v)
  local n = tonumber(v); if not n or n ~= n or n == math.huge or n == -math.huge then return 0 end
  return math.max(0, math.floor(n))
end
local function value(row)
  if not row then return {} end
  local v = row.value
  if type(v) == "string" then local ok, parsed = pcall(nk.json_decode, v); if ok then v=parsed end end
  if type(v) ~= "table" then error("cosmetics_invalid_source") end
  return v
end
local function read_sources(uid)
  local keys = {
    {collection=CFG.STATS_COLLECTION,key=CFG.STATS_KEY,user_id=uid},
    {collection=CFG.PVE_PROGRESS_COLLECTION,key=CFG.PVE_PROGRESS_KEY,user_id=uid},
    {collection=CFG.CHARACTER_SHEET_COLLECTION,key=CFG.CHARACTER_SHEET_KEY,user_id=uid},
    {collection=COLLECTION,key=KEY,user_id=uid},
  }
  local rows = nk.storage_read(keys); if type(rows) ~= "table" then error("cosmetics_source_unavailable") end
  local data = {}; for _, row in ipairs(rows) do data[row.collection .. "/" .. row.key] = row end
  local old = data[COLLECTION .. "/" .. KEY]
  if old and (type(old.value) ~= "table" or old.value.schema_version ~= 1 or type(old.value.unlocked) ~= "table" or
    (old.value.selected_theme ~= "default" and not INDEX[old.value.selected_theme])) then error("cosmetics_invalid_record") end
  return value(data[CFG.STATS_COLLECTION .. "/" .. CFG.STATS_KEY]), value(data[CFG.PVE_PROGRESS_COLLECTION .. "/" .. CFG.PVE_PROGRESS_KEY]),
    value(data[CFG.CHARACTER_SHEET_COLLECTION .. "/" .. CFG.CHARACTER_SHEET_KEY]), old,
    data[CFG.PVE_PROGRESS_COLLECTION .. "/" .. CFG.PVE_PROGRESS_KEY]
end
local function achievement_rank(stats, id)
  local claimed = type(stats.achievement_claimed) == "table" and stats.achievement_claimed or {}
  local rank = 0
  for _, token in ipairs(claimed) do
    if type(token) == "string" then
      local chain, ix = string.match(token, "^(.-):(%d+)$")
      if chain == id then rank = math.max(rank, number(ix) + 1) end
    end
  end
  local chain = Cat.chain_by_id(id)
  if chain then
    local counters = type(stats.achievement_stats) == "table" and stats.achievement_stats or {}
    local current = number(counters[chain.counter_key])
    for i = 1, #chain.steps do
      local need = Cat.cumulative_need(chain, i - 1)
      if need and need > 0 and current >= need then rank = math.max(rank, i) end
    end
  end
  return rank
end
local function current(rule, stats, progress, sheet)
  local floors = type(progress.mine) == "table" and progress.mine.floor_states or {}
  floors = type(floors) == "table" and floors or {}
  if rule.kind == "mine_kills" then
    local from_floors, defeated = 0, 0
    for _, floor in pairs(floors) do if type(floor) == "table" then from_floors = from_floors + number(floor.wins) end end
    for id, kills in pairs(type(progress.defeated) == "table" and progress.defeated or {}) do
      if type(id) == "string" and string.match(id, "^mine_%d+$") then defeated = defeated + number(kills) end
    end
    return math.max(from_floors, defeated) -- Two durable views of the same kills; never double-count them.
  elseif rule.kind == "floor" then return number(type(floors[rule.key]) == "table" and floors[rule.key].wins)
  elseif rule.kind == "counter" then return number(type(stats.achievement_stats) == "table" and stats.achievement_stats[rule.key])
  elseif rule.kind == "achievement" then return achievement_rank(stats, rule.key)
  elseif rule.kind == "mode_wins" then return number(type(stats.modes) == "table" and type(stats.modes[rule.key]) == "table" and stats.modes[rule.key].wins)
  elseif rule.kind == "pvp_wins" then
    local count = 0
    for _, mode in ipairs({"duel","pro","race","arena"}) do
      count = count + number(type(stats.modes) == "table" and type(stats.modes[mode]) == "table" and stats.modes[mode].wins)
    end
    return count
  elseif rule.kind == "recipes" then
    local seen, count = {}, 0
    for _, id in ipairs(type(sheet.learned_recipes) == "table" and sheet.learned_recipes or {}) do
      if type(id) == "string" and id ~= "" and not seen[id] then seen[id]=true; count=count+1 end
    end
    return count
  end
  error("cosmetics_unknown_requirement")
end
local function snapshot(uid, requested, legacy)
  local identity = Gender.authoritative(uid)
  if type(identity) ~= "table" or identity.finalized ~= true then return {ok=false,err="registration_incomplete"} end
  local female = identity.gender == "female"
  for _ = 1, 5 do
    local stats, progress, sheet, row, progress_row = read_sources(uid)
    local old = row and row.value or {}
    local unlocked, catalog, changed = {}, {}, row == nil
    assert(old.unlock_reward_receipts == nil or type(old.unlock_reward_receipts) == "table", "cosmetics_invalid_reward_receipts")
    assert(old.unlock_mail_schema == nil or old.unlock_mail_schema == 1, "cosmetics_invalid_mail_schema")
    assert(old.unlock_mail_receipts == nil or type(old.unlock_mail_receipts) == "table", "cosmetics_invalid_mail_receipts")
    local receipts, new_count, rewarded_count = {}, 0, 0
    local notice_receipts, notices = {}, {}
    for id,proof in pairs(old.unlock_mail_receipts or {}) do notice_receipts[id]=proof end
    for _, theme in ipairs(THEMES) do
      local requirements, earned = {}, true
      if theme.id ~= "dawnwarden" then
        for _, rule in ipairs(theme.requirements) do
          local v = current(rule, stats, progress, sheet)
          local met = v >= rule.required; earned = earned and met
          requirements[#requirements+1] = {id=rule.id,label=rule.label,current=v,required=rule.required,met=met}
        end
      end
      unlocked[theme.id] = earned or type(old.unlocked) == "table" and old.unlocked[theme.id] == true
      if unlocked[theme.id] and not (type(old.unlocked) == "table" and old.unlocked[theme.id] == true) then changed=true end
      local prestige = theme.id ~= "dawnwarden"
      local receipt = type(old.unlock_reward_receipts) == "table" and old.unlock_reward_receipts[theme.id] or nil
      if receipt then
        assert(prestige and type(receipt) == "table" and receipt.version == 1 and receipt.matter == REWARD_MATTER
          and receipt.hp_basis_points == REWARD_HP_BASIS_POINTS, "cosmetics_invalid_reward_receipt")
        unlocked[theme.id] = true -- A paid permanent unlock cannot be removed by later source changes.
      elseif prestige and unlocked[theme.id] then
        receipt={version=1,matter=REWARD_MATTER,hp_basis_points=REWARD_HP_BASIS_POINTS,granted_at=os.time()}
        new_count=new_count+1; changed=true
      end
      if receipt then receipts[theme.id]=receipt; rewarded_count=rewarded_count+1 end
      if prestige and unlocked[theme.id] and not notice_receipts[theme.id] then
        -- Existing collections are baselined silently on first upgrade. Newly
        -- earned appearances and their mail are committed in the same batch.
        local historical = old.unlock_mail_schema == nil and type(old.unlocked)=="table" and old.unlocked[theme.id]==true
        if not historical then
          notices[#notices+1]={id="appearance_unlock:"..theme.id,kind="appearance_unlock",action="hero",
            theme_id=theme.id,avatar_id=theme.id..(female and "-female" or "-male"),created_at=os.time(),
            title="Новый образ героя: «"..NAMES[theme.id].."»",
            body="Вы выполнили условия открытия образа. Найдите его в разделе «Герой» → «Образы». Награда за открытие уже начислена: 5 материи и +0,5% к здоровью навсегда."}
        end
        notice_receipts[theme.id]=true;changed=true
      end
      -- Omit the starter's empty array: Lua JSON implementations disagree on
      -- whether an untyped empty table means {} or []. The client normalizes it.
      catalog[#catalog+1] = {id=theme.id,unlocked=unlocked[theme.id],requirements=#requirements > 0 and requirements or nil,
        unlock_reward_matter=prestige and REWARD_MATTER or 0,
        unlock_reward_hp_percent=prestige and REWARD_HP_BASIS_POINTS/100 or 0,reward_granted=receipt ~= nil}
    end
    local selected = old.selected_theme or "default"
    if selected ~= "default" and not unlocked[selected] then selected="default"; changed=true end
    if not row and legacy and INDEX[legacy] and unlocked[legacy] then selected=legacy; changed=true end
    if requested then
      if requested ~= "default" and not unlocked[requested] then return {ok=false,err="appearance_locked",themes=catalog} end
      if requested ~= selected then selected=requested; changed=true end
    end
    local result = {ok=true,gender=identity.gender,selected_theme=selected,themes=catalog,unlock_reward_schema=1,
      unlocked_prestige_count=rewarded_count,passive_hp_bonus_percent=rewarded_count*REWARD_HP_BASIS_POINTS/100,
      unlock_reward_matter_granted=new_count*REWARD_MATTER,rewarded_matter_total=rewarded_count*REWARD_MATTER}
    if changed then
      local record={};for key,value in pairs(old)do record[key]=value end
      record.schema_version=1;record.selected_theme=selected;record.unlocked=unlocked;record.unlock_reward_receipts=receipts
      record.unlock_mail_schema=1;record.unlock_mail_receipts=notice_receipts;record.updated_at=os.time()
      local writes={{ collection=COLLECTION,key=KEY,user_id=uid,
        value=record,
        version=row and row.version or "*",permission_read=0,permission_write=0 }}
      if new_count > 0 then
        local matter=number(progress.matter)
        assert(matter <= MAX_SAFE_INTEGER-new_count*REWARD_MATTER,"cosmetics_matter_overflow")
        progress.matter=matter+new_count*REWARD_MATTER; progress.updated_at=os.time()
        writes[#writes+1]={collection=CFG.PVE_PROGRESS_COLLECTION,key=CFG.PVE_PROGRESS_KEY,user_id=uid,value=progress,
          version=progress_row and progress_row.version or "*",permission_read=1,permission_write=0}
      end
      for _,write in ipairs(Mail.plan_delivery_writes(uid,notices)) do writes[#writes+1]=write end
      local committed = pcall(nk.storage_write, writes)
      if committed then return result end
    else return result end
  end
  error("cosmetics_storage_conflict")
end
-- These internal APIs never accept client unlock/progress claims. Reconciliation
-- also grants old earned skins retroactively, even without opening the Hero UI.
function M.reconcile(uid) return snapshot(uid) end
function M.passive_hp_bonus(uid)
  local result=M.reconcile(uid)
  return result.ok and result.passive_hp_bonus_percent/100 or 0
end
local function handler(ctx, payload, select)
  local uid = ctx and ctx.user_id
  if type(uid) ~= "string" or uid == "" then return nk.json_encode({ok=false,err="unauthorized"}) end
  local ok, result = pcall(function()
    local p = payload
    if p == nil or p == "" then p={} end
    if type(p) == "string" then
      if #p > 256 then return {ok=false,err="bad_payload"} end
      local decoded, data=pcall(nk.json_decode,p); if not decoded then return {ok=false,err="bad_payload"} end; p=data
    end
    if type(p) ~= "table" then return {ok=false,err="bad_payload"} end
    local field = select and "theme_id" or "legacy_theme_id"
    for key in pairs(p) do if key ~= field then return {ok=false,err="bad_payload"} end end
    local id = p[field]
    if id ~= nil and (type(id) ~= "string" or #id > 48 or id ~= "default" and not INDEX[id]) then return {ok=false,err="unknown_appearance"} end
    if select and id == nil then return {ok=false,err="unknown_appearance"} end
    return snapshot(uid, select and id or nil, not select and id or nil)
  end)
  if not ok then nk.logger_error("duel_player_cosmetics: " .. tostring(result)); result={ok=false,err="cosmetics_unavailable"} end
  return nk.json_encode(result)
end
RateLimit.register_rpc(function(ctx,payload) return handler(ctx,payload,false) end,"duel_player_cosmetics_get")
RateLimit.register_rpc(function(ctx,payload) return handler(ctx,payload,true) end,"duel_player_cosmetics_select")
return M
