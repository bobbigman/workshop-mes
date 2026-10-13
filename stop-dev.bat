@echo off
chcp 65001 >nul
cd /d "%~dp0"

echo ========================================
echo   车间管理系统 - 停止开发环境
echo ========================================
echo.

echo 停止后端（WorkshopMes / 8080）...
for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":8080" ^| findstr "LISTENING"') do (
  taskkill /F /PID %%p >nul 2>&1
)
taskkill /F /IM WorkshopMes.exe >nul 2>&1

echo 停止前端（Vite / 5173）...
for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":5173" ^| findstr "LISTENING"') do (
  taskkill /F /PID %%p >nul 2>&1
)

:: 关掉我们打开的标题窗口（若还在）
taskkill /F /FI "WINDOWTITLE eq MES-后端*" >nul 2>&1
taskkill /F /FI "WINDOWTITLE eq MES-前端*" >nul 2>&1

echo.
echo 已停止。现在可以用 Visual Studio 生成/F5。
echo.
pause
