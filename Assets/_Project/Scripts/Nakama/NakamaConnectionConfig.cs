using UnityEngine;

namespace Project.Nakama
{
    [CreateAssetMenu(menuName = "Project/Nakama/Connection Config", fileName = "NakamaConnectionConfig")]
    public sealed class NakamaConnectionConfig : ScriptableObject
    {
        [Header("Server")]
        public string scheme = "http";
        public string host = "127.0.0.1";
        public int port = 7350;
        [Tooltip("Server key из Nakama (обычно 'defaultkey' если не меняли).")]
        public string serverKey = "defaultkey";

        [Header("Client")]
        public bool useSsl = false;
        [Tooltip("Если true — логирует сетевые события в консоль.")]
        public bool verboseLogging = true;

        public string GetScheme() => useSsl ? "https" : scheme;
    }
}

