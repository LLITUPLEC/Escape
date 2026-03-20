$ErrorActionPreference = "Stop"

$RepoUrl = "https://github.com/LLITUPLEC/Escape.git"
$Branch = "main"

$root = $PSScriptRoot
Set-Location $root

function Test-Git {
  $git = Get-Command git -ErrorAction SilentlyContinue
  if ($null -eq $git) { throw "git not found in PATH" }
}

function New-Repo {
  if (!(Test-Path ".git")) {
    Write-Host "Initializing git repository in $root ..."
    git init
  }
}

function Write-Gitignore {
  if (!(Test-Path ".gitignore")) {
    Write-Host "Creating minimal Unity .gitignore ..."
    @"
[Ll]ibrary/
[Tt]emp/
[Oo]bj/
[Bb]uild/
[Bb]uilds/
Logs/
.vscode/
.idea/
.vs/
*.csproj
*.sln
ParrelSync/
Assets/_Project/Resources/NakamaConnectionConfig.asset
Assets/_Project/Resources/NakamaConnectionConfig.asset.meta
"@ | Out-File -Encoding utf8 .gitignore
  }
}

Test-Git
New-Repo
Write-Gitignore

Write-Host "Using branch: $Branch"
git checkout -B $Branch | Out-Host

git add -A

$porcelain = git status --porcelain
if ([string]::IsNullOrWhiteSpace($porcelain)) {
  Write-Host "No changes to commit."
  exit 0
}

$Comment = Read-Host "Enter commit message"
if ([string]::IsNullOrWhiteSpace($Comment)) {
  Write-Host "Empty commit message. Aborting."
  exit 1
}

Write-Host "Committing..."
git commit -m $Comment | Out-Host

$hasOrigin = @(git remote) -contains "origin"
if (-not $hasOrigin) {
  Write-Host "Adding remote origin: $RepoUrl"
  git remote add origin $RepoUrl
}
else {
  $current = git remote get-url origin
  if ($current -ne $RepoUrl) {
    Write-Host "Updating origin url to $RepoUrl"
    git remote set-url origin $RepoUrl
  }
}

Write-Host "Pushing to $Branch ..."
git push -u origin $Branch | Out-Host

Write-Host "Done."

$Comment2 = Read-Host "Done."