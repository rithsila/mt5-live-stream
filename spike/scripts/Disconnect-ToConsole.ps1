#Requires -RunAsAdministrator
param(
    # If > 0, the desktop locks itself this many seconds after the session
    # is moved to the console (for the auto-logon + lock test).
    [int]$LockAfterSeconds = 0
)

# Moves THIS session from RDP to the physical console. The desktop keeps
# rendering after the RDP window closes. Run it INSIDE the RDP session as
# Administrator. Your RDP window will close — that is the point.

$ErrorActionPreference = 'Stop'

$sessionId = [System.Diagnostics.Process]::GetCurrentProcess().SessionId
Write-Host "Current session id: $sessionId"

if ($LockAfterSeconds -gt 0) {
    Write-Host "A hidden helper will lock the desktop $LockAfterSeconds s after tscon runs."
    $cmd = "Start-Sleep -Seconds $LockAfterSeconds; rundll32.exe user32.dll,LockWorkStation"
    Start-Process -FilePath 'powershell.exe' `
        -ArgumentList '-NoProfile','-WindowStyle','Hidden','-Command',$cmd `
        -WindowStyle Hidden
}

Write-Host "Running: tscon.exe $sessionId /dest:console"
Write-Host "Your RDP window will now close. Reconnect later to see the result."
tscon.exe $sessionId /dest:console
