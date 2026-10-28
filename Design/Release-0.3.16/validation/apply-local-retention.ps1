[CmdletBinding()]
param([switch]$Apply)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..')).TrimEnd('\','/')
if (-not (Test-Path -LiteralPath (Join-Path $taskRoot 'Assets/_Project'))) { throw 'Not the Escape workspace' }
$taskTargets = @('Builds/0.3.14','Design/Release-0.3.14')

function Assert-OwnedDirectory([string]$Relative) {
    if ($Relative -notin $taskTargets) { throw 'Target is not in the exact retention allowlist' }
    $taskFull = [IO.Path]::GetFullPath((Join-Path $taskRoot $Relative))
    if (-not $taskFull.StartsWith($taskRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Target escapes workspace' }
    $taskCursor = $taskFull
    while ($taskCursor.Length -gt $taskRoot.Length) {
        if (Test-Path -LiteralPath $taskCursor) {
            if (((Get-Item -LiteralPath $taskCursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Reparse-point ancestor rejected' }
        }
        $taskCursor = [IO.Path]::GetDirectoryName($taskCursor)
    }
    if (Test-Path -LiteralPath $taskFull) {
        if (@(Get-ChildItem -LiteralPath $taskFull -Force -Recurse -Attributes ReparsePoint).Count -gt 0) { throw 'Nested reparse point rejected' }
    }
    return $taskFull
}

function Verify-RetainedApk([string]$Version) {
    $taskReport = Get-Content -Raw -LiteralPath (Join-Path $taskRoot ('Design/Release-' + $Version + '/validation/apk-validation.json')) | ConvertFrom-Json
    $taskApks = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot ('Builds/' + $Version)) -Filter '*.apk' -File)
    if ($taskApks.Count -ne 1 -or $taskReport.version -cne $Version -or -not $taskReport.signature_verified -or $taskReport.development_build -or $taskReport.debuggable) { throw 'Retained APK identity is not validated' }
    $taskHash = (Get-FileHash -LiteralPath $taskApks[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($taskHash -cne $taskReport.sha256 -or $taskApks[0].Length -ne $taskReport.bytes) { throw 'Retained APK bytes changed' }
    return [ordered]@{version=$Version;file=$taskApks[0].Name;bytes=$taskApks[0].Length;sha256=$taskHash}
}

$taskDeleted = @()
if ($Apply) {
    $taskPublic = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'public-file-integrity.json') | ConvertFrom-Json
    $taskSnapshot = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'source-snapshot-check.json') | ConvertFrom-Json
    if (-not $taskPublic.passed -or -not $taskPublic.fullApkVerified -or $taskPublic.version -cne '0.3.16' -or -not $taskSnapshot.passed -or $taskSnapshot.version -cne '0.3.16') { throw 'Successful new publication and source proof required before retention cleanup' }
    $taskRetained = @(Verify-RetainedApk '0.3.15'; Verify-RetainedApk '0.3.16')
}
foreach ($taskRelative in $taskTargets) {
    $taskTarget = Assert-OwnedDirectory $taskRelative
    if (-not (Test-Path -LiteralPath $taskTarget)) { continue }
    $taskFiles = @(Get-ChildItem -LiteralPath $taskTarget -File -Force -Recurse)
    $taskBytes = ($taskFiles | Measure-Object Length -Sum).Sum
    if ($Apply) { Remove-Item -LiteralPath $taskTarget -Recurse -Force }
    $taskDeleted += [ordered]@{path=$taskRelative;files=$taskFiles.Count;bytes=[long]$taskBytes;deleted=[bool]$Apply}
}
$taskResult = [ordered]@{passed=$true;applied=[bool]$Apply;utc=[DateTime]::UtcNow.ToString('o');workspace=$taskRoot;targets=$taskDeleted;retainedVersions=@('0.3.16','0.3.15');gitHistoryRewritten=$false}
if ($Apply) {
    $taskResult['retainedApks'] = $taskRetained
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'local-retention-result.json'),($taskResult | ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
}
$taskResult | ConvertTo-Json -Depth 6
