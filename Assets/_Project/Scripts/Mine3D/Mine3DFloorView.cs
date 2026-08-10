using UnityEngine;
using UnityEngine.UI;

namespace Project.Mine3D
{
    /// <summary>
    /// Маркер этажа 3D-шахты и ссылки на корни контента / UI для MineSceneController.
    /// </summary>
    public sealed class Mine3DFloorView : MonoBehaviour
    {
        public int Floor;
        public string Difficulty;
        public Transform MonsterRoot;
        public Transform BarrierRoot;
        public RectTransform FloorUi;
        public Button MonsterButton;
        public Button LockButton;

        public void SetGameplayVisible(bool unlocked, bool monsterReady)
        {
            if (BarrierRoot != null)
                BarrierRoot.gameObject.SetActive(!unlocked);
            if (MonsterRoot != null)
                MonsterRoot.gameObject.SetActive(unlocked && monsterReady);
        }

        public void BindInteractables()
        {
            // Старый невидимый хитбокс больше не нужен — клик только по капсуле/барьеру.
            var legacy = transform.Find("FloorClickVolume");
            if (legacy != null)
                Destroy(legacy.gameObject);

            if (MonsterRoot != null)
            {
                foreach (var col in MonsterRoot.GetComponentsInChildren<Collider>(true))
                {
                    if (col == null) continue;
                    col.enabled = true;
                    BindOne(col.gameObject);
                }
            }

            if (BarrierRoot != null)
            {
                var plate = BarrierRoot.Find("BarrierPlate");
                if (plate != null)
                {
                    var col = plate.GetComponent<Collider>() ?? plate.gameObject.AddComponent<BoxCollider>();
                    col.enabled = true;
                    BindOne(plate.gameObject);
                }
            }
        }

        private void BindOne(GameObject go)
        {
            if (go == null) return;
            var inter = go.GetComponent<Mine3DFloorInteractable>()
                        ?? go.AddComponent<Mine3DFloorInteractable>();
            inter.Floor = Floor;
            inter.Difficulty = Difficulty;
            inter.FloorView = this;
        }
    }
}
