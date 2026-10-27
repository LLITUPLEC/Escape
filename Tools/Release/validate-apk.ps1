[CmdletBinding()]
param([string]$StatusPath, [string]$ReportDirectory, [string]$ExpectedVersion = '0.3.15', [long]$MinimumVersionCode = 17)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ([string]::IsNullOrWhiteSpace($StatusPath)) { $StatusPath = Join-Path $project ('Builds/' + $ExpectedVersion + '/build-status.json') }
if ([string]::IsNullOrWhiteSpace($ReportDirectory)) { $ReportDirectory = Join-Path $project ('Design/Release-' + $ExpectedVersion + '/validation') }
$status = Get-Content -LiteralPath $StatusPath -Raw | ConvertFrom-Json
if ($status.state -cne 'succeeded' -or $status.result -cne 'Succeeded') { throw 'APK build has not succeeded' }
$developmentProperty = $status.PSObject.Properties['development']
if ($null -eq $developmentProperty -or $developmentProperty.Value -isnot [bool]) { throw 'Build status must contain an explicit boolean development flag' }
if ($status.development) { throw 'A development build cannot be validated as a release' }
$apk = Get-Item -LiteralPath $status.output
if ($apk.PSIsContainer -or $apk.Extension -ine '.apk' -or $apk.Length -le 0) { throw 'Expected a nonempty APK file' }
$sdk = 'C:\Program Files\Unity\Hub\Editor\6000.3.9f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK'
$java = 'C:\Program Files\Unity\Hub\Editor\6000.3.9f1\Editor\Data\PlaybackEngines\AndroidPlayer\OpenJDK\bin\java.exe'
$reportDir = $ReportDirectory
New-Item -ItemType Directory -Path $reportDir -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($apk.FullName)
try {
    $entries = @($zip.Entries | ForEach-Object { $_.FullName })
    $abis = @($entries | Where-Object { $_ -match '^lib/([^/]+)/libil2cpp\.so$' } | ForEach-Object { ($_ -split '/')[1] } | Sort-Object -Unique)
    if ($abis.Count -ne 2 -or 'arm64-v8a' -cnotin $abis -or 'armeabi-v7a' -cnotin $abis) { throw 'Expected exactly ARMv7 + ARM64 native libraries' }
    foreach ($abi in $abis) {
        if (('lib/' + $abi + '/libunity.so') -notin $entries) { throw ('Unity native runtime missing: ' + $abi) }
    }
    $unityData = @($entries | Where-Object { $_ -like 'assets/bin/Data/*' })
    if ($unityData.Count -eq 0) { throw 'Unity player data missing' }
} finally { $zip.Dispose() }
$badging = & (Join-Path $sdk 'build-tools/36.0.0/aapt.exe') dump badging $apk.FullName 2>&1
if ($LASTEXITCODE -ne 0) { throw 'aapt could not parse the APK manifest' }
$badgingText = $badging -join [Environment]::NewLine
$package = [regex]::Match($badgingText, '(?m)^package:.*$')
$versionName = [regex]::Match($package.Value, "(?:^|\s)versionName='([^']*)'").Groups[1].Value
$versionCodeText = [regex]::Match($package.Value, "(?:^|\s)versionCode='([0-9]+)'").Groups[1].Value
$versionCode = [long]0
if ($versionName -cne $ExpectedVersion -or -not [long]::TryParse($versionCodeText, [ref]$versionCode) -or $versionCode -lt $MinimumVersionCode) {
    throw 'APK versionName/versionCode do not match this release'
}
$xmltree = & (Join-Path $sdk 'build-tools/36.0.0/aapt.exe') dump xmltree $apk.FullName AndroidManifest.xml 2>&1
if ($LASTEXITCODE -ne 0) { throw 'aapt could not dump AndroidManifest.xml' }
$xmlText = $xmltree -join [Environment]::NewLine
if ($xmlText -notmatch '(?m)^\s*E:\s+application(?:\s|$)') { throw 'Manifest application element is missing' }
$debugAttributes = [regex]::Matches($xmlText, '(?m)^\s*A:\s+android:debuggable(?:\([^)]*\))?\s*=\s*(.+)$')
if ($debugAttributes.Count -gt 1) { throw 'Manifest contains ambiguous debuggable attributes' }
$debuggable = $false
if ($debugAttributes.Count -eq 1) {
    $value = $debugAttributes[0].Groups[1].Value.Trim()
    $encodedBool = [regex]::Match($value, '^\(type\s+0x12\)\s*0x([0-9a-fA-F]+)(?:\s|$)')
    if ($encodedBool.Success) { $debuggable = [Convert]::ToUInt64($encodedBool.Groups[1].Value, 16) -ne 0 }
    elseif ($value -notmatch '^(?:false|"false")(?:\s|$)') { throw 'Could not prove that android:debuggable is false' }
}
# An omitted application debuggable attribute defaults to false on Android.
if ($debuggable) { throw 'APK is debuggable; refusing release validation' }
$xmlText | Set-Content -LiteralPath (Join-Path $reportDir 'apk-manifest-xmltree.txt') -Encoding utf8
$badging | Set-Content -LiteralPath (Join-Path $reportDir 'apk-manifest.txt') -Encoding utf8
$signature = & $java -jar (Join-Path $sdk 'build-tools/36.0.0/lib/apksigner.jar') verify --verbose --print-certs $apk.FullName 2>&1
if ($LASTEXITCODE -ne 0) { throw 'APK signature verification failed' }
$signature | Set-Content -LiteralPath (Join-Path $reportDir 'apk-signature.txt') -Encoding utf8
$hash = (Get-FileHash -LiteralPath $apk.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
@{
    date = (Get-Date).ToString('yyyy-MM-ddTHH:mm:ssK')
    apk = $apk.Name
    version = $versionName
    version_code = $versionCode
    bytes = $apk.Length
    sha256 = $hash
    build_result = $status.result
    build_seconds = $status.seconds
    build_errors = $status.errors
    build_warnings = $status.warnings
    native_abis = $abis
    unity_data_entries = $unityData.Count
    manifest_parsed = $true
    debuggable = $debuggable
    debuggable_attribute_present = $debugAttributes.Count -eq 1
    signature_verified = $true
    development_build = $status.development
    physical_device_tested = $false
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $reportDir 'apk-validation.json') -Encoding utf8
# Retain build provenance without the machine-specific output path or Editor message.
@{ state = $status.state; result = $status.result; development = $status.development;
   output = $apk.Name; seconds = $status.seconds; errors = $status.errors; warnings = $status.warnings
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $reportDir 'android-build-status.json') -Encoding utf8
($hash + '  ' + $apk.Name) | Set-Content -LiteralPath ($apk.FullName + '.sha256') -Encoding ascii
Get-Content -LiteralPath (Join-Path $reportDir 'apk-validation.json')
$badging | Select-String -Pattern '^package:|^sdkVersion:|^targetSdkVersion:|^native-code:'
$signature | Select-Object -First 7
