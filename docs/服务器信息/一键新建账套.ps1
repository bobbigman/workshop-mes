# -*- coding: utf-8 -*-
# 一键新建账套（工厂）工具 —— 自动版：双击 .bat 即全自动执行，无需任何输入 / 确认
# 配置已移到同目录 .bat 顶部（set 变量），由 bat 作为参数传入本脚本；
# 本脚本也可直接带参数运行，缺省用下列默认值。
# 后端地址来自 start.bat：https://localhost:8080（HTTPS + 自签名证书，已自动信任）
# 流程：连后端自检 -> 用现有账套管理员登录拿 token -> 自动新建账套（灌演示数据 + admin）

param(
    [string]$Base        = "https://localhost:8080",  # 后端地址（与 start.bat 一致，HTTPS）
    [string]$CurFactory  = "F001",                    # 现有账套工厂代码（登录拿通行证用）
    [string]$CurAccount  = "admin",                   # 管理员账号
    [string]$CurPassword = "Admin123",                # 管理员密码
    [string]$NewCode     = "CJ002",                   # 要新建的工厂代码（现有 F001、CJ001 已被占用）
    [string]$NewName     = "新厨具五金厂"              # 要新建的工厂名称
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

function Write-Step([string]$t){ Write-Host ""; Write-Host "---- $t ----" -ForegroundColor Cyan }
function Write-Ok([string]$t){ Write-Host "[OK] $t" -ForegroundColor Green }
function Write-Fail([string]$t){ Write-Host "[失败] $t" -ForegroundColor Red }

Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "   一键新建账套（工厂）工具 - 自动版" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

# 信任后端自签名证书（HTTPS 必需）
# 注意：不能用 { $true } 这种脚本块做回调，PS 5.1 在后台线程调用它没有运行空间会报错。
# 这里用编译好的 .NET 委托（真实方法），任何线程都能跑。
Add-Type -TypeDefinition @"
namespace MESTrust {
  public static class Cert {
    public static System.Net.Security.RemoteCertificateValidationCallback GetHandler(){
      return new System.Net.Security.RemoteCertificateValidationCallback(Cert.All);
    }
    private static bool All(object s, System.Security.Cryptography.X509Certificates.X509Certificate c,
      System.Security.Cryptography.X509Certificates.X509Chain ch, System.Net.Security.SslPolicyErrors e){ return true; }
  }
}
"@ -ErrorAction Stop
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
[Net.ServicePointManager]::ServerCertificateValidationCallback = [MESTrust.Cert]::GetHandler()

# 1) 连通性自检（只读，不改数据）
Write-Step "自检：后端是否可达（$Base）"
try {
    $probe = Invoke-WebRequest -Method GET -Uri "$Base/api/factories" -UseBasicParsing -TimeoutSec 8 -ErrorAction Stop
    Write-Ok "后端可达（HTTP $($probe.StatusCode)）"
} catch {
    Write-Fail "连不上后端：$($_.Exception.Message)"
    Write-Host "请确认后端已启动：先双击「车间管理系统」里的 start 快捷方式，等窗口出现 https://localhost:8080 后再运行本工具。" -ForegroundColor Yellow
    exit 1
}

# 通用 API 调用（登录 / 建账套共用，错误原文返回）
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

# 2) 登录现有账套拿 token（只读，不碰数据）
Write-Step "第 1 步：用现有账套 $CurFactory / $CurAccount 登录"
$login = Invoke-ApiJson -Method 'POST' -Path '/api/auth/login' `
    -Body @{ FactoryCode = $CurFactory; Account = $CurAccount; Password = $CurPassword }
if ($login.Code -ne 0 -or $null -eq $login.Data.token) {
    Write-Fail "登录未成功：$($login.Msg)"
    Write-Host "请核对「写死配置」里的 CurFactory / CurAccount / CurPassword。" -ForegroundColor Yellow
    exit 1
}
$token = $login.Data.token
Write-Ok "登录成功，通行证已取得（$($login.Data.factoryName)）"

# 3) 自动新建账套（无需确认）
Write-Step "第 2 步：自动新建账套 代码=$NewCode 名称=$NewName"
$create = Invoke-ApiJson -Method 'POST' -Path '/api/factories' `
    -Body @{ FactoryCode = $NewCode; FactoryName = $NewName } -Token $token
if ($create.Code -eq 0) {
    Write-Host ""
    Write-Host "==========================================" -ForegroundColor Green
    Write-Host " [成功] 账套已建好！" -ForegroundColor Green
    Write-Host "   代码：$NewCode" -ForegroundColor Green
    Write-Host "   名称：$NewName" -ForegroundColor Green
    Write-Host "==========================================" -ForegroundColor Green
    Write-Host "接下来："
    Write-Host "  1. 刷新系统登录页，「账套 / 工厂」下拉里会出现这家厂"
    Write-Host "  2. 用 admin / Admin123 登录进去，就是这家新厂的完整后台"
    Write-Host "  3. 演示数据记得按客户实际改（部门、工序、产品、工单都能在新厂后台配）"
} else {
    Write-Fail "建账套未成功：$($create.Msg)"
    if ($create.Msg -match '已存在') {
        Write-Host "提示：工厂代码 $NewCode 已被占用，请改「写死配置」里的 NewCode 换个不重复的（现有：F001、CJ001）。" -ForegroundColor Yellow
    }
    exit 1
}

# 恢复证书校验回调（仅本进程生效，退出即复原）
[Net.ServicePointManager]::ServerCertificateValidationCallback = $null