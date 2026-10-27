[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$ExpectedVersion = '0.3.15',
    [string]$ManifestPath,
    [string]$ApkDirectory,
    [string]$ReportPath,
    [uri]$BaseUrl = 'https://prokrust-play.ru/escape/',
    [uri]$HealthUrl = 'https://prokrust-play.ru/healthcheck',
    [Parameter(Mandatory = $true)][uri]$PreviousApkUrl,
    [ValidateRange(0, [long]::MaxValue)][long]$PreviousApkBytes = 0,
    [ValidateRange(1, 300)][int]$TimeoutSeconds = 30,
    [switch]$PrepareOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $projectRoot 'Website/escape/dist/release.json'
}
if ([string]::IsNullOrWhiteSpace($ApkDirectory)) {
    $ApkDirectory = Join-Path $projectRoot 'Website/escape/dist/downloads'
}
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $projectRoot ('Design/Release-' + $ExpectedVersion + '/validation/public-release.json')
}
$ReportPath = [IO.Path]::GetFullPath($ReportPath)

function Assert-HttpsUrl([uri]$Url, [string]$Name) {
    if (-not $Url.IsAbsoluteUri -or $Url.Scheme -cne 'https' -or
        -not [string]::IsNullOrEmpty($Url.UserInfo) -or
        -not [string]::IsNullOrEmpty($Url.Query) -or
        -not [string]::IsNullOrEmpty($Url.Fragment)) {
        throw ($Name + ' must be an absolute HTTPS URL without credentials, query or fragment.')
    }
}

Assert-HttpsUrl $BaseUrl 'BaseUrl'
Assert-HttpsUrl $HealthUrl 'HealthUrl'
Assert-HttpsUrl $PreviousApkUrl 'PreviousApkUrl'
$BaseUrl = [uri]($BaseUrl.AbsoluteUri.TrimEnd('/') + '/')
$downloadPrefix = ([uri]::new($BaseUrl, 'downloads/')).AbsolutePath
if ($PreviousApkUrl.Authority -cne $BaseUrl.Authority -or
    -not $PreviousApkUrl.AbsolutePath.StartsWith($downloadPrefix, [StringComparison]::Ordinal) -or
    -not $PreviousApkUrl.AbsolutePath.EndsWith('.apk', [StringComparison]::Ordinal)) {
    throw 'PreviousApkUrl must point to an APK in this website downloads directory.'
}

$report = [ordered]@{
    state = 'not_run'
    success = $null
    prepared_at_utc = [DateTimeOffset]::UtcNow.ToString('o')
    checked_at_utc = $null
    version = $ExpectedVersion
    url = $BaseUrl.AbsoluteUri
    manifest_url = ([uri]::new($BaseUrl, 'release.json')).AbsoluteUri
    apk_url = $null
    apk_bytes = $null
    apk_sha256 = $null
    previous_apk_url = $PreviousApkUrl.AbsoluteUri
    expected_previous_apk_bytes = $(if ($PreviousApkBytes -gt 0) { $PreviousApkBytes } else { $null })
    network_checks_performed = $false
    physical_device_tested = $false
    page_status = $null
    manifest_status = $null
    apk_head_status = $null
    apk_mime = $null
    apk_range_status = $null
    apk_zip_header_verified = $null
    previous_apk_head_status = $null
    previous_apk_bytes = $null
    api_health_status = $null
    checks = [ordered]@{
        local_manifest_and_apk = 'not_run'
        public_manifest = 'not_run'
        page_and_download_links = 'not_run'
        page_and_manifest_no_cache = 'not_run'
        apk_head_size_mime_cache = 'not_run'
        apk_range_and_zip_header = 'not_run'
        previous_apk_head = 'not_run'
        api_health = 'not_run'
    }
    sha256_check_scope = 'Local dist APK and public manifest only; the full public APK is not downloaded by this script.'
    error = $null
}

function Save-Report {
    $directory = [IO.Path]::GetDirectoryName($ReportPath)
    if (-not [IO.Directory]::Exists($directory)) { [IO.Directory]::CreateDirectory($directory) | Out-Null }
    $json = $report | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText($ReportPath, $json + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
}

# This mode prepares an explicitly unverified report and never creates an HTTP client.
if ($PrepareOnly) {
    Save-Report
    $report | ConvertTo-Json -Depth 8
    return
}

function Get-RequiredField($Object, [string]$Name) {
    if ($null -eq $Object -or $Object -is [array]) { throw 'Release manifest must contain one JSON object.' }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { throw ('Release manifest is missing field: ' + $Name) }
    return $property.Value
}

function Get-HeaderText($Response, [string]$Name) {
    # HttpClient keeps content headers separate and rejects querying them through response.Headers.
    if ($Name.StartsWith('Content-', [StringComparison]::OrdinalIgnoreCase)) {
        if ($null -ne $Response.Content -and $Response.Content.Headers.Contains($Name)) {
            return [string]::Join(',', $Response.Content.Headers.GetValues($Name))
        }
        return ''
    }
    if ($Response.Headers.Contains($Name)) { return [string]::Join(',', $Response.Headers.GetValues($Name)) }
    return ''
}

function Assert-Status($Response, [int]$Expected, [string]$Name) {
    if ([int]$Response.StatusCode -ne $Expected) { throw ($Name + ' returned HTTP ' + [int]$Response.StatusCode + ', expected ' + $Expected + '.') }
}

function Assert-NoCache($Response, [string]$Name) {
    if ((Get-HeaderText $Response 'Cache-Control') -notmatch '(?i)(?:^|,)\s*no-cache\s*(?:,|$)') {
        throw ($Name + ' must include Cache-Control: no-cache.')
    }
}

function Assert-Mime($Response, [string]$Expected, [string]$Name) {
    $mime = ((Get-HeaderText $Response 'Content-Type') -split ';', 2)[0].Trim().ToLowerInvariant()
    if ($mime -cne $Expected) { throw ($Name + ' MIME type is incorrect.') }
}

function Get-ContentBytes($Response, [string]$Name) {
    $lengthText = Get-HeaderText $Response 'Content-Length'
    $length = [long]0
    if ($lengthText -notmatch '^\d+$' -or -not [long]::TryParse($lengthText, [ref]$length) -or $length -le 0) {
        throw ($Name + ' must have one positive integer Content-Length.')
    }
    return $length
}

$client = $null
$handler = $null
$responses = New-Object 'System.Collections.Generic.List[System.IDisposable]'
try {
    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) { throw 'Local release manifest is missing.' }
    $expected = [IO.File]::ReadAllText([IO.Path]::GetFullPath($ManifestPath)) | ConvertFrom-Json -ErrorAction Stop
    $version = Get-RequiredField $expected 'version'
    $filename = Get-RequiredField $expected 'filename'
    $sha256 = Get-RequiredField $expected 'sha256'
    $relativeUrl = Get-RequiredField $expected 'url'
    $bytesValue = Get-RequiredField $expected 'bytes'
    if ($bytesValue -isnot [int] -and $bytesValue -isnot [long]) { throw 'Local manifest bytes must be a JSON integer.' }
    $bytesText = [string]$bytesValue
    $bytes = [long]0
    if ($version -cne $ExpectedVersion) { throw 'Local manifest version does not match ExpectedVersion.' }
    $filenamePattern = '^Escape-' + [regex]::Escape($ExpectedVersion) + '-[a-f0-9]{12}\.apk$'
    if ($filename -isnot [string] -or $filename -cnotmatch $filenamePattern) { throw 'Local APK filename is invalid for this version.' }
    if ($sha256 -isnot [string] -or $sha256 -cnotmatch '^[a-f0-9]{64}$' -or
        $filename -cne ('Escape-' + $ExpectedVersion + '-' + $sha256.Substring(0, 12) + '.apk')) {
        throw 'Local APK filename does not match its SHA-256 prefix.'
    }
    if ($relativeUrl -cne ('./downloads/' + $filename)) { throw 'Local manifest download URL is invalid.' }
    if ($bytesText -notmatch '^\d+$' -or -not [long]::TryParse($bytesText, [ref]$bytes) -or $bytes -le 0) {
        throw 'Local manifest bytes must be a positive integer.'
    }
    $releaseDate = [string](Get-RequiredField $expected 'date')
    $parsedDate = [datetime]::MinValue
    if (-not [datetime]::TryParseExact($releaseDate, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::None, [ref]$parsedDate)) { throw 'Local manifest date must use YYYY-MM-DD.' }
    $localApk = Join-Path ([IO.Path]::GetFullPath($ApkDirectory)) $filename
    if (-not (Test-Path -LiteralPath $localApk -PathType Leaf)) { throw 'Local dist APK is missing.' }
    if ((Get-Item -LiteralPath $localApk).Length -ne $bytes -or
        (Get-FileHash -LiteralPath $localApk -Algorithm SHA256).Hash.ToLowerInvariant() -cne $sha256) {
        throw 'Local dist APK size or SHA-256 does not match the manifest.'
    }
    $report.apk_url = ([uri]::new($BaseUrl, 'downloads/' + $filename)).AbsoluteUri
    if ($report.apk_url -ceq $PreviousApkUrl.AbsoluteUri) { throw 'Current and previous APK URLs must differ.' }
    $report.apk_bytes = $bytes
    $report.apk_sha256 = $sha256
    $report.checks.local_manifest_and_apk = 'passed'

    Add-Type -AssemblyName System.Net.Http
    $handler = New-Object Net.Http.HttpClientHandler
    $handler.AllowAutoRedirect = $false
    $client = New-Object Net.Http.HttpClient($handler)
    $client.Timeout = [TimeSpan]::FromSeconds($TimeoutSeconds)
    $client.DefaultRequestHeaders.UserAgent.ParseAdd('Escape-ReleaseVerifier/1.0')
    $client.DefaultRequestHeaders.AcceptEncoding.ParseAdd('identity')
    function Send-Request([string]$Method, [uri]$Url, [string]$Range) {
        $request = New-Object Net.Http.HttpRequestMessage([Net.Http.HttpMethod]::new($Method), $Url)
        try {
            if (-not [string]::IsNullOrEmpty($Range)) { $request.Headers.Range = [Net.Http.Headers.RangeHeaderValue]::Parse($Range) }
            $response = $client.SendAsync($request, [Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
            $responses.Add($response)
            return $response
        }
        finally { $request.Dispose() }
    }

    $report.state = 'running'
    $report.network_checks_performed = $true
    $manifestResponse = Send-Request 'GET' ([uri]$report.manifest_url) ''
    $report.manifest_status = [int]$manifestResponse.StatusCode
    Assert-Status $manifestResponse 200 'Public manifest'
    Assert-Mime $manifestResponse 'application/json' 'Public manifest'
    Assert-NoCache $manifestResponse 'Public manifest'
    $actual = $manifestResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json -ErrorAction Stop
    foreach ($field in @('version', 'date', 'filename', 'bytes', 'sha256', 'url')) {
        if ((Get-RequiredField $actual $field) -cne (Get-RequiredField $expected $field)) {
            throw ('Public manifest mismatch: ' + $field)
        }
    }
    $actualBytes = Get-RequiredField $actual 'bytes'
    if ($actualBytes -isnot [int] -and $actualBytes -isnot [long]) { throw 'Public manifest bytes must be a JSON integer.' }
    foreach ($field in @('signature_verified', 'architecture')) {
        if ($null -ne $expected.PSObject.Properties[$field]) {
            $actualValue = Get-RequiredField $actual $field
            $expectedValue = Get-RequiredField $expected $field
            if ($field -ceq 'architecture') {
                if (($actualValue -join ',') -cne ($expectedValue -join ',')) { throw 'Public manifest mismatch: architecture' }
            }
            elseif ($actualValue -isnot [bool] -or $actualValue -cne $expectedValue) { throw 'Public manifest mismatch: signature_verified' }
        }
    }
    $report.checks.public_manifest = 'passed'

    $pageResponse = Send-Request 'GET' $BaseUrl ''
    $report.page_status = [int]$pageResponse.StatusCode
    Assert-Status $pageResponse 200 'Public page'
    Assert-Mime $pageResponse 'text/html' 'Public page'
    Assert-NoCache $pageResponse 'Public page'
    $page = $pageResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    if ($page -notmatch 'Что нового' -or $page -notmatch [regex]::Escape($ExpectedVersion) -or $page -match '\{\{[^}]+\}\}') {
        throw 'Public page release content is incorrect or contains unexpanded template tokens.'
    }
    $apkLinks = [regex]::Matches($page, '(?is)<a\b[^>]*\bhref\s*=\s*["''](?<url>[^"'']+\.apk)["''][^>]*>')
    if ($apkLinks.Count -lt 2) { throw 'Public page must contain both APK download links.' }
    foreach ($link in $apkLinks) {
        if ($link.Groups['url'].Value -cne $relativeUrl) { throw 'Public page contains an APK link that does not match the current manifest.' }
    }
    $report.checks.page_and_download_links = 'passed'
    $report.checks.page_and_manifest_no_cache = 'passed'

    $apkHead = Send-Request 'HEAD' ([uri]$report.apk_url) ''
    $report.apk_head_status = [int]$apkHead.StatusCode
    Assert-Status $apkHead 200 'Public APK HEAD'
    Assert-Mime $apkHead 'application/vnd.android.package-archive' 'Public APK'
    if ((Get-ContentBytes $apkHead 'Public APK') -ne $bytes) { throw 'Public APK byte count mismatch.' }
    if ((Get-HeaderText $apkHead 'Cache-Control') -notmatch '(?i)(?:^|,)\s*immutable\s*(?:,|$)') { throw 'Public APK must have immutable cache policy.' }
    $report.apk_mime = Get-HeaderText $apkHead 'Content-Type'
    $report.checks.apk_head_size_mime_cache = 'passed'

    $rangeResponse = Send-Request 'GET' ([uri]$report.apk_url) 'bytes=0-3'
    $report.apk_range_status = [int]$rangeResponse.StatusCode
    Assert-Status $rangeResponse 206 'Public APK range'
    Assert-Mime $rangeResponse 'application/vnd.android.package-archive' 'Public APK range'
    if ((Get-HeaderText $rangeResponse 'Content-Range') -cne ('bytes 0-3/' + $bytes) -or
        (Get-ContentBytes $rangeResponse 'Public APK range') -ne 4) { throw 'Public APK Content-Range or range size is incorrect.' }
    $stream = $rangeResponse.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
    $firstBytes = New-Object byte[] 4
    $read = 0
    while ($read -lt 4) {
        $count = $stream.Read($firstBytes, $read, 4 - $read)
        if ($count -eq 0) { throw 'Public APK range body is incomplete.' }
        $read += $count
    }
    if (($firstBytes -join ',') -cne '80,75,3,4') { throw 'Public APK range did not return the ZIP header.' }
    $report.apk_zip_header_verified = $true
    $report.checks.apk_range_and_zip_header = 'passed'

    $previousHead = Send-Request 'HEAD' $PreviousApkUrl ''
    $report.previous_apk_head_status = [int]$previousHead.StatusCode
    Assert-Status $previousHead 200 'Previous APK HEAD'
    Assert-Mime $previousHead 'application/vnd.android.package-archive' 'Previous APK'
    $report.previous_apk_bytes = Get-ContentBytes $previousHead 'Previous APK'
    if ($PreviousApkBytes -gt 0 -and $report.previous_apk_bytes -ne $PreviousApkBytes) { throw 'Previous APK byte count changed.' }
    $report.checks.previous_apk_head = 'passed'

    $healthResponse = Send-Request 'GET' $HealthUrl ''
    $report.api_health_status = [int]$healthResponse.StatusCode
    Assert-Status $healthResponse 200 'Existing API health check'
    $report.checks.api_health = 'passed'
    $report.state = 'passed'
    $report.success = $true
}
catch {
    $report.state = 'failed'
    $report.success = $false
    # Do not serialize request objects, local paths, response bodies or credentials.
    $message = $_.Exception.Message
    if ($message -match '(?i)(?<![a-z])[a-z]:[\\/]|\\\\') { $message = 'Local input or HTTP verification failed; see the invoking console for details.' }
    $report.error = $message
    throw
}
finally {
    if ($report.network_checks_performed) { $report.checked_at_utc = [DateTimeOffset]::UtcNow.ToString('o') }
    Save-Report
    foreach ($response in $responses) { $response.Dispose() }
    if ($null -ne $client) { $client.Dispose() }
    if ($null -ne $handler) { $handler.Dispose() }
}

$report | ConvertTo-Json -Depth 8
