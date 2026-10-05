# 每分钟执行此脚本；交期预警仍单独使用原每日任务。
# 配置运行任务账号的环境变量 MES_BASE_URL / MES_WECHAT_CRON_TOKEN。
param(
    [string]$BaseUrl = $env:MES_BASE_URL,
    [string]$CronToken = $env:MES_WECHAT_CRON_TOKEN
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($BaseUrl) -or [string]::IsNullOrWhiteSpace($CronToken)) {
    throw '请设置 MES_BASE_URL 和 MES_WECHAT_CRON_TOKEN，或传入对应参数'
}
$url = $BaseUrl.TrimEnd('/') + '/api/WechatAlert/dispatch-events'
try {
    $result = Invoke-RestMethod -Method Post -Uri $url -Headers @{ 'X-Cron-Token' = $CronToken } -TimeoutSec 2700
    $result | ConvertTo-Json -Depth 8
    if ($result.code -ne 0) { exit 1 }
}
catch {
    # 不输出含 Token 的请求头。
    Write-Error "事件投递任务调用失败；网址：$url；请查看服务器错误日志和发送记录。"
    exit 1
}
