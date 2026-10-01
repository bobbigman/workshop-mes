@echo off
chcp 65001 >nul
cd /d "%~dp0"
title WorkshopMes apply-update

set TARGET=D:\WorkShop\backend\publish-win64

echo ========================================
echo   WorkshopMes apply-update（一键）
echo ========================================
echo.
echo Target: %TARGET%
echo 步骤：停进程 → 更新 wwwroot/dll → 启动
echo.

if not exist "%TARGET%\WorkshopMes.exe" goto NOEXE
if not exist "%TARGET%\coreclr.dll" goto NOCORE

echo [1/3] 停止目标目录中的 WorkshopMes.exe ...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$dir = [System.IO.Path]::GetFullPath('%TARGET%');" ^
  "$list = Get-CimInstance Win32_Process -Filter \"Name='WorkshopMes.exe'\" |" ^
  "  Where-Object { $_.ExecutablePath -and ([System.IO.Path]::GetFullPath($_.ExecutablePath).StartsWith($dir, [StringComparison]::OrdinalIgnoreCase)) };" ^
  "if (-not $list) { Write-Host '[OK] 未在跑，跳过停止'; exit 0 };" ^
  "$list | ForEach-Object { Write-Host ('Stopping PID ' + $_.ProcessId + ' ...'); Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue };" ^
  "Start-Sleep -Seconds 1;" ^
  "$left = Get-CimInstance Win32_Process -Filter \"Name='WorkshopMes.exe'\" |" ^
  "  Where-Object { $_.ExecutablePath -and ([System.IO.Path]::GetFullPath($_.ExecutablePath).StartsWith($dir, [StringComparison]::OrdinalIgnoreCase)) };" ^
  "if ($left) { Write-Host '[FAIL] 仍在运行，请任务管理器结束后再试'; exit 1 }" ^
  "else { Write-Host '[OK] 已停止' }"
if errorlevel 1 goto FAILSTOP

echo.
echo [2/3] 拷贝 wwwroot 与 dll ...
echo Iron rule: 不碰 appsettings / start.bat / stop.bat / exe / runtimeconfig / deps / certs / logs
robocopy "%~dp0wwwroot" "%TARGET%\wwwroot" /E /R:2 /W:1 /NFL /NDL /NJH /NJS /NC /NS
if errorlevel 8 goto FAILWWW

copy /Y "%~dp0*.dll" "%TARGET%\" >nul
if errorlevel 1 goto FAILCOPY

if exist "%~dp0web.config" copy /Y "%~dp0web.config" "%TARGET%\" >nul

echo [OK] 文件已更新: %TARGET%
echo.

echo [3/3] 用安装目录原有 start.bat 启动（新窗口）...
if not exist "%TARGET%\start.bat" goto NOSTART
start "WorkshopMes" /D "%TARGET%" cmd /k start.bat

echo.
echo [OK] 一键更新完成。新窗口应出现「已启动」。
echo 浏览器打开后请 Ctrl+F5 强刷。
echo.
goto END

:NOEXE
echo [FAIL] 找不到 %TARGET%\WorkshopMes.exe
goto END

:NOCORE
echo [FAIL] 找不到 coreclr.dll — 不是自包含安装目录
goto END

:FAILSTOP
echo [FAIL] 未能停止进程，未做任何覆盖
goto END

:FAILWWW
echo [FAIL] wwwroot 拷贝失败
goto END

:FAILCOPY
echo [FAIL] dll 拷贝失败（文件是否仍被占用？）
goto END

:NOSTART
echo [FAIL] 找不到 %TARGET%\start.bat（请用服务器原有那份，勿从开发机覆盖）
echo 文件已更新，请手工双击安装目录 start.bat
goto END

:END
pause
