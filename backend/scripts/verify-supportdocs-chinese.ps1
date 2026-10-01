# 黑盒验收：SupportDocs 中文检索是否恢复（真实 localhost:8080）
# 由豆包预先锁定判据，Cursor 只负责运行到 ALL PASS，不得改判据。
# 发送：curl.exe + UTF-8 请求体（避免 PS 5.1 Invoke-RestMethod -Body 默认 ANSI 编码坑）。
$ErrorActionPreference = "Stop"
$Base = "http://localhost:8080/mcp"
$tmpDir = Join-Path $env:TEMP ("supportdocs-verify-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tmpDir | Out-Null

function Invoke-SearchDocs {
  param([string]$Query)
  $id = Get-Random -Minimum 1000 -Maximum 9999
  # ConvertTo-Json 保证 Unicode 正确；再整包写成 UTF-8 无 BOM，交给 curl.exe
  $payload = @{
    jsonrpc = "2.0"
    id      = $id
    method  = "tools/call"
    params  = @{ name = "search_support_docs"; arguments = @{ query = $Query } }
  }
  $bodyJson = $payload | ConvertTo-Json -Depth 6 -Compress
  $bodyPath = Join-Path $tmpDir ("req-$id.json")
  $respPath = Join-Path $tmpDir ("resp-$id.txt")
  $utf8 = New-Object System.Text.UTF8Encoding $false
  [System.IO.File]::WriteAllText($bodyPath, $bodyJson, $utf8)

  $http = & curl.exe -s -X POST $Base `
    -H "Authorization: Bearer laohu-demo-2026" `
    -H "Accept: application/json, text/event-stream" `
    -H "MCP-Protocol-Version: 2024-11-05" `
    -H "Content-Type: application/json; charset=utf-8" `
    --data-binary "@$bodyPath" -o $respPath -w "%{http_code}"
  if ($http -ne "200") { throw "HTTP $http" }

  $text = [System.IO.File]::ReadAllText($respPath, $utf8)
  $dataLine = ($text -split "`n" | Where-Object { $_ -match '^data: ' } | Select-Object -First 1)
  if (-not $dataLine) { throw "无 data 行: $text" }
  $json = $dataLine.Substring(6).Trim()
  $outer = $json | ConvertFrom-Json
  $innerText = $outer.result.content[0].text
  $inner = $innerText | ConvertFrom-Json
  return $inner
}

# 固定判据：中文必命中（state=ready 且 totalMatched>0），英文照常
# 用 Unicode 码点构造，避免脚本文件编码导致字面量损坏
$cases = @(
  @{ q = ([string][char]0x626B + [char]0x7801 + [char]0x62A5 + [char]0x5DE5 + [char]0x600E + [char]0x4E48 + [char]0x5F00 + [char]0x59CB) } # 扫码报工怎么开始
  @{ q = ([string][char]0x626B + [char]0x7801) } # 扫码
  @{ q = ([string][char]0x4E8C + [char]0x7EF4 + [char]0x7801) } # 二维码
  @{ q = ([string][char]0x6D41 + [char]0x8F6C + [char]0x5361) } # 流转卡
  @{ q = ([string][char]0x4E00 + [char]0x952E + [char]0x8865 + [char]0x62A5) } # 一键补报
  @{ q = "PDA" }
)

try {
  $passAll = $true
  "=== SupportDocs 中文检索验收（真实 localhost:8080）==="
  foreach ($c in $cases) {
    try {
      $r = Invoke-SearchDocs -Query $c.q
      $state  = $r.state
      $matched = [int]$r.totalMatched
      $src = (($r.sources | ForEach-Object { "$($_.title) / $($_.chapter)" }) -join "; ")
      $ok = ($state -eq "ready" -and $matched -gt 0)
      if ($ok) { "[PASS] $($c.q) -> state=$state matched=$matched | $src" }
      else     { "[FAIL] $($c.q) -> state=$state matched=$matched"; $passAll = $false }
    } catch {
      "[ERROR] $($c.q) -> $($_.Exception.Message)"
      $passAll = $false
    }
  }

  ""
  if ($passAll) { "== ALL PASS: SupportDocs 中文检索已恢复 =="; $exitCode = 0 }
  else          { "== FAIL: 仍有中文检索问题，勿交付 =="; $exitCode = 1 }
} finally {
  Remove-Item -LiteralPath $tmpDir -Recurse -Force -ErrorAction SilentlyContinue
}
exit $exitCode
