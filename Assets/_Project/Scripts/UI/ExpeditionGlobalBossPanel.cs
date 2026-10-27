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
        private Button _enter,_bossTab,_rankingTab,_prev,_next;
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
            Label(panel,"Kicker","ЕЖЕНЕДЕЛЬНЫЙ МИРОВОЙ БОСС",22,.055f,.95f,.87f,.982f).color=ExpeditionTheme.Amber;
            ExpeditionUi.Button(panel,"Close","×",.89f,.94f,.973f,.984f,()=>Destroy(gameObject),false,40);
            _name=Label(panel,"BossName","СЕРДЦЕ РАЗЛОМА",37,.055f,.90f,.945f,.946f);_name.fontStyle=FontStyles.Bold;
            _bossTab=ExpeditionUi.Button(panel,"BossDossierTab","БОСС",.055f,.849f,.49f,.893f,()=>SelectTab(false),false,25);
            _rankingTab=ExpeditionUi.Button(panel,"BossParticipantsTab","УЧАСТНИКИ",.51f,.849f,.945f,.893f,()=>SelectTab(true),false,25);
            _time=Label(panel,"EventClock","Связываемся с измерением…",21,.06f,.813f,.94f,.847f);
            _bossPage=ExpeditionUi.Rect(panel,"BossDossierPage",0,0,1,1);_bossPage.SetAsFirstSibling();
            Picture(_bossPage,"BossArt",Resources.Load<Sprite>(GlobalBossService.Art),.09f,.604f,.48f,.81f);
            BuildStats();
            var health=Card(_bossPage,"SharedHealth",.045f,.525f,.955f,.598f);
            Label(health,"HealthKicker","ОБЩЕЕ ЗДОРОВЬЕ ДЛЯ ВСЕХ ИГРОКОВ",18,.03f,.66f,.97f,.97f).color=ExpeditionTheme.Muted;
            var track=ExpeditionUi.Fill(health,"HealthTrack",.035f,.14f,.965f,.61f,new Color32(8,12,18,255));track.sprite=ModalPanelCloseButton.WhiteSprite();
            _hpFill=ExpeditionUi.Fill(track.transform,"HealthFill",0,0,1,1,new Color32(142,51,124,255));_hpFill.sprite=ModalPanelCloseButton.WhiteSprite();_hpFill.type=Image.Type.Filled;_hpFill.fillMethod=Image.FillMethod.Horizontal;
            _hp=Label(track.transform,"HealthValue","…",27,0,0,1,1,TextAlignmentOptions.Center);_hp.fontStyle=FontStyles.Bold;
            Ability("Cry","УЖАСАЮЩИЙ КРИК","20% вашего начального здоровья сквозь броню. Без маны, не завершает ход. Перезарядка — 2 хода босса.","horrifying-scream",.395f,.517f);
            Ability("Adaptation","АДАПТАЦИЯ","На ходе n текущий урон ×(1+n%). К текущему шансу крита +n п.п. Усиление накапливается.","adaptation",.265f,.387f);
            var loot=Card(_bossPage,"VictoryLoot",.045f,.151f,.955f,.257f);
            Label(loot,"RewardsKicker","НАГРАДА ПОСЛЕ ОБЩЕЙ ПОБЕДЫ",18,.03f,.69f,.73f,.96f).color=ExpeditionTheme.Amber;
            ExpeditionUi.Button(loot,"RewardDetails","СОСТАВ ›",.75f,.69f,.97f,.97f,ShowRewardDetails,false,17);
            Reward(loot,"LegendaryRecipe","recipe_drop_t1_gold_Helmet","1 из 8\nлегендарных рецептов",.02f,.38f);
            Reward(loot,"Tesseract","tesseract","Тессеракт",.39f,.64f);Reward(loot,"Supplies",ConsumablePresentation.Bounty,"Ресурсы\nи припасы",.65f,.98f);
            _rankingPage=ExpeditionUi.Rect(panel,"BossParticipantsPage",0,.151f,1,.813f);BuildRanking();
            _attempts=Label(panel,"Attempts","…",23,.06f,.109f,.94f,.149f);
            _contribution=Label(panel,"Contribution","",20,.06f,.073f,.94f,.109f);
            _enter=ExpeditionUi.Button(panel,"EnterRift","ВОЙТИ · 1 ЖЕТОН",.055f,.012f,.70f,.073f,()=>{if(_fixture==null)_=GlobalBossService.EnterAsync(_cts.Token);},true,28);_enterLabel=_enter.GetComponentInChildren<TMP_Text>();
            ExpeditionUi.Button(panel,"BuyToken","МАГАЗИН",.72f,.012f,.947f,.073f,()=>ExpeditionShopPanel.Open(transform.parent),false,22);
            _status=Label(transform,"BossStatus","",20,.05f,.012f,.78f,.052f);
            ExpeditionUi.Button(transform,"RefreshEvent","Обновить",.80f,.012f,.95f,.052f,()=>{if(_fixture==null){_=GlobalBossService.RefreshAsync(_cts.Token);if(_participants)LoadRanking(_cursor);}},false,19);
            SelectTab(false);
        }
        private void BuildStats()
        {
            var stats=Card(_bossPage,"BossCharacteristics",.49f,.605f,.955f,.811f);
            Label(stats,"StatsTitle","ХАРАКТЕРИСТИКИ",20,.05f,.855f,.95f,.97f).color=ExpeditionTheme.Amber;
            _damage=Stat(stats,"damage","УРОН",.61f);_armor=Stat(stats,"armor","БРОНЯ",.40f);_crit=Stat(stats,"critical","КРИТ",.19f);
            _rules=Label(stats,"BossLimits","Не лечится · до 30 ходов",18,.05f,.025f,.95f,.17f,TextAlignmentOptions.Center);_rules.color=ExpeditionTheme.Muted;
        }
        private static TMP_Text Stat(Transform host,string id,string caption,float y)
        {
            Picture(host,"StatIcon-"+id,ExpeditionTheme.Icon(id),.045f,y+.02f,.16f,y+.18f);
            Label(host,"StatName-"+id,caption,18,.19f,y,.51f,y+.19f).color=ExpeditionTheme.Muted;
            var value=Label(host,"StatValue-"+id,"…",27,.53f,y,.95f,y+.19f,TextAlignmentOptions.Right);value.fontStyle=FontStyles.Bold;return value;
        }
        private void Ability(string name,string title,string detail,string icon,float y0,float y1)
        {
            var card=Card(_bossPage,name,.045f,y0,.955f,y1);
            Picture(card,"AbilityIcon-"+name,Resources.Load<Sprite>("Expedition/Boss/0314/"+icon),.02f,.16f,.185f,.85f);
            Label(card,"Title",title,24,.21f,.68f,.97f,.94f).fontStyle=FontStyles.Bold;
            Label(card,"Description",detail,21,.21f,.08f,.97f,.68f).color=ExpeditionTheme.Muted;
        }
        private void BuildRanking()
        {
            _rankingStatus=Label(_rankingPage,"RankingIntro","Суммарный урон всех попыток за это событие.",23,.055f,.89f,.945f,.985f);_rankingStatus.color=ExpeditionTheme.Muted;
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
        private static void Tab(Button tab,bool selected){var text=tab.GetComponentInChildren<TMP_Text>();text.color=selected?ExpeditionTheme.Amber:ExpeditionTheme.Ivory;text.fontStyle=selected?FontStyles.Bold:FontStyles.Normal;}
        private GlobalBossLeaderboardService.Page Page=>_fixture!=null?_fixtureRanking:GlobalBossLeaderboardService.Current;
        private void LoadRanking(string cursor)
        {
            if(_fixture!=null)return;ObserveScope();var eventId=GlobalBossService.Current?.event_id;
            if(string.IsNullOrEmpty(eventId)||GlobalBossLeaderboardService.Busy)return;
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
                _rankingStatus.text=_fixture==null?GlobalBossLeaderboardService.Error??"Загружаем участников…":"Пока нет участников";return;
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
            var rank=Label(root,"Rank",entry.rank.ToString(),29,.02f,.19f,.10f,.81f,TextAlignmentOptions.Center);rank.color=entry.rank<=3?ExpeditionTheme.Amber:ExpeditionTheme.Muted;
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
            var card=ExpeditionUi.Fill(host,name,x0,y0,x1,y1,new Color32(12,30,37,255));card.sprite=ModalPanelCloseButton.WhiteSprite();var edge=ExpeditionUi.Panel(card.transform,"MetalRim",0,0,1,1);edge.fillCenter=false;return card.rectTransform;
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
            _status.text=_fixture==null?GlobalBossService.Error??(busy?"Связываемся с измерением…":"До 3 попыток за событие. Награды доставляются в почту."):"Просмотр события";
            _enter.interactable=state!=null&&!busy&&state.hp>0&&state.status=="active"&&(state.active_attempt?.resumable==true||state.attempts_left>0&&state.token_count>0);
            if(state==null){_name.text="МИРОВОЙ БОСС";_hp.text="…";_hpFill.fillAmount=0;_attempts.text="Ожидаем данные события…";_contribution.text="";_enterLabel.text="ЗАГРУЗКА…";_time.text="Связываемся с измерением…";_damage.text=_armor.text=_crit.text="…";RefreshRanking();return;}
            _name.text=string.IsNullOrEmpty(state.name)?"СЕРДЦЕ РАЗЛОМА":state.name.ToUpperInvariant();_hp.text=Number(state.hp)+" / "+Number(state.hp_max);_hpFill.fillAmount=(float)((double)state.hp/state.hp_max);
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
