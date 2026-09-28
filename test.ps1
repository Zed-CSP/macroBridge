[CmdletBinding()]
param([ValidateSet('All', 'AppTests', 'ObsClientTests', 'UiTests')][string]$Suite = 'All')

$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw 'The Windows .NET Framework C# compiler was not found.' }
$outputDirectory = Join-Path $PSScriptRoot 'test-output'
$null = New-Item -ItemType Directory -Path $outputDirectory -Force
$references = @('/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll', '/r:System.Security.dll')
$appSources = @('App.cs', 'ObsClient.cs', 'Ui.cs', 'Theme.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$suites = @(
    @{ Name = 'AppTests'; Main = 'StreamingBridge.AppTests'; Sources = @($appSources) + (Join-Path $PSScriptRoot 'AppTests.cs') },
    @{ Name = 'ObsClientTests'; Main = 'ObsClientTests'; Sources = @((Join-Path $PSScriptRoot 'ObsClient.cs'), (Join-Path $PSScriptRoot 'tests\ObsClientTests.cs')) },
    @{ Name = 'UiTests'; Main = 'StreamingBridge.UiTests'; Sources = @($appSources) + @((Join-Path $PSScriptRoot 'tests\UiTests.cs'), (Join-Path $PSScriptRoot 'tests\PreviewRenderer.cs')) }
)
if ($Suite -ne 'All') { $suites = @($suites | Where-Object { $_.Name -eq $Suite }) }
foreach ($suiteDefinition in $suites) {
    $suiteExecutable = Join-Path $outputDirectory ($suiteDefinition.Name + '.exe')
    & $compilerPath /nologo /target:exe /platform:x64 /warn:4 /codepage:65001 "/main:$($suiteDefinition.Main)" "/out:$suiteExecutable" $references $suiteDefinition.Sources
    if ($LASTEXITCODE -ne 0) { throw "$($suiteDefinition.Name) compilation failed." }
    & $suiteExecutable
    if ($LASTEXITCODE -ne 0) { throw "$($suiteDefinition.Name) failed." }
}
Write-Output 'Selected checks passed. No live OBS requests or desktop switches were performed.'
