using Project.Networking;
using UnityEngine;

namespace Project.Player
{
    public sealed class NetworkTransformView : MonoBehaviour
    {
        [SerializeField] private bool isLocal;
        [SerializeField] private float lerpSpeed = 12f;

        private Vector3 _targetPos;
        private float _targetYaw;
        private bool _hasTarget;

        public bool IsLocal => isLocal;

        public void SetLocal(bool local)
        {
            isLocal = local;
        }

        public void SetTarget(NetTransformState state)
        {
            _targetPos = new Vector3(state.px, state.py, state.pz);
            _targetYaw = state.ry;
            _hasTarget = true;
        }

        private void Update()
        {
            if (isLocal || !_hasTarget) return;

            transform.position = Vector3.Lerp(transform.position, _targetPos, 1f - Mathf.Exp(-lerpSpeed * Time.deltaTime));
            var rot = Quaternion.Euler(0f, _targetYaw, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, 1f - Mathf.Exp(-lerpSpeed * Time.deltaTime));
        }
    }
}

