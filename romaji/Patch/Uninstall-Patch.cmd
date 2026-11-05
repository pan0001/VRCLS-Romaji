@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup.ps1" -Uninstall
if errorlevel 1 (pause & exit /b 1)
pause
