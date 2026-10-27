using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Project.Character;
using Project.Character.UI;
using Project.Match3;
using Project.Nakama;
using Project.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Production views and local callbacks in the disposable offline battle scene. Never instantiates an account controller or sends RPCs.</summary>
    public static class ExpeditionConsumableQa
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string Output = ".cache/qa/legacy-0.3.9/consumables";
        private static readonly string[] Ids = { "elixir_assault", "elixir_stone", "elixir_gambit", "scroll_pve_bounty" };
        private static readonly List<string> Checks = new(), Shots = new();
        private static readonly List<Action> Stages = new();
        private static GameObject _host, _dim;
        private static Canvas _canvas;
        private static CharacterScreenView _view;
        private static CharacterExpeditionLayout _layout;
        private static CharacterItemActionModalView _action;
        private static ConsumableUseModalView _modal;
        private static ItemCatalog _catalog;
        private static Match3PlayerPanel[] _panels;
        private static ConsumableStatus _empty;
        private static int _stage, _frame, _uses;
        private static double _next, _started;
        private static string _pendingShot;
        [Serializable] private sealed class Report
        {
            public bool completed, passed, offline = true, rpcOrAccountActions;
            public string checkedUtc, error, canvasViewport;
            public string[] checks, screenshots;
        }
        public static string Run()
        {
            Require(_host == null, "Consumable UI QA is already running.");
            Require(EditorApplication.isPlaying && !EditorApplication.isPaused, "Use unpaused Play in the existing isolated battle fixture.");
            var manager = UnityEngine.Object.FindFirstObjectByType<DuelMatch3Manager>();
            Require(manager != null && !manager.enabled && manager.gameObject.scene.name.StartsWith("NotificationStakeQaFixture", StringComparison.Ordinal) &&
                (bool)typeof(DuelMatch3Manager).GetField("_useLocalBotSimulation", Hidden).GetValue(manager), "Only the disabled offline manager is permitted.");
            NoNetworkController();
            var board = UnityEngine.Object.FindFirstObjectByType<Match3BoardView>();
            _canvas = board.GetComponentInParent<Canvas>().rootCanvas;
            Require(Math.Abs(_canvas.pixelRect.width - 1080) < 1 && Math.Abs(_canvas.pixelRect.height - 1920) < 1, "Reuse the settled native1080x1920 battle canvas.");
            _catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/_Project/Data/Character/Items/MainItemCatalog.asset");
            Checks.Clear(); Shots.Clear(); Stages.Clear(); Directory.CreateDirectory(Output);
            foreach (var id in Ids)
            {
                var item = _catalog.Get(id);
                Require(item != null && item.Kind == ItemKind.Consumable && !item.Equippable && item.MaxStack == 20 && item.SalePrice == 0, "Invalid consumable catalog: " + id);
                Require(item.Icon != null && item.Icon == ConsumablePresentation.Icon(id), "The shipped generated sprite is not assigned: " + id);
            }
            Require(_catalog.EnumerateDefinitions().Count() == 72, "Expected the complete72-item client catalog.");
            _empty = Status();
            Require(ConsumablePresentation.BlockedReason(Ids[0], _empty) == null, "Empty arm should permit activation.");
            Require(ConsumablePresentation.BlockedReason(Ids[1], Status(Ids[0])) != null, "Armed replacement must be blocked.");
            Require(ConsumablePresentation.BlockedReason(Ids[3], Status(null, true)) != null, "An active scroll must reject extension.");
            var expired = Status(null, true); expired.bounty_ends_at = expired.server_now;
            Require(ConsumablePresentation.BlockedReason(Ids[3], expired) == null, "An expired scroll must permit a new activation.");
            Checks.Add("All72 catalog entries retained; four generated icons, consumable kind, non-equippable, stack20 and zero sale price; arm/active/expired state rules.");
            Build();
            Stages.Add(() => Shot("inventory", () => { _view.ApplyCharacterResponse(Profile(_empty), _catalog); }));
            Stages.Add(() => Shot("filter", () => Invoke(_layout, "SelectFilter", 3)));
            Stages.Add(() => Shot("action", () => {
                _dim.SetActive(true);
                _action.ConfigureItem(_catalog.Get(Ids[0]), true, () => _uses++);
                _action.ShowAt(Vector2.zero);
                var use = Read<Button>(_action, "sellButton");
                Require(use.interactable && use.GetComponentInChildren<TMP_Text>().text == "Использовать", "The action must offer activation, not a sale.");
                use.onClick.Invoke(); Require(_uses == 1, "The inventory action did not invoke the use callback.");
                Checks.Add("Actual inventory action routes the use button to activation; its sell callback throws if accidentally used.");
            }));
            foreach (var id in Ids) { var value = id; Stages.Add(() => Shot("confirm-" + value.Replace('_','-'), () => {
                _action.HideImmediate(); _modal.Show(_catalog.Get(value), _empty, () => _uses++, () => _modal.gameObject.SetActive(false));
                Require(_modal.ConfirmButton.interactable, "Valid consumable confirmation is disabled.");
            })); }
            Stages.Add(() => Shot("already-armed", () => {
                _modal.Show(_catalog.Get(Ids[1]), Status(Ids[0]), () => throw new Exception("A blocked elixir submitted."), null);
                Require(!_modal.ConfirmButton.interactable, "An armed elixir can be replaced in the confirmation.");
            }));
            Stages.Add(() => Shot("scroll-active", () => {
                _modal.Show(_catalog.Get(Ids[3]), Status(null,true), () => throw new Exception("An active scroll submitted."), null);
                Require(!_modal.ConfirmButton.interactable, "Active scroll confirmation allows an extension.");
            }));
            Stages.Add(() => Shot("retry", () => {
                var before = _uses; _modal.Show(_catalog.Get(Ids[0]), _empty, () => _uses++, null);
                _modal.ConfirmButton.onClick.Invoke(); _modal.ConfirmButton.onClick.Invoke();
                Require(_uses == before + 1 && !_modal.ConfirmButton.interactable, "Repeated confirm submitted twice.");
                _modal.ShowError("storage_unavailable"); _modal.ConfirmButton.onClick.Invoke();
                Require(_uses == before + 2, "An ambiguous failure cannot be retried.");
                _modal.ShowError("storage_unavailable");
                Checks.Add("Actual confirm suppresses double clicks, disables pending interaction, and permits an explicit retry after an ambiguous response; only local callbacks.");
            }));
            Stages.Add(() => Shot("inventory-active", () => {
                _modal.gameObject.SetActive(false); _dim.SetActive(false); _view.ApplyCharacterResponse(Profile(Status(Ids[2],true)), _catalog);
            }));
            Stages.Add(() => Shot("battle-active", () => {
                _host.SetActive(false); _panels = UnityEngine.Object.FindObjectsByType<Match3PlayerPanel>(FindObjectsSortMode.None).OrderBy(p => p.transform.position.x).ToArray();
                Require(_panels.Length == 2, "The real battle fixture must have two panels.");
                _panels[0].SetConsumableVisual(Ids[0]); _panels[1].SetConsumableVisual(Ids[2]);
                Match3ConsumableBountyBadge.Ensure(_panels[0]).Bind(_empty.server_now + 3600, _empty.server_now);
            }));
            _stage = 0; _pendingShot = null; _next = _started = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick; AssemblyReloadEvents.beforeAssemblyReload += Abort;
            return "Started12 native production-view captures in an offline fixture; no RPC, inventory or account changes. Result: " + Output + "/ui-report.json";
        }
        private static void Build()
        {
            _host = ExpeditionUi.Rect(_canvas.transform, "ConsumableQaOffline", 0, 0, 1, 1).gameObject; _host.SetActive(false);
            ExpeditionUi.Fill(_host.transform, "PrivateBackground", 0, 0, 1, 1, new Color(.025f,.055f,.065f));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/CharacterUI/CharacterHudOverlay.prefab");
            var clone = UnityEngine.Object.Instantiate(prefab, _host.transform, false);
            // Awake on this production controller authenticates. Remove it before the inactive clone can ever enter the hierarchy.
            foreach (var controller in clone.GetComponentsInChildren<CharacterScreenController>(true)) UnityEngine.Object.DestroyImmediate(controller);
            foreach (var controller in clone.GetComponentsInChildren<CharacterSheetDragController>(true)) UnityEngine.Object.DestroyImmediate(controller);
            foreach (var controller in clone.GetComponentsInChildren<CharacterNicknameEditor>(true)) UnityEngine.Object.DestroyImmediate(controller);
            foreach (var scaler in clone.GetComponentsInChildren<CanvasScaler>(true)) scaler.enabled = false;
            _view = clone.GetComponentInChildren<CharacterScreenView>(true);
            _view.AttachToShell(_host.transform as RectTransform);
            _layout = (CharacterExpeditionLayout)Invoke(_view, "ApplyExpeditionLayout", null, null, null, null);
            _layout.SetShellLayout(false);
            // Pin one real shipped body only inside this disposable UI, without fabricating account gender or cosmetic authorization.
            var body = _view.GetComponentsInChildren<Image>(true).First(x => x.name == "ExpeditionHero");
            var binding = body.GetComponent<CharacterAppearanceImage>(); if(binding != null) binding.enabled = false;
            body.sprite = Resources.Load<Sprite>("Expedition/Appearance/dawnwarden-male-body"); body.enabled = true;
            Require(body.sprite != null, "Fixture body missing.");
            _dim = ExpeditionUi.Fill(_host.transform, "ItemModalOverlay", 0, 0, 1, 1, new Color(0,0,0,.35f)).gameObject;
            _action = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<CharacterItemActionModalView>("Assets/_Project/Prefabs/CharacterUI/CharacterItemActionModal.prefab"), _dim.transform);
            _host.SetActive(true); clone.SetActive(true); _view.SetVisible(true);
            _action.Bind(() => {}, () => throw new Exception("Consumable action accidentally sold an item."));
            _modal = ConsumableUseModalView.Create(_dim.transform); _dim.SetActive(false); NoNetworkController();
        }
        private static CharacterGetRpcResponse Profile(ConsumableStatus status)
        {
            var inventory = new string[25]; var counts = new int[25];
            for(var i=0;i<Ids.Length;i++){inventory[i]=Ids[i];counts[i]=2;}
            inventory[4]="ingot_green";counts[4]=3;
            return new CharacterGetRpcResponse { ok=true, inventory_capacity=25, inventory_slot_price_matter=3,
                equipment_def_ids=new string[8], inventory_def_ids=inventory,inventory_counts=counts,consumables=status,
                stats=new StatsMap{hp=1045,damage=40,armor=20,healing=10,crit_chance=.1f} };
        }
        private static ConsumableStatus Status(string armed=null,bool bounty=false) => new ConsumableStatus {
            schema=1,server_now=1792915200,armed_item_id=armed,armed_at=armed==null?0:1792915200,
            bounty_active=bounty,bounty_started_at=bounty?1792915200:0,bounty_ends_at=bounty?1792918800:0 };
        private static void Shot(string tag, Action setup){setup();_pendingShot=tag;_frame=Time.frameCount;_next=EditorApplication.timeSinceStartup+.4;}
        private static void Tick()
        {
            try
            {
                Require(EditorApplication.isPlaying, "Play ended before consumable QA finished.");
                Require(EditorApplication.timeSinceStartup-_started<45,"Consumable QA timed out.");
                if(EditorApplication.timeSinceStartup<_next || Time.frameCount<_frame+2)return;
                if(_pendingShot!=null)
                {
                    Verify(); var path=ExpeditionReleaseCaptureView.Capture("publish-039-consumable-"+_pendingShot);
                    var target=Output+"/"+_pendingShot+".png"; File.Copy(path,target,true);Shots.Add(target);
                    _pendingShot=null;_stage++;
                }
                if(_stage>=Stages.Count){Checks.Add("Twelve actual whole1080x1920 native frames; confirmation/status text and controls fit the existing production canvas. Battle shows both actor effects and server-clock bounty.");Finish(null);return;}
                Stages[_stage]();
            }
            catch(Exception ex){Finish(ex);}
        }
        private static void Verify()
        {
            NoNetworkController(); Canvas.ForceUpdateCanvases();
            var root = _host.activeSelf ? _host.transform : _canvas.transform;
            foreach(var text in root.GetComponentsInChildren<TMP_Text>())
            {
                if(!text.name.StartsWith("Consumable",StringComparison.Ordinal) && text.name!="ArmedStatus" && text.name!="BountyStatus" && text.name!="BountyTime" && !text.name.StartsWith("ExpeditionInventoryFilter",StringComparison.Ordinal))continue;
                text.ForceMeshUpdate(); Require(!text.isTextOverflowing,"Text overflow: "+text.name+" / "+text.text+" / "+text.rectTransform.rect);
                Within(text.rectTransform);
            }
            if(_modal.gameObject.activeInHierarchy)
            {
                Within(_modal.PanelRect);
                foreach(var button in _modal.GetComponentsInChildren<Button>())
                    if(button.transform!=_modal.PanelRect)Require(Physical(button.transform as RectTransform).height>=40,"Touch target below40px: "+button.name);
            }
        }
        private static void Within(RectTransform rect)
        {
            var p=Physical(rect);var v=_canvas.pixelRect;
            Require(p.width>0&&p.height>0&&p.xMin>=v.xMin-1&&p.xMax<=v.xMax+1&&p.yMin>=v.yMin-1&&p.yMax<=v.yMax+1,"Outside native viewport: "+rect.name+" / "+p+" / "+v);
        }
        private static Rect Physical(RectTransform rect)
        {
            var corners=new Vector3[4];rect.GetWorldCorners(corners);var min=new Vector2(float.PositiveInfinity,float.PositiveInfinity);var max=new Vector2(float.NegativeInfinity,float.NegativeInfinity);
            var camera=_canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:_canvas.worldCamera;
            foreach(var corner in corners){var p=RectTransformUtility.WorldToScreenPoint(camera,corner);min=Vector2.Min(min,p);max=Vector2.Max(max,p);}return Rect.MinMaxRect(min.x,min.y,max.x,max.y);
        }
        private static void Abort()=>Finish(new InvalidOperationException("Assembly reload interrupted the isolated consumable fixture."));
        private static void Finish(Exception error)
        {
            EditorApplication.update-=Tick;AssemblyReloadEvents.beforeAssemblyReload-=Abort;
            var report=new Report{completed=true,passed=error==null,checkedUtc=DateTime.UtcNow.ToString("O"),error=error?.ToString(),canvasViewport=_canvas!=null?_canvas.pixelRect.ToString():null,checks=Checks.ToArray(),screenshots=Shots.ToArray()};
            File.WriteAllText(Output+"/ui-report.json",JsonUtility.ToJson(report,true));
            if(_panels!=null){foreach(var panel in _panels)if(panel!=null)panel.SetConsumableVisual(null);if(_panels.Length>0&&_panels[0]!=null)Match3ConsumableBountyBadge.Ensure(_panels[0]).Bind(0,0);}
            if(_host!=null)UnityEngine.Object.DestroyImmediate(_host);_host=null;_panels=null;
            if(error!=null)Debug.LogError("Consumable offline UI QA failed: "+error);else Debug.Log("Consumable offline UI QA PASS: "+Shots.Count+" native frames.");
        }
        private static object Invoke(object target,string name,params object[] args)=>target.GetType().GetMethod(name,Hidden).Invoke(target,args);
        private static T Read<T>(object target,string name)=>(T)target.GetType().GetField(name,Hidden).GetValue(target);
        private static void NoNetworkController()=>Require(UnityEngine.Object.FindFirstObjectByType<NakamaBootstrap>(FindObjectsInactive.Include)==null,"The offline fixture instantiated a network bootstrap.");
        private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}
