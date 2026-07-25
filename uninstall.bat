@echo off
REM One-click uninstaller for the HEAVYPOLY Blender config.
REM Double-click this file. It reads the manifest written by install.bat and
REM removes ONLY the files that were installed, restoring anything it replaced.
REM Your own scripts in scripts\startup\ are left untouched.
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\heavypoly_setup.ps1" -Action uninstall -Interactive
echo.
pause
