# Disinstallazione: toglie programma, collegamenti e voce in "App installate".
# Le impostazioni (%APPDATA%\Video2All) e i video registrati NON vengono toccati.
$dst = Join-Path $env:LOCALAPPDATA 'Programs\Video2All'
$name = 'Video2All'
# se il programma è aperto (magari sta registrando) si chiede prima di chiuderlo
if (Get-Process Video2All,Acquisizione -ErrorAction SilentlyContinue) {
    $ws = New-Object -ComObject WScript.Shell
    $r = $ws.Popup("Video2All è aperto (potrebbe registrare). Chiuderlo e disinstallare?", 0, "Disinstallazione", 4 + 48)
    if ($r -ne 6) { exit 1 }
    Get-Process Video2All,Acquisizione -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
}
foreach ($l in (Join-Path ([Environment]::GetFolderPath('Programs')) "$name.lnk"), (Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) "$name.lnk")) {
    Remove-Item -LiteralPath $l -Force -ErrorAction SilentlyContinue
}
Remove-Item -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Video2All' -Recurse -Force -ErrorAction SilentlyContinue
# la cartella si cancella dopo la chiusura di questo script
Start-Process cmd.exe -ArgumentList '/c', "timeout /t 2 /nobreak >nul & rmdir /s /q `"$dst`"" -WindowStyle Hidden
Write-Host 'Video2All disinstallato. Impostazioni e video sono stati mantenuti.'
