# Удаление лидербордов через Nakama Console API (обходит баг dashboard с "%" в ID).
# Запуск из PowerShell:
#   cd Server\nakama\tools
#   .\purge_leaderboards.ps1 -ConsolePassword "your_password"
#   .\purge_leaderboards.ps1 -BrokenOnly          # только битые *_w_%GW%V
#   .\purge_leaderboards.ps1 -AllLb              # все lb_* (полная очистка тестовых)

param(
    [string]$ConsoleHost = "http://127.0.0.1:7351",
    [string]$Username = "admin",
    [string]$Password = "",
    [switch]$AllLb,
    [switch]$BrokenOnly = $true
)

if ([string]::IsNullOrWhiteSpace($Password)) {
    $secure = Read-Host "Console password ($Username)" -AsSecureString
    $Password = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
}

function Should-Delete([string]$Id) {
    if ($AllLb) {
        return $Id.StartsWith("lb_")
    }
    if ($BrokenOnly) {
        return ($Id -match '%GW%V') -or ($Id -match '%')
    }
    return $false
}

Write-Host "Auth: $ConsoleHost"
$authBody = @{ username = $Username; password = $Password } | ConvertTo-Json -Compress
try {
    $session = Invoke-RestMethod -Method Post `
        -Uri "$ConsoleHost/v2/console/authenticate" `
        -Body $authBody `
        -ContentType "application/json; charset=utf-8"
} catch {
    Write-Error "Console auth failed: $_"
    exit 1
}

$token = $session.token
if ([string]::IsNullOrWhiteSpace($token)) {
    Write-Error "Empty console token"
    exit 1
}

$headers = @{ Authorization = "Bearer $token" }

Write-Host "Fetching leaderboards..."
$list = Invoke-RestMethod -Method Get `
    -Uri "$ConsoleHost/v2/console/leaderboard?limit=1000" `
    -Headers $headers

$boards = @()
if ($null -ne $list.leaderboards) { $boards = $list.leaderboards }
elseif ($null -ne $list.leaderboardList) { $boards = $list.leaderboardList }

if ($boards.Count -eq 0) {
    Write-Host "No leaderboards in response (check API shape or limit)."
    exit 0
}

$deleted = 0
$failed = 0

foreach ($lb in $boards) {
    $id = [string]$lb.id
    if ([string]::IsNullOrWhiteSpace($id)) { continue }
    if (-not (Should-Delete $id)) { continue }

    $encoded = [uri]::EscapeDataString($id)
    try {
        Invoke-RestMethod -Method Delete `
            -Uri "$ConsoleHost/v2/console/leaderboard/$encoded" `
            -Headers $headers | Out-Null
        Write-Host "OK  deleted: $id"
        $deleted++
    } catch {
        Write-Host "ERR failed:  $id"
        Write-Host "     $_"
        $failed++
    }
}

Write-Host ""
Write-Host "Done. Deleted=$deleted Failed=$failed"
