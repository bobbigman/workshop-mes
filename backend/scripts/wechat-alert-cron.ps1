# 企业微信交期预警 — Windows 计划任务调用脚本（docs/75）
# 用法：
#   .\wechat-alert-cron.ps1 -BaseUrl "http://127.0.0.1:8080" -CronToken "你的Token"
# 或设环境变量 MES_BASE_URL / MES_WECHAT_CRON_TOKEN
# schtasks 示例（每天 8:00，按实例改路径与参数）：
#   schtasks /Create /TN "MES-WechatDueAlert" /SC DAILY /ST 08:00 /RL LIMITED /TR "powershell.exe -NoProfile -ExecutionPolicy Bypass -File D:\Mes\scripts\wechat-alert-cron.ps1 -BaseUrl http://127.0.0.1:8080 -CronToken 你的Token"

param(
    [string]$BaseUrl = $env:MES_BASE_URL,
    [string]$CronToken = $env:MES_WECHAT_CRON_TOKEN
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($BaseUrl)) {
    Write-Error "请传 -BaseUrl 或设环境变量 MES_BASE_URL"
}
if ([string]::IsNullOrWhiteSpace($CronToken)) {
    Write-Error "请传 -CronToken 或设环境变量 MES_WECHAT_CRON_TOKEN"
}

$BaseUrl = $BaseUrl.TrimEnd("/")
$url = "$BaseUrl/api/WechatAlert/cron-push"
$tmp = [System.IO.Path]::GetTempFileName()

try {
    $code = curl.exe -s -S -X POST $url `
        -H "X-Cron-Token: $CronToken" `
        -H "Content-Type: application/json" `
        -o $tmp `
        -w "%{http_code}"
    $body = Get-Content -Raw -Encoding UTF8 $tmp
    Write-Host "HTTP:$code"
    Write-Host $body
    if ($code -ne "200") {
        exit 1
    }
    if ($body -notmatch '"code"\s*:\s*0\b') {
        exit 1
    }
}
finally {
    Remove-Item -Force -ErrorAction SilentlyContinue $tmp
}
