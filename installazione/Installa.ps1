# Installazione per l'utente corrente: %LOCALAPPDATA%\Programs\Video2All
$ErrorActionPreference = 'Stop'
$src = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$dst = Join-Path $env:LOCALAPPDATA 'Programs\Video2All'
$name = 'Video2All'

Get-Process Video2All,Acquisizione -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host 'Chiudi Video2All prima di installare.' -ForegroundColor Yellow
    exit 1
}

# versione precedente col vecchio nome "Acquisizione Video": si rimuove (impostazioni e video restano)
$oldDst = Join-Path $env:LOCALAPPDATA 'Programs\AcquisizioneVideo'
if (Test-Path -LiteralPath $oldDst) { Remove-Item -LiteralPath $oldDst -Recurse -Force -ErrorAction SilentlyContinue }
foreach ($l in @((Join-Path ([Environment]::GetFolderPath('Programs')) 'Acquisizione Video.lnk'), (Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Acquisizione Video.lnk'))) {
    if (Test-Path -LiteralPath $l) { Remove-Item -LiteralPath $l -Force -ErrorAction SilentlyContinue }
}
Remove-Item -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AcquisizioneVideo' -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Installazione in $dst ..."
New-Item -ItemType Directory -Force -Path $dst | Out-Null
foreach ($item in 'Video2All.exe','ffmpeg.exe','Video2All.ps1','Avvia (alternativo).bat','LEGGIMI.txt','LICENZE.txt') {
    $p = Join-Path $src $item
    if (Test-Path -LiteralPath $p) { Copy-Item -LiteralPath $p -Destination $dst -Force }
}
foreach ($dir in 'sorgenti','installazione') {
    $p = Join-Path $src $dir
    $old = Join-Path $dst $dir
    # aggiornando si tolgono i sorgenti vecchi: l'avvio alternativo compila tutti i .cs della cartella
    if ((Test-Path -LiteralPath $old) -and ($dir -eq 'sorgenti')) { Remove-Item -LiteralPath $old -Recurse -Force -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath $p) { Copy-Item -LiteralPath $p -Destination $dst -Recurse -Force }
}
Unblock-File -Path (Join-Path $dst '*') -ErrorAction SilentlyContinue

$exe = Join-Path $dst 'Video2All.exe'
$shell = New-Object -ComObject WScript.Shell
$links = @(
    (Join-Path ([Environment]::GetFolderPath('Programs')) "$name.lnk"),
    (Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) "$name.lnk")
)
foreach ($l in $links) {
    $s = $shell.CreateShortcut($l)
    $s.TargetPath = $exe
    $s.WorkingDirectory = $dst
    $s.IconLocation = "$exe,0"
    $s.Save()
}

# voce in "App installate" di Windows
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Video2All'
New-Item -Path $key -Force | Out-Null
$un = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $dst 'installazione\Disinstalla.ps1') + '"'
Set-ItemProperty -Path $key -Name DisplayName -Value $name
Set-ItemProperty -Path $key -Name DisplayVersion -Value '6.1.0'
Set-ItemProperty -Path $key -Name Publisher -Value 'Video2All'
Set-ItemProperty -Path $key -Name DisplayIcon -Value $exe
Set-ItemProperty -Path $key -Name InstallLocation -Value $dst
Set-ItemProperty -Path $key -Name UninstallString -Value $un
Set-ItemProperty -Path $key -Name NoModify -Value 1 -Type DWord
Set-ItemProperty -Path $key -Name NoRepair -Value 1 -Type DWord
$size = [int]((Get-ChildItem $dst -Recurse | Measure-Object Length -Sum).Sum / 1KB)
Set-ItemProperty -Path $key -Name EstimatedSize -Value $size -Type DWord

Write-Host 'Fatto. Collegamenti creati nel menu Start e sul desktop.' -ForegroundColor Green
