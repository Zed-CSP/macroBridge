[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$fixtureRoot = Join-Path $repositoryRoot ('test-output\publication-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'scripts') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'check-repository.ps1') -Destination (Join-Path $fixtureRoot 'scripts\check-repository.ps1')
$fixtureChecker = Join-Path $fixtureRoot 'scripts\check-repository.ps1'
$assertions = 0

function Invoke-FixtureGit {
    param([string[]]$Arguments)
    $output = & git -C $fixtureRoot -c core.autocrlf=false @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Could not prepare the publication-check fixture.' }
}
function Assert-Check {
    param([bool]$ExpectedPass, [string]$Mode, [string]$Description, [string]$HiddenValue = '')
    $output = & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $fixtureChecker $Mode 2>&1
    $passed = $LASTEXITCODE -eq 0
    if ($passed -ne $ExpectedPass) { throw "Publication check: $Description" }
    if (-not $passed -and -not ($output -join "`n").Contains('Publication check failed.')) { throw 'The guard failed unexpectedly instead of reporting a detected private value.' }
    if ($HiddenValue -and ($output -join "`n").Contains($HiddenValue)) { throw 'Publication check exposed a fixture secret in its output.' }
    $script:assertions++
}
function Write-Fixture {
    param([string]$Name, [string]$Content)
    [IO.File]::WriteAllText((Join-Path $fixtureRoot $Name), $Content, (New-Object Text.UTF8Encoding($false)))
}

Invoke-FixtureGit -Arguments @('init', '--quiet', '--initial-branch=main')
Write-Fixture -Name '.gitignore' -Content "config.json*`n"
Write-Fixture -Name 'README.md' -Content 'Public sample project using localhost and 192.0.2.10.'
Invoke-FixtureGit -Arguments @('add', '.gitignore', 'README.md', 'scripts/check-repository.ps1')
Assert-Check -ExpectedPass $true -Mode '-Staged' -Description 'Safe staged source must pass.'

Write-Fixture -Name 'config.json' -Content '{"Host":"localhost","ProtectedPassword":"fixture-only"}'
Invoke-FixtureGit -Arguments @('add', '--force', 'config.json')
Assert-Check -ExpectedPass $false -Mode '-Staged' -Description 'A forced private config must be blocked.'
Invoke-FixtureGit -Arguments @('rm', '--cached', '--quiet', '--', 'config.json')

# Deliberately synthetic values assembled from fragments; no real credentials.
$sampleToken = 'gh' + 'p_' + ('a' * 36)
Write-Fixture -Name 'token.txt' -Content $sampleToken
Assert-Check -ExpectedPass $true -Mode '-Staged' -Description 'Unstaged data must not contaminate the index check.'
Invoke-FixtureGit -Arguments @('add', 'token.txt')
Assert-Check -ExpectedPass $false -Mode '-Staged' -Description 'A token in the staged snapshot must be blocked.' -HiddenValue $sampleToken
Write-Fixture -Name 'token.txt' -Content 'Fixture removed.'
Invoke-FixtureGit -Arguments @('add', 'token.txt')

$sampleAddress = '192.' + '168.' + '11.22'
Write-Fixture -Name 'network.txt' -Content $sampleAddress
Invoke-FixtureGit -Arguments @('add', 'network.txt')
Assert-Check -ExpectedPass $false -Mode '-Staged' -Description 'Real-format LAN addresses must be blocked.' -HiddenValue $sampleAddress
Write-Fixture -Name 'network.txt' -Content 'obs-studio.local'
Invoke-FixtureGit -Arguments @('add', 'network.txt')

$sampleHome = 'C:' + '\Users\' + 'fixture-person\config'
Write-Fixture -Name 'paths.txt' -Content $sampleHome
Invoke-FixtureGit -Arguments @('add', 'paths.txt')
Assert-Check -ExpectedPass $false -Mode '-Staged' -Description 'Personal home paths must be blocked.' -HiddenValue $sampleHome
Write-Fixture -Name 'paths.txt' -Content 'Relative paths only.'
Invoke-FixtureGit -Arguments @('add', 'paths.txt')

Write-Fixture -Name 'environment.txt' -Content ('password = "' + 'synthetic-value-only' + '"')
Invoke-FixtureGit -Arguments @('add', 'environment.txt')
Assert-Check -ExpectedPass $false -Mode '-Staged' -Description 'Literal password assignments must be blocked.' -HiddenValue 'synthetic-value-only'
Write-Fixture -Name 'environment.txt' -Content 'No literal credentials.'
Invoke-FixtureGit -Arguments @('add', 'environment.txt')
Assert-Check -ExpectedPass $true -Mode '-Staged' -Description 'Cleaned fixture source must pass.'

$authorArguments = @('-c', 'user.name=Publication Test', '-c', 'user.email=fixture@example.invalid', 'commit', '--quiet', '-m')
Invoke-FixtureGit -Arguments ($authorArguments + 'Safe fixture')
Write-Fixture -Name 'token.txt' -Content $sampleToken
Invoke-FixtureGit -Arguments @('add', 'token.txt')
Invoke-FixtureGit -Arguments ($authorArguments + 'Synthetic bad fixture')
Write-Fixture -Name 'token.txt' -Content 'Fixture removed from the latest commit.'
Invoke-FixtureGit -Arguments @('add', 'token.txt')
Invoke-FixtureGit -Arguments ($authorArguments + 'Remove synthetic fixture')
Assert-Check -ExpectedPass $true -Mode '-Staged' -Description 'The latest snapshot is clean.'
Assert-Check -ExpectedPass $false -Mode '-History' -Description 'Deleted credentials in reachable history must still be blocked.' -HiddenValue $sampleToken

Write-Output "PASS: $assertions publication checks, including forced private files, staged content, redacted reports, and deleted history."
