using UnityEngine;

namespace Project.Duel
{
    public sealed class DuelPlayerIdentity : MonoBehaviour
    {
        [SerializeField] private string userId;
        [SerializeField] private bool isLocal;

        public string UserId => userId;
        public bool IsLocal => isLocal;

        public void Set(string userIdValue, bool local)
        {
            userId = userIdValue;
            isLocal = local;
        }
    }
}

