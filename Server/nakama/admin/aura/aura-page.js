import {AuraConsole,AuraError,EFFECTS,payloadOf,formOf,inputMoscow,displayMoscow,windowOf,stateOf,canonical} from "./aura-model.js";
const $=id=>document.getElementById(id);
const labels={inactive:"Неактивна",scheduled:"Запланирована",running:"Работает",finished:"Закончена"};
const errors={invalid_origin:"Откройте страницу на домене Console или через локальный SSH-туннель.",invalid_date:"Проверьте дату и время начала по МСК.",invalid_duration:"Длительность: от 1 минуты до 365 дней. Минуты — от 0 до 59.",invalid_text:"Проверьте название и описание.",title_required:"Для включённой ауры укажите название.",invalid_effect:"Проверьте диапазоны бонусов: от −95% до 1000%, крит — до 100%, сокращение ожидания — до 95%.",invalid_row:"Ответ не является защищённой системной записью ауры. Сохранение заблокировано.",invalid_response:"Console вернула неожиданный ответ. Загрузите данные заново.",login_denied:"Не удалось войти. Проверьте логин и пароль от Nakama Console. Одноразовый код нужен только при включённой двухфакторной защите.",login_connection:"Не удалось отправить запрос входа в Console. Обновите страницу и проверьте соединение. Настройки ауры не изменены.",unauthorized:"Сессия редактора ауры закончилась. Войдите на этой странице заново.",mfa_setup:"Завершите настройку MFA в обычной Console, затем войдите здесь снова.",rate_limit:"Достигнут лимит запросов. Подождите немного перед повторным входом.",conflict:"Другой администратор изменил ауру. Ваши изменения не записаны. Загрузите актуальные данные.",connection:"Нет подтверждения от Console. Проверьте соединение и загрузите состояние с сервера; запись автоматически не повторяется.",uncertain:"Запись пока не подтверждена. Нажмите «Загрузить с сервера», чтобы сравнить состояние. Автоматического повтора записи нет.",reload_required:"Сначала загрузите текущее состояние с сервера.",session_changed:"Сессия изменилась. Результат старого запроса не применён.",server_error:"Console не подтвердила действие. Загрузите текущее состояние перед повторным сохранением."};
let api,preview=null,busy=false;
function message(text,kind=""){ $("message").textContent=text;$("message").className="notice "+kind; }
function fail(error){message(errors[error?.code] || "Действие остановлено. Загрузите состояние с сервера.","error");preview=null;render();}
function data(){const v={active:$("active").checked,title:$("title").value,description:$("description").value,start:$("start").value,hours:$("hours").value,minutes:$("minutes").value};for(const key of EFFECTS)v[key]=$(key).value;return v;}
function fill(snapshot){const v=formOf(snapshot);for(const [key,value]of Object.entries(v)){if(key==="active")$(key).checked=value;else $(key).value=value;}preview=null;}
function render(){
 const authenticated=!!api?.token;$("login-card").hidden=authenticated;$("editor").hidden=!authenticated;
 for(const id of ["login","reload","backup","disable","preview","now"])$(id).disabled=busy;
 $("login").disabled=busy || !api;
 $("save").disabled=busy || !preview || !authenticated || api.snapshot===undefined || !!api.pending;
 if(!authenticated)return;
 const value=api.snapshot?.value, state=stateOf(value,api.snapshot),window=windowOf(value,api.snapshot);
 $("status").textContent=api.snapshot===undefined?"Нужно обновление":labels[state];$("current-window").textContent=api.snapshot===undefined?"Нужна повторная загрузка":api.snapshot===null?"Запись ещё не создана":`${displayMoscow(window.start)} → ${displayMoscow(window.end)}`;
 $("version").textContent=api.snapshot?.version?"Загруженная версия: "+api.snapshot.version:"Новая запись · создание только при её отсутствии";
 try{const draft=payloadOf(data(),api.snapshot);$("end-preview").textContent="Окончание: "+displayMoscow(draft.ends_at_unix);}catch{$("end-preview").textContent="Укажите дату начала и длительность.";}
}
async function action(fn){if(busy)return;busy=true;render();try{await fn();}catch(error){fail(error);}finally{busy=false;render();}}
function prepare(value){
 preview={value,fingerprint:canonical(data()),version:api.snapshot?.version || "*"};
 const state=stateOf(value,null);$("preview-title").textContent=value.active?value.title:"Отключение ауры";
 $("preview-summary").textContent=value.active?"После записи: "+labels[state].toLowerCase()+". Время по Москве, независимо от часового пояса компьютера.":"Бонусы ауры перестанут применяться после обновления кэша.";
 $("preview-window").textContent=value.active?`${displayMoscow(value.started_at_unix)} → ${displayMoscow(value.ends_at_unix)}`:"";
 $("preview-effects").textContent=value.active?"Общий бонус: "+value.all_stats_pct+"%; крит: "+value.crit_pct+" п.п.; опыт: "+value.xp_bonus_pct+"%.":"";
 $("payload").textContent=JSON.stringify(value,null,2);$("save").textContent=value.active?"Сохранить на сервере":"Подтвердить отключение";render();
}
try{api=new AuraConsole(location.origin);}catch(error){fail(error);$("login").disabled=true;}
$("login-form").addEventListener("submit",event=>{event.preventDefault();action(async()=>{const password=$("password").value,mfa=$("mfa").value;$("password").value="";$("mfa").value="";await api.login($("username").value.trim(),password,mfa);fill(api.snapshot);message("Данные загружены. Изменение формы само по себе ничего не записывает.","success");});});
$("logout").addEventListener("click",()=>{preview=null;api.logout();$("password").value="";$("mfa").value="";message("Вы вышли. Данные авторизации удалены из памяти страницы.");render();});
$("reload").addEventListener("click",()=>action(async()=>{const pending=!!api.pending;const result=await api.reconcile();fill(result.row);message(pending?(result.matched?"Предыдущая запись найдена на сервере и подтверждена.":"Загружено фактическое состояние. Оно отличается от прошлого намерения; автоматического повтора записи не было."):"Актуальное состояние загружено.","success");}));
$("aura-form").addEventListener("submit",event=>{event.preventDefault();try{if(api.snapshot===undefined)throw new AuraError("reload_required");prepare(payloadOf(data(),api.snapshot));message("Предпросмотр готов. Проверьте время и бонусы перед сохранением.");}catch(error){fail(error);}});
$("aura-form").addEventListener("input",()=>{preview=null;$("preview-summary").textContent="Форма изменилась. Выполните предпросмотр заново.";$("payload").textContent="Нет подготовленных изменений";render();});
$("now").addEventListener("click",()=>{$("start").value=inputMoscow(Math.floor(Date.now()/1000));preview=null;render();});
$("disable").addEventListener("click",()=>{try{if(api.snapshot===undefined)throw new AuraError("reload_required");const value={...(api.snapshot?.value || {}),active:false};prepare(value);message("Подготовлено отключение текущей серверной ауры. Оно ещё не сохранено.");}catch(error){fail(error);}});
$("save").addEventListener("click",()=>action(async()=>{if(!preview || preview.fingerprint!==canonical(data()) || preview.version!==(api.snapshot?.version || "*"))throw new AuraError("reload_required");const value=preview.value;await api.save(value);fill(api.snapshot);message("Сохранено и перечитано с сервера. "+labels[stateOf(value,api.snapshot)]+".","success");}));
$("backup").addEventListener("click",()=>{if(api.snapshot===undefined)return;const blob=new Blob([JSON.stringify({collection:"duel_match3_server_aura",key:"active",snapshot:api.snapshot},null,2)],{type:"application/json"});const url=URL.createObjectURL(blob),a=document.createElement("a");a.href=url;a.download="escape-aura-"+new Date().toISOString().replaceAll(":","-")+".json";a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);});
setInterval(()=>{if(api?.token)render();},1000);
render();
