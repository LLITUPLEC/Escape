// Pure date/payload rules and the existing authenticated Console API only.
export const OWNER = "00000000-0000-0000-0000-000000000000";
export const COLLECTION = "duel_match3_server_aura";
export const STORAGE_PATH = `/v2/console/storage/${COLLECTION}/active/${OWNER}`;
export const EFFECTS = ["all_stats_pct", "hp_pct", "damage_pct", "armor_pct", "healing_pct", "crit_pct", "xp_bonus_pct", "mine_respawn_wait_pct", "gold_bonus_pct", "ore_bonus_pct", "matter_bonus_pct"];
const aliases = {all_stats_pct:"stats_bonus_pct",xp_bonus_pct:"xp_pct",mine_respawn_wait_pct:"mine_respawn_pct",gold_bonus_pct:"gold_pct",ore_bonus_pct:"ore_pct",matter_bonus_pct:"matter_pct"};
const clone = value => JSON.parse(JSON.stringify(value));
export class AuraError extends Error { constructor(code) { super(code); this.code = code; } }
const object = value => value !== null && typeof value === "object" && !Array.isArray(value);
export function canonical(value) {
    if (Array.isArray(value)) return "[" + value.map(canonical).join(",") + "]";
    if (object(value)) return "{" + Object.keys(value).sort().map(k => JSON.stringify(k) + ":" + canonical(value[k])).join(",") + "}";
    return JSON.stringify(value);
}
export function parseMoscow(input) {
    const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/.exec(input || "");
    if (!match) throw new AuraError("invalid_date");
    const [y,m,d,h,min] = match.slice(1).map(Number);
    if (y < 2020 || y > 2100 || m < 1 || m > 12 || d < 1 || d > 31 || h > 23 || min > 59) throw new AuraError("invalid_date");
    const ms = Date.UTC(y,m-1,d,h-3,min), check = new Date(ms + 3*3600000);
    if ([check.getUTCFullYear(),check.getUTCMonth()+1,check.getUTCDate(),check.getUTCHours(),check.getUTCMinutes()].some((v,i)=>v!==[y,m,d,h,min][i])) throw new AuraError("invalid_date");
    return ms / 1000;
}
export function inputMoscow(unix) { return new Date((Number(unix)+10800)*1000).toISOString().slice(0,16); }
export function displayMoscow(unix) {
    if (!Number.isFinite(Number(unix)) || Number(unix)<=0) return "Не задано";
    const value=inputMoscow(unix);return `${value.slice(8,10)}.${value.slice(5,7)}.${value.slice(0,4)} ${value.slice(11)} МСК`;
}
export function windowOf(value, row) {
    const start=Number(value?.started_at_unix ?? value?.anchor_unix) || 0;
    let end=Number(value?.ends_at_unix ?? value?.ends_at) || 0;
    if (!end && Number(value?.duration_hours)>0) {
        const updated=typeof row?.update_time==="string" ? Date.parse(row.update_time)/1000 : Number(row?.update_time);
        end=Math.round((start || updated || 0)+Number(value.duration_hours)*3600);
    }
    return {start,end};
}
export function stateOf(value, row, now=Math.floor(Date.now()/1000)) {
    if (!value || value.active===false || value.enabled===false) return "inactive";
    const {start,end}=windowOf(value,row);
    if (end>0 && now>=end) return "finished";
    if (start>0 && now<start) return "scheduled";
    return "running";
}
export function formOf(snapshot, now=Math.floor(Date.now()/1000)) {
    const value=snapshot?.value || {}, {start,end}=windowOf(value,snapshot);
    const duration=end>start && start>0 ? Math.max(1,Math.round((end-start)/60)) : Math.round(Number(value.duration_hours)*60) || 120;
    const form={active:value.active===true && value.enabled!==false,title:String(value.title || ""),description:String(value.description || ""),start:inputMoscow(start || now),hours:Math.floor(duration/60),minutes:duration%60};
    for (const key of EFFECTS) form[key]=Number(value[key] ?? value[aliases[key]] ?? (key.endsWith("_bonus_pct") && key!=="xp_bonus_pct" ? value.currency_bonus_pct : 0)) || 0;
    return form;
}
export function payloadOf(form,snapshot) {
    if (typeof form.active!=="boolean" || typeof form.title!=="string" || form.title.length>120 || typeof form.description!=="string" || form.description.length>1500) throw new AuraError("invalid_text");
    const hours=Number(form.hours),minutes=Number(form.minutes);
    if (!Number.isInteger(hours) || !Number.isInteger(minutes) || hours<0 || minutes<0 || minutes>59 || hours*60+minutes<1 || hours*60+minutes>525600) throw new AuraError("invalid_duration");
    let start=parseMoscow(form.start),end=start+(hours*60+minutes)*60;
    // A title/effect edit must not silently discard seconds on an old schedule.
    const prior=snapshot?.value, original=windowOf(prior,snapshot);
    if (original.start>0 && form.start===inputMoscow(original.start)) {
        start=original.start;end=start+(hours*60+minutes)*60;
        if (original.end>start && Math.round((original.end-start)/60)===hours*60+minutes) end=original.end;
    }
    const result={...(prior || {})};
    for (const key of ["enabled","anchor_unix","ends_at","currency_bonus_pct",...Object.values(aliases)]) delete result[key];
    Object.assign(result,{active:form.active,title:form.title.trim(),description:form.description.trim(),started_at_unix:start,ends_at_unix:end,duration_hours:(end-start)/3600});
    for (const key of EFFECTS) {
        const number=Number(form[key]);
        if (!Number.isFinite(number) || number < -95 || number > 1000 || (key==="crit_pct" && number>100) || (key==="mine_respawn_wait_pct" && number>95)) throw new AuraError("invalid_effect");
        result[key]=number;
    }
    if (result.active && !result.title) throw new AuraError("title_required");
    return result;
}
export function validateRow(row) {
    if (row===null) return null;
    const owners=[row?.user_id,row?.userId].filter(v=>v!==undefined);
    if (!object(row) || row.collection!==COLLECTION || row.key!=="active" || !owners.length || owners.some(v=>v!==OWNER) || typeof row.version!=="string" || !row.version || row.version==="*") throw new AuraError("invalid_row");
    const value=typeof row.value==="string" ? JSON.parse(row.value) : row.value;
    if (!object(value) || (row.permission_read ?? row.permissionRead ?? 0)!==0 || (row.permission_write ?? row.permissionWrite ?? 0)!==0) throw new AuraError("invalid_row");
    return {...row,value:clone(value)};
}
export class AuraConsole {
    constructor(origin,fetcher=globalThis.fetch) {
        const url=new URL(origin);
        if (url.origin!==origin || !(url.protocol==="https:" && url.hostname==="console.prokrust-play.ru" || url.protocol==="http:" && ["localhost","127.0.0.1","[::1]"].includes(url.hostname))) throw new AuraError("invalid_origin");
        // Browser fetch requires its Window receiver. Calling an unbound native
        // function as this.fetcher(...) would use AuraConsole and fail before HTTP.
        this.origin=origin;this.fetcher=fetcher.bind(globalThis);this.token="";this.epoch=0;this.snapshot=undefined;this.pending=null;
    }
    async send(path,method="GET",body,token=this.token,epoch=this.epoch) {
        if (![STORAGE_PATH,"/v2/console/authenticate","/v2/console/authenticate/logout"].includes(path)) throw new AuraError("invalid_path");
        const headers={"Content-Type":"application/json","Accept":"application/json"};if(token)headers.Authorization="Bearer "+token;
        let response;
        const controller=new AbortController(),timer=setTimeout(()=>controller.abort(),15000);
        try {response=await this.fetcher(this.origin+path,{method,headers,body:body===undefined?undefined:JSON.stringify(body),mode:"same-origin",credentials:"omit",cache:"no-store",redirect:"error",referrerPolicy:"no-referrer",signal:controller.signal});}
        catch {throw new AuraError("connection");}
        finally {clearTimeout(timer);}
        if(epoch!==this.epoch)throw new AuraError("session_changed");
        if (response.status===404 && method==="GET" && path===STORAGE_PATH) return null;
        let data;try {data=await response.json();}catch {throw new AuraError("invalid_response");}
        if(epoch!==this.epoch)throw new AuraError("session_changed");
        if (!response.ok) {
            if(response.status===401){this.token="";this.snapshot=undefined;this.epoch++;throw new AuraError("unauthorized");}
            if(data?.message==="Storage write rejected - version check failed.")throw new AuraError("conflict");
            throw new AuraError(response.status===429?"rate_limit":"server_error");
        }
        return data;
    }
    async login(username,password,mfa="") {
        this.epoch++;this.token="";this.snapshot=undefined;this.pending=null;
        let data;
        try {data=await this.send("/v2/console/authenticate","POST",{username,password,mfa});}
        catch(error) {
            if(error.code==="unauthorized")throw new AuraError("login_denied");
            if(error.code==="connection")throw new AuraError("login_connection");
            throw error;
        }
        if (typeof data?.token!=="string" || !data.token || data.mfa_code) throw new AuraError("mfa_setup");
        this.token=data.token;return this.load();
    }
    async load() {
        if (!this.token) throw new AuraError("unauthorized");
        const row=validateRow(await this.send(STORAGE_PATH));this.snapshot=row;return row;
    }
    async save(value) {
        if(!this.token || this.snapshot===undefined)throw new AuraError("reload_required");
        if(this.pending)throw new AuraError("uncertain");
        const version=this.snapshot?.version || "*";
        this.pending={value:clone(value),version};
        try {
            await this.send(STORAGE_PATH,"PUT",{collection:COLLECTION,key:"active",user_id:OWNER,value:JSON.stringify(value),version,permission_read:0,permission_write:0});
            const row=await this.load();
            if(!row || canonical(row.value)!==canonical(value))throw new AuraError("uncertain");
            this.pending=null;return row;
        } catch(error) {
            if(error.code==="conflict"){this.pending=null;this.snapshot=undefined;}
            throw error;
        }
    }
    async reconcile() {
        const intent=this.pending;
        const row=await this.load();
        const matched=!!(intent && row && canonical(intent.value)===canonical(row.value));
        this.pending=null;return {row,matched}; // No automatic PUT, even if unchanged.
    }
    async logout() {
        const token=this.token;this.token="";this.snapshot=undefined;this.pending=null;this.epoch++;
        if(token)try{await this.send("/v2/console/authenticate/logout","POST",{token},token);}catch{}
    }
}
