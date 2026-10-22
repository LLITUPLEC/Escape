using UnityEngine;

namespace Project.Nakama
{
    [CreateAssetMenu(menuName = "Project/Nakama/Connection Config", fileName = "NakamaConnectionConfig")]
    public sealed class NakamaConnectionConfig : ScriptableObject
    {
        [Header("Server")]
        public string scheme = "https";
        public string host = "prokrust-play.ru";
        public int port = 443;
        [Tooltip("Server key из Nakama (обычно 'defaultkey' если не меняли).")]
        public string serverKey = "defaultkey";

        [Header("Client")]
        public bool useSsl = true;
        [Tooltip("Если true — логирует сетевые события в консоль.")]
        public bool verboseLogging = false;

        public string GetScheme() => useSsl ? "https" : scheme;
    }
}

