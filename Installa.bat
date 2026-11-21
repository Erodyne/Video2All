@echo off
rem Installa Video2All per l'utente corrente (non servono permessi di amministratore)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0installazione\Installa.ps1"
pause
