[CmdletBinding()]
param([string]$OutputDirectory = $PSScriptRoot)

$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sourcePath = Join-Path $PSScriptRoot 'VirtualDesktop11-24H2.cs'
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$outputPath = Join-Path $OutputDirectory 'VirtualDesktop11-24H2.exe'

if (-not (Test-Path -LiteralPath $compilerPath)) {
    throw "The Windows .NET Framework C# compiler was not found: $compilerPath"
}

& $compilerPath /nologo /optimize+ /target:exe "/out:$outputPath" $sourcePath
if ($LASTEXITCODE -ne 0) {
    throw "Desktop helper compilation failed with exit code $LASTEXITCODE."
}

Write-Output "Built $outputPath"
