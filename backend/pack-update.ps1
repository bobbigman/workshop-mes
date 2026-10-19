# Build incremental update pack for an existing self-contained install.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File backend/pack-update.ps1

$ErrorActionPreference = "Stop"
Remove-Item env:HTTP_PROXY -ErrorAction SilentlyContinue
Remove-Item env:HTTPS_PROXY -ErrorAction SilentlyContinue
Remove-Item env:ALL_PROXY -ErrorAction SilentlyContinue

$backend = $PSScriptRoot
$root = Split-Path $backend -Parent
$out = Join-Path $backend "publish-update"

# 0/4 刷新企微客户问答库（docs/企微客户问答库.docx）：从 SupportDocs 15/19/08 生成并自动 version+1。
# 幂等：三份源文件没变化则不升版。失败即抛，不交付残缺包。详见 docs/未做指令/239。
Write-Host "== 0/4 refresh WeCom customer QA docx =="
$qaScript = Join-Path $root "scripts\build-wecom-qa.ps1"
if (-not (Test-Path $qaScript)) { throw "build-wecom-qa.ps1 not found: $qaScript" }
& powershell -NoProfile -ExecutionPolicy Bypass -File $qaScript
if ($LASTEXITCODE -ne 0) { throw "build-wecom-qa.ps1 failed (exit $LASTEXITCODE)" }

Write-Host "== 1/4 frontend build =="
Set-Location (Join-Path $root "frontend")
npm run build
if ($LASTEXITCODE -ne 0) { throw "frontend build failed" }

$wwwSrc = Join-Path $backend "wwwroot"
if (Test-Path $wwwSrc) { Remove-Item $wwwSrc -Recurse -Force }
New-Item -ItemType Directory -Path $wwwSrc | Out-Null
Copy-Item (Join-Path $root "frontend\dist\*") $wwwSrc -Recurse -Force

Write-Host "== 2/4 dotnet publish (framework-dependent, small) =="
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
# 212: 历史验证构建残渣目录（如 _verify_build_81），非程序本体，递归删除
Get-ChildItem $out -Recurse -Directory -Filter "_verify_*" | Remove-Item -Recurse -Force
# 开发调试残留：非程序本体，删掉保持包干净
# 212: 补漏——接口调试响应 _*.json（登录/工资/订单等）会被 publish 带进包，一律递归删除，含 wwwroot 下
Get-ChildItem $out -Recurse -Include "global.json","_mcp_body.json","_t.json","*.staticwebassets*.json" -File | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem $out -Recurse -Filter "_*.json" -File | Remove-Item -Force -ErrorAction SilentlyContinue
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

Write-Host "== 3/4 write apply script + readme =="
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
