using UnityEngine;

namespace Project.Duel
{
    [DisallowMultipleComponent]
    public sealed class DuelFinishTrigger : MonoBehaviour
    {
        private DuelRoomManager _room;

        public void SetRoom(DuelRoomManager room)
        {
            _room = room;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_room == null) return;

            var id = other.GetComponentInParent<DuelPlayerIdentity>();
            if (id == null) return;

            _room.NotifyPlayerReachedFinish(id.UserId);
        }
    }
}

