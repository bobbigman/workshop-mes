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

rem ---------- if port 8080 is listening, do not start another copy ----------
set "PORT_PID="
for /f "tokens=5" %%a in ('netstat -ano ^| findstr /C:":8080 " ^| findstr /C:"LISTENING"') do (
  if not defined PORT_PID set "PORT_PID=%%a"
)

set "PORT_NAME="
if defined PORT_PID (
  for /f "tokens=1 delims=," %%b in ('tasklist /FI "PID eq %PORT_PID%" /NH /FO CSV 2^>nul') do set "PORT_NAME=%%~b"
)

if defined PORT_PID (
  call :port_in_use
  echo.
  pause
  exit /b 0
)
rem ----------------------------------------------------------

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
goto :eof

:port_in_use
rem Chinese hint is base64 so cmd code page 65001 does not eat the next line.
powershell -NoProfile -ExecutionPolicy Bypass -Command "$t=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('W+aPkOekul0g56uv5Y+jIDgwODAg5bey6KKr5Y2g55So77yM5LiN6ZyA6KaB5YaN5ZCv5Yqo5LiA5Liq44CCDQoNCiAgICAgICAg5Y2g55So6L+b56iL77yae3tOQU1FfX3vvIhQSUQ9e3tQSUR9fe+8iQ0KICAgICAgICDnm7TmjqXnlKjlsLHooYzvvJpodHRwOi8vbG9jYWxob3N0OjgwODAvDQoNCiAgICAgICAg5oOz6YeN5ZCv55qE6K+d77ya5YWI5Y+M5Ye7IHN0b3DvvIznrYnnqpflj6PmmL7npLogU3RvcHBlZO+8jOWGjeWPjOWHu+acrOiEmuacrOOAgg0KICAgICAgICDlpoLmnpzkuIrpnaLpgqPkuKrov5vnqIvlkI3kuI3mmK8gV29ya3Nob3BNZXMuZXhl77yM6K+05piO5piv5Yir55qE56iL5bqP5Y2g5LqGIDgwODDvvIwNCiAgICAgICAg6KaB5YWI5oqK5a6D5YWz5o6J77yM5YaN5Y+M5Ye75pys6ISa5pys44CCDQo=')); $t=$t.Replace('{{NAME}}',$env:PORT_NAME).Replace('{{PID}}',$env:PORT_PID); Write-Output $t"
goto :eof
