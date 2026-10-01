@echo off
title 一键新建账套

rem ============ 写死配置（要改就改这里） ============
set BASE=https://localhost:8080
set CUR_FACTORY=F001
set CUR_ACCOUNT=admin
set CUR_PASSWORD=Admin123
set NEW_CODE=CJ002
set NEW_NAME=新厨具五金厂
rem ================================================

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0一键新建账套.ps1" -Base "%BASE%" -CurFactory "%CUR_FACTORY%" -CurAccount "%CUR_ACCOUNT%" -CurPassword "%CUR_PASSWORD%" -NewCode "%NEW_CODE%" -NewName "%NEW_NAME%"

pause
