$ErrorActionPreference = 'Stop'
$bridgeExecutable = Join-Path $PSScriptRoot 'StreamingBridge.exe'
if (-not (Test-Path -LiteralPath $bridgeExecutable)) { throw 'Build StreamingBridge.exe first.' }
$shortcutShell = New-Object -ComObject WScript.Shell
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Streaming Bridge.lnk'
$startupShortcut = Join-Path ([Environment]::GetFolderPath('Startup')) 'Streaming Bridge.lnk'
foreach ($shortcutPath in @($desktopShortcut, $startupShortcut)) {
    $shortcut = $shortcutShell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $bridgeExecutable
    $shortcut.WorkingDirectory = $PSScriptRoot
    $shortcut.Arguments = if ($shortcutPath -eq $startupShortcut) { '--tray' } else { '--settings' }
    $shortcut.Description = 'Corsair G keys: Windows desktops and Mac OBS scenes'
    $shortcut.IconLocation = "$bridgeExecutable,0"
    $shortcut.Save()
    Write-Output "Created $shortcutPath"
}
