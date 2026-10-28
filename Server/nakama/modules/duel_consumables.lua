local nk = require("nakama")
local function load(n) local ok,m=pcall(require,"modules."..n);if ok then return m end;return require(n) end
local Effects = load("duel_consumable_effects")
local Inventory = load("duel_match3_inventory")
local M = { Effects = Effects }

function M.status(sheet, now)
  now=math.floor(tonumber(now) or os.time())
  local state=Effects.normalize(sheet)
  local arm=state.armed or {};local bounty=state.bounty or {}
  return {schema=1,server_now=now,armed_item_id=arm.item_id or "",armed_at=arm.armed_at or 0,
    bounty_started_at=bounty.started_at or 0,bounty_ends_at=bounty.ends_at or 0,
    bounty_active=Effects.bounty_multiplier(bounty,now)>1,next_use_seq=((state.use_ledger or {}).last_seq or 0)+1}
end

function M.new(d)
  local C={}
  local function guarded(ctx,payload,fn)
    local uid=ctx and ctx.user_id
    if type(uid)~="string" or uid=="" then return nk.json_encode({ok=false,err="unauthorized"}) end
    local valid,why=d.Guard.assert_client_epoch_matches(uid,payload)
    if not valid then return nk.json_encode({ok=false,err=why}) end
    local decoded,p=pcall(nk.json_decode,payload or "{}")
    if not decoded or type(p)~="table" then return nk.json_encode({ok=false,err="bad_payload"}) end
    return fn(uid,p)
  end
  function C.rpc_get(ctx,payload)
    local ok,result=pcall(guarded,ctx,payload,function(uid)
      return nk.json_encode({ok=true,consumables=M.status(d.read_sheet(uid))})
    end)
    if ok then return result end
    nk.logger_error("consumables get: "..tostring(result));return nk.json_encode({ok=false,err="storage_unavailable"})
  end
  function C.rpc_use(ctx,payload)
    local ok,result=pcall(guarded,ctx,payload,function(uid,p)
      if type(p.request_id)~="string" or #p.request_id<8 or #p.request_id>80 or p.request_id:find("[^%w_%-]") then
        return nk.json_encode({ok=false,err="bad_request_id"})
      end
      if type(p.use_seq)~="number" or p.use_seq<1 or p.use_seq~=math.floor(p.use_seq) or p.use_seq>=9007199254740991 then
        return nk.json_encode({ok=false,err="bad_use_sequence"})
      end
      if type(p.inv_index)~="number" or p.inv_index~=math.floor(p.inv_index) or p.inv_index<0 or p.inv_index>=50
          or type(p.expected_def_id)~="string" or not Effects.DEFS[p.expected_def_id] then
        return nk.json_encode({ok=false,err="bad_consumable_request"})
      end
      for attempt=1,5 do
        local sheet=d.read_sheet(uid);Inventory.normalize(sheet)
        local state=Effects.normalize(sheet)
        local ledger=state.use_ledger or {schema=1,last_seq=0,receipts={}}
        for _,receipt in ipairs(ledger.receipts) do
          if receipt.request_id==p.request_id or receipt.use_seq==p.use_seq then
            if receipt.request_id~=p.request_id or receipt.use_seq~=p.use_seq or receipt.inv_index~=p.inv_index or receipt.item_id~=p.expected_def_id then
              return nk.json_encode({ok=false,err="request_id_reused"})
            end
            return d.encode_character(sheet,d.read_progress(uid),uid)
          end
        end
        if p.use_seq<=ledger.last_seq then return nk.json_encode({ok=false,err="request_compacted"}) end
        if p.use_seq~=ledger.last_seq+1 then return nk.json_encode({ok=false,err="sequence_changed"}) end
        local defs=d.get_defs();local def=defs and defs[p.expected_def_id]
        if not def or def.kind~="consumable" then return nk.json_encode({ok=false,err="consumable_catalog_unavailable"}) end
        local index=p.inv_index+1
        if index>Inventory.capacity(sheet) or sheet.inventory[index]~=p.expected_def_id or (sheet.inventory_counts[index] or 0)<1 then
          return nk.json_encode({ok=false,err="inventory_changed"})
        end
        local now=os.time()
        if Effects.is_elixir(p.expected_def_id) then
          if state.armed then return nk.json_encode({ok=false,err="consumable_already_armed"}) end
          state.armed={item_id=p.expected_def_id,activation_id=p.request_id,armed_at=now}
        else
          if Effects.bounty_multiplier(state.bounty,now)>1 then return nk.json_encode({ok=false,err="bounty_already_active"}) end
          state.bounty={activation_id=p.request_id,started_at=now,ends_at=now+Effects.BOUNTY_SECONDS}
        end
        sheet.inventory_counts[index]=sheet.inventory_counts[index]-1
        if sheet.inventory_counts[index]==0 then sheet.inventory[index]="" end
        local receipt={request_id=p.request_id,use_seq=p.use_seq,inv_index=p.inv_index,item_id=p.expected_def_id,activation_id=p.request_id,activated_at=now,
          ends_at=state.bounty and p.expected_def_id=="scroll_pve_bounty" and state.bounty.ends_at or 0}
        local recent={receipt};for _,old in ipairs(ledger.receipts) do if #recent<4 then recent[#recent+1]=old end end
        state.use_ledger={schema=1,last_seq=p.use_seq,receipts=recent}
        local committed,err=pcall(nk.storage_write,{d.build_sheet_write(uid,sheet)})
        if committed then return d.encode_character(sheet,d.read_progress(uid),uid) end
        if not tostring(err):find("version",1,true) or attempt==5 then return nk.json_encode({ok=false,err="storage_unavailable"}) end
      end
    end)
    if ok then return result end
    nk.logger_error("consumables use: "..tostring(result));return nk.json_encode({ok=false,err="storage_unavailable"})
  end

  -- The reveal may show the equipped effect, but cannot spend its armed receipt.
  -- Ready/first accepted gameplay commits the actual CAS binding below.
  function C.preview_match(state,users)
    if not Effects.eligible(state)then return true end
    local preview={}
    for _,uid in ipairs(users or{})do if d.is_human(uid)then
      local sheet=d.read_sheet(uid);local data=Effects.normalize(sheet);local arm=data.armed or{}
      local receipt={item_id=arm.item_id or"",activation_id=arm.activation_id or""}
      if state.mode=="pve"and Effects.bounty_multiplier(data.bounty,os.time())>1 then receipt.bounty=data.bounty end
      preview[uid]=receipt
    end end
    state._consumable_preview=preview
    return true
  end

  -- Called only once the real battle is ready to begin, never during match_create
  -- or matchmaking. Both human Pro participants claim in one atomic CAS batch.
  function C.bind_match(state, users)
    if not Effects.eligible(state) then return true end
    if state._consumable_bindings then return true end
    local match_id=tostring(state.match_id or state.stats_result_id or "")
    if match_id=="" then return false,"match_id_required" end
    local mode=state.mode=="pve" and "pve" or "pro"
    local specs={}
    for _,uid in ipairs(users or {}) do
      if d.is_human(uid) then
        local user_id=uid
        local id="consumable-bind:"..match_id
        specs[#specs+1]={uid=uid,id=id,kind="consumable_bind",ticket=d.journal().ticket and d.journal().ticket(uid,id),plan={schema=1,match_id=match_id,mode=mode},
          build=function()
            local sheet=d.read_sheet(user_id);local data=Effects.normalize(sheet);local arm=data.armed or {}
            local receipt={item_id=arm.item_id or "",activation_id=arm.activation_id or "",bound_at=os.time()}
            if mode=="pve" and Effects.bounty_multiplier(data.bounty,receipt.bound_at)>1 then
              receipt.bounty={started_at=data.bounty.started_at,ends_at=data.bounty.ends_at,activation_id=data.bounty.activation_id}
            end
            data.armed=nil
            -- Even empty arms carry a CAS write: activation racing the initial snapshot
            -- is ordered before this battle or after it, never invisibly skipped.
            local writes={d.build_sheet_write(user_id,sheet)}
            if d.bind_extra_writes then
              local extra,why=d.bind_extra_writes(state,user_id)
              if not extra then return nil,nil,why end
              for _,op in ipairs(extra) do writes[#writes+1]=op end
            end
            return writes,receipt
          end}
      end
    end
    if #specs==0 then state._consumable_bindings={};return true end
    local ok,err,receipts=d.journal().atomic_many(specs)
    if not ok and err=="storage_unavailable" then
      -- Internal acknowledgement loss must not make match_join terminate a
      -- successfully committed start. Re-read the same immutable receipts;
      -- an uncommitted failure is still all-or-nothing and fails closed.
      ok,err,receipts=d.journal().atomic_many(specs)
    end
    if not ok then return false,err end
    state._consumable_bindings={}
    for i,spec in ipairs(specs) do state._consumable_bindings[spec.uid]=receipts[i] end
    return true
  end
  function C.apply_match(state)
    for uid,receipt in pairs(state._consumable_bindings or state._consumable_preview or {}) do Effects.apply(state.stats[uid],receipt) end
  end
  return C
end
return M
