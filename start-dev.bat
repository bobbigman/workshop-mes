@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

echo ========================================
echo   车间管理系统 - 一键启动开发环境
echo ========================================
echo.

:: 若已有后端在跑，先关掉，避免锁住 WorkshopMes.exe
echo [1/3] 清理旧进程（端口 8080 / WorkshopMes）...
for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":8080" ^| findstr "LISTENING"') do (
  taskkill /F /T /PID %%p >nul 2>&1
)
taskkill /F /T /IM WorkshopMes.exe >nul 2>&1
taskkill /F /FI "WINDOWTITLE eq MES-后端*" >nul 2>&1
taskkill /F /FI "WINDOWTITLE eq *dotnet run*" >nul 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-CimInstance Win32_Process -Filter \"Name='dotnet.exe'\" | Where-Object { $_.CommandLine -match 'dotnet\s+run' -and $_.CommandLine -match '8080' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }"
set /a WAIT_N=0
:wait_unlock
powershell -NoProfile -ExecutionPolicy Bypass -Command "try{$f=[IO.File]::Open('%~dp0backend\bin\Debug\net8.0\WorkshopMes.exe','Open','ReadWrite','None');$f.Close();exit 0}catch{exit 1}"
if not errorlevel 1 goto unlocked
set /a WAIT_N+=1
if %WAIT_N% GEQ 15 goto still_locked
timeout /t 1 /nobreak >nul
goto wait_unlock
:still_locked
echo.
echo [失败] 旧的后端还在运行，文件被占用，无法重新生成。
echo        请先关掉标题里带 dotnet run 的黑色窗口，再双击本快捷方式。
echo        若窗口关不掉，右键 stop-dev.bat，选「以管理员身份运行」。
echo.
pause
exit /b 1
:unlocked

:: 前端依赖
if not exist "frontend\node_modules\" (
  echo [提示] 首次运行，正在 npm install ...
  pushd frontend
  call npm install
  if errorlevel 1 (
    echo npm install 失败，请检查 Node.js 是否已安装。
    pause
    exit /b 1
  )
  popd
)

echo [2/3] 启动后端 https://localhost:8080 ...
start "MES-后端" cmd /k "cd /d "%~dp0backend" && title MES-后端 && dotnet run"

echo [3/3] 启动前端 https://localhost:5173 ...
start "MES-前端" cmd /k "cd /d "%~dp0frontend" && title MES-前端 && npm run dev"

echo.
echo 等待服务起来后自动打开浏览器...
timeout /t 8 /nobreak >nul
start "" "https://localhost:5173"

echo.
echo 已启动：
echo   后端  https://localhost:8080
echo   前端  https://localhost:5173
echo   演示  工厂 F001 / admin 或 zhangchu / Admin123
echo.
echo 提示：要用 Visual Studio 生成时，先双击 stop-dev.bat 停掉后端。
echo.
pause
