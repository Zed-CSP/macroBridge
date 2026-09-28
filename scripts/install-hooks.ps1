[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$existing = & git -C $repositoryRoot config --local --get core.hooksPath
if ($existing -and $existing -ne '.githooks') { throw "A different local hooks directory is configured: $existing. Keep or integrate it before installing these hooks." }
& git -C $repositoryRoot config --local core.hooksPath .githooks
if ($LASTEXITCODE -ne 0) { throw 'Could not configure repository hooks.' }
Write-Output 'Installed repository-local pre-commit and pre-push checks.'
