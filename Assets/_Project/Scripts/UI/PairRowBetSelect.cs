using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>
    /// Две взаимоисключающие ставки на игроков пары (слева/справа от Vs).
    /// Видимость управляется хост-тогглом BettingToggle через <see cref="SetBettingUiActive"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PairRowBetSelect : MonoBehaviour
    {
        public const string BetAName = "BetA";
        public const string BetBName = "BetB";

        [SerializeField] private Toggle betA;
        [SerializeField] private Toggle betB;
        [SerializeField] private ToggleGroup group;

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

        private void Awake()
        {
            ResolveRefs();
            WireGroup();
        }

        public void ResolveRefs()
        {
            if (betA == null) betA = transform.Find(BetAName)?.GetComponent<Toggle>();
            if (betB == null) betB = transform.Find(BetBName)?.GetComponent<Toggle>();
            if (group == null) group = GetComponent<ToggleGroup>();
        }

        public void WireGroup()
        {
            if (group == null) return;
            group.allowSwitchOff = true;
            if (betA != null)
            {
                betA.group = group;
                betA.isOn = false;
            }
            if (betB != null)
            {
                betB.group = group;
                betB.isOn = false;
            }
        }

        public void SetBettingUiActive(bool active)
        {
            ResolveRefs();
            if (betA != null) betA.gameObject.SetActive(active);
            if (betB != null) betB.gameObject.SetActive(active);
            if (!active)
            {
                if (betA != null) betA.SetIsOnWithoutNotify(false);
                if (betB != null) betB.SetIsOnWithoutNotify(false);
            }
        }

        public void ClearSelection()
        {
            if (betA != null) betA.SetIsOnWithoutNotify(false);
            if (betB != null) betB.SetIsOnWithoutNotify(false);
        }
    }
}
