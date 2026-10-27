// Node standard library only. Actual browser model/API, no account or network.
import {readFile} from "node:fs/promises";
import assert from "node:assert/strict";
const source=await readFile(new URL("../admin/aura/aura-model.js",import.meta.url),"utf8");
const M=await import("data:text/javascript;base64,"+Buffer.from(source).toString("base64"));
let passed=0;
async function test(name,fn){await fn();passed++;console.log("PASS "+name);}
const at=M.parseMoscow("2026-10-27T12:30");
const value={active:true,title:"Тестовая аура",description:"Описание",started_at_unix:at,ends_at_unix:at+7200,all_stats_pct:10,crit_pct:5};
function row(v=value,version="v1"){return{collection:M.COLLECTION,key:"active",user_id:M.OWNER,version,permission_read:0,permission_write:0,value:JSON.stringify(v)};}
function response(data,status=200){return{ok:status>=200&&status<300,status,json:async()=>data};}
async function rejects(p,code){await assert.rejects(p,e=>e.code===code);}
function fixture(){let current=row(),writes=0,requests=[],fail="",hold=null;
 const fetcher=async(url,opts)=>{requests.push({url,opts});assert.equal(opts.mode,"same-origin");assert.equal(opts.credentials,"omit");assert.equal(opts.redirect,"error");assert.equal(opts.cache,"no-store");assert.equal(opts.referrerPolicy,"no-referrer");
  if(url.endsWith("/authenticate"))return response({token:"private-test-token"});
  assert.equal(opts.headers.Authorization,"Bearer private-test-token");
  if(url.endsWith("/logout"))return response({});
  assert.equal(url,"https://console.prokrust-play.ru"+M.STORAGE_PATH);
  if(opts.method==="GET")return current?response(current):response({},404);
  assert.equal(opts.method,"PUT");writes++;
  const body=JSON.parse(opts.body);assert.equal(body.collection,M.COLLECTION);assert.equal(body.key,"active");assert.equal(body.user_id,M.OWNER);assert.equal(body.permission_read,0);assert.equal(body.permission_write,0);
  if(fail==="conflict")return response({message:"Storage write rejected - version check failed."},500);
  if(hold)return await hold;
  if(body.version!==(current?.version || "*"))return response({message:"Storage write rejected - version check failed."},500);
  current=row(JSON.parse(body.value),"v"+(writes+1));
  if(fail==="lost_ack")throw Error("socket closed");return response(current);
 };
 const api=new M.AuraConsole("https://console.prokrust-play.ru",fetcher);
 return{api,get current(){return current;},set current(v){current=v;},get writes(){return writes;},requests,set fail(v){fail=v;},set hold(v){hold=v;}};
}
await test("Moscow input converts independently of machine timezone",()=>{assert.equal(at,Date.UTC(2026,9,27,9,30)/1000);assert.equal(M.inputMoscow(at),"2026-10-27T12:30");});
await test("Moscow midnight and leap date roundtrip",()=>{for(const d of ["2026-01-01T00:00","2028-02-29T23:59"])assert.equal(M.inputMoscow(M.parseMoscow(d)),d);});
await test("invalid normalized calendar dates and offsets rejected",()=>{for(const d of ["2026-02-29T10:00","2026-04-31T10:00","2026-10-27T24:00","2026-10-27T12:60","2026-10-27T10:00Z","1999-01-01T10:00"])assert.throws(()=>M.parseMoscow(d));});
await test("state is scheduled until start, running at start, finished at end",()=>{assert.equal(M.stateOf(value,null,at-1),"scheduled");assert.equal(M.stateOf(value,null,at),"running");assert.equal(M.stateOf(value,null,at+7200),"finished");});
await test("disabled and absent records remain inactive",()=>{assert.equal(M.stateOf(null),"inactive");assert.equal(M.stateOf({...value,active:false}),"inactive");assert.equal(M.stateOf({...value,enabled:false}),"inactive");});
await test("payload sets both exact times and duration without mutating snapshot",()=>{const snapshot=M.validateRow(row());const original=M.canonical(snapshot);const form=M.formOf(snapshot);form.hours=3;form.minutes=15;const p=M.payloadOf(form,snapshot);assert.equal(p.ends_at_unix-at,11700);assert.equal(p.duration_hours,3.25);assert.equal(M.canonical(snapshot),original);});
await test("old seconds survive text-only edits",()=>{const snapshot=M.validateRow(row({...value,started_at_unix:at+17,ends_at_unix:at+7217}));const form=M.formOf(snapshot);form.title="Новый текст";const p=M.payloadOf(form,snapshot);assert.equal(p.started_at_unix,at+17);assert.equal(p.ends_at_unix,at+7217);});
await test("aliases migrate consistently while unrelated fields remain",()=>{const snapshot=M.validateRow(row({active:true,title:"x",enabled:false,anchor_unix:at,ends_at:at+3600,stats_bonus_pct:20,currency_bonus_pct:50,custom_flag:"keep"}));const form=M.formOf(snapshot);form.active=true;const p=M.payloadOf(form,snapshot);assert.equal(p.all_stats_pct,20);assert.equal(p.gold_bonus_pct,50);assert.equal(p.custom_flag,"keep");assert.equal(p.enabled,undefined);assert.equal(p.ends_at,undefined);});
await test("zero/fractional/oversized duration and invalid effects rejected",()=>{for(const extra of [{hours:0,minutes:0},{hours:1.5},{minutes:60},{hours:9000},{damage_pct:NaN},{crit_pct:101},{mine_respawn_wait_pct:96}])assert.throws(()=>M.payloadOf({...M.formOf(M.validateRow(row())),...extra},M.validateRow(row())));});
await test("foreign owner, conflicting aliases, unsafe permissions and wrong key refused",()=>{for(const extra of [{user_id:"foreign"},{userId:"foreign"},{permission_write:1},{key:"other"},{version:"*"},{value:"[]"}])assert.throws(()=>M.validateRow({...row(),...extra}));});
await test("origin cannot redirect token to arbitrary host",()=>{for(const origin of ["https://example.com","http://console.prokrust-play.ru","https://console.prokrust-play.ru/path","https://console.prokrust-play.ru.evil.test"])assert.throws(()=>new M.AuraConsole(origin));});
await test("unauthenticated read or write sends no request",async()=>{const f=fixture();await rejects(f.api.load(),"unauthorized");await rejects(f.api.save(value),"reload_required");assert.equal(f.requests.length,0);});
await test("login uses Console auth then reads fixed system aura",async()=>{const f=fixture();await f.api.login("admin","fake-password","123456");assert.equal(f.requests.length,2);assert.equal(JSON.parse(f.requests[0].opts.body).mfa,"123456");assert.equal(f.requests[0].opts.headers.Authorization,undefined);assert.equal(f.api.snapshot.version,"v1");});
await test("default browser transport keeps the Window receiver through login/read/write/logout",async()=>{
 const original=globalThis.fetch,f=fixture();let calls=0;
 globalThis.fetch=function(...args){assert.equal(this,globalThis,"Native browser fetch cannot use AuraConsole as its receiver");calls++;return f.api.fetcher(...args);};
 try{const api=new M.AuraConsole("https://console.prokrust-play.ru");await api.login("admin","fake-password");assert.equal(api.snapshot.version,"v1");await api.save({...value,title:"Receiver checked"});await api.logout();assert.equal(calls,5);assert.equal(f.writes,1);assert.equal(api.token,"");}
 finally{globalThis.fetch=original;}
});
await test("bad login credentials are distinct from an expired editor session",async()=>{
 const api=new M.AuraConsole("https://console.prokrust-play.ru",async()=>response({code:16,message:"Invalid credentials."},401));
 await rejects(api.login("missing-user","fake-password"),"login_denied");assert.equal(api.token,"");assert.equal(api.snapshot,undefined);
});
await test("login transport failure never suggests reloading a hidden aura editor",async()=>{
 const api=new M.AuraConsole("https://console.prokrust-play.ru",async()=>{throw new TypeError("Illegal invocation");});
 await rejects(api.login("missing-user","fake-password"),"login_connection");assert.equal(api.token,"");assert.equal(api.snapshot,undefined);assert.equal(api.pending,null);
});
await test("save uses loaded CAS version and verifies full reread",async()=>{const f=fixture();await f.api.login("admin","fake-password");const changed={...value,title:"Изменено"};const result=await f.api.save(changed);assert.equal(result.value.title,"Изменено");assert.equal(JSON.parse(f.requests.find(r=>r.opts.method==="PUT").opts.body).version,"v1");assert.equal(f.writes,1);assert.equal(f.api.pending,null);});
await test("missing row creates only with star version",async()=>{const f=fixture();f.current=null;await f.api.login("admin","fake-password");await f.api.save(value);assert.equal(JSON.parse(f.requests.find(r=>r.opts.method==="PUT").opts.body).version,"*");});
await test("stale operator cannot overwrite newer version or auto retry",async()=>{const f=fixture();await f.api.login("admin","fake-password");f.current=row({...value,title:"Другой оператор"},"v-new");await rejects(f.api.save(value),"conflict");assert.equal(f.writes,1);assert.equal(f.api.snapshot,undefined);assert.equal(JSON.parse(f.current.value).title,"Другой оператор");await rejects(f.api.save(value),"reload_required");});
await test("lost acknowledgement preserves intent; reconcile only reads and confirms",async()=>{const f=fixture();await f.api.login("admin","fake-password");f.fail="lost_ack";await rejects(f.api.save({...value,title:"Сохранено"}),"connection");assert(f.api.pending);await rejects(f.api.save(value),"uncertain");const resolved=await f.api.reconcile();assert.equal(resolved.matched,true);assert.equal(f.writes,1);assert.equal(f.api.pending,null);});
await test("reconcile differing actual value never writes",async()=>{const f=fixture();await f.api.login("admin","fake-password");f.api.pending={value,version:"v1"};f.current=row({...value,title:"Другая аура"},"other");assert.equal((await f.api.reconcile()).matched,false);assert.equal(f.writes,0);});
await test("logout invalidates pending JSON parse from old session",async()=>{const f=fixture();await f.api.login("admin","fake-password");let complete;f.api.fetcher=async()=>({ok:true,status:200,json:()=>new Promise(resolve=>complete=resolve)});const load=f.api.load();await new Promise(resolve=>setTimeout(resolve,0));const logout=f.api.logout();complete(row());await rejects(load,"session_changed");assert.equal(f.api.token,"");assert.equal(f.api.snapshot,undefined); // Logout transport need not finish to clear local authority.
});
console.log(JSON.stringify({passed:true,cases:passed,networkActions:false,accountActions:false}));
