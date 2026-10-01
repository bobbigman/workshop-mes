@echo off
chcp 65001 >nul
cd /d "%~dp0"
title WorkshopMes Start

echo ========================================
echo   WorkshopMes Start
echo ========================================
echo.

if not exist "WorkshopMes.exe" (
  echo [FAIL] WorkshopMes.exe not found
  echo.
  pause
  exit /b 1
)

if not exist "coreclr.dll" (
  echo [FAIL] coreclr.dll missing. Runtime broken.
  echo Use full publish-win64 pack, or publish-repair-runtime.
  echo.
  pause
  exit /b 1
)

findstr /C:"includedFrameworks" "WorkshopMes.runtimeconfig.json" >nul 2>nul
if errorlevel 1 (
  echo [FAIL] WorkshopMes.runtimeconfig.json is wrong ^(framework-dependent^).
  echo Copy WorkshopMes.runtimeconfig.json from publish-repair-runtime or full pack.
  echo.
  pause
  exit /b 1
)

echo [OK] Folder: %CD%
echo [OK] URL: http://0.0.0.0:8080
echo [OK] Local: http://localhost:8080/
echo [OK] Logs: %CD%\logs\
echo.
echo Starting WorkshopMes.exe ...
echo Keep this window open. Close = stop.
echo ----------------------------------------
echo.

set ASPNETCORE_URLS=http://0.0.0.0:8080
WorkshopMes.exe
set EXITCODE=%ERRORLEVEL%

echo.
echo ----------------------------------------
if not "%EXITCODE%"=="0" (
  echo [FAIL] Exit code=%EXITCODE%
  echo Check logs\mes-error-*.log
) else (
  echo [OK] Process exited normally
)
echo.
pause