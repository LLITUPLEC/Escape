using UnityEngine;

namespace Project.Duel
{
    /// <summary>
    /// Если объект клавиатуры нельзя переименовать в Keypad_left_2 и т.п.,
    /// повесьте компонент на корень с NavKeypad.Keypad и укажите id двери 1–4.
    /// Длина кода: двери 1/3 — 2 цифры, 2/4 — 3. PIN на сервере (Nakama).
    /// </summary>
    public sealed class DuelKeypadDoorLink : MonoBehaviour
    {
        [Tooltip("1 = L1, 2 = L2, 3 = R1, 4 = R2")]
        [SerializeField] [Range(1, 4)] private int duelDoorId = 1;

        public int DuelDoorId => duelDoorId;

        public bool TryGetDoorConfig(out int doorId, out int codeLen)
        {
            doorId = duelDoorId;
            return DuelDoorPins.TryGetCodeLengthForDoorId(doorId, out codeLen);
        }
    }
}
