using System;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>
    /// Две взаимоисключающие ставки на игроков пары (слева/справа от Vs).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PairRowBetSelect : MonoBehaviour
    {
        public const string BetAName = "BetA";
        public const string BetBName = "BetB";

        [SerializeField] private Toggle betA;
        [SerializeField] private Toggle betB;
        [SerializeField] private ToggleGroup group;

        private int _slot;
        private string _uidA = "";
        private string _uidB = "";
        private bool _suppressNotify;
        private Action<int, int> _onSideChanged;

        /// <summary>0 = левый (A), 1 = правый (B), -1 = нет выбора.</summary>
        public int SelectedSide
        {
            get
            {
                if (betA != null && betA.isOn) return 0;
                if (betB != null && betB.isOn) return 1;
                return -1;
            }
        }

        public int Slot => _slot;
        public string UidA => _uidA;
        public string UidB => _uidB;

        private void Awake()
        {
            ResolveRefs();
            EnsureGroup();
            HookToggles(true);
        }

        private void OnDestroy()
        {
            HookToggles(false);
        }

        public void ResolveRefs()
        {
            if (betA == null) betA = FindDeep(transform, BetAName)?.GetComponent<Toggle>();
            if (betB == null) betB = FindDeep(transform, BetBName)?.GetComponent<Toggle>();
            if (group == null) group = GetComponent<ToggleGroup>();
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name))
                return null;
            var direct = root.Find(name);
            if (direct != null)
                return direct;
            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found != null)
                    return found;
            }
            return null;
        }

        public void EnsureGroup()
        {
            if (group == null) return;
            group.allowSwitchOff = true;
            if (betA != null) betA.group = group;
            if (betB != null) betB.group = group;
        }

        public void Configure(int slot, string uidA, string uidB, Action<int, int> onSideChanged)
        {
            _slot = slot;
            _uidA = uidA ?? "";
            _uidB = uidB ?? "";
            _onSideChanged = onSideChanged;
            ResolveRefs();
            EnsureGroup();
            HookToggles(true);
        }

        /// <summary>
        /// visible — показать кнопки; interactable — можно менять; side -1/0/1.
        /// </summary>
        public void ApplyState(bool visible, bool interactable, int side)
        {
            ResolveRefs();
            EnsureGroup();
            if (betA != null) betA.gameObject.SetActive(visible);
            if (betB != null) betB.gameObject.SetActive(visible);
            if (!visible)
            {
                SetSideWithoutNotify(-1);
                return;
            }

            if (betA != null) betA.interactable = interactable;
            if (betB != null) betB.interactable = interactable;
            SetSideWithoutNotify(side);
        }

        public void SetSideWithoutNotify(int side)
        {
            ResolveRefs();
            _suppressNotify = true;
            try
            {
                if (betA != null) betA.SetIsOnWithoutNotify(side == 0);
                if (betB != null) betB.SetIsOnWithoutNotify(side == 1);
            }
            finally
            {
                _suppressNotify = false;
            }
        }

        public void SetBettingUiActive(bool active)
        {
            ApplyState(active, active, active ? SelectedSide : -1);
        }

        public void ClearSelection()
        {
            SetSideWithoutNotify(-1);
        }

        private void HookToggles(bool on)
        {
            ResolveRefs();
            if (betA != null)
            {
                betA.onValueChanged.RemoveListener(OnBetAChanged);
                if (on) betA.onValueChanged.AddListener(OnBetAChanged);
            }
            if (betB != null)
            {
                betB.onValueChanged.RemoveListener(OnBetBChanged);
                if (on) betB.onValueChanged.AddListener(OnBetBChanged);
            }
        }

        private void OnBetAChanged(bool isOn)
        {
            if (_suppressNotify) return;
            if (isOn) _onSideChanged?.Invoke(_slot, 0);
            else if (betB == null || !betB.isOn) _onSideChanged?.Invoke(_slot, -1);
        }

        private void OnBetBChanged(bool isOn)
        {
            if (_suppressNotify) return;
            if (isOn) _onSideChanged?.Invoke(_slot, 1);
            else if (betA == null || !betA.isOn) _onSideChanged?.Invoke(_slot, -1);
        }
    }
}
