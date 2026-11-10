@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

echo ========================================
echo   车间管理系统 - 一键重启后端
echo ========================================
echo.
echo 只重启后端（8080），前端 5173 尽量不动。
echo.

echo [1/2] 停止旧后端...
for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":8080" ^| findstr "LISTENING"') do (
  taskkill /F /T /PID %%p >nul 2>&1
)
taskkill /F /T /IM WorkshopMes.exe >nul 2>&1
taskkill /F /FI "WINDOWTITLE eq MES-dev*" >nul 2>&1
taskkill /F /FI "WINDOWTITLE eq MES-后端*" >nul 2>&1
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
echo [失败] 旧的后端还在运行，文件被占用。
echo        请先关掉标题 MES-dev 或 MES-后端 的黑色窗口，或右键 stop-dev.bat 以管理员身份运行。
echo.
pause
exit /b 1
:unlocked

echo [2/2] 启动后端 https://localhost:8080 ...
rem 单独开一个后端窗：若原来用 start-dev 单窗，原窗会提示后端已退出；前端 5173 仍可继续用
start "MES-后端" cmd /k "cd /d "%~dp0backend" && title MES-后端 && dotnet run"

echo.
echo 已重启后端（dotnet run 会自动编译改动）。
echo   后端  https://localhost:8080
echo.

rem 前端没在跑时顺手拉起，避免只开后端却打开 5173 连不上
netstat -ano | findstr ":5173" | findstr "LISTENING" >nul
if errorlevel 1 (
  echo [附] 前端 5173 没在跑，正在本机后台启动...
  if not exist "frontend\node_modules\" (
    pushd frontend
    call npm install
    popd
  )
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\start-frontend-hidden.ps1"
  echo   前端  https://localhost:5173  （等几秒再刷新）
) else (
  echo   前端  https://localhost:5173  （本来就在跑，未动）
)
echo.
echo 提示：整套开发环境（一个窗口）请用 start-dev.bat / stop-dev.bat。
echo   若刚才是 start-dev 单窗，原「MES-开发」可关掉；后端已在「MES-后端」窗。
echo.
pause
