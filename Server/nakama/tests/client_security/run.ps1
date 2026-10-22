param([string]$UnityEditor = 'C:/Program Files/Unity/Hub/Editor/6000.3.9f1/Editor')
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../../..')).Path
$outputDirectory = Join-Path $PSScriptRoot 'output'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$runtimeDirectory = Join-Path $UnityEditor 'Data/NetCoreRuntime/shared/Microsoft.NETCore.App/6.0.21'
$dotnetRuntime = Join-Path $UnityEditor 'Data/NetCoreRuntime/dotnet.exe'
$compiler = Join-Path $UnityEditor 'Data/DotNetSdkRoslyn/csc.dll'
$executable = Join-Path $outputDirectory 'CredentialSecurityTests.dll'
$responseFile = Join-Path $outputDirectory 'CredentialSecurityTests.rsp'
$compileArguments = @('-nologo', '-nostdlib+', '-target:exe', '-langversion:latest', ('-out:"' + $executable + '"'))
foreach ($assembly in Get-ChildItem -LiteralPath $runtimeDirectory -Filter '*.dll') {
    if ($assembly.Name -match '^(System\.|Microsoft\.|netstandard\.|mscorlib\.|WindowsBase\.)' -and $assembly.Name -notmatch '\.Native\.') {
        $compileArguments += '-r:"' + $assembly.FullName + '"'
    }
}
$securityDirectory = Join-Path $projectRoot 'Assets/_Project/Scripts/Nakama/Security'
foreach ($source in @('SecretVault.cs', 'NakamaCredentialStore.cs', 'PlatformSecretVault.cs')) {
    $compileArguments += '"' + (Join-Path $securityDirectory $source) + '"'
}
foreach ($source in @('UnityTestStubs.cs', 'CredentialSecurityTests.cs')) {
    $compileArguments += '"' + (Join-Path $PSScriptRoot $source) + '"'
}
Set-Content -LiteralPath $responseFile -Value $compileArguments -Encoding utf8
& $dotnetRuntime $compiler -noconfig ('@' + $responseFile)
if ($LASTEXITCODE -ne 0) { throw 'Credential test compilation failed.' }
Set-Content -LiteralPath (Join-Path $outputDirectory 'CredentialSecurityTests.runtimeconfig.json') -Encoding utf8 -Value '{"runtimeOptions":{"tfm":"net6.0","framework":{"name":"Microsoft.NETCore.App","version":"6.0.21"}}}'
$testDataDirectory = Join-Path $outputDirectory ('fixtures-' + [guid]::NewGuid().ToString('N'))
& $dotnetRuntime $executable $testDataDirectory
if ($LASTEXITCODE -ne 0) { throw 'Credential security regression failed.' }
