using UnityEngine;

namespace Project.UI
{
    [DisallowMultipleComponent]
    public sealed class ArenaMenuSceneCleanup : MonoBehaviour
    {
        [SerializeField] private string[] destroyByName =
        {
            "ProfileProgressHud",
            "OnlinePlayersBadge",
            "OnlinePlayersBadge(Clone)",
        };

        private void Awake()
        {
            foreach (var n in destroyByName)
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                var go = GameObject.Find(n);
                if (go != null) Destroy(go);
            }
        }
    }
}

