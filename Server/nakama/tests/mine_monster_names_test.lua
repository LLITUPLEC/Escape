-- Actual config and actual engine fallback/storage normalization, with no Nakama I/O.
package.path="Server/nakama/?.lua;"..package.path
local CFG=require("modules.duel_match3_config")
local f=assert(io.open("Docs/Design/monstr.txt","r"));local spec=f:read("*a");f:close()
local expected={}
for line in spec:gmatch("[^\r\n]+") do local floor,name=line:match("^(%d+)\t([^\t]+)");if floor then expected[tonumber(floor)]=name end end
assert(#expected==12)
f=assert(io.open("Server/nakama/modules/duel_match3.lua","r"));local source=f:read("*a");f:close()
local start=assert(source:find("function build_floor_bot_entry",1,true))
local finish=assert(source:find("\nmath.randomseed",start,true))
local normalize_start=assert(source:find("local function normalize_stored_bot",finish,true))
local normalize_end=assert(source:find("local function read_bots_from_storage_key",normalize_start,true))
local env=setmetatable({CFG=CFG,
 is_boss_floor=function(floor)return floor==4 or floor==8 or floor==12 end,
 mine_bot_id_for_floor=function(floor)return"mine_"..floor end,
 PveMineCost={DEFAULT_ATTACK={},DEFAULT_BANISH={},clone_list=function(t)return t end,normalize=function(a,b)return a or b end},
},{__index=_G})
local chunk=assert(loadstring(source:sub(start,finish-1)..source:sub(normalize_start,normalize_end-1).."\nreturn normalize_stored_bot","mine-names-production-functions"))
setfenv(chunk,env);local normalize=chunk()
local legacy={"Острый скол","Ржавый гвоздь","Соляной клещ","Корунд","Шлаковый червь","Кремень",
 "Пыльный коготь","Сталеплав","Липкий ломун","Тихий шорох","Тихий шорох","Жила"}
for floor=1,12 do
 assert(CFG.MINE_MONSTER_NAMES[floor]==expected[floor],"config differs from monstr.txt at "..floor)
 assert(env.build_floor_bot_entry(floor).name==expected[floor],"fallback identity at "..floor)
 assert(normalize("mine_"..floor,{floor=floor,name=legacy[floor]}).name==expected[floor],"stored legacy identity at "..floor)
 for _,generic in ipairs({"Монстр "..floor,"Шахтный монстр "..floor,"Страж шахты "..floor,"   "})do
  assert(normalize("mine_"..floor,{floor=floor,name=generic}).name==expected[floor])
 end
 local admin="Хранитель Северной штольни "..floor
 local bot=normalize("mine_"..floor,{floor=floor,name=admin,base_damage=991,reward_gold=1234})
 assert(bot.name==admin and bot.base_damage==991 and bot.reward_gold==1234,"custom name/stats must survive")
end
print("PASS mine monster names: all12 match monstr.txt; legacy/generic aliases migrate without storage writes; admin names/stats preserved")
