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
    /// Клик по иконке энергии (или устаревшей «+») в HeaderResources: модалка с двумя вариантами и подтверждением, RPC <see cref="PlayerResourcesService.BuyEnergyAsync"/>.</summary>
    public sealed class EnergyHeaderPurchaseController
    {
        private const int MatterCost = 1;
        private const int MatterGrant = 100;
        private const int GoldCost = 1000;
        private const int GoldGrant = 100;
        private const int MaxPacks = 50;

        private const float UiScale = 1.5f;
        private const int DialogFontSize = 38; // ~25 * 1.5
        private const float PanelWidth = 660f; // 440 * 1.5
            private const float PanelHeight = 620f;
        private const float PanelPad = 27f; // 18 * 1.5
        private const float PanelInnerWidth = PanelWidth - PanelPad * 2f; // 606

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
        private int _pendingCount = 1;
        private UnityAction _openModalAction;

        private OfferRowState _matterRow;
        private OfferRowState _goldRow;

        private sealed class OfferRowState
        {
            public bool IsMatter;
            public int BaseCost;
            public int BaseGrant;
            public int Qty = 1;
            public Text CostLabel;
            public Text GrantLabel;
            public Button UpButton;
            public Button DownButton;
        }

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

        public void EnsurePlusOnEnergyRow(Transform searchRoot)
        {
            if (searchRoot == null) return;
            var energyTr = FindChildByName(searchRoot, "Energy");
            if (energyTr == null) return;
            if (_openModalAction == null)
                _openModalAction = ShowChoiceModal;

            var existingPlus = energyTr.Find("EnergyBuyPlus");
            if (existingPlus != null)
            {
                var plusBtn = existingPlus.GetComponent<Button>() ?? existingPlus.GetComponentInChildren<Button>(true);
                if (plusBtn != null)
                    WirePurchaseButton(plusBtn);
                return;
            }

            var iconTr = energyTr.Find("Icon") ?? FindChildByName(energyTr, "Icon");
            if (iconTr == null) return;
            var iconImg = iconTr.GetComponent<Image>() ?? iconTr.gameObject.AddComponent<Image>();
            iconImg.raycastTarget = true;
            var iconBtn = iconTr.GetComponent<Button>() ?? iconTr.gameObject.AddComponent<Button>();
            if (iconBtn.targetGraphic == null)
                iconBtn.targetGraphic = iconImg;
            WirePurchaseButton(iconBtn);
        }

        /// <summary>Открыть модалку покупки энергии (нехватка энергии в шахте / PvE и т.п.).</summary>
        public void ShowPurchaseDialog()
        {
            ShowChoiceModal();
        }

        private void WirePurchaseButton(Button btn)
        {
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
            btn.onClick.RemoveListener(_openModalAction);
            btn.onClick.AddListener(_openModalAction);
        }

        private void ShowChoiceModal()
        {
            EnsureModal();
            if (_root == null) return;
            ResetOfferQty(_matterRow);
            ResetOfferQty(_goldRow);
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
            _pendingCount = _matterRow != null ? Mathf.Max(1, _matterRow.Qty) : 1;
            _choiceRoot.SetActive(false);
            _confirmRoot.SetActive(true);
            if (_confirmText != null)
                _confirmText.text = BuildConfirmLine(true, _pendingCount);
        }

        private void OnPickGold()
        {
            _pendingMode = "gold";
            _pendingCount = _goldRow != null ? Mathf.Max(1, _goldRow.Qty) : 1;
            _choiceRoot.SetActive(false);
            _confirmRoot.SetActive(true);
            if (_confirmText != null)
                _confirmText.text = BuildConfirmLine(false, _pendingCount);
        }

        private static string BuildConfirmLine(bool matter, int count)
        {
            count = Mathf.Clamp(count, 1, MaxPacks);
            if (matter)
            {
                var total = MatterCost * count;
                return count == 1
                    ? $"Потратить {total} ед. материи?"
                    : $"Потратить {total} ед. материи (x{count})?";
            }

            var goldTotal = GoldCost * count;
            return count == 1
                ? $"Потратить {goldTotal} золота?"
                : $"Потратить {goldTotal} золота (x{count})?";
        }

        private async void OnConfirmYes()
        {
            var mode = _pendingMode;
            var count = Mathf.Clamp(_pendingCount, 1, MaxPacks);
            HideAll();
            try
            {
                var r = await PlayerResourcesService.BuyEnergyAsync(mode, count, _sceneCt).ConfigureAwait(true);
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
            panelRt.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            panel.GetComponent<Image>().color = new Color(0.11f, 0.13f, 0.18f, 1f);
            panel.GetComponent<Image>().raycastTarget = true;

            var v = panel.GetComponent<VerticalLayoutGroup>();
            var pad = Mathf.RoundToInt(PanelPad);
            v.padding = new RectOffset(pad, pad, Mathf.RoundToInt(16f * UiScale), Mathf.RoundToInt(16f * UiScale));
            v.spacing = 12f; // 8 * 1.5
            v.childControlHeight = true;
            v.childControlWidth = true;
            v.childForceExpandWidth = false;
            v.childAlignment = TextAnchor.UpperCenter;

            NewText(panel.transform, "Title", "Купить энергию", DialogFontSize, FontStyle.Bold, 54f, flexibleWidth: 0f, preferredWidth: PanelInnerWidth);

            _choiceRoot = new GameObject("ChoiceBlock", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            var chRt = _choiceRoot.GetComponent<RectTransform>();
            chRt.SetParent(panel.transform, false);
            chRt.sizeDelta = new Vector2(PanelInnerWidth, 360f);
            var chLe = _choiceRoot.GetComponent<LayoutElement>();
            chLe.preferredWidth = PanelInnerWidth;
            chLe.flexibleWidth = 0f;
            chLe.minHeight = 360f;
            var chV = _choiceRoot.GetComponent<VerticalLayoutGroup>();
            chV.spacing = 15f; // 10 * 1.5
            chV.childControlHeight = true;
            chV.childControlWidth = true;
            chV.childForceExpandWidth = false;
            chV.childAlignment = TextAnchor.UpperCenter;

            _matterRow = MakeOfferRow(_choiceRoot.transform, _matterSprite, MatterCost, _energySprite, MatterGrant, isMatter: true, OnPickMatter);
            _goldRow = MakeOfferRow(_choiceRoot.transform, _goldSprite, GoldCost, _energySprite, GoldGrant, isMatter: false, OnPickGold);

            _confirmRoot = new GameObject("Confirm", typeof(RectTransform), typeof(VerticalLayoutGroup));
            _confirmRoot.GetComponent<RectTransform>().SetParent(panel.transform, false);
            var cV = _confirmRoot.GetComponent<VerticalLayoutGroup>();
            cV.spacing = 18f; // 12 * 1.5
            cV.childControlHeight = true;
            cV.childControlWidth = true;
            cV.childForceExpandWidth = false;
            cV.childAlignment = TextAnchor.MiddleCenter;

            _confirmText = NewText(_confirmRoot.transform, "Q", "", DialogFontSize, FontStyle.Bold, 60f, flexibleWidth: 0f, preferredWidth: PanelInnerWidth);
            var yn = new GameObject("YesNo", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            yn.GetComponent<RectTransform>().SetParent(_confirmRoot.transform, false);
            var h = yn.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 36f; // 24 * 1.5
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

        private OfferRowState MakeOfferRow(
            Transform parent,
            Sprite costSp,
            int cost,
            Sprite getSp,
            int grant,
            bool isMatter,
            Action onRowClick)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(Button), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            go.GetComponent<RectTransform>().SetParent(parent, false);
            var rowLe = go.GetComponent<LayoutElement>();
            rowLe.minHeight = 160f;
            rowLe.preferredHeight = 160f;
            rowLe.preferredWidth = PanelInnerWidth;
            rowLe.flexibleWidth = 0f;
            go.GetComponent<Image>().color = new Color(0.18f, 0.2f, 0.25f, 1f);
            go.GetComponent<Button>().onClick.AddListener(() => onRowClick());
            var h = go.GetComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(12, 12, 6, 6); // ~8/4 * 1.5
            h.spacing = 6f;
            h.childControlHeight = true;
            h.childControlWidth = true;
            h.childForceExpandWidth = false;
            h.childAlignment = TextAnchor.MiddleCenter;

            var state = new OfferRowState
            {
                IsMatter = isMatter,
                BaseCost = cost,
                BaseGrant = grant,
                Qty = 1,
            };

            AddStepper(go.transform, state);

            if (costSp != null) AddRowIcon(go.transform, costSp);
            state.CostLabel = AddRowLabel(go.transform, "Cost", cost.ToString(), 132f); // 88 * 1.5
            AddRowLabel(go.transform, "Eq", "=", 42f); // 28 * 1.5
            if (getSp != null) AddRowIcon(go.transform, getSp);
            state.GrantLabel = AddRowLabel(go.transform, "Gr", grant.ToString(), 132f);

            RefreshOfferRow(state);
            return state;
        }

        private void AddStepper(Transform row, OfferRowState state)
        {
            var box = new GameObject("Stepper", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            box.transform.SetParent(row, false);
            var le = box.GetComponent<LayoutElement>();
            le.preferredWidth = 108f; // 54 * 2
            le.minWidth = 108f;
            le.preferredHeight = 156f; // 78 * 2
            le.minHeight = 144f;
            le.flexibleWidth = 0f;
            var v = box.GetComponent<VerticalLayoutGroup>();
            v.spacing = 6f;
            v.childAlignment = TextAnchor.MiddleCenter;
            v.childControlHeight = true;
            v.childControlWidth = true;
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = true;
            v.padding = new RectOffset(0, 0, 0, 0);

            state.UpButton = MakeStepperButton(box.transform, "Up", "+", () => ChangeQty(state, +1));
            state.DownButton = MakeStepperButton(box.transform, "Down", "-", () => ChangeQty(state, -1));
        }

        private static Button MakeStepperButton(Transform parent, string name, string label, Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.28f, 0.32f, 0.40f, 1f);
            img.raycastTarget = true;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = 72f; // 36 * 2
            le.minHeight = 66f;
            le.flexibleWidth = 1f;

            var t = NewText(go.transform, "L", label, 36, FontStyle.Bold, 66f, flexibleWidth: 0f, preferredWidth: 96f);
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            return btn;
        }

        private void ChangeQty(OfferRowState state, int delta)
        {
            if (state == null) return;
            var max = ResolveMaxQty(state);
            state.Qty = Mathf.Clamp(state.Qty + delta, 1, max);
            RefreshOfferRow(state);
        }

        private void ResetOfferQty(OfferRowState state)
        {
            if (state == null) return;
            state.Qty = 1;
            RefreshOfferRow(state);
        }

        private void RefreshOfferRow(OfferRowState state)
        {
            if (state == null) return;
            var max = ResolveMaxQty(state);
            if (state.Qty > max) state.Qty = max;
            if (state.Qty < 1) state.Qty = 1;

            if (state.CostLabel != null)
                state.CostLabel.text = (state.BaseCost * (long)state.Qty).ToString();
            if (state.GrantLabel != null)
                state.GrantLabel.text = (state.BaseGrant * (long)state.Qty).ToString();

            if (state.UpButton != null)
                state.UpButton.interactable = state.Qty < max;
            if (state.DownButton != null)
                state.DownButton.interactable = state.Qty > 1;
        }

        private static int ResolveMaxQty(OfferRowState state)
        {
            if (state == null)
                return 1;

            var grantPerPack = state.BaseGrant;
            var costPerPack = state.BaseCost;
            var max = MaxPacks;
            if (PlayerResourcesService.TryReadCached(out var res) && res != null && res.ok)
            {
                if (res.energy_max > 0 && grantPerPack > 0)
                {
                    var room = res.energy_max - res.energy;
                    if (room <= 0)
                        return 1;
                    max = Math.Min(max, Math.Max(1, room / grantPerPack));
                }

                if (costPerPack > 0)
                {
                    var wallet = state.IsMatter ? res.matter : res.gold;
                    if (wallet >= 0)
                        max = Math.Min(max, Math.Max(1, (int)(wallet / costPerPack)));
                }
            }

            return Mathf.Clamp(max, 1, MaxPacks);
        }

        private static Text AddRowLabel(Transform p, string name, string s, float preferredWidth)
        {
            var t = NewText(p, name, s, DialogFontSize, FontStyle.Bold, 51f, flexibleWidth: 0f, preferredWidth: preferredWidth);
            t.alignment = TextAnchor.MiddleCenter;
            return t;
        }

        private static void AddRowIcon(Transform parent, Sprite sp)
        {
            if (sp == null) return;
            var igo = new GameObject("Ic", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            igo.GetComponent<RectTransform>().SetParent(parent, false);
            var le = igo.GetComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = 48f; // 32 * 1.5
            le.minHeight = le.preferredHeight = 48f;
            le.flexibleWidth = 0f;
            igo.GetComponent<Image>().sprite = sp;
            igo.GetComponent<Image>().preserveAspect = true;
        }

        private static Text NewText(Transform p, string name, string s, int size, FontStyle fs, float minH, float flexibleWidth = 1f, float preferredWidth = 0f)
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
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var le = tgo.GetComponent<LayoutElement>();
            le.minHeight = minH;
            le.flexibleWidth = flexibleWidth;
            if (preferredWidth > 0.5f)
                le.preferredWidth = preferredWidth;
            return t;
        }

        private void AddModalBtn(Transform p, string label, Action act)
        {
            var bgo = new GameObject(label, typeof(Image), typeof(Button), typeof(LayoutElement));
            bgo.transform.SetParent(p, false);
            bgo.GetComponent<LayoutElement>().minWidth = 150f; // 100 * 1.5
            bgo.GetComponent<LayoutElement>().minHeight = 60f; // 40 * 1.5
            bgo.GetComponent<Image>().color = new Color(0.28f, 0.25f, 0.3f, 1f);
            bgo.GetComponent<Button>().onClick.AddListener(() => act());
            NewText(bgo.transform, "L", label, DialogFontSize, FontStyle.Bold, 60f, flexibleWidth: 0f, preferredWidth: 132f);
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
