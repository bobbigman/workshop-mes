@echo off
chcp 65001 >nul
title 一键新建账套
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0一键新建账套.ps1"
