# bump-version.ps1 — 读 version.txt，patch +1，写回并同步 frontend/package.json
param(
  [Parameter(Mandatory = $true)]
  [string]$OutFile
)
$ErrorActionPreference = 'Stop'
$verFile = Join-Path $PSScriptRoot 'version.txt'
$pkg = Join-Path $PSScriptRoot '..\frontend\package.json'

if (-not (Test-Path $verFile)) { throw "missing version.txt" }
$old = ([System.IO.File]::ReadAllText($verFile)).Trim()
$parts = $old.Split('.')
if ($parts.Length -lt 3) { throw "Version must be major.minor.patch, got: $old" }
$patch = [int]$parts[2] + 1
$new = "$($parts[0]).$($parts[1]).$patch"

[System.IO.File]::WriteAllText($verFile, $new + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))

if (Test-Path $pkg) {
  $pj = [System.IO.File]::ReadAllText($pkg)
  if ($pj -match '"version"\s*:\s*"[^"]+"') {
    $pj2 = [regex]::Replace($pj, '"version"\s*:\s*"[^"]+"', ('"version": "' + $new + '"'), 1)
    [System.IO.File]::WriteAllText($pkg, $pj2, [System.Text.UTF8Encoding]::new($false))
  }
}

$dir = Split-Path -Parent $OutFile
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
[System.IO.File]::WriteAllText($OutFile, $new, [System.Text.UTF8Encoding]::new($false))
Write-Host "version $old -> $new"
