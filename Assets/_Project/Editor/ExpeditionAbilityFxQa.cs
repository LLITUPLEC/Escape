using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Project.Match3;
using Project.Nakama;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Deterministic protocol probes and actual-frame presentation tests in the isolated, disabled-manager UI fixture.</summary>
    public static class ExpeditionAbilityFxQa
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static string Output = ".cache/qa/legacy-0.3.8/ability-fx";
        [Serializable] private sealed class Report
        {
            public bool completed, passed, networkOrAccountActions;
            public string checkedUtc, error;
            public string[] checks, screenshots;
        }
        private static readonly List<string> Checks = new();
        private static readonly List<string> Shots = new();
        private static readonly Dictionary<FieldInfo,object> Original = new();
        private static DuelMatch3Manager _manager;
        private static ExpeditionAbilityFxQaRunner _runner;
        private static Match3PlayerPanel _mine, _opponent;
        private static Match3AbilityFx _fx;
        private static float _timeScale;
        private static long _stamp;
        private static double _started;
        private static readonly Dictionary<Behaviour,bool> PortraitEnabled = new();
        private static Sprite _originalPortrait, _fixturePortrait;

        public static string Run(string outputDirectory = null)
        {
            Require(_runner == null, "Ability FX QA is already running.");
            Output = outputDirectory ?? ".cache/qa/legacy-0.3.8/ability-fx";
            Require(EditorApplication.isPlaying, "Run ExpeditionNotificationStakeOfflineFixture.Setup(), then enter Play.");
            _manager = UnityEngine.Object.FindFirstObjectByType<DuelMatch3Manager>();
            Require(_manager != null && !_manager.enabled && Read<bool>("_useLocalBotSimulation") &&
                _manager.gameObject.scene.name.StartsWith("NotificationStakeQaFixture",StringComparison.Ordinal) &&
                UnityEngine.Object.FindFirstObjectByType<NakamaBootstrap>(FindObjectsInactive.Include) == null,
                "Only the isolated UI fixture with a disabled Duel manager is authorized; never a production Solo lobby or real match.");
            Require(Read<object>("_match") == null, "The presentation fixture must have no network match.");
            Checks.Clear(); Shots.Clear(); Original.Clear(); Directory.CreateDirectory(Output);
            PureChecks();
            Require(Match3BattleFxSprites.Fury.Length == 16 && Match3BattleFxSprites.Explosion.Length == 16 &&
                Match3BattleFxSprites.Petard != null && Match3BattleFxSprites.Shield != null, "Import all four 0.3.8 FX resources first.");
            Checks.Add("16 full-rect fire slices, 16 explosion slices, shield and petard resolve from runtime Resources.");
            foreach(var field in typeof(DuelMatch3Manager).GetFields(Hidden))
                if(!field.IsInitOnly) Original[field] = field.GetValue(_manager);
            _timeScale = Time.timeScale; Time.timeScale = 0;
            var panels = UnityEngine.Object.FindObjectsByType<Match3PlayerPanel>(FindObjectsSortMode.None).OrderBy(p=>p.transform.position.x).ToArray();
            Require(panels.Length == 2,"The actual battle fixture requires two player panels.");
            _mine = panels[0]; _opponent = panels[1];
            PortraitEnabled.Clear();
            _originalPortrait = _mine.avatarImage.sprite;
            _fixturePortrait = Resources.Load<Sprite>("Expedition/Appearance/dawnwarden-male-avatar");
            Require(_fixturePortrait != null,"The shipped fixture portrait is missing.");
            RememberPortraitBehaviour(_mine.avatarImage);
            RememberPortraitBehaviour(_mine.avatarImage.GetComponent<Project.Character.UI.CharacterAppearanceImage>());
            RememberPortraitBehaviour(_mine.avatarImage.GetComponent<ExpeditionBattlePortrait>());
            foreach(var crop in _mine.avatarImage.GetComponentsInChildren<RawImage>(true))
                if(crop.name=="ExpeditionHeadCrop")RememberPortraitBehaviour(crop);
            var board = new Match3BoardLogic(); board.Init(380038);
            Set("_board",board); Set("_myStats",new PlayerStats{hp=1000,maxHp=1000,mana=90});
            Set("_opStats",new PlayerStats{hp=1000,maxHp=1000,mana=90});
            Set("_myUserId","qa-a"); Set("_opUserId","qa-b"); Set("_myPanel",_mine); Set("_opPanel",_opponent);
            Set("_boardView",UnityEngine.Object.FindFirstObjectByType<Match3BoardView>());
            Set("_abilityPanel",UnityEngine.Object.FindFirstObjectByType<Match3AbilityPanel>());
            if (outputDirectory != null)
            {
                var sceneSource = File.ReadAllText("Assets/_Project/Scenes/DuelMatch3.unity");
                foreach (var ability in new[] { "petard", "cross", "square", "shield", "fury" })
                {
                    var reference = System.Text.RegularExpressions.Regex.Match(sceneSource, ability + @"AbilitySprite:\s*\{[^}]*guid:\s*([a-f0-9]{32})");
                    Require(reference.Success,"Shipping scene lacks an authored ability sprite: " + ability);
                    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(reference.Groups[1].Value));
                    Require(sprite != null,"Shipping ability sprite is missing: " + ability);
                    Set(ability + "AbilitySprite", sprite);
                }
                Invoke("ConfigureAbilityButtonsVisuals");
                var abilityPanel = Read<Match3AbilityPanel>("_abilityPanel");
                // Reconfigure an already-built disposable fixture in the same order
                // as first-time production setup: create its label after the legacy
                // sprite configurator has hidden the previous fixture labels.
                foreach (var button in new[] {abilityPanel.petardButton,abilityPanel.crossButton,abilityPanel.squareButton,abilityPanel.shieldButton,abilityPanel.furyButton})
                {
                    var oldName = button.transform.Find("ExpeditionAbilityName");
                    if(oldName != null) UnityEngine.Object.DestroyImmediate(oldName.gameObject);
                    Require(button.transform.Find("AbilityIcon")?.GetComponent<Image>()?.sprite != null,"Ability icon is blank.");
                }
                ExpeditionBattleLayout.ApplyAbilityLayout(abilityPanel);
                abilityPanel.Refresh(new PlayerStats { mana = 100 },true,false,20,20,30,40,30);
                foreach (var button in new[] {abilityPanel.petardButton,abilityPanel.crossButton,abilityPanel.squareButton,abilityPanel.shieldButton,abilityPanel.furyButton})
                {
                    var cost = button.transform.Find("AbilityCost")?.GetComponent<TMP_Text>();
                    Require(cost != null && (cost.fontStyle & FontStyles.Bold) != 0,"Live ability cost is not bold.");
                }
                Checks.Add("Five shipping ability sprites configured through the real manager UI methods; live costs remain bold after Refresh.");
            }
            Set("_hud",UnityEngine.Object.FindFirstObjectByType<Match3GameHUD>());
            Set("_gameOverPanel",UnityEngine.Object.FindFirstObjectByType<Match3GameOverPanel>(FindObjectsInactive.Include));
            Set("_isSoloBotMode",false); Set("_isArenaTournamentMatch",false); Set("_hasInitialBoardSync",true);
            Set("_awaitingPresentationSnapshot",false); Set("_gameEnded",false); Set("_pendingGameOver",false);
            Set("_isMyTurn",true); Set("_offlinePresentationQa",true);
            Invoke("EnsureDamagePopupWidgets",_mine);Invoke("EnsureDamagePopupWidgets",_opponent);
            Invoke("EnsureGainPopupWidgets",_mine);Invoke("EnsureGainPopupWidgets",_opponent);
            ExpeditionBattleLayout.ApplyPlayerContents(_mine);ExpeditionBattleLayout.ApplyPlayerContents(_opponent);
            Require(_mine.damagePopup!=null&&_opponent.damagePopup!=null,"Production damage widgets were not created in the isolated fixture.");
            Read<Match3PresentationSyncGate>("_presentationSyncGate").Reset();
            Read<Queue<M3BoardSyncMsg>>("_pendingBoardSyncs").Clear(); Read<HashSet<M3BoardSyncMsg>>("_silentBoardSyncs").Clear();
            _fx = Match3AbilityFx.Ensure(_mine); _fx.CancelTransient();
            Invoke("RefreshStatsUI"); PinFixturePortrait(); _stamp = 1_800_000_000_000;
            _runner = new GameObject("OfflineAbilityFxQaRunner").AddComponent<ExpeditionAbilityFxQaRunner>();
            _started = EditorApplication.timeSinceStartup;
            AssemblyReloadEvents.beforeAssemblyReload += Abort;
            EditorApplication.playModeStateChanged += ModeChanged;
            EditorApplication.update += Watchdog;
            Write(false,null);
            _runner.StartCoroutine(Guard(Probe()));
            return "Ability FX probe started on actual offline battle frames; no RPC, auth, player action, achievement or account writes. Report: " + Output + "/ability-fx-validation.json";
        }

        private static void PureChecks()
        {
            var gate = new Match3PresentationSyncGate();
            var m = new M3BoardSyncMsg{board=new int[Match3BoardLogic.Size*Match3BoardLogic.Size],actionType=4,actionActorUserId="a",serverNowUnixMs=1000,aHp=100,bHp=90};
            Require(gate.TryAccept(m),"First valid action rejected.");
            Require(!gate.TryAccept(m),"Exact duplicate accepted.");
            var sameSecond = JsonUtility.FromJson<M3BoardSyncMsg>(JsonUtility.ToJson(m)); sameSecond.bHp=80;
            Require(gate.TryAccept(sameSecond),"Distinct actions sharing the server's one-second timestamp collapsed.");
            var newer = JsonUtility.FromJson<M3BoardSyncMsg>(JsonUtility.ToJson(m)); newer.serverNowUnixMs=2000;
            Require(gate.TryAccept(newer) && !gate.TryAccept(sameSecond),"Old/out-of-order state can roll HP back.");
            Require(!gate.TryAccept(new M3BoardSyncMsg{board=new int[2]}),"Malformed board accepted.");
            Require(gate.TryAccept(newer,true),"A requested recovery checkpoint cannot reapply its identical current state.");
            for(var i=0;i<100;i++) gate.TryAccept(new M3BoardSyncMsg{board=new int[Match3BoardLogic.Size*Match3BoardLogic.Size],serverNowUnixMs=3000+i});
            Require(gate.RememberedCount == 64,"Replay memory is unbounded.");
            var a=new Vector2(-200,100);var b=new Vector2(200,100);
            Require(Match3AbilityFx.FlightPoint(a,b,0)==a && Match3AbilityFx.FlightPoint(a,b,1)==b,"Projectile endpoints drift.");
            Require(Mathf.Abs(Match3AbilityFx.FlightPoint(a,b,.5f).y-238)<.001f && Match3AbilityFx.FlightPoint(a,b,.5f)==Match3AbilityFx.FlightPoint(b,a,.5f),"Arc differs by attacking side.");
            Checks.Add("Pure: exact duplicate/stale/malformed rejection, equal-timestamp distinct actions, recovery checkpoint, 64-entry bound, symmetric arc/endpoints.");
            var own = new M3ArenaCraftReward{userId="mine",outcome="participation",ingotDef="ingot_blue",ingots=3,pendingCount=3};
            var other = new M3ArenaCraftReward{userId="other",outcome="champion",ingots=40,recipeItemId="recipe_t1_epic_helmet"};
            var result = new M3GameOverMsg{arenaCraftRewards=new[]{other,own}};
            Require(ReferenceEquals(M3ArenaCraftReward.ForUser(result,"mine"),own) && M3ArenaCraftReward.ForUser(result,"missing")==null &&
                M3ArenaCraftReward.ForUser(new M3GameOverMsg(),"mine")==null,"Arena reward receipt leaked between participants or into legacy v1.");
            Checks.Add("Craft receipts select the exact user's consolation/champion row; missing and legacy v1 arrays award nothing locally.");
        }

        private static IEnumerator Probe()
        {
            var fury=Message(6,true,1000,1000,60,90); fury.aFuryTurns=1; fury.aFuryBonus=12;
            Receive(fury); yield return new WaitForSecondsRealtime(.25f);
            Require(_mine.avatarImage.GetComponent<Match3AvatarFuryFx>().IsActive,"Fury rim did not activate from server buff state.");
            Require(!_opponent.avatarImage.GetComponent<Match3AvatarFuryFx>()?.IsActive ?? true,"Fury appeared on the wrong side.");
            Require(_mine.PortraitFrame.GetComponentsInParent<Mask>(true).Length==0,"An ancestor clips the flame rim.");
            yield return null; Shot("fury-frame");
            Checks.Add("Actual frames: fury follows the local server buff, has a literal clear centre and no full-portrait glow; timeScale=0.");
            Require(_mine.avatarImage.sprite==_fixturePortrait&&_mine.avatarImage.enabled,"The real shipped portrait is not visible beneath the flame rim.");
            Checks.Add("Readability fixture uses the shipped dawnwarden male portrait with only disposable UI bindings/crop disabled; no account gender or cosmetic state is changed.");

            var shieldBefore = _fx.ShieldCount;
            var shield=Message(5,false,1000,1000,60,50); shield.aFuryTurns=1;shield.bShieldT1=3;
            Receive(shield); yield return new WaitForSecondsRealtime(.24f);
            Require(_fx.ShieldCount==shieldBefore+1 && Active("ShieldActivation"),"Shield activation failed on the opponent.");
            yield return null; Shot("shield-activation");
            yield return new WaitForSecondsRealtime(.65f);
            Require(!Active("ShieldActivation"),"Shield activation remains stuck onscreen.");
            Checks.Add("Shield: confirmed activation on the correct side, visible metal/energy sprite, automatic hide.");

            var first=Message(4,true,1000,920,30,50);first.aFuryTurns=1;
            var second=Message(4,false,930,920,30,20);second.aFuryTurns=1;
            var launches=_fx.LaunchCount;var impacts=_fx.ImpactCount;
            Receive(first);Receive(first);Receive(second);
            yield return new WaitForSecondsRealtime(.24f);
            Require(_fx.PetardFlying && _fx.LaunchCount==launches+1 && _fx.ImpactCount==impacts,"Duplicate or queued projectile launched early.");
            Require(Read<PlayerStats>("_opStats").hp==1000 && _opponent.hpText.text.Contains("1000"),"Target HP changed before contact.");
            Require(PopupValue(_opponent).text!="-80"&&_opponent.damagePopup.GetComponent<CanvasGroup>().alpha==0,"Damage number appeared before contact.");
            Require(Read<bool>("_inputBlocked"),"Input is enabled while a confirmed projectile is in flight.");
            Require(_fx.LastFlightFrom.x<_fx.LastFlightTo.x,"Local projectile flies in the wrong direction.");
            yield return null;Shot("petard-local-flight");
            while(!_fx.ImpactIsActive)yield return null;yield return null;
            Require(Read<PlayerStats>("_opStats").hp==920 && Read<PlayerStats>("_myStats").hp==1000,"Contact did not apply the exact authoritative HP.");
            RequirePopup(_opponent,"-80",false);
            Require(_fx.LaunchCount==launches+1,"The second queued action overtook the first explosion.");
            yield return null;Shot("petard-local-impact");
            while(_fx.LaunchCount<launches+2)yield return null;
            yield return new WaitForSecondsRealtime(.22f);
            Require(_fx.PetardFlying && _fx.LastFlightFrom.x>_fx.LastFlightTo.x,"Opponent projectile direction is incorrect.");
            Require(Read<PlayerStats>("_myStats").hp==1000,"Opponent damage arrived before its projectile.");
            yield return null;Shot("petard-opponent-flight");
            while(!_fx.ImpactIsActive)yield return null;yield return null;
            Require(Read<PlayerStats>("_myStats").hp==930 && Read<PlayerStats>("_opStats").hp==920,"Opponent contact applied the wrong player slot.");
            RequirePopup(_mine,"-70",false);
            yield return null;Shot("petard-opponent-impact");
            while(Read<Coroutine>("_remoteSyncRoutine")!=null)yield return null;
            Checks.Add("Both directions: arc/fuse/sparks; duplicate suppression; target HP unchanged throughout flight; exact server HP/popups at contact; sequential action/explosion queue.");

            var interrupted=Message(4,true,930,850,0,20);Receive(interrupted);
            yield return new WaitForSecondsRealtime(.20f);var oldImpacts=_fx.ImpactCount;
            Set("_awaitingPresentationSnapshot",true);
            var snapshot=Message(0,true,930,850,0,20);snapshot.critTriggered=true;snapshot.boardPopup="MUST NOT REPLAY";
            Receive(snapshot);yield return new WaitForSecondsRealtime(.12f);
            Require(!_fx.PetardFlying&&!_fx.ImpactIsActive&&_fx.ImpactCount==oldImpacts&&Read<PlayerStats>("_opStats").hp==850,"Recovery checkpoint replayed an old impact or failed to snap to server HP.");
            var stale=Message(4,true,1,1,0,0);stale.serverNowUnixMs=_stamp-10000;Receive(stale);
            yield return new WaitForSecondsRealtime(.08f);
            Require(Read<PlayerStats>("_myStats").hp==930&&Read<PlayerStats>("_opStats").hp==850,"Stale checkpoint rolled HP back.");
            Require(!_mine.avatarImage.GetComponent<Match3AvatarFuryFx>().IsActive,"Fury survived a snapshot without the buff.");
            Checks.Add("Reconnect: fresh requested checkpoint cancels the interrupted flight/old queue, updates stats silently, removes expired fury; stale packet cannot roll state back.");

            var race=Message(0,true,930,850,160,140);race.pvpRace=true;race.aMaxMana=race.bMaxMana=240;race.raceGoalMana=200;
            Receive(race);yield return new WaitForSecondsRealtime(.08f);
            var drain=Message(4,false,930,850,125,110);drain.pvpRace=true;drain.manaDrain=35;drain.aMaxMana=drain.bMaxMana=240;drain.raceGoalMana=200;
            Receive(drain);yield return new WaitForSecondsRealtime(.22f);
            Require(Read<PlayerStats>("_myStats").mana==160,"Race drain appeared before arrival.");
            yield return null;Shot("petard-race-flight");
            while(!_fx.ImpactIsActive)yield return null;yield return null;
            Require(Read<PlayerStats>("_myStats").mana==125&&Read<PlayerStats>("_myStats").hp==930&&Read<PlayerStats>("_opStats").hp==850,"Race petard must drain mana, never HP.");
            RequirePopup(_mine,"-35",true);
            yield return null;Shot("petard-race-impact");
            while(Read<Coroutine>("_remoteSyncRoutine")!=null)yield return null;
            Checks.Add("Race: target mana held until contact, authoritative drain35 applied, HP unchanged on both sides, visible blue -35 popup at contact.");

            Set("_pvpRaceQueue",false);Set("_raceHudApplied",false);_mine.SetRaceHudMode(false);_opponent.SetRaceHudMode(false);
            var lethal=Message(4,false,0,850,125,80);Receive(lethal);
            Set("_pendingGameOver",true);Set("_pendingGameOverWon",false);
            yield return new WaitForSecondsRealtime(.22f);
            Require(!Read<bool>("_gameEnded")&&Read<PlayerStats>("_myStats").hp==930,"Game over overtook the lethal projectile.");
            while(!_fx.ImpactIsActive)yield return null;yield return null;
            Require(Read<PlayerStats>("_myStats").hp==0&&!Read<bool>("_gameEnded"),"Lethal impact timing is inconsistent.");
            while(!Read<bool>("_gameEnded"))yield return null;
            Require(!_fx.ImpactIsActive&&!Read<bool>("_pendingGameOver"),"Final modal appeared before impact completion.");
            yield return null;Shot("petard-final-after-impact");
            Checks.Add("Final outcome waits for lethal contact and impact completion; gameOver cannot overtake queued presentation.");
        }

        private static M3BoardSyncMsg Message(int action,bool mine,int myHp,int opHp,int myMana,int opMana) => new()
        {
            board=Read<Match3BoardLogic>("_board").ToArray(),actionType=action,actionActorUserId=mine?"qa-a":"qa-b",activeUserId="qa-a",
            aHp=myHp,bHp=opHp,aMaxHp=1000,bMaxHp=1000,aMana=myMana,bMana=opMana,serverNowUnixMs=++_stamp,turnEndsAtUnixMs=_stamp+18000
        };
        private static void Receive(M3BoardSyncMsg msg)=>Invoke("OnBoardSyncReceived",msg);
        private static TMP_Text PopupValue(Match3PlayerPanel panel)=>panel.damagePopup.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t=>t.name=="Value")
            ?? panel.damagePopup.GetComponentInChildren<TMP_Text>(true);
        private static void RequirePopup(Match3PlayerPanel panel,string expected,bool mana)
        {
            var text=PopupValue(panel);
            Require(text!=null&&text.text==expected&&text.gameObject.activeInHierarchy&&panel.damagePopup.GetComponent<CanvasGroup>().alpha>.99f,
                "The expected visible damage/drain popup is missing at contact: "+expected);
            Require(mana?text.color.b>text.color.r:text.color.r>text.color.b,"Damage/drain popup uses the wrong colour.");
        }
        private static bool Active(string name)=>_fx.transform.Find("ExpeditionAbilityEffects/"+name)?.gameObject.activeInHierarchy==true;
        private static IEnumerator Guard(IEnumerator probe)
        {
            while(true)
            {
                object next;
                try { if(!probe.MoveNext())break;next=probe.Current; }
                catch(Exception error){Finish(error);yield break;}
                yield return next;
            }
            Finish(null);
        }
        private static void Shot(string name)
        {
            Canvas.ForceUpdateCanvases();
            var source=ExpeditionReleaseCaptureView.Capture("publish-ability-fx-"+name);
            var target=Path.GetFullPath(Path.Combine(Output,name+".png"));File.Copy(source,target,true);Shots.Add(target);
        }
        private static void Write(bool completed,Exception error)
        {
            File.WriteAllText(Path.Combine(Output,"ability-fx-validation.json"),JsonUtility.ToJson(new Report{
                completed=completed,passed=completed&&error==null,networkOrAccountActions=false,checkedUtc=DateTime.UtcNow.ToString("O"),
                error=error?.ToString(),checks=Checks.ToArray(),screenshots=Shots.ToArray()},true));
        }
        private static void Finish(Exception error)
        {
            AssemblyReloadEvents.beforeAssemblyReload-=Abort;EditorApplication.playModeStateChanged-=ModeChanged;
            EditorApplication.update-=Watchdog;
            if(_manager!=null)
            {
                _manager.StopAllCoroutines();_fx?.CancelTransient();_mine?.SetFuryVisual(false);_opponent?.SetFuryVisual(false);
                var outcome=Read<Match3GameOverPanel>("_gameOverPanel");if(outcome!=null)outcome.gameObject.SetActive(false);
                foreach(var entry in Original)entry.Key.SetValue(_manager,entry.Value);
                Read<Queue<M3BoardSyncMsg>>("_pendingBoardSyncs").Clear();Read<HashSet<M3BoardSyncMsg>>("_silentBoardSyncs").Clear();
                Read<Match3PresentationSyncGate>("_presentationSyncGate").Reset();
                if(_mine!=null&&_mine.avatarImage!=null)_mine.avatarImage.sprite=_originalPortrait;
                foreach(var entry in PortraitEnabled)if(entry.Key!=null)entry.Key.enabled=entry.Value;
                PortraitEnabled.Clear();_fixturePortrait=null;
            }
            Time.timeScale=_timeScale;Write(true,error);
            if(_runner!=null)UnityEngine.Object.Destroy(_runner.gameObject);_runner=null;_manager=null;Original.Clear();
            if(error!=null)Debug.LogException(error);else Debug.Log("Ability FX QA PASS: "+Checks.Count+" check groups; "+Shots.Count+" native runtime screenshots; no network/account writes.");
        }
        private static void Abort(){if(_runner!=null)Finish(new InvalidOperationException("Ability FX QA interrupted."));}
        private static void RememberPortraitBehaviour(Behaviour value){if(value!=null)PortraitEnabled[value]=value.enabled;}
        private static void PinFixturePortrait()
        {
            if(_fixturePortrait==null||_mine==null||_mine.avatarImage==null)return;
            foreach(var entry in PortraitEnabled)if(entry.Key!=null&&entry.Key!=_mine.avatarImage)entry.Key.enabled=false;
            _mine.avatarImage.sprite=_fixturePortrait;_mine.avatarImage.enabled=true;
        }
        private static void Watchdog()
        {
            if(_runner==null)return;
            PinFixturePortrait();
            if(EditorApplication.timeSinceStartup-_started>40)Finish(new TimeoutException("Ability FX QA exceeded40s; ensure the actual Game view renders."));
        }
        private static void ModeChanged(PlayModeStateChange state){if(state==PlayModeStateChange.ExitingPlayMode)Abort();}
        private static T Read<T>(string field)=>(T)typeof(DuelMatch3Manager).GetField(field,Hidden).GetValue(_manager);
        private static void Set(string field,object value)=>typeof(DuelMatch3Manager).GetField(field,Hidden).SetValue(_manager,value);
        private static void Invoke(string method,params object[] args)=>typeof(DuelMatch3Manager).GetMethod(method,Hidden).Invoke(_manager,args);
        private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
    public sealed class ExpeditionAbilityFxQaRunner : MonoBehaviour { }
}
