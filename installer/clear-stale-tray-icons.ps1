[CmdletBinding()]
param(
    [switch]$Apply,
    [switch]$RestartExplorer
)

$ErrorActionPreference = "Stop"

$notifyIconSettingsPath = "HKCU:\Control Panel\NotifyIconSettings"

if (-not (Test-Path $notifyIconSettingsPath)) {
    Write-Host "NotifyIconSettings registry path was not found. There is nothing to clean."
    return
}

$entries = Get-ChildItem $notifyIconSettingsPath |
    ForEach-Object {
        $properties = Get-ItemProperty $_.PSPath
        $executablePath = [string]$properties.ExecutablePath
        $tooltip = [string]$properties.InitialTooltip

        if ($executablePath -match "\\VmManager\.App\.exe$" -or $executablePath -match "LittleBitsSoftware\.VmManager" -or $tooltip -eq "VM Manager") {
            [pscustomobject]@{
                Key = $_.PSChildName
                RegistryPath = $_.PSPath
                ExecutablePath = $executablePath
                InitialTooltip = $tooltip
                Exists = if ($executablePath) { Test-Path -LiteralPath $executablePath } else { $false }
                IsInstalledCurrent = $executablePath -match "\\AppData\\Local\\LittleBitsSoftware\.VmManager\\current\\VmManager\.App\.exe$"
            }
        }
    }

if (-not $entries) {
    Write-Host "No VM Manager tray icon entries were found."
    return
}

$staleEntries = $entries | Where-Object { -not $_.IsInstalledCurrent }

Write-Host "VM Manager tray icon entries:"
$entries |
    Sort-Object IsInstalledCurrent, ExecutablePath |
    Format-Table Key, Exists, IsInstalledCurrent, InitialTooltip, ExecutablePath -AutoSize

if (-not $staleEntries) {
    Write-Host "No stale VM Manager tray icon entries were found."
    return
}

if (-not $Apply) {
    Write-Host ""
    Write-Host "Dry run only. Re-run with -Apply to remove the stale entries shown below:"
    $staleEntries | Format-Table Key, Exists, InitialTooltip, ExecutablePath -AutoSize
    return
}

foreach ($entry in $staleEntries) {
    Remove-Item -LiteralPath $entry.RegistryPath -Force
    Write-Host "Removed stale tray icon entry $($entry.Key)."
}

if ($RestartExplorer) {
    Write-Host "Restarting Windows Explorer so taskbar settings refresh."
    Stop-Process -Name explorer -Force
}
