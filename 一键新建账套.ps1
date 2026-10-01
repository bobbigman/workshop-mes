# -*- coding: utf-8 -*-
# 一键新建账套工具（给老沈用，双击 .bat 即可）
# 原理：登录现有任一账套拿管理员 token -> 调 POST /api/factories 新建账套（自动灌演示数据 + admin）
# 说明：本脚本只调用接口，不改任何代码；业务失败原因会原文打印。

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "   一键新建账套（工厂）工具" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

# 1) 后端地址
$Base = Read-Host "后端地址（回车用默认 http://localhost:8080）"
if ([string]::IsNullOrWhiteSpace($Base)) { $Base = "http://localhost:8080" }
$Base = $Base.TrimEnd('/')

# 2) 连通性自检（只读，不改数据）
Write-Host ""
Write-Host "正在检查后端是否可达 ..." -ForegroundColor Yellow
try {
    $probe = Invoke-WebRequest -Method GET -Uri "$Base/api/factories" -UseBasicParsing -TimeoutSec 8 -ErrorAction Stop
    if ($probe.StatusCode -eq 200) { Write-Host "[OK] 后端可达" -ForegroundColor Green }
} catch {
    Write-Host "[失败] 连不上后端（$($_.Exception.Message)）" -ForegroundColor Red
    Write-Host "请确认后端服务已启动、地址正确。" -ForegroundColor Yellow
    Read-Host "按回车退出"
    exit 1
}

# 3) 用现有账套的管理员登录，拿 token
Write-Host ""
Write-Host "---- 第 1 步：用现有账套的管理员登录（只是拿通行证，不会动任何数据）----" -ForegroundColor Cyan
$curCode  = Read-Host "现有账套的工厂代码（如 F001）"
$curAcct  = Read-Host "管理员账号（默认 admin）"
if ([string]::IsNullOrWhiteSpace($curAcct)) { $curAcct = "admin" }
$curPwd   = Read-Host "管理员密码（输入不显示）" -AsSecureString
$curPwdT  = [Runtime.InteropServices.Marshal]::PtrToStringUni([Runtime.InteropServices.Marshal]::SecureStringToBSTR($curPwd))

function Invoke-ApiJson {
    param([string]$Method, [string]$Path, [hashtable]$Body, [string]$Token)
    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    $bytes = $null
    if ($Body) { $bytes = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Compress)) }
    try {
        $r = Invoke-WebRequest -Method $Method -Uri "$Base$Path" -Headers $headers `
             -ContentType 'application/json; charset=utf-8' -Body $bytes -UseBasicParsing -ErrorAction Stop
        $j = $r.Content | ConvertFrom-Json
        return [pscustomobject]@{ Code = $j.code; Msg = $j.msg; Data = $j.data; Http = [int]$r.StatusCode }
    } catch {
        $resp = $_.Exception.Response
        if ($resp) {
            try {
                $rd = New-Object IO.StreamReader($resp.GetResponseStream())
                $body = $rd.ReadToEnd()
                $j = $body | ConvertFrom-Json
                return [pscustomobject]@{ Code = $j.code; Msg = $j.msg; Data = $j.data; Http = [int]$resp.StatusCode }
            } catch {
                return [pscustomobject]@{ Code = 0; Msg = $body; Data = $null; Http = [int]$resp.StatusCode }
            }
        }
        return [pscustomobject]@{ Code = 0; Msg = $_.Exception.Message; Data = $null; Http = 0 }
    }
}

$login = Invoke-ApiJson -Method 'POST' -Path '/api/auth/login' `
    -Body @{ FactoryCode = $curCode; Account = $curAcct; Password = $curPwdT }
if ($login.Code -ne 0 -or $null -eq $login.Data.token) {
    Write-Host "[失败] 登录未成功：$($login.Msg)" -ForegroundColor Red
    Write-Host "请检查厂代码 / 账号 / 密码，或该账号是否停用。" -ForegroundColor Yellow
    Read-Host "按回车退出"
    exit 1
}
$token = $login.Data.token
Write-Host "[OK] 登录成功，已取得管理员通行证（$($login.Data.factoryName)）" -ForegroundColor Green

# 4) 输入新账套
Write-Host ""
Write-Host "---- 第 2 步：填新账套信息（会自动灌演示数据 + admin 账号）----" -ForegroundColor Cyan
$newCode = Read-Host "新工厂代码（不能与已有重复，如 CJ001）"
$newName = Read-Host "新工厂名称（如 厨具五金厂）"
if ([string]::IsNullOrWhiteSpace($newCode) -or [string]::IsNullOrWhiteSpace($newName)) {
    Write-Host "[失败] 工厂代码和名称都不能为空" -ForegroundColor Red
    Read-Host "按回车退出"; exit 1
}

# 5) 确认后建厂
Write-Host ""
Write-Host "将新建账套：代码=$newCode  名称=$newName" -ForegroundColor Yellow
$confirm = Read-Host "确认执行？(Y/N，回车=Y)"
if ($confirm -ne 'Y' -and $confirm -ne 'y' -and $confirm -ne '') {
    Write-Host "已取消，未做任何改动。" -ForegroundColor Yellow
    Read-Host "按回车退出"; exit 0
}

$create = Invoke-ApiJson -Method 'POST' -Path '/api/factories' `
    -Body @{ FactoryCode = $newCode; FactoryName = $newName } -Token $token
if ($create.Code -eq 0) {
    Write-Host ""
    Write-Host "==========================================" -ForegroundColor Green
    Write-Host " [成功] 账套已建好！" -ForegroundColor Green
    Write-Host "   代码：$newCode" -ForegroundColor Green
    Write-Host "   名称：$newName" -ForegroundColor Green
    Write-Host "==========================================" -ForegroundColor Green
    Write-Host "接下来："
    Write-Host "  1. 刷新系统登录页，「账套 / 工厂」下拉里会出现这家厂"
    Write-Host "  2. 用 admin / Admin123 登录进去，就是这家新厂的完整后台"
    Write-Host "  3. 演示数据记得按客户实际改（部门、工序、产品、工单都能在新厂后台配）"
} else {
    Write-Host ""
    Write-Host "[失败] 建账套未成功：$($create.Msg)" -ForegroundColor Red
    if ($create.Msg -match '已存在') {
        Write-Host "提示：工厂代码已被人用过，换个新代码再试即可。" -ForegroundColor Yellow
    }
    Read-Host "按回车退出"; exit 1
}

Read-Host "按回车退出"
