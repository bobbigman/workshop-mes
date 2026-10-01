# 本机同时开两套：WorkShop → WorkshopMes:8080，WorkShopB → WorkshopMesB:8081
# 先编译一次，再起两个进程。不要对第二个实例再 dotnet run（会抢 exe 编译锁）。
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..\backend")
Set-Location $root
dotnet build -c Debug
if ($LASTEXITCODE -ne 0) { throw "编译失败" }

$exe = Join-Path $root "bin\Debug\net8.0\WorkshopMes.exe"
if (-not (Test-Path $exe)) { throw "找不到 $exe" }

function Start-Instance($name, $envName, $url) {
  $cmd = "`$env:ASPNETCORE_ENVIRONMENT='$envName'; `$env:ASPNETCORE_URLS='$url'; `$env:ASPNETCORE_CONTENTROOT='$root'; & '$exe'"
  Start-Process powershell -ArgumentList "-NoExit", "-Command", $cmd -WindowStyle Normal
  Write-Host "已启动 $name  $url"
}

Start-Instance "WorkShop" "Development" "http://127.0.0.1:8080"
Start-Instance "WorkShopB" "WorkshopMesB" "http://127.0.0.1:8081"
Write-Host "前端：WorkShop 用 npm run dev（5173）；WorkShopB 用 npm run dev:b（5174）"
