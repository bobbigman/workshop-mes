# Start Vite dev server hidden (no extra console window).
$ErrorActionPreference = 'Stop'
$frontend = Join-Path (Split-Path $PSScriptRoot -Parent) 'frontend'
if (-not (Test-Path -LiteralPath $frontend)) {
    throw "frontend folder not found: $frontend"
}
Start-Process -FilePath $env:ComSpec -ArgumentList '/c', 'npm run dev' -WorkingDirectory $frontend -WindowStyle Hidden
