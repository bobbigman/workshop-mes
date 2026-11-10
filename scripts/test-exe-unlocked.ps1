# Exit 0 if missing or can be opened for write (not locked); else exit 1.
param(
    [Parameter(Mandatory = $true)]
    [string] $Path
)
if (-not (Test-Path -LiteralPath $Path)) { exit 0 }
try {
    $f = [System.IO.File]::Open($Path, 'Open', 'ReadWrite', 'None')
    $f.Close()
    exit 0
} catch {
    exit 1
}
