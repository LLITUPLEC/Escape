[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ApkPath,
    [Parameter(Mandatory = $true)][string]$ScreensRoot,
    [string]$FinalValidationJson,
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')][string]$Version = '0.3.18',
    [ValidateRange(1, [int]::MaxValue)][int]$VersionCode = 20
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$releaseVersion = $Version
$releaseCode = $VersionCode
$utf8 = New-Object System.Text.UTF8Encoding($false)
$siteRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$distPath = Join-Path $siteRoot 'dist'
$stagePath = Join-Path $siteRoot ('.dist-stage-' + [guid]::NewGuid().ToString('N'))
$backupPath = Join-Path $siteRoot ('.dist-previous-' + [guid]::NewGuid().ToString('N'))
$projectRoot = [IO.Path]::GetFullPath((Join-Path $siteRoot '../..'))
$fontRoot = Join-Path $projectRoot 'Assets/_Project/Fonts/PTSerif'
$templatePath = Join-Path $siteRoot 'index.template.html'
$stageCreated = $false
$oldDistMoved = $false
$publishedLocally = $false

function Assert-OwnedOutputPath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $prefix = $siteRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetDirectoryName($full) -ne $siteRoot -or
        [IO.Path]::GetFileName($full) -notmatch '^(dist|\.dist-stage-[a-f0-9]{32}|\.dist-previous-[a-f0-9]{32})$') {
        throw "Output operation is outside this website's managed directories."
    }
    if (Test-Path -LiteralPath $full) {
        $entry = Get-Item -LiteralPath $full -Force
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Output directories must not be symbolic links or junctions.'
        }
    }
}

function Get-RequiredFile([string]$Path, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw ($Description + ' is missing: ' + $Path) }
    return (Get-Item -LiteralPath $Path -Force)
}

function Assert-Png([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $bytes = New-Object byte[] 24
        if ($stream.Read($bytes, 0, 24) -ne 24) { throw 'Screenshot is not a complete PNG.' }
        $magic = @(137, 80, 78, 71, 13, 10, 26, 10)
        for ($i = 0; $i -lt 8; $i++) {
            if ($bytes[$i] -ne $magic[$i]) { throw 'Screenshot must be a PNG file.' }
        }
        $width = [uint32]$bytes[16] * 16777216 + [uint32]$bytes[17] * 65536 + [uint32]$bytes[18] * 256 + [uint32]$bytes[19]
        $height = [uint32]$bytes[20] * 16777216 + [uint32]$bytes[21] * 65536 + [uint32]$bytes[22] * 256 + [uint32]$bytes[23]
        if ($width -lt 240 -or $height -lt 400) { throw 'Screenshot dimensions are too small for the release page.' }
    }
    finally { $stream.Dispose() }
}

function Get-Field($Object, [string]$Name) {
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { throw ('Final validation is missing required field: ' + $Name) }
    return $property.Value
}

# Validate every input before creating or replacing a dist directory.
$apk = Get-RequiredFile $ApkPath 'APK'
if ($apk.Extension -ine '.apk' -or $apk.Length -le 0) { throw 'ApkPath must point to a nonempty .apk file.' }
$screens = (Get-Item -LiteralPath $ScreensRoot -Force)
if (-not $screens.PSIsContainer) { throw 'ScreensRoot must be a directory.' }
$screenPaths = @{}
foreach ($name in @('hub', 'character', 'mine')) {
    $screen = Get-RequiredFile (Join-Path $screens.FullName ($name + '.png')) ($name + ' screenshot')
    Assert-Png $screen.FullName
    $screenPaths[$name] = $screen.FullName
}
$font = Get-RequiredFile (Join-Path $fontRoot 'PT_Serif-Web-Regular.ttf') 'PT Serif font'
$license = Get-RequiredFile (Join-Path $fontRoot 'OFL.txt') 'PT Serif license'
$template = Get-RequiredFile $templatePath 'HTML template'

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($apk.FullName)
try {
    if ($null -eq $zip.GetEntry('AndroidManifest.xml')) { throw 'APK archive is missing AndroidManifest.xml.' }
}
finally { $zip.Dispose() }

$sha256 = (Get-FileHash -LiteralPath $apk.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$apkBytes = [long]$apk.Length
$apkFilename = 'Escape-' + $releaseVersion + '-' + $sha256.Substring(0, 12) + '.apk'
if ($apkFilename -cnotmatch ('^Escape-' + [regex]::Escape($releaseVersion) + '-[a-f0-9]{12}\.apk$')) { throw 'Invalid generated APK filename.' }
$apkUrl = './downloads/' + $apkFilename
$apkMib = ([double]$apkBytes / 1MB).ToString('0.0', [Globalization.CultureInfo]::InvariantCulture)
$releaseDate = [DateTimeOffset]::Now.ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
$signatureVerified = $null
$architecture = @()

if (-not [string]::IsNullOrWhiteSpace($FinalValidationJson)) {
    $reportFile = Get-RequiredFile $FinalValidationJson 'Final validation JSON'
    $report = [IO.File]::ReadAllText($reportFile.FullName) | ConvertFrom-Json -ErrorAction Stop
    if ($null -eq $report -or $report -is [array]) { throw 'Final validation JSON must contain an object.' }
    if ((Get-Field $report 'build_result') -cne 'Succeeded') { throw 'APK build did not succeed.' }
    if ((Get-Field $report 'version') -cne $releaseVersion) { throw 'APK version does not match this release.' }
    $reportedCode = Get-Field $report 'version_code'
    if (($reportedCode -isnot [int] -and $reportedCode -isnot [long]) -or [long]$reportedCode -ne $releaseCode) { throw 'APK version code does not match this release.' }
    $signature = Get-Field $report 'signature_verified'
    if ($signature -isnot [bool] -or -not $signature) { throw 'APK signature has not been verified.' }
    $development = Get-Field $report 'development_build'
    if ($development -isnot [bool] -or $development) { throw 'Development APKs must not be packaged as release downloads.' }
    $debuggable = Get-Field $report 'debuggable'
    if ($debuggable -isnot [bool] -or $debuggable) { throw 'Debuggable APKs must not be packaged as release downloads.' }
    $reportedHash = [string](Get-Field $report 'sha256')
    if ($reportedHash -notmatch '^[a-fA-F0-9]{64}$' -or $reportedHash.ToLowerInvariant() -cne $sha256) {
        throw 'Final validation SHA-256 does not match this APK.'
    }
    $reportedBytes = Get-Field $report 'bytes'
    if (($reportedBytes -isnot [int] -and $reportedBytes -isnot [long]) -or [long]$reportedBytes -ne $apkBytes) {
        throw 'Final validation byte count must be an exact integer matching this APK.'
    }
    $reportedAbis = @(Get-Field $report 'native_abis')
    foreach ($abi in $reportedAbis) {
        if ($abi -isnot [string] -or $abi -cnotin @('arm64-v8a', 'armeabi-v7a')) { throw 'Final validation contains an unexpected Android architecture.' }
    }
    if ($reportedAbis.Count -ne 2 -or 'arm64-v8a' -cnotin $reportedAbis -or 'armeabi-v7a' -cnotin $reportedAbis) {
        throw 'Expected both arm64-v8a and armeabi-v7a in the validated release.'
    }
    $signatureVerified = $true
    $architecture = @('arm64-v8a', 'armeabi-v7a')
    # Only these explicitly selected values may enter the public release metadata.
    # The report may contain private paths and Editor data: never copy or serialize it.
}

$html = [IO.File]::ReadAllText($template.FullName)
$values = [ordered]@{
    '{{RELEASE_VERSION}}' = $releaseVersion
    '{{APK_FILENAME}}' = $apkFilename
    '{{APK_MIB}}' = $apkMib
    '{{APK_SHA256}}' = $sha256
    '{{RELEASE_DATE}}' = $releaseDate
}
foreach ($token in $values.Keys) {
    if (-not $html.Contains($token)) { throw ('HTML template is missing token: ' + $token) }
    $html = $html.Replace($token, $values[$token])
}
if ($html -match '\{\{[^}]+\}\}') { throw 'HTML still contains unresolved template tokens.' }
if (-not $html.Contains($apkUrl) -or -not $html.Contains($apkFilename)) { throw 'Rendered HTML is missing the APK download.' }

$release = [ordered]@{
    version = $releaseVersion
    date = $releaseDate
    filename = $apkFilename
    bytes = $apkBytes
    sha256 = $sha256
    url = $apkUrl
}
if ($null -ne $signatureVerified) {
    $release['signature_verified'] = $signatureVerified
    $release['architecture'] = $architecture
}
$releaseJson = $release | ConvertTo-Json -Depth 4
$mark = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64"><rect width="64" height="64" rx="12" fill="#102a30"/><path d="m34 8-18 27h14l-6 21 26-33H34l7-15Z" fill="#edaa53"/></svg>'

try {
    Assert-OwnedOutputPath $stagePath
    Assert-OwnedOutputPath $distPath
    Assert-OwnedOutputPath $backupPath
    New-Item -ItemType Directory -Path $stagePath | Out-Null
    $stageCreated = $true
    $assetsPath = Join-Path $stagePath 'assets'
    $downloadsPath = Join-Path $stagePath 'downloads'
    New-Item -ItemType Directory -Path $assetsPath, $downloadsPath | Out-Null
    foreach ($name in @('hub', 'character', 'mine')) {
        Copy-Item -LiteralPath $screenPaths[$name] -Destination (Join-Path $assetsPath ($name + '.png'))
    }
    Copy-Item -LiteralPath $font.FullName -Destination (Join-Path $assetsPath 'PTSerif-Regular.ttf')
    Copy-Item -LiteralPath $license.FullName -Destination (Join-Path $assetsPath 'OFL.txt')
    [IO.File]::WriteAllText((Join-Path $assetsPath 'mark.svg'), $mark, $utf8)
    $stagedApk = Join-Path $downloadsPath $apkFilename
    Copy-Item -LiteralPath $apk.FullName -Destination $stagedApk
    if ((Get-Item -LiteralPath $stagedApk).Length -ne $apkBytes -or
        (Get-FileHash -LiteralPath $stagedApk -Algorithm SHA256).Hash.ToLowerInvariant() -cne $sha256) {
        throw 'Staged APK differs from its source.'
    }
    [IO.File]::WriteAllText((Join-Path $stagePath 'index.html'), $html, $utf8)
    [IO.File]::WriteAllText((Join-Path $stagePath 'release.json'), $releaseJson + [Environment]::NewLine, $utf8)
    # Retain only the user's nineteen selected clips and their public choice history.
    $audioSource = Join-Path $projectRoot 'Design/Audio'
    $audioTarget = Join-Path $stagePath 'audio-035'
    New-Item -ItemType Directory -Path $audioTarget | Out-Null
    Copy-Item -LiteralPath (Join-Path $audioSource 'index.html') -Destination (Join-Path $audioTarget 'index.html')
    $selectedFile = Get-RequiredFile (Join-Path $audioSource 'selected-metadata.json') 'Selected audio metadata'
    $choicesFile = Get-RequiredFile (Join-Path $audioSource 'Escape-audio-choices-0.3.5.txt') 'Audio choice history'
    $selectedJson = [IO.File]::ReadAllText($selectedFile.FullName)
    if ($selectedJson -match '(?i)([a-z]:[\\/]|file://|https?://|"[/\\])') { throw 'Selected audio metadata contains a non-public absolute path or URL.' }
    $selected = $selectedJson | ConvertFrom-Json -ErrorAction Stop
    if ((Get-Field $selected 'audition_version') -cne '0.3.5' -or (Get-Field $selected 'selected_count') -ne 19 -or @($selected.selected).Count -ne 19) { throw 'Expected nineteen selected audio effects.' }
    $selectionSha = Get-Field $selected 'selection_sha256'
    if ($selectionSha -cnotmatch '^[a-f0-9]{64}$' -or (Get-FileHash -LiteralPath $choicesFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -cne $selectionSha) { throw 'Audio choice history hash does not match selected metadata.' }
    Copy-Item -LiteralPath $selectedFile.FullName -Destination (Join-Path $audioTarget 'selected-metadata.json')
    Copy-Item -LiteralPath $choicesFile.FullName -Destination (Join-Path $audioTarget 'Escape-audio-choices-0.3.5.txt')
    New-Item -ItemType Directory -Path (Join-Path $audioTarget 'selected') | Out-Null
    $audioManifest = [System.Collections.Generic.List[object]]::new()
    $audioPaths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($effect in $selected.selected) {
        $audioRel = [string](Get-Field $effect 'file')
        $id = [string](Get-Field $effect 'id')
        $choice = [string](Get-Field $effect 'choice')
        $expectedSha = [string](Get-Field $effect 'sha256')
        if ($id -cnotmatch '^[a-z][a-z0-9_]*$' -or $audioRel -cne ('selected/' + $id + '.wav') -or $choice -cnotin @('A', 'B') -or -not $audioPaths.Add($audioRel) -or $expectedSha -cnotmatch '^[a-f0-9]{64}$') { throw 'Unexpected or duplicate selected audio path.' }
        $audioFile = Get-RequiredFile (Join-Path $audioSource $audioRel) 'Selected WAV'
        if (($audioFile.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $audioFile.Length -le 44 -or $audioFile.Length -ge 10000000) { throw 'Unexpected selected media file.' }
        if ((Get-FileHash -LiteralPath $audioFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -cne $expectedSha) { throw 'Selected WAV hash does not match user choice metadata.' }
        Copy-Item -LiteralPath $audioFile.FullName -Destination (Join-Path $audioTarget $audioRel)
        $audioManifest.Add([ordered]@{ file = $audioRel; bytes = $audioFile.Length; sha256 = $expectedSha })
    }
    if ($audioManifest.Count -ne 19) { throw 'Expected nineteen public selected WAV files.' }
    [IO.File]::WriteAllText((Join-Path $audioTarget 'files.json'), (ConvertTo-Json -InputObject @($audioManifest.ToArray()) -Depth 4), $utf8)
    # Verify the resolved absolute targets immediately before recursive moves/removal.
    Assert-OwnedOutputPath $distPath
    Assert-OwnedOutputPath $stagePath
    Assert-OwnedOutputPath $backupPath
    if (Test-Path -LiteralPath $distPath) {
        Move-Item -LiteralPath $distPath -Destination $backupPath
        $oldDistMoved = $true
    }
    Move-Item -LiteralPath $stagePath -Destination $distPath
    $stageCreated = $false
    $publishedLocally = $true
    if ($oldDistMoved) {
        Assert-OwnedOutputPath $backupPath
        Remove-Item -LiteralPath $backupPath -Recurse -Force
        $oldDistMoved = $false
    }
    Write-Output ('Local dist ready: ' + $distPath)
    Write-Output ('APK: ' + $apkFilename)
    Write-Output ('Bytes: ' + $apkBytes + '; SHA-256: ' + $sha256)
}
catch {
    if ($oldDistMoved -and -not $publishedLocally -and -not (Test-Path -LiteralPath $distPath)) {
        Assert-OwnedOutputPath $backupPath
        Assert-OwnedOutputPath $distPath
        Move-Item -LiteralPath $backupPath -Destination $distPath
        $oldDistMoved = $false
    }
    throw
}
finally {
    if ($stageCreated -and (Test-Path -LiteralPath $stagePath)) {
        Assert-OwnedOutputPath $stagePath
        Remove-Item -LiteralPath $stagePath -Recurse -Force
    }
}
