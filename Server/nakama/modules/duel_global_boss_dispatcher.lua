local nk=require('nakama')
local function api()local ok,m=pcall(require,'modules.duel_global_boss');if not ok then m=require('duel_global_boss')end;return m end
return{
 match_init=function(ctx,p)
  if not p.bootstrap then assert(api().bind_worker(p.event_id,p.nonce,ctx.match_id))end
  return{event_id=p.event_id,bootstrap=p.bootstrap==true,next_retry=0},1,'service=global_boss_mail'
 end,
 match_join_attempt=function(ctx,d,t,s)return s,false,'service_match'end,
 match_join=function(ctx,d,t,s)return s end,
 match_leave=function(ctx,d,t,s)return s end,
 match_loop=function(ctx,d,t,s)
  if t>=s.next_retry then
   s.next_retry=t+3
   local ok,done
   if s.bootstrap then ok,done=pcall(api().recover_workers)else ok,done=pcall(api().dispatch_page,s.event_id)end
   if not ok then nk.logger_warn('boss mail deferred: '..tostring(done))end
   if ok and done then return nil end
  end
  return s
 end,
 match_terminate=function()return nil end,
 match_signal=function(ctx,d,t,s)return s,'service_match'end,
}
