@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
rem Cleanup title must NOT be MES-dev, or taskkill may kill this window
title MES-starting

echo ========================================
echo   Workshop MES - start-dev
echo ========================================
echo.

echo [1/3] cleanup ports 8080 / 5173 / WorkshopMes ...
for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":8080" ^| findstr "LISTENING"') do (
  taskkill /F /T /PID %%p >nul 2>&1
)
for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":5173" ^| findstr "LISTENING"') do (
  taskkill /F /T /PID %%p >nul 2>&1
)
taskkill /F /T /IM WorkshopMes.exe >nul 2>&1
taskkill /F /FI "WINDOWTITLE eq MES-dev*" >nul 2>&1
taskkill /F /FI "WINDOWTITLE eq *dotnet run*" >nul 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\kill-dotnet-run-8080.ps1"

set /a WAIT_N=0
:wait_unlock
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\test-exe-unlocked.ps1" -Path "%~dp0backend\bin\Debug\net8.0\WorkshopMes.exe"
if not errorlevel 1 goto unlocked
set /a WAIT_N+=1
if %WAIT_N% GEQ 15 goto still_locked
timeout /t 1 /nobreak >nul
goto wait_unlock
:still_locked
echo.
echo [FAIL] backend exe still locked.
echo        Close window titled MES-dev, then retry.
echo        If it will not close, right-click stop-dev.bat - Run as administrator.
echo.
pause
exit /b 1
:unlocked
title MES-dev

if not exist "frontend\node_modules\" (
  echo [info] first run, npm install ...
  pushd frontend
  call npm install
  if errorlevel 1 (
    echo npm install failed. Check Node.js install.
    pause
    exit /b 1
  )
  popd
)

set "LAN_IP="
for /f "usebackq delims=" %%i in (`powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\get-lan-ip.ps1"`) do set "LAN_IP=%%i"
if "%LAN_IP%"=="" set "LAN_IP=127.0.0.1"
set "FRONT_URL=https://%LAN_IP%:5173"

echo [2/3] start frontend https://localhost:5173  hidden, no extra window
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\start-frontend-hidden.ps1"

echo [3/3] start backend https://localhost:8080  keep this window open
echo.
echo waiting for backend :8080 then open browser...
echo   LAN url: %FRONT_URL%

start /b powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\wait-backend-open-browser.ps1" -Url "%FRONT_URL%"

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\print-start-dev-banner.ps1" -Mode tips -FrontUrl "%FRONT_URL%"

pushd backend
dotnet run
set EXITCODE=%ERRORLEVEL%
popd

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\print-start-dev-banner.ps1" -Mode exit -ExitCode %EXITCODE%
pause
exit /b %EXITCODE%
