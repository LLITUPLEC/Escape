param([switch]$Prune, [string]$PythonPath = 'python')
$ErrorActionPreference = 'Stop'
$workspacePath = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$archiveRoot = (Resolve-Path (Join-Path $workspacePath 'Design/Audio')).Path
$expectedRoot = [IO.Path]::GetFullPath((Join-Path $workspacePath 'Design/Audio'))
if ($archiveRoot -ne $expectedRoot) { throw 'Archive root resolves outside the explicitly named Design directory' }
$metadata = [IO.File]::ReadAllText((Join-Path $archiveRoot 'selected-metadata.json')) | ConvertFrom-Json
if ($metadata.selected_count -ne 19 -or $metadata.selected.Count -ne 19) { throw 'Expected nineteen selected copies' }
$prefix = $archiveRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
foreach ($effect in $metadata.selected) {
    $copyPath = (Resolve-Path (Join-Path $archiveRoot $effect.file)).Path
    if (-not $copyPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or $effect.file -notmatch '^selected/[a-z_]+\.wav$') { throw 'Selected copy is outside archive root' }
    if ((Get-FileHash -LiteralPath $copyPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $effect.sha256) { throw ('Selected copy hash mismatch: ' + $effect.id) }
}
& $PythonPath (Join-Path $PSScriptRoot 'build_selected_archive.py')
if ($LASTEXITCODE -ne 0) { throw 'Selected archive build failed' }
if ($Prune) {
    # Every recursive target is an explicit child name; resolved paths are checked
    # before removal, using PowerShell end to end without a second shell.
    foreach ($directory in @('A','B','pairs','montages','showcases')) {
        $candidate = Join-Path $archiveRoot $directory
        if (-not (Test-Path -LiteralPath $candidate)) { continue }
        $item = Get-Item -LiteralPath $candidate -Force
        $resolved = (Resolve-Path -LiteralPath $candidate).Path
        if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or -not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetDirectoryName($resolved) -ne $archiveRoot) { throw ('Unsafe cleanup target: ' + $directory) }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    foreach ($filename in @('metadata.json','inventory.json','quality-checks.json','page-dom-fixture.json','page-validation.json','validation-inline.cjs')) {
        $candidate = Join-Path $archiveRoot $filename
        if (-not (Test-Path -LiteralPath $candidate)) { continue }
        $resolved = (Resolve-Path -LiteralPath $candidate).Path
        if ([IO.Path]::GetDirectoryName($resolved) -ne $archiveRoot) { throw 'Unsafe obsolete manifest path' }
        Remove-Item -LiteralPath $resolved -Force
    }
}
& $PythonPath (Join-Path $PSScriptRoot 'validate_selected_archive.py')
if ($LASTEXITCODE -ne 0) { throw 'Selected archive validation failed' }
Write-Output ('Selected archive ready. Pruned=' + $Prune)
