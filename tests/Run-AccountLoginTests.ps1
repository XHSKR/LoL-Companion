param(
    [Parameter(Mandatory = $true)][string]$CompilerPath,
    [Parameter(Mandatory = $true)][string]$FrameworkDirectory,
    [string]$AppDirectory = (Join-Path $PSScriptRoot '..\LoL Companion\bin\x64\AutoLogin'),
    [switch]$LiveReadOnly
)
$ErrorActionPreference = 'Stop'
$sourceDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\LoL Companion'))
$AppDirectory = [IO.Path]::GetFullPath($AppDirectory)
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ('lol-companion-account-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $AppDirectory 'LoL Companion.exe') -Destination $testDirectory
Get-ChildItem -LiteralPath $AppDirectory -Filter '*.dll' | Copy-Item -Destination $testDirectory
Copy-Item -LiteralPath (Join-Path $AppDirectory 'LoL Companion.exe.config') -Destination (Join-Path $testDirectory 'AccountLoginTests.exe.config')
$compilerArguments = @('/nologo', '/langversion:7.3', '/target:exe', '/platform:x64', '/nostdlib+', ('/out:' + (Join-Path $testDirectory 'AccountLoginTests.exe')))
foreach ($assembly in @('mscorlib', 'System', 'System.Core', 'System.Drawing', 'System.Management', 'System.Net.Http', 'System.Windows.Forms', 'System.Xml')) {
    $compilerArguments += '/reference:' + (Join-Path $FrameworkDirectory ($assembly + '.dll'))
}
foreach ($assembly in @('LoL Companion.exe', 'Newtonsoft.Json.dll', 'MaterialSkin.dll')) {
    $compilerArguments += '/reference:' + (Join-Path $AppDirectory $assembly)
}
$compilerArguments += Join-Path $PSScriptRoot 'AccountLoginTests.cs'
foreach ($source in @('RiotClientApi.cs', 'AccountLoginService.cs', 'SettingSaver.cs')) {
    $compilerArguments += Join-Path $sourceDirectory $source
}
& $CompilerPath @compilerArguments
if ($LASTEXITCODE -ne 0) { throw 'Account login tests failed to compile.' }
$runArguments = @()
if ($LiveReadOnly) { $runArguments += '--live-read-only' }
& (Join-Path $testDirectory 'AccountLoginTests.exe') @runArguments
if ($LASTEXITCODE -ne 0) { throw 'Account login tests failed.' }
Write-Output ('Test artifacts: ' + $testDirectory)
