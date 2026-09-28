[CmdletBinding()]
param(
    [ValidateRange(1, 64)][int]$EnsureCount = 6,
    [switch]$TestRoundTrip
)
$ErrorActionPreference = 'Stop'
$helperPath = Join-Path $PSScriptRoot 'desktop\VirtualDesktop11-24H2.exe'
if (-not (Test-Path -LiteralPath $helperPath)) { throw 'Desktop helper is missing.' }
function Invoke-DesktopHelper {
    param([string[]]$DesktopArguments)
    & $helperPath @DesktopArguments | Out-Null
    $result = $LASTEXITCODE
    if ($result -lt 0) { throw "Desktop helper failed with code $result." }
    return $result
}
$originalDesktop = Invoke-DesktopHelper -DesktopArguments @('/Quiet', '/GetCurrentDesktop')
$desktopCount = Invoke-DesktopHelper -DesktopArguments @('/Quiet', '/Count')
while ($desktopCount -lt $EnsureCount) {
    $null = Invoke-DesktopHelper -DesktopArguments @('/Quiet', '/New')
    $updatedCount = Invoke-DesktopHelper -DesktopArguments @('/Quiet', '/Count')
    if ($updatedCount -ne ($desktopCount + 1)) { throw 'Could not verify the newly created desktop.' }
    $desktopCount = $updatedCount
}
if ($TestRoundTrip -and $desktopCount -gt 1) {
    $testDesktop = ($originalDesktop + 1) % $desktopCount
    try {
        $null = Invoke-DesktopHelper -DesktopArguments @('/Quiet', '/Animation:Off', "/Switch:$testDesktop")
        $observedDesktop = Invoke-DesktopHelper -DesktopArguments @('/Quiet', '/GetCurrentDesktop')
        if ($observedDesktop -ne $testDesktop) { throw 'Desktop switch verification failed.' }
    } finally {
        $null = Invoke-DesktopHelper -DesktopArguments @('/Quiet', '/Animation:Off', "/Switch:$originalDesktop")
        $restoredDesktop = Invoke-DesktopHelper -DesktopArguments @('/Quiet', '/GetCurrentDesktop')
        if ($restoredDesktop -ne $originalDesktop) { throw 'Could not restore the starting desktop.' }
    }
    Write-Output 'Numbered desktop switch and return verified.'
}
Write-Output "Desktop count: $desktopCount. Current desktop: $($originalDesktop + 1)."
