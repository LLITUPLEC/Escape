$ErrorActionPreference = "Stop"

$RepoUrl = "https://github.com/LLITUPLEC/Escape.git"
$Branch = "main"

function Test-Git {
  $git = Get-Command git -ErrorAction SilentlyContinue
  if ($null -eq $git) { throw "git not found in PATH" }
}

function Get-RepoName([string]$url) {
  $leaf = Split-Path $url -Leaf
  if ($leaf.ToLower().EndsWith(".git")) { return $leaf.Substring(0, $leaf.Length - 4) }
  return $leaf
}

Test-Git

$root = $PSScriptRoot
$defaultTarget = Join-Path $root (Get-RepoName $RepoUrl)

$TargetDir = Read-Host "Target dir (empty = $defaultTarget)"
if ([string]::IsNullOrWhiteSpace($TargetDir)) {
  $TargetDir = $defaultTarget
}

if (!(Test-Path $TargetDir)) {
  Write-Host "Cloning into $TargetDir ..."
  git clone --branch $Branch $RepoUrl $TargetDir
}
else {
  Write-Host "Pull in $TargetDir ..."
  Set-Location $TargetDir
  git fetch --all
  git checkout $Branch | Out-Host
  git pull --ff-only | Out-Host
  Write-Host "Done."
  exit 0
}

Write-Host "Done. Open Unity project at: $TargetDir"

