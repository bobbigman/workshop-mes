# ============================================================
# 车间小工单 · cpolar 隧道一键同步（服务模式版 v2）
# 背景：cpolar 已注册为 Windows 服务（开机自启，自动转发 8080），
#       不需要手动开 cpolar 窗口。免费版域名每次服务重启会变，
#       本脚本负责：读服务日志拿当前域名 -> 改 appsettings.json
#       -> 重启后端 -> 验证 -> 打印扣子/方舟要填的新地址。
# 用法：双击 restart-tunnel.bat
# ============================================================

$ErrorActionPreference = 'Stop'
$script:Root    = Split-Path -Parent $MyInvocation.MyCommand.Path
$backendDir     = Join-Path $Root 'backend'
$cfgPath        = Join-Path $backendDir 'appsettings.json'
$logDir         = Join-Path $Root 'logs'
$backendLog     = Join-Path $logDir 'backend.log'
$cpolarLogDir   = Join-Path $env:USERPROFILE '.cpolar\logs'

New-Item -ItemType Directory -Force -Path $logDir | Out-Null

# ---------- 工具函数 ----------
function Stop-ProcessOnPort([int]$port) {
    $lines = netstat -ano | Select-String ":$port" | Select-String 'LISTENING'
    foreach ($line in $lines) {
        $tokens = ($line.ToString().Trim() -split '\s+')
        $pidStr = $tokens[$tokens.Count - 1]
        if ($pidStr -match '^\d+$') {
            cmd /c "taskkill /F /PID $pidStr >nul 2>&1" | Out-Null
        }
    }
}

function Get-CpolarServiceStatus {
    $q = sc.exe query cpolar 2>&1 | Out-String
    if ($q -match 'STATE\s*:\s*4\s+RUNNING') { return 'RUNNING' }
    if ($q -match 'STATE\s*:\s*1\s+STOPPED') { return 'STOPPED' }
    return 'UNKNOWN'
}

# 从 cpolar 服务日志里抓最后一个 "Tunnel established at https://..."
function Get-CpolarCurrentUrl {
    $today = Get-Date -Format 'yyyyMMdd'
    $logs = @(Get-ChildItem $cpolarLogDir -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "cpolar_service.log.$today*" -and $_.Name -notlike '*master*' })
    if ($logs.Count -eq 0) { $logs = @(Get-ChildItem $cpolarLogDir -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like 'cpolar_service.log.*' -and $_.Name -notlike '*master*' }) }
    if ($logs.Count -eq 0) { return $null }
    $latest = $logs | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $line = Get-Content $latest.FullName -ErrorAction SilentlyContinue |
        Where-Object { $_ -match 'Tunnel established at (https://[a-zA-Z0-9.\-]+\.cpolar\.(?:top|cn))' } |
        Select-Object -Last 1
    if (-not $line) { return $null }
    if ($line -match 'Tunnel established at (https://[a-zA-Z0-9.\-]+\.cpolar\.(?:top|cn))') {
        return $Matches[1].TrimEnd('/')
    }
    return $null
}

# ---------- 1/5 停旧后端进程 ----------
Write-Host '[1/5] 停旧后端（8080 / WorkshopMes）...' -ForegroundColor Cyan
Stop-ProcessOnPort 8080
cmd /c "taskkill /F /IM WorkshopMes.exe >nul 2>&1" | Out-Null
Start-Sleep -Seconds 2

# ---------- 2/5 确保 cpolar 服务在跑 ----------
Write-Host '[2/5] 检查 cpolar 服务...' -ForegroundColor Cyan
$svc = Get-CpolarServiceStatus
if ($svc -eq 'RUNNING') {
    Write-Host '  cpolar 服务运行中，直接用当前隧道。' -ForegroundColor Green
} elseif ($svc -eq 'STOPPED') {
    Write-Host '  cpolar 服务未运行，尝试启动...' -ForegroundColor Yellow
    cmd /c "sc start cpolar" | Out-Null
    Start-Sleep -Seconds 8
    if ((Get-CpolarServiceStatus) -ne 'RUNNING') {
        throw 'cpolar 服务启动失败（可能需要管理员权限）。请右键本脚本"以管理员身份运行"，或手动启动服务。'
    }
    Write-Host '  cpolar 服务已启动。' -ForegroundColor Green
} else {
    throw '无法读取 cpolar 服务状态，请检查服务是否安装。'
}

# ---------- 3/5 从服务日志抓当前域名 ----------
Write-Host '[3/5] 从服务日志读取当前隧道域名（最多 30 秒）...' -ForegroundColor Cyan
$newBase = $null
for ($i = 0; $i -lt 30; $i++) {
    $newBase = Get-CpolarCurrentUrl
    if ($newBase) { break }
    Start-Sleep -Seconds 1
}
if (-not $newBase) {
    throw "没读到隧道域名。请打开服务日志目录确认：$cpolarLogDir"
}
Write-Host "  当前隧道域名: $newBase" -ForegroundColor Green

# ---------- 4/5 更新 appsettings.json ----------
Write-Host '[4/5] 更新 appsettings.json 的 Mcp:AuthBaseUrl ...' -ForegroundColor Cyan
$raw = Get-Content $cfgPath -Raw -Encoding UTF8

$tokenMatch = [regex]::Match($raw, '"Token"\s*:\s*"([^"]+)"')
$mcpToken = if ($tokenMatch.Success) { $tokenMatch.Groups[1].Value } else { 'laohu-demo-2026' }

$m = [regex]::Match($raw, '"AuthBaseUrl"\s*:\s*"([^"]+)"')
if (-not $m.Success) { throw 'appsettings.json 里没找到 AuthBaseUrl，请手动检查该文件。' }

if ($m.Groups[1].Value -eq $newBase) {
    Write-Host '  AuthBaseUrl 已是当前隧道域名，无需更新。' -ForegroundColor Green
} else {
    $newRaw = [regex]::Replace($raw, '("AuthBaseUrl"\s*:\s*")[^"]+(")', "`${1}$newBase`${2}")
    Copy-Item $cfgPath "$cfgPath.bak" -Force
    [IO.File]::WriteAllText($cfgPath, $newRaw, (New-Object System.Text.UTF8Encoding $false))
    Write-Host '  已备份到 appsettings.json.bak，AuthBaseUrl 已更新。' -ForegroundColor Green
}

# ---------- 5/5 启动后端并验证 ----------
Write-Host '[5/5] 启动后端 dotnet run（http://localhost:8080）...' -ForegroundColor Cyan
Remove-Item $backendLog -ErrorAction SilentlyContinue
Remove-Item "$backendLog.err" -ErrorAction SilentlyContinue
Start-Process -FilePath 'cmd.exe' `
    -ArgumentList '/k', "cd /d `"$backendDir`" && dotnet run" `
    -RedirectStandardOutput $backendLog `
    -RedirectStandardError "$backendLog.err" `
    -WindowStyle Minimized

$mcpOk = $false
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 1
    try {
        Invoke-WebRequest -Uri 'http://localhost:8080/mcp' -Method POST `
            -Body '{}' -ContentType 'application/json' `
            -Headers @{ Authorization = "Bearer $mcpToken" } `
            -UseBasicParsing -TimeoutSec 3 | Out-Null
    } catch {
        if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 400) { $mcpOk = $true; break }
        if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 401) { $mcpOk = $true; break }
    }
}

Write-Host ''
Write-Host '====================== 结果 ======================' -ForegroundColor Yellow
if ($mcpOk) {
    Write-Host '  [OK] 本机 MCP 服务已就绪：http://localhost:8080/mcp' -ForegroundColor Green
} else {
    Write-Host '  [!!] 后端可能还没起来，请打开 logs\backend.log 查看。' -ForegroundColor Red
}
Write-Host ''
Write-Host '  新的公网 MCP 地址:' -ForegroundColor White
Write-Host "    $newBase/mcp" -ForegroundColor Green
Write-Host ''
Write-Host '  老胡极简报工牛牛牛，这个MPC, JSON报文变了，请帮我更改，不要新建一个:' -ForegroundColor White
Write-Host '{' -ForegroundColor Green
Write-Host '  "mcpServers": {' -ForegroundColor Green
Write-Host '    "laohu-workshop": {' -ForegroundColor Green
Write-Host '      "type": "streamable-http",' -ForegroundColor Green
Write-Host "      `"url`": `"$newBase/mcp`"," -ForegroundColor Green
Write-Host '      "headers": {' -ForegroundColor Green
Write-Host "        `"Authorization`": `"Bearer $mcpToken`"" -ForegroundColor Green
Write-Host '      }' -ForegroundColor Green
Write-Host '    }' -ForegroundColor Green
Write-Host '  }' -ForegroundColor Green
Write-Host '}' -ForegroundColor Green
Write-Host ''
Write-Host '  下一步（网页操作，脚本代不了）:' -ForegroundColor Yellow
Write-Host '    1. 扣子：删除旧 MCP -> 粘贴上面 JSON 重建 -> 挂回 Agent'
Write-Host '    2. 微信发「查工单」验收'
Write-Host '    3. 不要把 token 拼在 url 后面，Token 已放 Header（上面 JSON 里）'
Write-Host '==================================================' -ForegroundColor Yellow
Write-Host ''
Write-Host '提示：cpolar 是 Windows 服务（开机自启），不用再手动开窗口。' -ForegroundColor DarkGray
Write-Host '      如果之前手动开过 cpolar 窗口，请手动关掉，统一走服务。' -ForegroundColor DarkGray
