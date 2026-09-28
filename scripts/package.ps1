[CmdletBinding()]
param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.2.0')

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = Join-Path $repositoryRoot 'artifacts'
# Never reuse an app directory: it might contain a user's runtime settings.
$stagingRoot = Join-Path $artifactsRoot ('package-staging\' + [Guid]::NewGuid().ToString('N'))
$packagesRoot = Join-Path $artifactsRoot 'packages'
$null = New-Item -ItemType Directory -Path $stagingRoot,$packagesRoot -Force
& (Join-Path $repositoryRoot 'build.ps1') -OutputDirectory $stagingRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination (Join-Path $stagingRoot 'LICENSE')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\QUICKSTART.md') -Destination (Join-Path $stagingRoot 'QUICKSTART.md')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'desktop\LICENSE.VirtualDesktop') -Destination (Join-Path $stagingRoot 'desktop\LICENSE.VirtualDesktop')

$packagePath = Join-Path $packagesRoot "StreamingBridge-$Version-win-x64.zip"
$allowedFiles = @(
    (Join-Path $stagingRoot 'StreamingBridge.exe'),
    (Join-Path $stagingRoot 'LICENSE'),
    (Join-Path $stagingRoot 'QUICKSTART.md'),
    (Join-Path $stagingRoot 'desktop')
)
Compress-Archive -LiteralPath $allowedFiles -DestinationPath $packagePath -Force

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $expected = @('StreamingBridge.exe', 'LICENSE', 'QUICKSTART.md', 'desktop/VirtualDesktop11-24H2.exe', 'desktop/LICENSE.VirtualDesktop')
    $actual = @($archive.Entries | Where-Object { $_.Name } | ForEach-Object { $_.FullName.Replace('\', '/') })
    if (@(Compare-Object ($expected | Sort-Object) ($actual | Sort-Object)).Count) { throw 'Unexpected release archive contents. Do not distribute this archive.' }
} finally { $archive.Dispose() }
Write-Output "Packaged and checked $packagePath (app, helper, licenses, and quick-start guide only)."
