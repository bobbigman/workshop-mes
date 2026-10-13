@echo off
chcp 65001 >nul
cd /d "%~dp0"

echo ========================================
echo   车间管理系统 - 停止开发环境
echo ========================================
echo.

echo 停止后端（WorkshopMes / 8080）...
for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":8080" ^| findstr "LISTENING"') do (
  taskkill /F /T /PID %%p >nul 2>&1
)
taskkill /F /T /IM WorkshopMes.exe >nul 2>&1
taskkill /F /FI "WINDOWTITLE eq MES-后端*" >nul 2>&1
taskkill /F /FI "WINDOWTITLE eq *dotnet run*" >nul 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-CimInstance Win32_Process -Filter \"Name='dotnet.exe'\" | Where-Object { $_.CommandLine -match 'dotnet\s+run' -and $_.CommandLine -match '8080' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }"

set /a WAIT_N=0
:wait_backend
tasklist /FI "IMAGENAME eq WorkshopMes.exe" | find /I "WorkshopMes.exe" >nul
if errorlevel 1 goto backend_gone
set /a WAIT_N+=1
if %WAIT_N% GEQ 15 goto backend_stuck
timeout /t 1 /nobreak >nul
goto wait_backend
:backend_stuck
echo.
echo [失败] 后端还在运行，普通权限停不掉。
echo        请右键本脚本，选「以管理员身份运行」。
echo.
pause
exit /b 1
:backend_gone

echo 停止前端（Vite / 5173）...
for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":5173" ^| findstr "LISTENING"') do (
  taskkill /F /T /PID %%p >nul 2>&1
)
taskkill /F /FI "WINDOWTITLE eq MES-前端*" >nul 2>&1

echo.
echo 已停止。现在可以用 Visual Studio 生成/F5。
echo.
pause
