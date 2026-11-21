@echo off
rem Usa questo solo se Video2All.exe viene bloccato dall antivirus
start "" powershell -NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0Video2All.ps1"
