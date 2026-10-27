using UnityEngine;

namespace Project.Audio
{
    /// <summary>Author future background-music sources on the Music bus; unmarked legacy sources are sounds.</summary>
    [RequireComponent(typeof(AudioSource))]
    [DisallowMultipleComponent]
    public sealed class ExpeditionAudioSourceBus : MonoBehaviour
    {
        [SerializeField] private ExpeditionAudioBus bus = ExpeditionAudioBus.Sound;
        public ExpeditionAudioBus Bus => bus;
        private void OnEnable() => ExpeditionAudioRouting.RegisterSource(GetComponent<AudioSource>(), bus);
        private void OnDestroy() => ExpeditionAudioRouting.UnregisterSource(GetComponent<AudioSource>());
    }
}
