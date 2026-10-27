$ErrorActionPreference = 'Stop'
$workspacePath = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$outputPath = Join-Path $workspacePath '.utmp/expedition/validation-audio-selected'
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$workshopPath = Join-Path $workspacePath 'Assets/_Project/Scripts/Character/UI/WorkshopSceneController.cs'
$audioPath = Join-Path $workspacePath 'Assets/_Project/Scripts/Audio/ExpeditionAudio.cs'
$workshopSource = [IO.File]::ReadAllText($workshopPath)
$audioSource = [IO.File]::ReadAllText($audioPath)
$hashes = @{ workshop = (Get-FileHash -LiteralPath $workshopPath).Hash; audio = (Get-FileHash -LiteralPath $audioPath).Hash }
$methodNames = @('OnCreateClicked', 'OnClaimClicked', 'OnRushClicked', 'ClaimAtSlot', 'RunCraftRequestAsync')
$methods = foreach ($methodName in $methodNames) {
    $pattern = '(?ms)^        private async Task ' + [regex]::Escape($methodName) + '\([^\r\n]*\)\r?\n        \{.*?^        \}'
    $matches = [regex]::Matches($workshopSource, $pattern)
    if ($matches.Count -ne 1) { throw "Cannot extract $methodName" }
    $matches[0].Value
}
$buttonClass = [regex]::Match($audioSource, '(?ms)^    public sealed class ExpeditionButtonSfx : MonoBehaviour\r?\n    \{.*?^    \}').Value
$readyClass = [regex]::Match($audioSource, '(?ms)^    public sealed class ExpeditionCraftReadyTracker\r?\n    \{.*?^    \}').Value
if (-not $buttonClass -or -not $readyClass) { throw 'Cannot extract actual audio components' }
$template = @'
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace SelectedAudioValidation {
    public enum ExpeditionCue { CraftStart, CraftReady, RewardClaim, UiBlocked, UiClick, UiBack }
    public static class ExpeditionAudio {
        public static readonly List<ExpeditionCue> Played = new List<ExpeditionCue>();
        public static void Play(ExpeditionCue cue) { Played.Add(cue); }
    }
    public sealed class ButtonEvent {
        private readonly List<Action> handlers = new List<Action>();
        public int Count => handlers.Count;
        public void AddListener(Action action) { handlers.Add(action); }
        public void RemoveListener(Action action) { handlers.RemoveAll(x => x == action); }
        public void Invoke() { foreach (var handler in handlers.ToArray()) handler(); }
    }
    public sealed class Button { public bool interactable = true; public ButtonEvent onClick = new ButtonEvent(); }
    public class MonoBehaviour { public Button boundButton; protected T GetComponent<T>() where T: class { return boundButton as T; } }
    public sealed class Text { public string text = ""; }
    public static class Debug { public static void LogWarning(string message) {} }
    public sealed class CharacterGetRpcResponse { public bool ok; public string err; }
    public static class CharacterProfileService {
        public static Func<CancellationToken, Task<CharacterGetRpcResponse>> Next;
        public static int Starts, Rushes, Claims, Slot;
        public static string Output;
        public static void Reset() { Starts = Rushes = Claims = 0; Slot = -1; Output = null; ExpeditionAudio.Played.Clear(); }
        public static Task<CharacterGetRpcResponse> WorkshopCraftStartAsync(int slot, string output, CancellationToken ct) { Starts++; Slot=slot; Output=output; return Next(ct); }
        public static Task<CharacterGetRpcResponse> WorkshopCraftRushAsync(int slot, CancellationToken ct) { Rushes++; Slot=slot; return Next(ct); }
        public static Task<CharacterGetRpcResponse> WorkshopCraftClaimAsync(int slot, CancellationToken ct) { Claims++; Slot=slot; return Next(ct); }
    }
    // ACTUAL_AUDIO_COMPONENTS
    public sealed class Harness {
        private bool _craftRequestInFlight;
        private CharacterGetRpcResponse _profile = new CharacterGetRpcResponse { ok=true };
        private int _selectedSlot = 1;
        private string _selectedOutputDefId = "recipe-a";
        private CancellationTokenSource _cts;
        private Button _createButton = new Button(), _claimButton = new Button(), _rushButton = new Button();
        private Text hintText = new Text();
        public int AppliedProfiles;
        public bool ThrowOnApply;
        public bool SlotBusy=true;
        public bool Busy => _craftRequestInFlight;
        public bool Disabled => !_createButton.interactable && !_claimButton.interactable && !_rushButton.interactable;
        public Task Create() => OnCreateClicked();
        public Task Rush() => OnRushClicked();
        public Task Claim() => OnClaimClicked();
        public void Select(int slot, string output) { _selectedSlot=slot; _selectedOutputDefId=output; }
        public void Cancel() { _cts.Cancel(); }
        private enum WorkshopSlotState { Empty, Busy, Ready }
        private WorkshopSlotState WorkshopState(int slot) => SlotBusy ? WorkshopSlotState.Busy : WorkshopSlotState.Empty;
        private string ErrToRu(string err) => err;
        private void ApplyProfile(CharacterGetRpcResponse response) { AppliedProfiles++; if (ThrowOnApply) throw new InvalidOperationException("controlled post-confirmation refresh error"); }
        private void UpdateDetailPanel() { _createButton.interactable = _claimButton.interactable = _rushButton.interactable = !_craftRequestInFlight; }
        // ACTUAL_WORKSHOP_METHODS
    }
    public static class Checks {
        public static readonly List<string> Passed = new List<string>();
        private static void Assert(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        private static CharacterGetRpcResponse Success() => new CharacterGetRpcResponse { ok=true };
        private static void One(ExpeditionCue cue) { Assert(ExpeditionAudio.Played.Count==1 && ExpeditionAudio.Played[0]==cue, "wrong or repeated audio: " + string.Join(",", ExpeditionAudio.Played)); }
        private static async Task RunAsync() {
            CharacterProfileService.Reset();
            var gate = new TaskCompletionSource<CharacterGetRpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            CharacterProfileService.Next = ct => gate.Task;
            var h = new Harness();
            var first = h.Create();
            var repeat = h.Create(); var mixed=h.Rush();
            Assert(h.Busy && h.Disabled && ExpeditionAudio.Played.Count==0, "must stay silent before confirmation");
            h.Select(7,"changed"); gate.SetResult(Success());
            await Task.WhenAll(first,repeat,mixed);
            Assert(CharacterProfileService.Starts==1 && CharacterProfileService.Rushes==0 && CharacterProfileService.Slot==1 && CharacterProfileService.Output=="recipe-a", "single-flight/captured selection regressed");
            Assert(!h.Busy && !h.Disabled, "success did not clear busy"); One(ExpeditionCue.CraftStart);
            Passed.Add("double-create-single-RPC-captured-slot-no-premature-sound-one-success-cue");
            foreach (var action in new[] { "rush", "claim" }) {
                CharacterProfileService.Reset(); CharacterProfileService.Next = ct => Task.FromResult(Success()); h=new Harness();
                if(action=="rush") await h.Rush(); else await h.Claim();
                One(action=="rush" ? ExpeditionCue.CraftStart : ExpeditionCue.RewardClaim);
                Passed.Add(action+"-confirmed-exactly-one-correct-cue");
            }
            foreach (var kind in new[] { "server-reject", "null", "transport" }) {
                CharacterProfileService.Reset();
                CharacterProfileService.Next = ct => kind=="transport" ? Task.FromException<CharacterGetRpcResponse>(new InvalidOperationException("fake failure")) : Task.FromResult(kind=="null" ? null : new CharacterGetRpcResponse { ok=false, err="not_enough_gold" });
                h=new Harness(); await h.Create(); One(ExpeditionCue.UiBlocked);
                Assert(!h.Busy && !h.Disabled && h.AppliedProfiles==0,"failure did not restore state");
                Passed.Add(kind+"-only-error-cue-and-busy-reset");
            }
            CharacterProfileService.Reset();
            var cancelGate = new TaskCompletionSource<CharacterGetRpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            CharacterProfileService.Next = ct => { ct.Register(()=>cancelGate.TrySetCanceled()); return cancelGate.Task; };
            h=new Harness(); var pending=h.Create(); h.Cancel(); await pending;
            Assert(ExpeditionAudio.Played.Count==0 && !h.Busy,"cancel must stay silent and clear busy"); Passed.Add("cancellation-silent");
            CharacterProfileService.Reset(); CharacterProfileService.Next = ct=>Task.FromResult(Success());
            h=new Harness { ThrowOnApply=true }; await h.Create(); One(ExpeditionCue.CraftStart); Passed.Add("post-success-render-refresh-error-does-not-play-false-failure");
            CharacterProfileService.Reset(); h=new Harness { SlotBusy=false }; await h.Rush(); h.Select(-1,""); await h.Create(); await h.Claim();
            Assert(ExpeditionAudio.Played.Count==0 && CharacterProfileService.Starts==0 && CharacterProfileService.Rushes==0 && CharacterProfileService.Claims==0,"invalid selection produced action/audio"); Passed.Add("invalid-preconditions-silent-no-RPC");
            var ready = new ExpeditionCraftReadyTracker();
            Assert(!ready.Observe(0,true,true),"already ready on first load sounded");
            Assert(!ready.Observe(0,false,false) && !ready.Observe(0,true,false),"initial/busy sounded");
            Assert(ready.Observe(0,true,true),"Busy->Ready not reported");
            for(var i=0;i<100;i++) Assert(!ready.Observe(0,true,true),"repeat timer sounded");
            Assert(!ready.Observe(0,false,false) && !ready.Observe(0,true,false) && ready.Observe(0,true,true),"next craft generation not reported");
            Assert(!ready.Observe(-1,true,true) && !ready.Observe(8,true,true),"invalid slot should stay silent"); Passed.Add("actual-ready-tracker-first-load-silent-one-transition-per-generation");
            CharacterProfileService.Reset(); var button=new Button(); var gameplayCalls=0; button.onClick.AddListener(()=>gameplayCalls++);
            var feedback = new ExpeditionButtonSfx { boundButton=button };
            for(var i=0;i<10;i++) feedback.Configure(ExpeditionCue.UiClick,false);
            Assert(button.onClick.Count==2,"styling duplicated listener or removed gameplay callback");
            button.onClick.Invoke(); Assert(gameplayCalls==1,"gameplay callback duplicated"); One(ExpeditionCue.UiClick);
            ExpeditionAudio.Played.Clear(); feedback.Configure(ExpeditionCue.UiBack,true); feedback.Configure(ExpeditionCue.UiClick,false); button.onClick.Invoke(); One(ExpeditionCue.UiBack);
            Passed.Add("actual-button-component-idempotent-binding-keeps-gameplay-and-explicit-back-cue");
        }
        public static string[] Run() { Task.Run((Func<Task>)RunAsync).GetAwaiter().GetResult(); return Passed.ToArray(); }
    }
}
'@
$harness = $template.Replace('// ACTUAL_AUDIO_COMPONENTS', $buttonClass + "`n" + $readyClass).Replace('// ACTUAL_WORKSHOP_METHODS', ($methods -join "`n`n"))
$harnessPath = Join-Path $outputPath 'SelectedAudioHarness.cs'
[IO.File]::WriteAllText($harnessPath, $harness, [Text.UTF8Encoding]::new($false))
Add-Type -Path $harnessPath
$passed = [SelectedAudioValidation.Checks]::Run()
if ($hashes.workshop -ne (Get-FileHash -LiteralPath $workshopPath).Hash -or $hashes.audio -ne (Get-FileHash -LiteralPath $audioPath).Hash) { throw 'Production source changed during verification' }
$report = [ordered]@{ passed=$true; scenarios=@($passed); source_sha256=$hashes; actual_bodies_used=$methodNames + @('ExpeditionButtonSfx','ExpeditionCraftReadyTracker'); scope='Managed fake services/buttons/PCM observer. No Unity, network, account data, or live RPC; renderer/audio output and Unity destroyed-object semantics are not simulated.'; production_sources_unchanged=$true }
$json = $report | ConvertTo-Json -Depth 6
[IO.File]::WriteAllText((Join-Path $outputPath 'functional-report.json'), $json, [Text.UTF8Encoding]::new($false))
$json
