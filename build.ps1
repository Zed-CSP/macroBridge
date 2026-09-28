[CmdletBinding()]
param([string]$OutputDirectory = $PSScriptRoot)

$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw 'The Windows .NET Framework C# compiler was not found.' }
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$outputPath = Join-Path $OutputDirectory 'StreamingBridge.exe'
$sourcePaths = @('App.cs', 'Config.cs', 'ObsClient.cs', 'Ui.cs', 'Theme.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
foreach ($sourcePath in $sourcePaths) {
    if (-not (Test-Path -LiteralPath $sourcePath)) { throw "Missing source: $sourcePath" }
}
$iconPath = Join-Path $PSScriptRoot 'assets\bridge.ico'
if (-not (Test-Path -LiteralPath $iconPath)) { throw 'Missing application icon: assets/bridge.ico' }
& $compilerPath /nologo /target:winexe /platform:x64 /optimize+ /warn:4 /codepage:65001 "/win32icon:$iconPath" "/out:$outputPath" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Security.dll $sourcePaths
if ($LASTEXITCODE -ne 0) { throw 'StreamingBridge build failed.' }
& (Join-Path $PSScriptRoot 'desktop\build.ps1') -OutputDirectory (Join-Path $OutputDirectory 'desktop')
Write-Output "Built $outputPath"
