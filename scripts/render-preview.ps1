[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$outputDirectory = Join-Path $repositoryRoot 'test-output'
$null = New-Item -ItemType Directory -Path $outputDirectory -Force
$rendererPath = Join-Path $outputDirectory 'RenderPreview.exe'
$sourcePaths = @('App.cs', 'ObsClient.cs', 'Ui.cs', 'Theme.cs', 'tests\RenderPreview.cs', 'tests\PreviewRenderer.cs') | ForEach-Object { Join-Path $repositoryRoot $_ }
& $compilerPath /nologo /target:exe /platform:x64 /warn:4 /codepage:65001 /main:StreamingBridge.RenderPreview "/out:$rendererPath" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Security.dll $sourcePaths
if ($LASTEXITCODE -ne 0) { throw 'Preview compilation failed.' }
& $rendererPath $repositoryRoot
if ($LASTEXITCODE -ne 0) { throw 'Preview rendering failed.' }
