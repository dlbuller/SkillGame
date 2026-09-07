# =====================================================================================
# SkillGame - Kiosk Watchdog
# Keeps the game running on the cabinet: launches SkillGame and relaunches it if it exits
# (crash, accidental close, Windows update popup dismissing it, etc.).
#
# HOW TO USE
#   1) Deploy the built app somewhere stable, e.g. C:\SkillGame\  (copy the bin\x64\Release
#      output there). Update $exe below to point at SkillGame.exe.
#   2) Set Windows to AUTO-LOGIN the cabinet user (netplwiz -> uncheck "must enter password",
#      or use Sysinternals Autologon).
#   3) Register this script to run at logon (see the schtasks command at the bottom), OR drop a
#      shortcut to it in shell:startup.
#   4) (Optional) disable sleep/screen-blanking:  powercfg /change monitor-timeout-ac 0
#
# Stop it by ending the task (or closing this window).
# =====================================================================================

$exe                  = "C:\SkillGame\SkillGame.exe"   # <-- set to your deployed exe path
$restartDelaySeconds  = 3
$log                  = Join-Path $env:LOCALAPPDATA "SkillGame\watchdog.log"

New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null
function Note($m) { $line = ("{0}  {1}" -f (Get-Date -Format s), $m); Write-Host $line; Add-Content -Path $log -Value $line }

Note "watchdog started; target = $exe"
while ($true) {
    if (-not (Test-Path $exe)) { Note "EXE not found: $exe (retrying in 10s)"; Start-Sleep 10; continue }
    try {
        $p = Start-Process -FilePath $exe -PassThru
        Note "launched SkillGame (pid $($p.Id))"
        $p.WaitForExit()
        Note "SkillGame exited (code $($p.ExitCode)); restarting in $restartDelaySeconds s"
    }
    catch { Note "launch failed: $($_.Exception.Message)" }
    Start-Sleep -Seconds $restartDelaySeconds
}

# ---------------------------------------------------------------------------------------
# Register to run at logon (run once, from an elevated PowerShell):
#
#   $action  = New-ScheduledTaskAction -Execute "powershell.exe" `
#                -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$PSCommandPath`""
#   $trigger = New-ScheduledTaskTrigger -AtLogOn
#   Register-ScheduledTask -TaskName "SkillGame Kiosk" -Action $action -Trigger $trigger `
#                -RunLevel Highest -Force
# ---------------------------------------------------------------------------------------
