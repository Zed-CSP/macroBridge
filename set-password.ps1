$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
$configPath = Join-Path $PSScriptRoot 'config.json'
if (-not (Test-Path -LiteralPath $configPath)) { throw 'Save bridge settings before setting the password.' }
$securePassword = Read-Host 'OBS WebSocket password' -AsSecureString
$passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
try {
    $plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
    if ([string]::IsNullOrEmpty($plainPassword)) { throw 'No password was entered.' }
    $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    $config.ProtectedPassword = [Convert]::ToBase64String(
        [System.Security.Cryptography.ProtectedData]::Protect(
            [System.Text.Encoding]::UTF8.GetBytes($plainPassword),
            $null,
            [System.Security.Cryptography.DataProtectionScope]::CurrentUser))
    $temporaryPath = $configPath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    $backupPath = $configPath + '.' + [Guid]::NewGuid().ToString('N') + '.bak'
    try {
        [System.IO.File]::WriteAllText($temporaryPath, ($config | ConvertTo-Json -Depth 5 -Compress), (New-Object System.Text.UTF8Encoding($false)))
        [System.IO.File]::Replace($temporaryPath, $configPath, $backupPath)
    } finally {
        if (Test-Path -LiteralPath $temporaryPath) { Remove-Item -LiteralPath $temporaryPath -Force }
        if (Test-Path -LiteralPath $backupPath) { Remove-Item -LiteralPath $backupPath -Force }
    }
    Write-Output 'Saved OBS credential for the current Windows user.'
} finally {
    $plainPassword = $null
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
    $securePassword.Dispose()
}
