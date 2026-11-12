using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using Project.Character;
using Project.Nakama;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Weekly raid dossier and one public damage row per participant across all attempts.</summary>
    public sealed class ExpeditionGlobalBossPanel : MonoBehaviour
    {
        private CancellationTokenSource _cts;
        private GlobalBossService.State _fixture;
        private GlobalBossLeaderboardService.Page _fixtureRanking;
        private TMP_Text _name,_time,_hp,_attempts,_contribution,_status,_enterLabel,_damage,_armor,_crit,_rules,_rankingStatus,_self,_pageLabel;
        private Image _hpFill;
        private Button _enter,_practice,_bossTab,_rankingTab,_prev,_next;
        private RectTransform _bossPage,_rankingPage,_rows;
        private ScrollRect _scroll;
        private bool _participants;
        private string _fingerprint,_cursor,_loadedEvent,_ownerScope;
        private bool _resetRankingScroll=true;
        private float _nextPoll,_nextClock;
        public static ExpeditionGlobalBossPanel Open(Transform host,GlobalBossService.State fixture=null,GlobalBossLeaderboardService.Page ranking=null)
        {
            var current=host.GetComponentInChildren<ExpeditionGlobalBossPanel>(true);if(current!=null)return current;
            var view=ExpeditionUi.Rect(host,"GlobalBossModal",0,0,1,1).gameObject.AddComponent<ExpeditionGlobalBossPanel>();
            view._fixture=fixture;view._fixtureRanking=ranking;view.Build();view.Refresh();
            if(fixture==null)_=GlobalBossService.RefreshAsync(view._cts.Token);return view;
        }
        private void Build()
        {
            _cts=new CancellationTokenSource();_nextPoll=Time.unscaledTime+10;
            GlobalBossService.Changed+=Refresh;GlobalBossLeaderboardService.Changed+=RefreshRanking;
            ExpeditionUi.Fill(transform,"Shade",0,0,1,1,new Color(0,0,0,.88f)).raycastTarget=true;
            var panel=ExpeditionUi.Panel(transform,"RiftDossier",.035f,.055f,.965f,.945f).rectTransform;
            Label(panel,"Kicker","МИРОВОЙ БОСС",22,.055f,.953f,.87f,.984f).color=ExpeditionTheme.Amber;
            ExpeditionUi.Button(panel,"Close","×",.89f,.94f,.973f,.984f,()=>Destroy(gameObject),false,40);
            _name=Label(panel,"BossName","СЕРДЦЕ РАЗЛОМА",42,.055f,.899f,.945f,.948f);_name.fontStyle=FontStyles.Bold;
            _bossTab=ExpeditionUi.Button(panel,"BossDossierTab","У РАЗЛОМА",.055f,.849f,.49f,.893f,()=>SelectTab(false),false,25);
            _rankingTab=ExpeditionUi.Button(panel,"BossParticipantsTab","ЛЕТОПИСЬ БОЯ",.51f,.849f,.945f,.893f,()=>SelectTab(true),false,25);
            _time=Label(panel,"EventClock","Связываемся с измерением…",22,.06f,.815f,.94f,.847f);
            _bossPage=ExpeditionUi.Rect(panel,"BossDossierPage",0,0,1,1);_bossPage.SetAsFirstSibling();
            var stage=ExpeditionUi.Rect(_bossPage,"RiftEncounter",.024f,.553f,.976f,.814f);
            var rift=ExpeditionUi.Fill(stage,"RiftLandscape",0,0,1,1,Color.white);
            rift.sprite=Resources.Load<Sprite>("Expedition/Art/encounter-boss");
            ExpeditionLocalAtmosphere.Install(stage,false);
            var health=Card(_bossPage,"SharedHealth",.047f,.502f,.953f,.566f);
            Picture(health,"SharedHealthGlyph",ExpeditionTheme.Icon("health"),.037f,.19f,.127f,.84f);
            Label(health,"HealthKicker","ОБЩЕЕ ЗДОРОВЬЕ БОССА",19,.142f,.60f,.96f,.94f).color=ExpeditionTheme.Muted;
            var track=ExpeditionUi.Fill(health,"HealthTrack",.142f,.18f,.96f,.59f,new Color32(8,12,18,255));track.sprite=ModalPanelCloseButton.WhiteSprite();
            _hpFill=ExpeditionUi.Fill(track.transform,"HealthFill",0,0,1,1,new Color32(148,57,130,255));_hpFill.sprite=ModalPanelCloseButton.WhiteSprite();_hpFill.type=Image.Type.Filled;_hpFill.fillMethod=Image.FillMethod.Horizontal;
            _hpFill.rectTransform.offsetMin=new Vector2(4,4);_hpFill.rectTransform.offsetMax=new Vector2(-4,-4);
            _hp=Label(track.transform,"HealthValue","…",27,.025f,.08f,.975f,.92f,TextAlignmentOptions.Center);_hp.fontStyle=FontStyles.Bold;
            BuildStats();
            Ability("Cry","УЖАСАЮЩИЙ КРИК","20% начального здоровья сквозь броню. Без маны, не завершает ход. Перезарядка — 2 хода босса.","horrifying-scream",.045f,.492f);
            Ability("Adaptation","АДАПТАЦИЯ","В начале своего хода босс повышает текущий урон и шанс крита. В первый ход — на 1% и 1 процентный пункт, во второй — на 2% и 2 пункта, и так далее.","adaptation",.508f,.955f);
            var loot=Card(_bossPage,"VictoryLoot",.045f,.157f,.955f,.263f);
            Label(loot,"RewardsKicker","ТРОФЕИ ЗА ОБЩУЮ ПОБЕДУ",20,.04f,.68f,.72f,.95f).color=ExpeditionTheme.Amber;
            ExpeditionUi.Button(loot,"RewardDetails","СОСТАВ ›",.75f,.68f,.97f,.97f,ShowRewardDetails,false,19);
            Reward(loot,"LegendaryRecipe","recipe_drop_t1_gold_Helmet","1 из 8\nлегендарных рецептов",.035f,.385f);
            Reward(loot,"Tesseract","tesseract","Тессеракт",.39f,.65f);
            Reward(loot,"Supplies",ConsumablePresentation.Bounty,"Ресурсы\nи припасы",.66f,.965f);
            _rankingPage=ExpeditionUi.Rect(panel,"BossParticipantsPage",0,.151f,1,.813f);BuildRanking();
            _attempts=Label(panel,"Attempts","…",23,.06f,.123f,.94f,.150f);
            _contribution=Label(panel,"Contribution","",20,.06f,.096f,.94f,.123f);
            _enter=ExpeditionUi.Button(panel,"EnterRift","ВОЙТИ · 1 ЖЕТОН",.055f,.052f,.70f,.095f,()=>{if(_fixture==null)_=GlobalBossService.EnterAsync(_cts.Token);},true,28);_enterLabel=_enter.GetComponentInChildren<TMP_Text>();
            ExpeditionUi.Button(panel,"BuyToken","МАГАЗИН",.72f,.052f,.947f,.095f,()=>ExpeditionShopPanel.Open(transform.parent),false,22);
            _practice=ExpeditionUi.Button(panel,"PracticeRift","ПРОБНЫЙ БОЙ · БЕЗ НАГРАД И ЗАТРАТ",.045f,.002f,.957f,.049f,()=>{if(_fixture==null)_=GlobalBossService.PracticeAsync(_cts.Token);},false,25);
            var practiceLabel=_practice.GetComponentInChildren<TMP_Text>();practiceLabel.color=ExpeditionTheme.Amber;practiceLabel.fontStyle=FontStyles.Bold;
            _status=Label(transform,"BossStatus","",20,.05f,.012f,.78f,.052f);
            ExpeditionUi.Button(transform,"RefreshEvent","Обновить",.80f,.012f,.95f,.052f,RefreshEvent,false,19);
            if(Application.isPlaying)
            {
                _bossPage.gameObject.AddComponent<ExpeditionPanelArrival>();
                _rankingPage.gameObject.AddComponent<ExpeditionPanelArrival>();
            }
            SelectTab(false);
        }
        private void RefreshEvent()
        {
            if (_fixture != null) return;
            if (!GlobalBossService.Busy) _ = GlobalBossService.RefreshAsync(_cts.Token);
            if (_participants) LoadRanking(_cursor);
        }
        private void BuildStats()
        {
            var stats=ExpeditionUi.Rect(_bossPage,"BossCharacteristics",.045f,.413f,.955f,.494f);
            _damage=Stat(stats,"damage","УРОН",0,.32f);
            _armor=Stat(stats,"armor","БРОНЯ",.34f,.66f);
            _crit=Stat(stats,"critical","КРИТ",.68f,1);
            _rules=Label(stats,"BossLimits","Не лечится · до 30 ходов",20,.05f,.01f,.95f,.30f,TextAlignmentOptions.Center);
            _rules.color=ExpeditionTheme.Muted;
        }
        private static TMP_Text Stat(Transform host,string id,string caption,float x0,float x1)
        {
            var seal=Card(host,"StatSeal-"+id,x0,.34f,x1,1);
            Picture(seal,"StatIcon-"+id,ExpeditionTheme.Icon(id),.07f,.16f,.30f,.85f);
            Label(seal,"StatName-"+id,caption,18,.35f,.54f,.94f,.91f).color=ExpeditionTheme.Muted;
            var value=Label(seal,"StatValue-"+id,"…",28,.35f,.09f,.94f,.56f);
            value.fontStyle=FontStyles.Bold;
            return value;
        }
        private void Ability(string name,string title,string detail,string icon,float x0,float x1)
        {
            var card=Card(_bossPage,name,x0,.272f,x1,.412f);
            Picture(card,"AbilityIcon-"+name,Resources.Load<Sprite>("Expedition/Boss/0314/"+icon),.047f,.62f,.26f,.94f);
            var heading=Label(card,"Title",title,24,.28f,.61f,.955f,.95f);
            heading.fontStyle=FontStyles.Bold;
            heading.color=new Color32(230,192,254,255);
            Label(card,"Description",detail,22,.055f,.05f,.945f,.605f).color=ExpeditionTheme.Muted;
        }
        private void BuildRanking()
        {
            var banner=Card(_rankingPage,"ChroniclePlaque",.045f,.895f,.955f,.99f);
            Picture(banner,"ChronicleEmblem",ExpeditionTheme.Icon("trophy"),.025f,.09f,.15f,.91f);
            _rankingStatus=Label(banner,"RankingIntro","Суммарный урон всех попыток за это событие.",23,.17f,.08f,.96f,.92f);
            _rankingStatus.color=ExpeditionTheme.Muted;
            var viewport=ExpeditionUi.Rect(_rankingPage,"ParticipantsViewport",.045f,.205f,.955f,.88f);viewport.gameObject.AddComponent<RectMask2D>();
            var hit=viewport.gameObject.AddComponent<Image>();hit.color=Color.clear;hit.raycastTarget=true;
            _rows=ExpeditionUi.Rect(viewport,"ParticipantsRows",0,0,1,1);_rows.anchorMin=new Vector2(0,1);_rows.anchorMax=Vector2.one;_rows.pivot=new Vector2(.5f,1);
            _scroll=viewport.gameObject.AddComponent<ScrollRect>();_scroll.viewport=viewport;_scroll.content=_rows;_scroll.horizontal=false;_scroll.movementType=ScrollRect.MovementType.Clamped;_scroll.scrollSensitivity=40;
            _prev=ExpeditionUi.Button(_rankingPage,"ParticipantsPrevious","‹ НАЗАД",.055f,.113f,.33f,.183f,()=>LoadRanking(Page?.prev_cursor),false,23);
            _next=ExpeditionUi.Button(_rankingPage,"ParticipantsNext","ДАЛЕЕ ›",.67f,.113f,.945f,.183f,()=>LoadRanking(Page?.next_cursor),false,23);
            _pageLabel=Label(_rankingPage,"RankingRange","",21,.34f,.113f,.66f,.183f,TextAlignmentOptions.Center);
            var self=Card(_rankingPage,"PersonalRank",.045f,.005f,.955f,.10f);
            _self=Label(self,"PersonalRankText","Нанесите урон, чтобы попасть в рейтинг.",23,.04f,.12f,.96f,.88f);_self.color=ExpeditionTheme.Amber;
        }
        public void SelectTab(bool participants)
        {
            _participants=participants;_bossPage.gameObject.SetActive(!participants);_rankingPage.gameObject.SetActive(participants);
            Tab(_bossTab,!participants);Tab(_rankingTab,participants);
            if(participants){RefreshRanking();if(_fixture==null)LoadRanking(_cursor);}
        }
        private static void Tab(Button tab,bool selected)
        {
            var text=tab.GetComponentInChildren<TMP_Text>();
            text.color=selected?new Color32(241,201,252,255):ExpeditionTheme.Ivory;
            text.fontStyle=selected?FontStyles.Bold:FontStyles.Normal;
            var surface=tab.targetGraphic;
            if(surface!=null)surface.color=selected?new Color32(195,146,224,255):Color.white;
        }
        private GlobalBossLeaderboardService.Page Page=>_fixture!=null?_fixtureRanking:GlobalBossLeaderboardService.Current;
        private void LoadRanking(string cursor)
        {
            if(_fixture!=null)return;ObserveScope();var eventId=GlobalBossService.Current?.event_id;
            if(string.IsNullOrEmpty(eventId)||GlobalBossLeaderboardService.Busy)return;
            if(_loadedEvent!=eventId)cursor=null;
            if(_cursor!=cursor)_resetRankingScroll=true;
            _cursor=cursor;_loadedEvent=eventId;_=GlobalBossLeaderboardService.RefreshAsync(eventId,cursor,_cts.Token);
        }
        private void RefreshRanking()
        {
            if(_rows==null)return;var page=Page;var state=_fixture??GlobalBossService.Current;
            var busy=_fixture==null&&GlobalBossLeaderboardService.Busy;
            if(page==null||page.event_id!=state?.event_id)
            {
                ClearRows();_fingerprint=null;_self.text="";_prev.interactable=_next.interactable=false;_pageLabel.text="";
                if (_fixture != null) _rankingStatus.text = "Пока нет участников";
                else if (state == null)
                    _rankingStatus.text = GlobalBossService.Error ??
                        (GlobalBossService.Busy ? "Загружаем данные события…" : "Обновите данные события, чтобы открыть летопись.");
                else _rankingStatus.text = GlobalBossLeaderboardService.Error ??
                    (busy ? "Загружаем летопись…" : "Летопись пока недоступна. Нажмите «Обновить».");
                return;
            }
            _rankingStatus.text=(_fixture==null?GlobalBossLeaderboardService.Error:null)??(page.updating?"Вклад сохраняется. Таблица обновляется…":"Суммарный урон всех попыток за это событие.");
            var fingerprint=JsonUtility.ToJson(page);
            if(fingerprint!=_fingerprint)
            {
                var scroll=_resetRankingScroll?1:_scroll.verticalNormalizedPosition;ClearRows();_fingerprint=fingerprint;var entries=page.entries??Array.Empty<GlobalBossLeaderboardService.Entry>();
                if(entries.Length==0){_rows.sizeDelta=new Vector2(0,190);Label(_rows,"EmptyParticipants","ПЕРВЫЙ УДАР ЕЩЁ ВПЕРЕДИ\nНанесите урон и откройте таблицу участников.",26,.05f,0,.95f,1,TextAlignmentOptions.Center);}
                else
                {
                    _rows.sizeDelta=new Vector2(0,entries.Length*132);
                    for(var i=0;i<entries.Length;i++)Participant(entries[i],i,state?.hp_max??0);
                }
                Canvas.ForceUpdateCanvases();_scroll.verticalNormalizedPosition=scroll;_resetRankingScroll=false;
            }
            var rows=page.entries??Array.Empty<GlobalBossLeaderboardService.Entry>();_pageLabel.text=rows.Length==0?"":rows[0].rank+"–"+rows[rows.Length-1].rank;
            _prev.interactable=!busy&&!string.IsNullOrEmpty(page.prev_cursor);_next.interactable=!busy&&!string.IsNullOrEmpty(page.next_cursor);
            var me=page.self_entry;_self.text=me==null||me.rank==0&&me.damage==0?"Нанесите урон, чтобы попасть в рейтинг.":me.rank==0?"ВАШ ВКЛАД: "+Number(me.damage)+" · место уточняется…":$"ВАШЕ МЕСТО: {me.rank}   ·   УРОН: {Number(me.damage)}   ·   ПОПЫТОК: {me.attempts_used}/3";
        }
        private void ClearRows(){foreach(Transform child in _rows){child.gameObject.SetActive(false);Destroy(child.gameObject);}}
        private void Participant(GlobalBossLeaderboardService.Entry entry,int index,long maxHp)
        {
            var root=Card(_rows,"Participant-"+entry.rank,.005f,0,.995f,1);root.anchorMin=new Vector2(.005f,1);root.anchorMax=new Vector2(.995f,1);root.pivot=new Vector2(.5f,1);root.sizeDelta=new Vector2(0,122);root.anchoredPosition=new Vector2(0,-index*132);
            var medal=ExpeditionUi.Rect(root,"RankMedal",.01f,.18f,.105f,.82f).gameObject.AddComponent<ExpeditionCircleGraphic>();
            medal.color=entry.rank<=3?new Color32(94,57,28,255):new Color32(33,33,55,255);
            medal.raycastTarget=false;
            var rank=Label(medal.transform,"Rank",entry.rank.ToString(),26,0,.06f,1,.94f,TextAlignmentOptions.Center);
            rank.color=entry.rank<=3?ExpeditionTheme.Amber:ExpeditionTheme.Muted;
            rank.fontStyle=FontStyles.Bold;
            Picture(root,"ParticipantAvatar",CharacterAppearance.Load(entry.avatar_id,true)??ExpeditionTheme.Icon("hero"),.105f,.10f,.235f,.90f);
            var name=Label(root,"ParticipantName",string.IsNullOrWhiteSpace(entry.display_name)?"Игрок":entry.display_name,27,.26f,.48f,.71f,.88f);name.richText=false;name.color=entry.is_self?ExpeditionTheme.Amber:ExpeditionTheme.Ivory;
            Label(root,"ParticipantAttempts","Попыток: "+entry.attempts_used+" / 3",20,.26f,.13f,.70f,.46f).color=ExpeditionTheme.Muted;
            Label(root,"ParticipantDamage",Number(entry.damage),29,.70f,.49f,.97f,.88f,TextAlignmentOptions.Right).fontStyle=FontStyles.Bold;
            Label(root,"ParticipantPercent",maxHp>0?(100.0*entry.damage/maxHp).ToString("0.00",CultureInfo.GetCultureInfo("ru-RU"))+"% здоровья босса":"",18,.72f,.13f,.97f,.46f,TextAlignmentOptions.Right).color=ExpeditionTheme.Muted;
        }
        private static void Reward(Transform parent,string name,string id,string text,float x0,float x1)
        {
            var catalog=Resources.FindObjectsOfTypeAll<ItemCatalog>().FirstOrDefault(c=>c.Get(id)!=null);var definition=catalog!=null?catalog.Get(id):null;
            Picture(parent,name+"Icon",ConsumablePresentation.IsKnown(id)?ConsumablePresentation.Icon(id):definition!=null?catalog.GetDisplayIcon(definition):null,x0,.15f,x0+(x1-x0)*.34f,.65f);
            Label(parent,name+"Label",text,19,x0+(x1-x0)*.34f,.09f,x1,.68f);
        }
        private static void Picture(Transform host,string name,Sprite sprite,float x0,float y0,float x1,float y1){var image=ExpeditionUi.Rect(host,name,x0,y0,x1,y1).gameObject.AddComponent<Image>();image.sprite=sprite;image.preserveAspect=true;image.raycastTarget=false;}
        private static RectTransform Card(Transform host,string name,float x0,float y0,float x1,float y1)
        {
            var card=ExpeditionUi.Fill(host,name,x0,y0,x1,y1,new Color32(24,24,39,255));
            var edge=ExpeditionUi.Panel(card.transform,"MetalRim",0,0,1,1);
            edge.fillCenter=false;
            edge.color=new Color32(188,156,206,255);
            return card.rectTransform;
        }
        private void ShowRewardDetails()
        {
            if(transform.Find("BossRewardsDetailsModal")!=null)return;
            var modal=ExpeditionUi.Rect(transform,"BossRewardsDetailsModal",0,0,1,1);ExpeditionUi.Fill(modal,"Shade",0,0,1,1,new Color(0,0,0,.91f)).raycastTarget=true;
            var panel=ExpeditionUi.Panel(modal,"RewardDetailsCard",.075f,.23f,.925f,.77f);
            Label(panel.transform,"Title","ДОБЫЧА РАЗЛОМА",34,.055f,.85f,.945f,.96f).fontStyle=FontStyles.Bold;
            var text="Случайный легендарный рецепт — 1 из 8\nТессеракт ×1\nСлучайный боевой эликсир ×1\nСвиток щедрой добычи ×1";
            foreach(var reward in (_fixture??GlobalBossService.Current)?.rewards_preview??Array.Empty<GlobalBossService.Reward>())
                if(reward.resource=="gold"||reward.resource=="ore"||reward.resource=="matter")text+="\n"+(reward.resource=="gold"?"Золото":reward.resource=="ore"?"Руда":"Материя")+" +"+Number(reward.amount);
            Label(panel.transform,"Composition",text,28,.075f,.36f,.925f,.83f);
            Label(panel.transform,"Terms","Нанесите боссу урон. Награда будет отправлена в почту, только если общее здоровье босса снизится до нуля до конца события. Получить её можно и после выхода из игры.",24,.075f,.17f,.925f,.35f).color=ExpeditionTheme.Muted;
            ExpeditionUi.Button(panel.transform,"Close","ПОНЯТНО",.18f,.04f,.82f,.15f,()=>Destroy(modal.gameObject),true,29);
        }
        private static TMP_Text Label(Transform host,string name,string text,float size,float x0,float y0,float x1,float y1,TextAlignmentOptions alignment=TextAlignmentOptions.MidlineLeft)
        {
            var label=ExpeditionUi.Text(host,name,text,size,x0,y0,x1,y1,alignment);label.margin=Vector4.zero;label.enableAutoSizing=true;label.fontSizeMin=size-3;label.fontSizeMax=size;return label;
        }
        private void Refresh()
        {
            if(_status==null)return;ObserveScope();var state=_fixture??GlobalBossService.Current;var busy=_fixture==null&&GlobalBossService.Busy;
            var error=_fixture==null?GlobalBossService.Error:null;
            _status.text=_fixture==null?error??(busy?"Связываемся с измерением…":"До 3 попыток за событие. Награды доставляются в почту."):"Просмотр события";
            _enter.interactable=state!=null&&!busy&&state.hp>0&&state.status=="active"&&(state.active_attempt?.resumable==true||state.attempts_left>0&&state.token_count>0);
            _practice.interactable=!busy;
            if(state==null)
            {
                _name.text="МИРОВОЙ БОСС";_hp.text="—";_hpFill.fillAmount=0;
                _attempts.text="";_contribution.text="";
                _enterLabel.text=error!=null?"ВХОД НЕДОСТУПЕН":"ОЖИДАЕМ ДАННЫЕ";
                _time.text=error!=null?error:busy?"Загружаем данные события…":"Ожидаем данные события…";
                _damage.text=_armor.text=_crit.text="—";
                RefreshRanking();
                return;
            }
            _name.text=string.IsNullOrEmpty(state.name)?"СЕРДЦЕ РАЗЛОМА":state.name.ToUpperInvariant();_hp.text=Number(state.hp)+" / "+Number(state.hp_max);_hpFill.fillAmount=state.hp_max>0?(float)((double)state.hp/state.hp_max):0;
            _damage.text=Number(state.base_damage);_armor.text=Number(state.base_armor);_crit.text=(state.base_crit*100).ToString("0.#",CultureInfo.GetCultureInfo("ru-RU"))+"%";_rules.text="Не лечится · до "+(state.max_turns>0?state.max_turns:30)+" ходов";
            _attempts.text=$"ПОПЫТКИ: {state.attempts_left} / {state.attempts_total}     ЖЕТОНЫ: {state.token_count}";
            _contribution.text="ВАШ ВКЛАД: "+Number(state.my_damage)+(state.qualified?" · награда обеспечена при победе":state.eligibility_required>0?" · до награды "+Number(Math.Max(0,state.eligibility_required-state.eligibility_progress))+" урона":"");
            _enterLabel.text=busy?"ПОДТВЕРЖДЕНИЕ…":state.hp<=0?"БОСС ПОВЕРЖЕН":state.status!="active"?"СОБЫТИЕ ЗАВЕРШЕНО":state.active_attempt?.resumable==true?"ПРОДОЛЖИТЬ БОЙ":state.attempts_left<=0?"ПОПЫТКИ ИСПОЛЬЗОВАНЫ":"ВОЙТИ · 1 ЖЕТОН";
            Clock();RefreshRanking();if(_participants&&_fixture==null&&_loadedEvent!=state.event_id){_cursor=null;LoadRanking(null);}
        }
        private void ObserveScope()
        {
            if(_fixture!=null)return;var scope=LocalAccountCacheScope.Current;if(scope==_ownerScope)return;
            _ownerScope=scope;_cursor=_loadedEvent=_fingerprint=null;_resetRankingScroll=true;
        }
        private void Clock()
        {
            var state=_fixture??GlobalBossService.Current;if(state==null)return;var seconds=Math.Max(0,state.ends_at-(_fixture==null?GlobalBossService.ServerTime:state.server_now));var span=TimeSpan.FromSeconds(seconds);
            _time.text=state.hp<=0?"Общая победа! Заберите письмо с наградой.":seconds==0?"Событие завершено. Награды доступны при победе.":$"До конца события: {span.Days} д. {span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}";
        }
        private void Update()
        {
            if(Time.unscaledTime>=_nextClock){_nextClock=Time.unscaledTime+1;Clock();}
            if(_fixture==null&&Time.unscaledTime>=_nextPoll){_nextPoll=Time.unscaledTime+10;if(!GlobalBossService.Busy)_=GlobalBossService.RefreshAsync(_cts.Token);if(_participants)LoadRanking(_cursor);}
        }
        public static string Number(long value)=>value.ToString("N0",CultureInfo.GetCultureInfo("ru-RU"));
        private void OnDestroy(){GlobalBossService.Changed-=Refresh;GlobalBossLeaderboardService.Changed-=RefreshRanking;_cts?.Cancel();_cts?.Dispose();}
    }
}
