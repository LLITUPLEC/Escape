using UnityEngine;

namespace Project.Duel
{
    public sealed class TimerOpenCondition : MonoBehaviour
    {
        [SerializeField] private Door door;
        [SerializeField] private float openAfterSeconds = 5f;

        public void Configure(Door targetDoor, float seconds)
        {
            door = targetDoor;
            openAfterSeconds = seconds;
        }

        private float _t;

        private void Awake()
        {
            if (door == null) door = GetComponentInChildren<Door>();
        }

        private void Update()
        {
            if (door == null) return;

            _t += Time.deltaTime;
            if (_t >= openAfterSeconds)
            {
                door.SetOpen(true);
                enabled = false;
            }
        }
    }
}

