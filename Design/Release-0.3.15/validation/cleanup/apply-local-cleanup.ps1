param([switch]$Apply, [switch]$IncludeTemporary)
$ErrorActionPreference = 'Stop'
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..')).TrimEnd('\')
$workspacePrefix = $workspaceRoot + '\'
if (-not (Test-Path -LiteralPath (Join-Path $workspaceRoot 'Assets/_Project'))) { throw 'Unexpected workspace root' }
$plan = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'local-proposed-delete.json') -Raw -Encoding UTF8 | ConvertFrom-Json)
if ($IncludeTemporary) { $plan += [pscustomobject]@{path='.utmp';reason='temporary inputs migrated; tests archived'} }
$allowedRoots = @('Library','Logs','Builds/Expedition','Design/Expedition-Implementation','Design/Media-2026-10-24',
    'Escape-audio-choices-0.3.5.txt','arena_tour_log.txt','arena_tour_log copy.txt','log8.txt','Mine3D_create_log.txt',
    'unity_build_modal_log.txt','leaderboard_build.log','leaderboard_dedupe.log','UpgradeLog.htm')
$validated = @()
foreach ($entry in $plan) {
    $relative = $entry.path.Replace('\','/')
    $allowed = $relative -in $allowedRoots -or $relative -match '^Design/Release-0\.3\.(?:[0-9]|1[0-3]|16)$' -or ($IncludeTemporary -and $relative -eq '.utmp')
    if (-not $allowed) { throw "Path is outside the reviewed deletion allowlist: $relative" }
    $target = [IO.Path]::GetFullPath((Join-Path $workspaceRoot $relative))
    if (-not $target.StartsWith($workspacePrefix,[StringComparison]::OrdinalIgnoreCase)) { throw "Outside workspace: $target" }
    if (-not (Test-Path -LiteralPath $target)) { continue }
    $resolved = (Resolve-Path -LiteralPath $target).ProviderPath
    if ($resolved -cne $target) { throw "Resolved path changed: $relative" }
    $item = Get-Item -LiteralPath $target -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Refusing root reparse point: $relative" }
    if ($item.PSIsContainer) {
        $links = @(Get-ChildItem -LiteralPath $target -Recurse -Force -Attributes ReparsePoint -ErrorAction Stop)
        if ($links.Count -gt 0) { throw "Nested reparse points require separate review: $relative" }
    }
    $validated += [pscustomobject]@{path=$relative;absolute=$target;reason=$entry.reason;directory=$item.PSIsContainer}
}
foreach ($release in @('0.3.14','0.3.15')) {
    $report = Get-Content -LiteralPath (Join-Path $workspaceRoot ('Design/Release-' + $release + '/validation/apk-validation.json')) -Raw -Encoding UTF8 | ConvertFrom-Json
    $apk = @(Get-ChildItem -LiteralPath (Join-Path $workspaceRoot ('Builds/' + $release)) -Filter '*.apk' -File)
    if ($apk.Count -ne 1 -or $apk[0].Length -ne $report.bytes -or (Get-FileHash -LiteralPath $apk[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant() -cne $report.sha256) { throw "Retained APK did not validate: $release" }
}
if (-not $Apply) { $validated | ConvertTo-Json -Depth 4; return }
$active = @(Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'Unity.exe' -and $_.CommandLine -and $_.CommandLine.IndexOf($workspaceRoot,[StringComparison]::OrdinalIgnoreCase) -ge 0 })
if ($active.Count -gt 0) { throw 'A Unity process is still using this workspace; cache deletion refused' }
$beforeFree = (Get-PSDrive -Name C).Free
$deleted = [Collections.Generic.List[object]]::new()
foreach ($entry in $validated) {
    $target = $entry.absolute
    $bytes = [long]0
    if ($entry.directory) {
        foreach ($file in [IO.Directory]::EnumerateFiles('\\?\' + $target,'*',[IO.SearchOption]::AllDirectories)) { $bytes += ([IO.FileInfo]$file).Length }
    } else { $bytes = (Get-Item -LiteralPath $target -Force).Length }
    Remove-Item -LiteralPath ('\\?\' + $target) -Recurse -Force -ErrorAction Stop
    if (Test-Path -LiteralPath $target) { throw "Target remains after cleanup: $target" }
    $deleted.Add([pscustomobject]@{path=$entry.path;bytes=$bytes;reason=$entry.reason})
    Write-Output ('Deleted ' + $entry.path + ' (' + $bytes + ' bytes)')
}
$reportPath = Join-Path $PSScriptRoot $(if ($IncludeTemporary) {'local-temporary-cleanup-result.json'} else {'local-cleanup-result.json'})
[ordered]@{passed=$true;workspace=$workspaceRoot;deleted=$deleted;deletedBytes=($deleted | Measure-Object bytes -Sum).Sum;driveFreeBefore=$beforeFree;driveFreeAfter=(Get-PSDrive -Name C).Free;retainedReleases=@('0.3.15','0.3.14');gitHistoryRewritten=$false} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding utf8
