using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace Project.Utils
{
    /// <summary>
    /// Выполняет actions в Unity main thread. Нужно для колбэков Nakama, которые приходят не из главного потока.
    /// </summary>
    public sealed class MainThreadDispatcher : MonoBehaviour
    {
        private static MainThreadDispatcher _instance;
        private static readonly ConcurrentQueue<Action> Queue = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject("MainThreadDispatcher");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<MainThreadDispatcher>();
        }

        public static void Enqueue(Action action)
        {
            if (action == null) return;
            Queue.Enqueue(action);
        }

        private void Update()
        {
            while (Queue.TryDequeue(out var a))
            {
                try { a(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }
}

