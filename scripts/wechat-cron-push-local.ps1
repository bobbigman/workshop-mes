# 本地开发机定时调用后端 cron-push（后端为 HTTPS 自签，curl 须带 -k）
# 用法：.\wechat-cron-push-local.ps1 -BaseUrl https://localhost:8080 -CronToken <token>
# 或设环境变量 MES_BASE_URL / MES_WECHAT_CRON_TOKEN
# schtasks 示例（每天 8:00）：
#   schtasks /Create /TN "MES-WechatRiskCron" /SC DAILY /ST 08:00 /RL LIMITED /TR "powershell.exe -NoProfile -ExecutionPolicy Bypass -File D:\workspace\pkub\bee\scripts\wechat-cron-push-local.ps1 -BaseUrl https://localhost:8080 -CronToken <token>"
param(
    [string]$BaseUrl = $env:MES_BASE_URL,
    [string]$CronToken = $env:MES_WECHAT_CRON_TOKEN
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($BaseUrl)) { Write-Error '请传 -BaseUrl 或设环境变量 MES_BASE_URL' }
if ([string]::IsNullOrWhiteSpace($CronToken)) { Write-Error '请传 -CronToken 或设环境变量 MES_WECHAT_CRON_TOKEN' }

$BaseUrl = $BaseUrl.TrimEnd('/')
$url = "$BaseUrl/api/WechatAlert/cron-push"
$tmp = [System.IO.Path]::GetTempFileName()

try {
    $code = curl.exe -k -s -S -m 120 -X POST $url `
        -H "X-Cron-Token: $CronToken" `
        -H "Content-Type: application/json" `
        -o $tmp `
        -w "%{http_code}"
    $body = Get-Content -Raw -Encoding UTF8 $tmp
    Write-Host "HTTP:$code"
    Write-Host $body
    if ($code -ne '200') { exit 1 }
    if ($body -notmatch '"code"\s*:\s*0\b') { exit 1 }
}
finally {
    Remove-Item -Force -ErrorAction SilentlyContinue $tmp
}
