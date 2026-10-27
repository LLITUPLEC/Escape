[CmdletBinding()]
param(
    [string]$Destination,
    [string]$ReportPath,
    [string]$PlatformSdk = 'C:/Program Files/Unity/Hub/Editor/6000.3.9f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK',
    [string]$CommandLineSdk = 'C:/Program Files/Unity/Hub/Editor/6000.0.81f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK',
    [string]$LicenseDirectory = (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) 'AppData/Local/Android/Sdk/licenses')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd('\', '/')
if ([string]::IsNullOrWhiteSpace($Destination)) { $Destination = Join-Path $project '.cache/android-sdk' }
if ([string]::IsNullOrWhiteSpace($ReportPath)) { $ReportPath = Join-Path $project '.cache/android-sdk-validation.json' }
$Destination = [IO.Path]::GetFullPath($Destination).TrimEnd('\', '/')
$ReportPath = [IO.Path]::GetFullPath($ReportPath)
$workspaceCache = [IO.Path]::GetFullPath((Join-Path $project '.cache')).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
if (-not $Destination.StartsWith($workspaceCache, [StringComparison]::OrdinalIgnoreCase)) { throw 'SDK destination must be a child of this workspace .cache directory.' }
if (-not $ReportPath.StartsWith($project + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Report must stay inside this workspace.' }
$ownerName = 'Escape.Tools.Release.prepare-android-sdk'
$ownerPath = Join-Path $Destination '.escape-sdk-copy-owner.json'
$utf8 = New-Object Text.UTF8Encoding($false)

function Assert-NoLink([string]$Path) {
    if (Test-Path -LiteralPath $Path) {
        if (((Get-Item -LiteralPath $Path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw ('Refusing a symlink/junction/reparse point: ' + $Path)
        }
    }
}

function Assert-WorkspaceAncestors([string]$Path) {
    $cursor = $Path
    while ($cursor.Length -ge $project.Length -and $cursor.StartsWith($project, [StringComparison]::OrdinalIgnoreCase)) {
        Assert-NoLink $cursor
        $parent = [IO.Directory]::GetParent($cursor)
        if ($null -eq $parent) { break }
        $cursor = $parent.FullName
    }
}

function Hash-File([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose(); $stream.Dispose() }
}

function Hash-Text([string]$Value) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($utf8.GetBytes($Value)))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Read-Inventory([string]$Root) {
    if (-not [IO.Directory]::Exists($Root)) { throw ('Missing component directory: ' + $Root) }
    Assert-NoLink $Root
    $queue = New-Object 'Collections.Generic.Queue[string]'
    $entries = New-Object 'Collections.Generic.List[object]'
    $queue.Enqueue($Root)
    while ($queue.Count -gt 0) {
        $directory = $queue.Dequeue()
        foreach ($path in [IO.Directory]::EnumerateFileSystemEntries($directory)) {
            $attributes = [IO.File]::GetAttributes($path)
            if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw ('Refusing a reparse point: ' + $path) }
            if (($attributes -band [IO.FileAttributes]::Directory) -ne 0) { $queue.Enqueue($path); continue }
            $file = New-Object IO.FileInfo($path)
            $entries.Add([pscustomobject]@{ relative = $path.Substring($Root.Length + 1).Replace('\', '/'); source = $path; bytes = [long]$file.Length; sha256 = (Hash-File $path) })
        }
    }
    return @($entries | Sort-Object relative)
}

function Assert-Property([string]$Directory, [string]$Property, [string]$Expected) {
    $path = Join-Path $Directory 'source.properties'
    $matches = @(Get-Content -LiteralPath $path | Where-Object { $_ -match ('^' + [regex]::Escape($Property) + '\s*=') })
    if ($matches.Count -ne 1 -or (($matches[0] -split '=', 2)[1]).Trim() -cne $Expected) { throw ('Invalid ' + $Property + ' in ' + $path) }
}

Assert-WorkspaceAncestors $Destination
Assert-WorkspaceAncestors ([IO.Path]::GetDirectoryName($ReportPath))
Assert-NoLink $ReportPath
$commandRoot = Join-Path $CommandLineSdk 'cmdline-tools'
$commandSource = Join-Path $commandRoot 'latest'
if (-not (Test-Path -LiteralPath (Join-Path $commandSource 'bin/sdkmanager.bat'))) {
    $versions = @(Get-ChildItem -LiteralPath $commandRoot -Directory | Where-Object { $_.Name -match '^\d+(\.\d+)*$' } | Sort-Object { [version]$_.Name } -Descending)
    $commandSource = $null
    foreach ($version in $versions) {
        if (Test-Path -LiteralPath (Join-Path $version.FullName 'bin/sdkmanager.bat')) { $commandSource = $version.FullName; break }
    }
    if ([string]::IsNullOrEmpty($commandSource)) { throw 'No complete installed command-line tools were found.' }
}
$licenseCandidates = @((Join-Path $PlatformSdk 'licenses'), (Join-Path $CommandLineSdk 'licenses'), $LicenseDirectory)
$licenseSource = $null
foreach ($candidate in $licenseCandidates) {
    if (Test-Path -LiteralPath (Join-Path $candidate 'android-sdk-license')) { $licenseSource = $candidate; break }
}
if ([string]::IsNullOrEmpty($licenseSource)) { throw 'No existing accepted SDK licenses were found. This script does not accept or download licenses.' }
$components = @(
    [pscustomobject]@{ relative = 'cmdline-tools/latest'; source = $commandSource },
    [pscustomobject]@{ relative = 'build-tools/36.0.0'; source = (Join-Path $PlatformSdk 'build-tools/36.0.0') },
    [pscustomobject]@{ relative = 'platforms/android-36'; source = (Join-Path $PlatformSdk 'platforms/android-36') },
    [pscustomobject]@{ relative = 'platform-tools'; source = (Join-Path $PlatformSdk 'platform-tools') },
    [pscustomobject]@{ relative = 'licenses'; source = $licenseSource },
    [pscustomobject]@{ relative = 'cmake/3.22.1'; source = (Join-Path $PlatformSdk 'cmake/3.22.1') }
)
Assert-Property $components[1].source 'Pkg.Revision' '36.0.0'
Assert-Property $components[2].source 'AndroidVersion.ApiLevel' '36'
Assert-Property $components[5].source 'Pkg.Revision' '3.22.1'
foreach ($required in @((Join-Path $commandSource 'bin/sdkmanager.bat'), (Join-Path $commandSource 'lib/sdkmanager-classpath.jar'),
    (Join-Path $components[1].source 'aapt2.exe'), (Join-Path $components[2].source 'android.jar'), (Join-Path $components[3].source 'adb.exe'),
    (Join-Path $components[5].source 'bin/cmake.exe'))) {
    if (-not [IO.File]::Exists($required)) { throw ('Missing SDK component file: ' + $required) }
}

# An existing SDK must have our exact ownership marker. No files in another
# directory, or files with unexpected/different content, are ever overwritten.
if (Test-Path -LiteralPath $Destination) {
    if (-not (Test-Path -LiteralPath $ownerPath)) { throw ('Refusing an unowned destination: ' + $Destination) }
    $owner = Get-Content -LiteralPath $ownerPath -Raw | ConvertFrom-Json
    if ($owner.owner -cne $ownerName -or $owner.project -ine $project -or $owner.destination -ine $Destination) { throw 'SDK ownership marker does not match this workspace.' }
}
if (Test-Path -LiteralPath $ReportPath) {
    $oldReport = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json
    if ($oldReport.owner -cne $ownerName -or $oldReport.destination -ine $Destination) { throw 'Refusing to replace a report owned by another operation.' }
}

$planned = New-Object 'Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
$componentReports = New-Object 'Collections.Generic.List[object]'
$totalBytes = [long]0; $missingBytes = [long]0; $reused = 0; $copied = 0
foreach ($component in $components) {
    $component.source = [IO.Path]::GetFullPath($component.source).TrimEnd('\', '/')
    if ($component.source.StartsWith($Destination + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'A source cannot be inside the destination.' }
    $inventory = @(Read-Inventory $component.source)
    if ($inventory.Count -eq 0) { throw ('Empty SDK component: ' + $component.source) }
    $bytes = [long]0; $digest = New-Object Text.StringBuilder
    foreach ($entry in $inventory) {
        $relative = $component.relative + '/' + $entry.relative
        $planned.Add($relative, $entry)
        $bytes += $entry.bytes
        [void]$digest.Append($entry.relative).Append("`t").Append($entry.bytes).Append("`t").Append($entry.sha256).Append("`n")
        $target = Join-Path $Destination $relative
        if ([IO.File]::Exists($target)) {
            if ((Hash-File $target) -cne $entry.sha256) { throw ('Existing destination file differs; preserving it: ' + $target) }
            $reused++
        } else { $missingBytes += $entry.bytes }
    }
    $totalBytes += $bytes
    $componentReports.Add([pscustomobject]@{ component = $component.relative; source = $component.source; destination = (Join-Path $Destination $component.relative);
        file_count = $inventory.Count; bytes = $bytes; content_sha256 = (Hash-Text $digest.ToString()) })
}
$generatedMetadata = @()
if ([IO.Directory]::Exists($Destination)) {
    foreach ($existing in @(Read-Inventory $Destination)) {
        if ($existing.relative -ceq '.escape-sdk-copy-owner.json') { continue }
        # Android tooling creates this package-list cache after a successful
        # build. Preserve it; it is not one of the installed component files.
        if ($existing.relative -ceq '.knownPackages' -and $existing.bytes -le 1MB) {
            $generatedMetadata += @{ relative = $existing.relative; bytes = $existing.bytes; sha256 = $existing.sha256 }
            continue
        }
        if (-not $planned.ContainsKey($existing.relative) -and $existing.relative -cmatch '^(build-tools/36\.0\.0|platforms/android-36|platform-tools)/package\.xml$' -and $existing.bytes -le 1MB) {
            $expectedPackage = $Matches[1].Replace('/', ';')
            $settings = [Xml.XmlReaderSettings]::new()
            $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
            $settings.XmlResolver = $null
            $reader = [Xml.XmlReader]::Create($existing.source, $settings)
            try {
                $document = [Xml.XmlDocument]::new()
                $document.XmlResolver = $null
                $document.Load($reader)
                $packages = $document.SelectNodes("//*[local-name()='localPackage']")
                if ($packages.Count -ne 1 -or $packages[0].GetAttribute('path') -cne $expectedPackage) { throw ('Generated SDK package identity differs: ' + $existing.source) }
            } finally { $reader.Dispose() }
            $generatedMetadata += @{ relative = $existing.relative; bytes = $existing.bytes; sha256 = $existing.sha256; package = $expectedPackage }
            continue
        }
        if (-not $planned.ContainsKey($existing.relative)) { throw ('Unexpected SDK file; preserving it: ' + $existing.source) }
    }
}
$drive = New-Object IO.DriveInfo([IO.Path]::GetPathRoot($Destination))
$freeBefore = [long]$drive.AvailableFreeSpace
if ($freeBefore -lt ($missingBytes + 512MB)) { throw ('Not enough disk space: need missing bytes +512MiB; free=' + $freeBefore) }
[void][IO.Directory]::CreateDirectory($Destination)
if (-not [IO.File]::Exists($ownerPath)) {
    $owner = @{ owner = $ownerName; schema = 1; project = $project; destination = $Destination; created_utc = [DateTime]::UtcNow.ToString('o') }
    $ownerBytes = $utf8.GetBytes(($owner | ConvertTo-Json))
    $markerStream = [IO.File]::Open($ownerPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $markerStream.Write($ownerBytes, 0, $ownerBytes.Length) } finally { $markerStream.Dispose() }
}
foreach ($pair in $planned.GetEnumerator()) {
    $target = Join-Path $Destination $pair.Key
    if ([IO.File]::Exists($target)) { continue }
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
    [IO.File]::Copy($pair.Value.source, $target, $false)
    $copied++
}
# Validate every copied byte and assert only the intended integer API platform
# exists. Nothing calls sdkmanager, installs components, or mutates source SDKs.
foreach ($pair in $planned.GetEnumerator()) {
    $target = Join-Path $Destination $pair.Key
    if ((Hash-File $target) -cne $pair.Value.sha256) { throw ('SDK copy hash mismatch: ' + $target) }
}
Assert-Property (Join-Path $Destination 'platforms/android-36') 'AndroidVersion.ApiLevel' '36'
$platforms = @(Get-ChildItem -LiteralPath (Join-Path $Destination 'platforms') -Directory)
if ($platforms.Count -ne 1 -or $platforms[0].Name -cne 'android-36') { throw 'The project SDK must contain only platforms/android-36.' }
$report = [ordered]@{
    owner = $ownerName; checked_utc = [DateTime]::UtcNow.ToString('o'); passed = $true; project = $project; destination = $Destination;
    sources_read_only = $true; junctions_or_symlinks = $false; source_bytes_preserved = $true;
    sdkmanager = (Join-Path $Destination 'cmdline-tools/latest/bin/sdkmanager.bat'); sdkmanager_invoked = $false;
    installs_or_license_acceptance = $false; lint_disabled = $false; platform_api_level = 36; cmake_version = '3.22.1';
    copied_files = $copied; reused_files = $reused; file_count = $planned.Count; bytes = $totalBytes;
    disk_free_bytes_before = $freeBefore; disk_free_bytes_after = [long]$drive.AvailableFreeSpace;
    components = @($componentReports.ToArray()); generated_metadata_preserved = $generatedMetadata;
    note = 'Installed cmdline-tools16.0 copied completely under latest, plus installed CMake3.22.1; accepted license files reused from an existing SDK. No global SDK changes, SDK package installation, or Unity invocation.'
}
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($ReportPath))
[IO.File]::WriteAllText($ReportPath, ($report | ConvertTo-Json -Depth 8), $utf8)
[pscustomobject]@{ passed = $true; destination = $Destination; copied_files = $copied; reused_files = $reused; file_count = $planned.Count; bytes = $totalBytes; report = $ReportPath } | ConvertTo-Json -Compress
