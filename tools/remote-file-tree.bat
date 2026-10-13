@echo off
setlocal enabledelayedexpansion
chcp 65001 >nul
title Export File Tree

rem ============================================================
rem  Export file tree (names only, NO file contents) to a .txt
rem  Usage: copy this .bat to the remote server, edit SRC/OUT,
rem         then double-click to run.
rem ============================================================

rem ====== Edit these two lines ======
set "SRC=D:\WorkShop"
set "OUT=D:\WorkShop_file_tree.txt"
rem ==================================

if not exist "%SRC%" (
    echo [ERROR] Source directory not found: %SRC%
    echo Please edit SRC in this script and run again.
    pause
    exit /b 1
)

echo Generating file tree of %SRC% ...
> "%OUT%" echo Directory: %SRC%
>> "%OUT%" echo Generated at: %date% %time%
>> "%OUT%" echo ========================================
tree "%SRC%" /F /A >> "%OUT%"

echo.
echo Done. Output saved to: %OUT%
for /f %%i in ('findstr /R /N "^" "%OUT%" ^| find /c ":"') do echo Lines: %%i
echo.
pause
