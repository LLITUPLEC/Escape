param([string]$UnityEditor = 'C:/Program Files/Unity/Hub/Editor/6000.3.9f1/Editor')
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$buildDir = Join-Path $projectRoot '.cache/qa/client-cache-tests'
New-Item -ItemType Directory -Path $buildDir -Force | Out-Null
$unityData = Join-Path $UnityEditor 'Data'
$framework = Join-Path $unityData 'MonoBleedingEdge/lib/mono/4.5'
$compiler = Join-Path $unityData 'DotNetSdkRoslyn/csc.dll'
$dotnetRuntime = Join-Path $unityData 'NetCoreRuntime/dotnet.exe'
$monoRuntime = Join-Path $unityData 'MonoBleedingEdge/bin/mono.exe'
$testExe = Join-Path $buildDir 'ClientCacheRegression.exe'
$sourceFiles = @(
    (Join-Path $PSScriptRoot 'ClientCacheRegression.cs'),
    (Join-Path $projectRoot 'Assets/_Project/Scripts/Nakama/LocalAccountCacheScope.cs'),
    (Join-Path $projectRoot 'Assets/_Project/Scripts/Achievements/AchievementTracker.cs'),
    (Join-Path $projectRoot 'Assets/_Project/Scripts/Character/PlayerResourcesService.cs'),
    (Join-Path $projectRoot 'Assets/_Project/Scripts/Character/PlayerResourcesRpcModels.cs'),
    (Join-Path $projectRoot 'Assets/_Project/Scripts/Character/CharacterProfileRpcModels.cs')
)
$compileArgs = @('/nologo', '/langversion:9', '/target:exe', "/out:$testExe", '/nostdlib+')
foreach ($assembly in @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Web.Extensions.dll')) {
    $compileArgs += '/reference:' + (Join-Path $framework $assembly)
}
& $dotnetRuntime $compiler @compileArgs @sourceFiles
if ($LASTEXITCODE -ne 0) { throw 'Client cache regression compilation failed.' }
& $monoRuntime $testExe
if ($LASTEXITCODE -ne 0) { throw 'Client cache regression failed.' }
