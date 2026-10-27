local nk=require('nakama')
local ok,R=pcall(require,'modules.duel_global_boss_ranking');if not ok then R=require('duel_global_boss_ranking')end
return{
 match_init=function(ctx,p)assert(R.bind(p.event_id,p.nonce,ctx.match_id));return{event_id=p.event_id,mid=ctx.match_id},1,'service=global_boss_rank'end,
 match_join_attempt=function(ctx,d,t,s)return s,false,'service_match'end,
 match_join=function(ctx,d,t,s)return s end,match_leave=function(ctx,d,t,s)return s end,
 match_loop=function(ctx,d,t,s)
  local ok,done=pcall(R.page,s.event_id,s.mid)
  if ok and done then return nil end
  if not ok and tostring(done):find('ranking_worker_replaced',1,true)then return nil end
  if not ok then nk.logger_warn('boss ranking deferred: '..tostring(done))end
  return s
 end,
 match_signal=function(ctx,d,t,s)return s,'service_match'end,match_terminate=function()return nil end,
}
