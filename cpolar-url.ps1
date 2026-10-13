$today = Get-Date -Format "yyyyMMdd"
$logDir = Join-Path $env:USERPROFILE ".cpolar\logs"
$log = Join-Path $logDir "cpolar_service.log.$today"
if (-not (Test-Path $log)) { $log = Join-Path $logDir "cpolar_service.log" }
if (-not (Test-Path $log)) { Write-Host "log not found: $log" -ForegroundColor Red; exit 1 }
$hits = Select-String -Path $log -Pattern "NewTunnel"
$result = [ordered]@{}
foreach ($h in $hits) {
    if ($h.Line -match 'TunnelName\\":\\"(\w+)\\".*?Url\\":\\"([^"\\]+)') {
        $result[$Matches[1]] = $Matches[2]
    }
}
Write-Host ""
Write-Host "===== cpolar current tunnels ($today) =====" -ForegroundColor Cyan
foreach ($k in $result.Keys) { Write-Host ("  {0,-16} => {1}" -f $k, $result[$k]) }
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""