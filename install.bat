@echo off
REM One-click installer for the HEAVYPOLY Blender config.
REM Double-click this file. It copies config\ and scripts\ into your Blender
REM user-config folder and records what it wrote, so uninstall.bat can undo it.
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\heavypoly_setup.ps1" -Action install -Interactive
echo.
pause
