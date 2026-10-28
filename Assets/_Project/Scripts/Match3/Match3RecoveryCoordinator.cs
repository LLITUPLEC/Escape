using System;
using System.Threading;
using System.Threading.Tasks;
using Project.Nakama;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Project.Match3
{
    /// <summary>Cold start restores only a server-confirmed durable match; no local match ID can create or pay an entry.</summary>
    public sealed class Match3RecoveryCoordinator : MonoBehaviour
    {
        private CancellationTokenSource _lifetime;
        private string _checkedOwner;
        private int _checkedGeneration;
        private bool _checking;
        private float _nextAttempt;
        private void Awake() => _lifetime = new CancellationTokenSource();
        private void OnDestroy() { _lifetime?.Cancel(); _lifetime?.Dispose(); }
        private void Update()
        {
            var boot = NakamaBootstrap.Instance;
            if (_checking || Time.unscaledTime < _nextAttempt || boot == null || !boot.InitialSetupCompleted ||
                !boot.GameplaySessionAllowed || !ServerConnectionGuard.CanPlay || SceneManager.GetActiveScene().name != "MainMenu") return;
            var owner = LocalAccountCacheScope.Current;
            if (owner == null || owner == _checkedOwner && boot.AuthenticationGeneration == _checkedGeneration) return;
            _checking = true;
            _ = RestoreAsync();
        }
        private async Task RestoreAsync()
        {
            var owner = Match3AccountOperation.Capture();
            try
            {
                var reply = await Match3RecoveryService.RecoverAsync(null, null, _lifetime.Token);
                if (!owner.IsCurrent || SceneManager.GetActiveScene().name != "MainMenu") return;
                if (reply == null || !reply.ok || reply.pending_admission) { _nextAttempt = Time.unscaledTime + 3; return; }
                _checkedOwner = owner.OwnerScope; _checkedGeneration = owner.Generation;
                if (!Match3RecoveryService.CanRestore(reply, owner.OwnerScope, owner.Generation)) return;
                if (Match3LaunchContext.ArmRecoveredMatch(reply, owner.OwnerScope, owner.Generation))
                    SceneManager.LoadScene("DuelMatch3");
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { _nextAttempt = Time.unscaledTime + 4; Debug.LogWarning("[Match3] Active match recovery: " + e.Message); }
            finally { _checking = false; }
        }
    }
}
