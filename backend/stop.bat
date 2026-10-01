@echo off
chcp 65001 >nul
cd /d "%~dp0"
title WorkshopMes Stop

echo ========================================
echo   WorkshopMes Stop
echo ========================================
echo.
echo Folder: %CD%
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$dir = [System.IO.Path]::GetFullPath('%CD%');" ^
  "$list = Get-CimInstance Win32_Process -Filter \"Name='WorkshopMes.exe'\" |" ^
  "  Where-Object { $_.ExecutablePath -and ([System.IO.Path]::GetFullPath($_.ExecutablePath).StartsWith($dir, [StringComparison]::OrdinalIgnoreCase)) };" ^
  "if (-not $list) { Write-Host '[OK] No WorkshopMes.exe running in this folder'; exit 0 };" ^
  "$list | ForEach-Object { Write-Host ('Stopping PID ' + $_.ProcessId + ' ...'); Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue };" ^
  "Start-Sleep -Seconds 1;" ^
  "$left = Get-CimInstance Win32_Process -Filter \"Name='WorkshopMes.exe'\" |" ^
  "  Where-Object { $_.ExecutablePath -and ([System.IO.Path]::GetFullPath($_.ExecutablePath).StartsWith($dir, [StringComparison]::OrdinalIgnoreCase)) };" ^
  "if ($left) { Write-Host '[FAIL] Still running. End it in Task Manager.'; exit 1 }" ^
  "else { Write-Host '[OK] Stopped.' }"

if errorlevel 1 (
  echo.
  pause
  exit /b 1
)

echo.
pause