#Requires -Version 5.1
[CmdletBinding()]
param([switch]$Staged, [switch]$History)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$failures = New-Object 'System.Collections.Generic.List[string]'
$checkedBlobs = New-Object 'System.Collections.Generic.HashSet[string]'
$checkedFiles = 0

function Invoke-RepositoryGit {
    param([string[]]$Arguments)
    $result = & git -C $repositoryRoot -c core.quotepath=false @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'Git could not inspect the repository. Audit stopped.' }
    return $result
}

function Test-PublishPath {
    param([string]$Path)
    $blocked = '(?i)(^|/)(config|settings|status)\.json(?:\..*)?$|(^|/)\.env(?:\..*)?$|\.(exe|dll|pdb|bin|log|bak|tmp|key|pem|pfx|p12|dmp|dump|zip|7z|suo|user)$|(^|/)(bin|obj|test-output|artifacts|\.vs|\.idea|\.vscode|\.git|\.codex|\.agents)(/|$)|(^|/)(\.DS_Store|Thumbs\.db|Desktop\.ini)$|(^|/)(?:.*password.*\.txt|.*credential.*\.json|.*secret.*\.json)$|(^|/)obs-zoom-to-mouse\.lua$'
    if ($Path -match '(^|/)\.env\.example$') { return $true }
    if ($Path -match $blocked) { $failures.Add("${Path}: private, generated, or unrelated file"); return $false }
    return $true
}

# Report file names and rule names only. Never print a matching secret or line.
$rules = [ordered]@{
    'private key' = '-----BEGIN (?:RSA |EC |DSA |OPENSSH |PGP )?PRIVATE KEY(?: BLOCK)?-----'
    'GitHub token' = '\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,})\b'
    'API secret' = '\bsk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{24,}\b'
    'AWS access key' = '\b(?:AKIA|ASIA)[A-Z0-9]{16}\b'
    'Slack token' = '\bxox[baprs]-[A-Za-z0-9-]{20,}\b'
    'Windows encrypted credential' = 'AQAAANCMnd8BFdERjHoAwE/Cl\+sB[A-Za-z0-9+/=]{30,}'
    'credential in URL' = '(?i)(?:https?|wss?)://[^\s/]+:[^\s/]+@'
    'local network address' = '\b(?:192\.168\.\d{1,3}\.\d{1,3}|10\.\d{1,3}\.\d{1,3}\.\d{1,3}|172\.(?:1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3})\b'
    'personal home directory' = '(?i)(?:[A-Z]:\\Users\\[^\\\s"<>]+|/' + 'Users/[^/\s"<>]+|/' + 'home/[^/\s"<>]+)'
    'literal credential assignment' = '(?i)\b(?:password|secret|api[_-]?key|access[_-]?token)\s*[:=]\s*["''][^"''\r\n]{8,}["'']'
}

function Test-PublishContent {
    param([string]$Path, [string]$Content)
    if ($Content.IndexOf([char]0) -ge 0) { $failures.Add("${Path}: unexpected binary content"); return }
    foreach ($rule in $rules.GetEnumerator()) {
        if ($Content -match $rule.Value) { $failures.Add("${Path}: $($rule.Key)") }
    }
}

function Test-Blob {
    param([string]$Path, [string]$Blob)
    $script:checkedFiles++
    if (-not (Test-PublishPath -Path $Path)) { return }
    if ($Path -match '\.(png|ico)$') { return }
    if (-not $checkedBlobs.Add($Blob)) { return }
    $size = [long](Invoke-RepositoryGit -Arguments @('cat-file', '-s', $Blob))
    if ($size -gt 2MB) { $failures.Add("${Path}: text file exceeds 2 MB; inspect before publishing"); return }
    $content = (Invoke-RepositoryGit -Arguments @('cat-file', 'blob', $Blob)) -join "`n"
    Test-PublishContent -Path $Path -Content $content
}

if ($Staged -and $History) { throw 'Choose -Staged or -History, not both.' }
if ($Staged -or $History) {
    foreach ($entry in (Invoke-RepositoryGit -Arguments @('ls-files', '--stage'))) {
        if ($entry -match '^\d+ ([a-f0-9]{40,64}) (\d)\t(.+)$') {
            if ($Matches[2] -ne '0') { $failures.Add("$($Matches[3]): unresolved merge entry"); continue }
            Test-Blob -Path $Matches[3] -Blob $Matches[1]
        }
    }
} else {
    foreach ($path in (Invoke-RepositoryGit -Arguments @('ls-files', '--cached', '--others', '--exclude-standard'))) {
        $checkedFiles++
        if (-not (Test-PublishPath -Path $path)) { continue }
        $localPath = Join-Path $repositoryRoot $path
        if (-not (Test-Path -LiteralPath $localPath -PathType Leaf)) { continue }
        if ($path -match '\.(png|ico)$') { continue }
        if ((Get-Item -LiteralPath $localPath).Length -gt 2MB) { $failures.Add("${path}: text file exceeds 2 MB; inspect before publishing"); continue }
        Test-PublishContent -Path $path -Content ([IO.File]::ReadAllText($localPath))
    }
}

if ($History) {
    # Scan every reachable tree, so deleting a credential in a later commit
    # cannot hide it from the pre-push check.
    foreach ($commit in (Invoke-RepositoryGit -Arguments @('rev-list', '--all'))) {
        foreach ($entry in (Invoke-RepositoryGit -Arguments @('ls-tree', '-r', '--full-tree', $commit))) {
            if ($entry -match '^\d+ blob ([a-f0-9]{40,64})\t(.+)$') { Test-Blob -Path $Matches[2] -Blob $Matches[1] }
        }
    }
}

if ($failures.Count) {
    Write-Output 'Publication check failed. File names and rule names only:'
    $failures | Sort-Object -Unique | ForEach-Object { Write-Output "  $_" }
    exit 1
}
Write-Output "PASS: $checkedFiles file entries checked for private files, credentials, and machine-specific details."
exit 0
