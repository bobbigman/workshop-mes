# Build incremental update pack for an existing self-contained install.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File backend/pack-update.ps1

$ErrorActionPreference = "Stop"
Remove-Item env:HTTP_PROXY -ErrorAction SilentlyContinue
Remove-Item env:HTTPS_PROXY -ErrorAction SilentlyContinue
Remove-Item env:ALL_PROXY -ErrorAction SilentlyContinue

$backend = $PSScriptRoot
$root = Split-Path $backend -Parent
$out = Join-Path $backend "publish-update"

Write-Host "== 1/3 frontend build =="
Set-Location (Join-Path $root "frontend")
npm run build
if ($LASTEXITCODE -ne 0) { throw "frontend build failed" }

$wwwSrc = Join-Path $backend "wwwroot"
if (Test-Path $wwwSrc) { Remove-Item $wwwSrc -Recurse -Force }
New-Item -ItemType Directory -Path $wwwSrc | Out-Null
Copy-Item (Join-Path $root "frontend\dist\*") $wwwSrc -Recurse -Force

Write-Host "== 2/3 dotnet publish (framework-dependent, small) =="
Set-Location $backend
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish -c Release -r win-x64 --self-contained false -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$wwwDst = Join-Path $out "wwwroot"
if (Test-Path $wwwDst) { Remove-Item $wwwDst -Recurse -Force }
New-Item -ItemType Directory -Path $wwwDst | Out-Null
Copy-Item (Join-Path $wwwSrc "*") $wwwDst -Recurse -Force

# Iron rule (docs/59, DEPLOYMENT_NOTES.md): never ship server-local files in update pack.
# Partner machines may have HTTPS ASPNETCORE_URLS / Kestrel certs already baked in.
# 递归清理：dotnet publish 会把项目目录下的历史产物（publish-win64 / _build_out 等）
# 一并带进输出；只删顶层会漏掉嵌套目录里的敏感文件，故一律 -Recurse。
Get-ChildItem $out -Recurse -Filter "appsettings*.json" -File | Remove-Item -Force
Get-ChildItem $out -Recurse -Filter "*.runtimeconfig.json" -File | Remove-Item -Force
Get-ChildItem $out -Recurse -Filter "*.deps.json" -File | Remove-Item -Force
Get-ChildItem $out -Recurse -Filter "WorkshopMes.exe" -File | Remove-Item -Force
Get-ChildItem $out -Recurse -Filter "WorkshopMes.pdb" -File | Remove-Item -Force
Get-ChildItem $out -Recurse -Include "start.bat","stop.bat" -File | Remove-Item -Force

# 删除被 publish 带进来的历史产物目录（内含 appsettings/exe/deps，绝不能出现在更新包）
foreach ($d in @("publish-win64", "_build_out")) {
    $p = Join-Path $out $d
    if (Test-Path $p) { Remove-Item $p -Recurse -Force }
}
# 开发调试残留：非程序本体，删掉保持包干净
Get-ChildItem $out -Recurse -Include "global.json","_mcp_body.json","_t.json","*.staticwebassets*.json" -File | Remove-Item -Force -ErrorAction SilentlyContinue
if (Test-Path (Join-Path $out "certs")) { Remove-Item (Join-Path $out "certs") -Recurse -Force }
if (Test-Path (Join-Path $out "logs")) { Remove-Item (Join-Path $out "logs") -Recurse -Force }

# 递归闸门校验：任一敏感文件残留即 throw，不交付
$bad = @(
    Get-ChildItem $out -Recurse -Filter "appsettings*.json" -File
    Get-ChildItem $out -Recurse -Filter "*.runtimeconfig.json" -File
    Get-ChildItem $out -Recurse -Filter "*.deps.json" -File
    Get-ChildItem $out -Recurse -Filter "WorkshopMes.exe" -File
    Get-ChildItem $out -Recurse -Include "start.bat","stop.bat" -File
) | Where-Object { $_ }
if ($bad) { throw ("update pack must NOT contain: " + (($bad | Sort-Object -Unique -Property Name | ForEach-Object Name) -join ", ")) }

if (Test-Path (Join-Path $out "publish-update")) { Remove-Item (Join-Path $out "publish-update") -Recurse -Force }
if (Test-Path (Join-Path $out "_s10_evidence")) { Remove-Item (Join-Path $out "_s10_evidence") -Recurse -Force }

Write-Host "== 3/3 write apply script + readme =="
Copy-Item (Join-Path $backend "apply-update.bat") (Join-Path $out "apply-update.bat") -Force
Copy-Item (Join-Path $backend "update-readme.txt") (Join-Path $out "README-update.txt") -Force

$mb = [math]::Round((Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
$verFile = Join-Path $out "WorkshopMes.dll"
$ver = if (Test-Path $verFile) {
  [System.Diagnostics.FileVersionInfo]::GetVersionInfo($verFile).ProductVersion
} else { "?" }
Write-Host ""
Write-Host "OK: $out"
Write-Host "Size ~ ${mb} MB / version $ver"
Write-Host "Copy the whole publish-update folder to the customer server."
Write-Host "On server: run apply-update.bat (see README-update.txt)."
