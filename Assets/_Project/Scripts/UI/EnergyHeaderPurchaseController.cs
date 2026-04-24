using System;
using System.Threading;
using System.Threading.Tasks;
using Project.Character;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>
    /// «+» у энергии в HeaderResources: модалка с двумя вариантами и подтверждением, RPC <see cref="PlayerResourcesService.BuyEnergyAsync"/>.</summary>
    public sealed class EnergyHeaderPurchaseController
    {
        private const int MatterCost = 1;
        private const int MatterGrant = 100;
        private const int GoldCost = 1000;
        private const int GoldGrant = 100;

        private readonly Transform _modalParent;
        private readonly Sprite _energySprite;
        private readonly Sprite _matterSprite;
        private readonly Sprite _goldSprite;
        private readonly Func<CancellationToken, Task> _refreshHeaderAsync;
        private readonly CancellationToken _sceneCt;

        private GameObject _root;
        private GameObject _choiceRoot;
        private GameObject _confirmRoot;
        private Text _confirmText;
        private string _pendingMode;
        private UnityAction _openModalAction;

        public EnergyHeaderPurchaseController(
            Transform modalParent,
            Sprite energySp,
            Sprite matterSp,
            Sprite goldSp,
            Func<CancellationToken, Task> refreshHeaderAsync,
            CancellationToken sceneCt)
        {
            _modalParent = modalParent;
            _energySprite = energySp;
            _matterSprite = matterSp;
            _goldSprite = goldSp;
            _refreshHeaderAsync = refreshHeaderAsync;
            _sceneCt = sceneCt;
        }

        public void EnsurePlusOnEnergyRow(Transform headerResourcesRoot)
        {
            if (headerResourcesRoot == null) return;
            var energyTr = FindChildByName(headerResourcesRoot, "Energy");
            if (energyTr == null) return;
            if (_openModalAction == null)
                _openModalAction = ShowChoiceModal;

            var existingPlus = energyTr.Find("EnergyBuyPlus");
            Button btn;
            if (existingPlus != null)
            {
                btn = existingPlus.GetComponent<Button>() ?? existingPlus.GetComponentInChildren<Button>(true);
                if (btn == null) return;
                if (btn.targetGraphic == null)
                {
                    var g = btn.GetComponent<Graphic>() ?? btn.GetComponentInChildren<Graphic>(true);
                    if (g != null)
                        btn.targetGraphic = g;
                }
                if (btn.targetGraphic is Image targetImg)
                    targetImg.raycastTarget = true;
                foreach (var t in btn.GetComponentsInChildren<Text>(true))
                    t.raycastTarget = false;
                foreach (var t in btn.GetComponentsInChildren<TMP_Text>(true))
                    t.raycastTarget = false;
            }
            else
            {
                var plusGo = new GameObject("EnergyBuyPlus", typeof(RectTransform), typeof(Image), typeof(Button));
                var prt = plusGo.GetComponent<RectTransform>();
                prt.SetParent(energyTr, false);
                prt.anchorMin = new Vector2(0.88f, 0.1f);
                prt.anchorMax = new Vector2(1f, 0.55f);
                prt.offsetMin = Vector2.zero;
                prt.offsetMax = Vector2.zero;
                plusGo.GetComponent<Image>().color = new Color(0.2f, 0.6f, 0.3f, 0.9f);
                btn = plusGo.GetComponent<Button>();
                var label = new GameObject("T", typeof(Text)).GetComponent<Text>();
                var lt = label.rectTransform;
                lt.SetParent(plusGo.transform, false);
                lt.anchorMin = Vector2.zero;
                lt.anchorMax = Vector2.one;
                lt.offsetMin = Vector2.zero;
                lt.offsetMax = Vector2.zero;
                label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                label.alignment = TextAnchor.MiddleCenter;
                label.text = "+";
                label.color = Color.white;
                label.fontSize = 18;
                label.raycastTarget = false;
            }

            btn.onClick.RemoveListener(_openModalAction);
            btn.onClick.AddListener(_openModalAction);
        }

        private void ShowChoiceModal()
        {
            EnsureModal();
            if (_root == null) return;
            _confirmRoot.SetActive(false);
            _choiceRoot.SetActive(true);
            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
        }

        private void HideAll()
        {
            if (_root != null) _root.SetActive(false);
        }

        private void OnPickMatter()
        {
            _pendingMode = "matter";
            _choiceRoot.SetActive(false);
            _confirmRoot.SetActive(true);
            if (_confirmText != null)
                _confirmText.text = BuildConfirmLine(true);
        }

        private void OnPickGold()
        {
            _pendingMode = "gold";
            _choiceRoot.SetActive(false);
            _confirmRoot.SetActive(true);
            if (_confirmText != null)
                _confirmText.text = BuildConfirmLine(false);
        }

        private string BuildConfirmLine(bool matter)
        {
            if (matter) return $"Потратить 1 ед. материи ({MatterCost})?";
            return $"Потратить {GoldCost} золота?";
        }

        private async void OnConfirmYes()
        {
            var mode = _pendingMode;
            HideAll();
            try
            {
                var r = await PlayerResourcesService.BuyEnergyAsync(mode, _sceneCt).ConfigureAwait(true);
                if (r is { ok: true } && _refreshHeaderAsync != null)
                    await _refreshHeaderAsync(_sceneCt).ConfigureAwait(true);
            }
            catch
            {
                // ignored
            }
        }

        private void EnsureModal()
        {
            if (_root != null) return;
            if (_modalParent == null) return;

            _root = new GameObject("EnergyBuyDialog", typeof(RectTransform));
            var rootRt = _root.GetComponent<RectTransform>();
            rootRt.SetParent(_modalParent, false);
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image), typeof(Button));
            var dimRt = dim.GetComponent<RectTransform>();
            dimRt.SetParent(rootRt, false);
            dimRt.anchorMin = Vector2.zero;
            dimRt.anchorMax = Vector2.one;
            dimRt.offsetMin = Vector2.zero;
            dimRt.offsetMax = Vector2.zero;
            dim.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            dim.GetComponent<Image>().raycastTarget = true;
            dim.GetComponent<Button>().onClick.AddListener(HideAll);

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            var panelRt = panel.GetComponent<RectTransform>();
            panelRt.SetParent(rootRt, false);
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(420f, 300f);
            panel.GetComponent<Image>().color = new Color(0.11f, 0.13f, 0.18f, 1f);
            panel.GetComponent<Image>().raycastTarget = true;

            var v = panel.GetComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(18, 18, 16, 16);
            v.spacing = 8f;
            v.childControlHeight = true;
            v.childControlWidth = true;
            v.childAlignment = TextAnchor.UpperCenter;

            NewText(panel.transform, "Title", "Купить энергию", 20, FontStyle.Bold, 32f);

            _choiceRoot = new GameObject("ChoiceBlock", typeof(RectTransform), typeof(VerticalLayoutGroup));
            var chRt = _choiceRoot.GetComponent<RectTransform>();
            chRt.SetParent(panel.transform, false);
            chRt.sizeDelta = new Vector2(380, 150);
            var chV = _choiceRoot.GetComponent<VerticalLayoutGroup>();
            chV.spacing = 10f;
            chV.childControlHeight = true;
            chV.childControlWidth = true;
            chV.childAlignment = TextAnchor.UpperCenter;

            MakeOfferRow(_choiceRoot.transform, _matterSprite, MatterCost, _energySprite, MatterGrant, OnPickMatter);
            MakeOfferRow(_choiceRoot.transform, _goldSprite, GoldCost, _energySprite, GoldGrant, OnPickGold);

            _confirmRoot = new GameObject("Confirm", typeof(RectTransform), typeof(VerticalLayoutGroup));
            _confirmRoot.GetComponent<RectTransform>().SetParent(panel.transform, false);
            var cV = _confirmRoot.GetComponent<VerticalLayoutGroup>();
            cV.spacing = 12f;
            cV.childControlHeight = true;
            cV.childControlWidth = true;
            cV.childAlignment = TextAnchor.MiddleCenter;

            _confirmText = NewText(_confirmRoot.transform, "Q", "", 18, FontStyle.Normal, 40f);
            var yn = new GameObject("YesNo", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            yn.GetComponent<RectTransform>().SetParent(_confirmRoot.transform, false);
            var h = yn.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 24f;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlHeight = true;
            h.childControlWidth = true;
            AddModalBtn(yn.transform, "Да", OnConfirmYes);
            AddModalBtn(yn.transform, "Нет", () =>
            {
                if (_choiceRoot != null) _choiceRoot.SetActive(true);
                if (_confirmRoot != null) _confirmRoot.SetActive(false);
            });

            _confirmRoot.SetActive(false);
            _root.SetActive(false);
        }

        private void MakeOfferRow(Transform parent, Sprite costSp, int cost, Sprite getSp, int grant, Action onRowClick)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(Button), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            go.GetComponent<RectTransform>().SetParent(parent, false);
            go.GetComponent<LayoutElement>().minHeight = 44f;
            go.GetComponent<Image>().color = new Color(0.18f, 0.2f, 0.25f, 1f);
            go.GetComponent<Button>().onClick.AddListener(() => onRowClick());
            var h = go.GetComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(8, 8, 6, 6);
            h.spacing = 8f;
            h.childControlHeight = true;
            h.childControlWidth = false;
            h.childAlignment = TextAnchor.MiddleCenter;
            if (costSp != null) AddRowIcon(go.transform, costSp);
            AddRowLabel(go.transform, "Cost", cost.ToString());
            AddRowLabel(go.transform, "Eq", "=");
            if (getSp != null) AddRowIcon(go.transform, getSp);
            AddRowLabel(go.transform, "Gr", grant.ToString());
        }

        private static void AddRowLabel(Transform p, string name, string s)
        {
            var t = NewText(p, name, s, 17, FontStyle.Normal, 22f);
            t.alignment = TextAnchor.MiddleCenter;
        }

        private static void AddRowIcon(Transform parent, Sprite sp)
        {
            if (sp == null) return;
            var igo = new GameObject("Ic", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            igo.GetComponent<RectTransform>().SetParent(parent, false);
            var le = igo.GetComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = 28f;
            le.minHeight = le.preferredHeight = 28f;
            igo.GetComponent<Image>().sprite = sp;
            igo.GetComponent<Image>().preserveAspect = true;
        }

        private static Text NewText(Transform p, string name, string s, int size, FontStyle fs, float minH)
        {
            var tgo = new GameObject(name, typeof(Text), typeof(LayoutElement));
            tgo.transform.SetParent(p, false);
            var t = tgo.GetComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text = s;
            t.fontSize = size;
            t.fontStyle = fs;
            t.color = new Color(0.94f, 0.92f, 0.86f);
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            var le = tgo.GetComponent<LayoutElement>();
            le.minHeight = minH;
            le.flexibleWidth = 1f;
            return t;
        }

        private void AddModalBtn(Transform p, string label, Action act)
        {
            var bgo = new GameObject(label, typeof(Image), typeof(Button), typeof(LayoutElement));
            bgo.transform.SetParent(p, false);
            bgo.GetComponent<LayoutElement>().minWidth = 100f;
            bgo.GetComponent<LayoutElement>().minHeight = 40f;
            bgo.GetComponent<Image>().color = new Color(0.28f, 0.25f, 0.3f, 1f);
            bgo.GetComponent<Button>().onClick.AddListener(() => act());
            NewText(bgo.transform, "L", label, 18, FontStyle.Normal, 40f);
        }

        private static Transform FindChildByName(Transform root, string name)
        {
            if (root == null) return null;
            var a = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in a)
            {
                if (t != null && string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase))
                    return t;
            }
            return null;
        }
    }
}
