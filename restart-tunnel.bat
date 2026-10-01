@echo off
cd /d "%~dp0"
echo ========================================
echo   WorkshopMes - cpolar one-click restart
echo   Restart cpolar + update domain + restart backend
echo ========================================
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0restart-tunnel.ps1"
echo.
pause
