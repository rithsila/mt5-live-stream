# SSH runs in session 0. Capture must run in the MT5 user's desktop session.
# This task does not restart MT5, change trading settings, or disconnect RDP.
[CmdletBinding()]
param(
    [ValidateSet('info', 'capture', 'bench')]
    [string]$Command = 'info',
    [ValidatePattern('^[A-Za-z0-9_-]+$')]
    [string]$Label = 'desktop-probe',
    [ValidateRange(1, 600)]
    [int]$Seconds = 120,
    [ValidateSet('gdi-screen', 'gdi-window', 'printwindow', 'wgc', 'chartshot')]
    [string]$Method = 'chartshot',
    [string]$DesktopUser = $env:USERNAME
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'CaptureSpike.exe'
if (!(Test-Path $exe)) { throw "CaptureSpike.exe missing: $exe" }
$arguments = "$Command --label $Label --report-root `"$root\reports`""
if ($Command -eq 'capture') { $arguments += " --duration $Seconds --delay 0" }
if ($Command -eq 'bench') { $arguments += " --method $Method --seconds $Seconds --delay 0" }
$name = "SafetyScore-Spike-$Label"
if (Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue) {
    throw "Task $name already exists. Use a new label to keep the previous run."
}
$action = New-ScheduledTaskAction -Execute $exe -Argument $arguments -WorkingDirectory $root
$principal = New-ScheduledTaskPrincipal -UserId $DesktopUser -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 90)
Register-ScheduledTask -TaskName $name -Action $action -Principal $principal -Settings $settings | Out-Null
Start-ScheduledTask -TaskName $name
Write-Host "Started $name in the logged-on desktop. Reports: $root\reports"
Write-Host "Check: Get-ScheduledTaskInfo -TaskName '$name'"
Write-Host "After it finishes: Unregister-ScheduledTask -TaskName '$name' -Confirm:`$false"
