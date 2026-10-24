param(
    [Parameter(Mandatory = $true)][string]$CompilerPath,
    [Parameter(Mandatory = $true)][string]$FrameworkDirectory,
    [string]$NewtonsoftJsonPath
)
$ErrorActionPreference = 'Stop'
if (!$NewtonsoftJsonPath) {
    $NewtonsoftJsonPath = Join-Path $PSScriptRoot '..\packages\Newtonsoft.Json.13.0.3\lib\net45\Newtonsoft.Json.dll'
}
$sourceDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\LoL Companion'))
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ('lol-dodge-offline-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDirectory | Out-Null
Copy-Item -LiteralPath $NewtonsoftJsonPath -Destination $testDirectory
$compilerArguments = @('/nologo', '/langversion:7.3', '/target:exe', '/nostdlib+', ('/out:' + (Join-Path $testDirectory 'QuickDodgeOfflineChecks.exe')))
foreach ($assembly in @('mscorlib', 'System', 'System.Core', 'System.Net.Http')) {
    $compilerArguments += '/reference:' + (Join-Path $FrameworkDirectory ($assembly + '.dll'))
}
$compilerArguments += '/reference:' + [IO.Path]::GetFullPath($NewtonsoftJsonPath)
$compilerArguments += Join-Path $PSScriptRoot 'QuickDodgeOfflineChecks.cs'
foreach ($source in @('LeagueEdgeClient.cs', 'QuickDodgeService.cs')) {
    $compilerArguments += Join-Path $sourceDirectory $source
}
& $CompilerPath @compilerArguments
if ($LASTEXITCODE -ne 0) { throw 'Quick Dodge offline checks failed to compile.' }
# This harness compiles fake LCU/process dependencies and a fake HTTP handler.
# It never loads the application executable or uses real client credentials.
& (Join-Path $testDirectory 'QuickDodgeOfflineChecks.exe')
if ($LASTEXITCODE -ne 0) { throw 'Quick Dodge offline checks failed.' }
Write-Output ('Test artifacts: ' + $testDirectory)
