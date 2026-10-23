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
            cmd /c "taskkill /F /T /PID $pidStr >nul 2>&1" | Out-Null
        }
    }
}

function Stop-DevBackend {
    Stop-ProcessOnPort 8080
    cmd /c "taskkill /F /T /IM WorkshopMes.exe >nul 2>&1" | Out-Null
    Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -match 'dotnet\s+run' -and $_.CommandLine -match '8080' } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    cmd /c "taskkill /F /FI `"WINDOWTITLE eq MES-后端*`" >nul 2>&1" | Out-Null
    cmd /c "taskkill /F /FI `"WINDOWTITLE eq *dotnet run*`" >nul 2>&1" | Out-Null
    # 判定"旧后端是否真占用"：看后端 exe 能否独占打开，而不是看进程名。
    # 杀不掉的空壳进程（线程0/句柄0/内存0）不锁文件，应放行；只有真占用文件才等。
    $exe = Join-Path $backendDir 'bin\Debug\net8.0\WorkshopMes.exe'
    for ($i = 0; $i -lt 15; $i++) {
        try {
            $f = [IO.File]::Open($exe, 'Open', 'ReadWrite', 'None')
            $f.Close()
            return   # 文件未被锁，旧后端已释放，放行
        } catch {
            # 文件仍被占用，继续等
        }
        Start-Sleep -Seconds 1
    }
    throw '旧后端仍占用 WorkshopMes.exe 文件（非空壳进程）。请先手动结束真正的后端进程，或右键 stop-dev.bat「以管理员身份运行」。'
}

function Get-CpolarServiceStatus {
    $q = sc.exe query cpolar 2>&1 | Out-String
    if ($q -match 'STATE\s*:\s*4\s+RUNNING') { return 'RUNNING' }
    if ($q -match 'STATE\s*:\s*1\s+STOPPED') { return 'STOPPED' }
    return 'UNKNOWN'
}

function Get-CpolarServiceLog {
    $today = Get-Date -Format 'yyyyMMdd'
    $logs = @(Get-ChildItem $cpolarLogDir -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "cpolar_service.log.$today*" -and $_.Name -notlike '*master*' })
    if ($logs.Count -eq 0) { $logs = @(Get-ChildItem $cpolarLogDir -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like 'cpolar_service.log.*' -and $_.Name -notlike '*master*' }) }
    if ($logs.Count -eq 0) { return $null }
    return ($logs | Sort-Object LastWriteTime -Descending | Select-Object -First 1)
}

# 隧道当前打到本机的地址（http://localhost:8080 或 https://localhost:8080）
function Get-CpolarLastLocalAddr {
    $latest = Get-CpolarServiceLog
    if (-not $latest) { return $null }
    $line = Get-Content $latest.FullName -ErrorAction SilentlyContinue |
        Where-Object { $_ -match 'LocalAddr\\":\\"https?://localhost:8080' } |
        Select-Object -Last 1
    if ($line -and $line -match 'LocalAddr\\":\\"(https?://localhost:8080)') {
        return $Matches[1]
    }
    return $null
}

# 从 cpolar 服务日志里抓最后一个 "Tunnel established at https://..."
# $notBefore：重启服务后只认这之后的新域名，避免读到重启前的旧行
function Get-CpolarCurrentUrl([datetime]$notBefore = ([datetime]'2000-01-01')) {
    $latest = Get-CpolarServiceLog
    if (-not $latest) { return $null }
    # 刚重启时新域名在文件末尾；平时只取最后一条，避免把几 MB 日志整份读进内存
    $recent = $notBefore -gt (Get-Date).AddHours(-1)
    if ($recent) {
        $lines = @(Get-Content $latest.FullName -Tail 2000 -ErrorAction SilentlyContinue)
    } else {
        $hit = Select-String -Path $latest.FullName -Pattern 'Tunnel established at https://' | Select-Object -Last 1
        $lines = @($(if ($hit) { $hit.Line } else { $null }))
    }
    $url = $null
    foreach ($line in $lines) {
        if ($line -match 'time="([^"]+)".*Tunnel established at (https://[a-zA-Z0-9.\-]+\.cpolar\.(?:top|cn))') {
            $t = [datetimeoffset]::Parse($Matches[1]).LocalDateTime
            if ($t -ge $notBefore) { $url = $Matches[2].TrimEnd('/') }
        }
    }
    return $url
}

# ---------- 1/5 停旧后端进程 ----------
Write-Host '[1/5] 停旧后端（8080 / WorkshopMes）...' -ForegroundColor Cyan
Stop-DevBackend

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

$urlNotBefore = [datetime]'2000-01-01'
$localAddr = Get-CpolarLastLocalAddr
if ($localAddr -and $localAddr -notlike 'https://*') {
    Write-Host "  隧道仍指向 $localAddr，重启 cpolar 以改走本地 https://localhost:8080 ..." -ForegroundColor Yellow
    Write-Host '  远程桌面隧道若连不上，网页隧道要等它超时后才启动，可能要 3～4 分钟。' -ForegroundColor DarkGray
    $urlNotBefore = Get-Date
    cmd /c "sc stop cpolar" | Out-Null
    for ($i = 0; $i -lt 20; $i++) {
        if ((Get-CpolarServiceStatus) -eq 'STOPPED') { break }
        Start-Sleep -Seconds 1
    }
    if ((Get-CpolarServiceStatus) -ne 'STOPPED') {
        throw 'cpolar 服务停不掉（可能需要管理员权限）。请右键 restart-tunnel.bat「以管理员身份运行」。'
    }
    cmd /c "sc start cpolar" | Out-Null
    Start-Sleep -Seconds 3
    if ((Get-CpolarServiceStatus) -ne 'RUNNING') {
        throw 'cpolar 服务重启失败（可能需要管理员权限）。请右键 restart-tunnel.bat「以管理员身份运行」。'
    }
    Write-Host '  cpolar 服务已重启。' -ForegroundColor Green
}

# ---------- 3/5 从服务日志抓当前域名 ----------
# 重启后网页隧道可能排在远程桌面后面，对方断开要将近 3 分钟
$waitSec = if ($urlNotBefore -gt (Get-Date).AddMinutes(-10)) { 240 } else { 20 }
Write-Host "[3/5] 从服务日志读取当前隧道域名（最多 $waitSec 秒）..." -ForegroundColor Cyan
$newBase = $null
for ($i = 0; $i -lt $waitSec; $i++) {
    $newBase = Get-CpolarCurrentUrl $urlNotBefore
    if ($newBase) { break }
    if ($i -gt 0 -and ($i % 15) -eq 0) {
        Write-Host "  还在等网页隧道域名（已等 $i 秒）..." -ForegroundColor DarkGray
    }
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
if ($tokenMatch.Success) {
    $mcpToken = $tokenMatch.Groups[1].Value
} elseif ($env:MCP_TOKEN) {
    $mcpToken = $env:MCP_TOKEN
} else {
    $mcpToken = ''
    Write-Host '  警告：appsettings.json 未找到 Token，且未设置环境变量 MCP_TOKEN，将跳过 MCP 就绪验证。' -ForegroundColor Yellow
}

$m = [regex]::Match($raw, '"AuthBaseUrl"\s*:\s*"([^"]+)"')
if (-not $m.Success) { throw 'appsettings.json 里没找到 AuthBaseUrl，请手动检查该文件。' }

$newRaw = $raw
$changed = $false
if ($m.Groups[1].Value -ne $newBase) {
    $newRaw = [regex]::Replace($newRaw, '("AuthBaseUrl"\s*:\s*")[^"]+(")', "`${1}$newBase`${2}")
    $changed = $true
}
if ($newRaw -match '"AuthLocalBaseUrl"\s*:\s*"http://localhost:8080"') {
    $newRaw = [regex]::Replace($newRaw, '("AuthLocalBaseUrl"\s*:\s*")http://localhost:8080(")', '${1}https://localhost:8080${2}')
    $changed = $true
}
if ($changed) {
    Copy-Item $cfgPath "$cfgPath.bak" -Force
    [IO.File]::WriteAllText($cfgPath, $newRaw, (New-Object System.Text.UTF8Encoding $false))
    Write-Host '  已备份到 appsettings.json.bak，地址已更新为 https。' -ForegroundColor Green
} else {
    Write-Host '  AuthBaseUrl 已是当前隧道域名，无需更新。' -ForegroundColor Green
}

# ---------- 5/5 启动后端并验证 ----------
Write-Host '[5/5] 启动后端 dotnet run（https://localhost:8080）...' -ForegroundColor Cyan
Remove-Item $backendLog -ErrorAction SilentlyContinue
Remove-Item "$backendLog.err" -ErrorAction SilentlyContinue
Start-Process -FilePath 'cmd.exe' `
    -ArgumentList '/k', "cd /d `"$backendDir`" && title MES-后端 && dotnet run --urls https://0.0.0.0:8080" `
    -RedirectStandardOutput $backendLog `
    -RedirectStandardError "$backendLog.err" `
    -WindowStyle Minimized

$mcpOk = $false
if (-not $mcpToken) {
    Write-Host '  未设置 MCP Token，跳过 MCP 就绪自检（地址仍已更新）。' -ForegroundColor DarkGray
} else {
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Seconds 1
        try {
            $code = curl.exe -sk -o NUL -w "%{http_code}" -X POST "https://localhost:8080/mcp" `
                -H "Authorization: Bearer $mcpToken" -H "Content-Type: application/json" `
                -d "{}" --max-time 3
            $code = "$code".Trim()
            if ($code -eq '400' -or $code -eq '401') { $mcpOk = $true; break }
        } catch {
            # 后端还在启动，继续等
        }
    }
}

Write-Host ''
Write-Host '====================== 结果 ======================' -ForegroundColor Yellow
if ($mcpOk) {
    Write-Host '  [OK] 本机 MCP 服务已就绪：https://localhost:8080/mcp' -ForegroundColor Green
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
