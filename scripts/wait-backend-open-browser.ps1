# Wait until backend :8080 accepts TCP, then open the frontend URL.
param(
    [Parameter(Mandatory = $true)]
    [string] $Url,
    [int] $Port = 8080,
    [int] $TimeoutSec = 90
)

$ready = $false
for ($i = 0; $i -lt $TimeoutSec; $i++) {
    try {
        $client = New-Object System.Net.Sockets.TcpClient
        $iar = $client.BeginConnect('127.0.0.1', $Port, $null, $null)
        if ($iar.AsyncWaitHandle.WaitOne(800)) {
            $client.EndConnect($iar)
            $client.Close()
            $ready = $true
            break
        }
        $client.Close()
    } catch {
        # keep waiting
    }
    Start-Sleep -Seconds 1
}

if (-not $ready) {
    Write-Host "[WARN] backend port $Port not ready in $TimeoutSec sec; open browser anyway"
}

Start-Process $Url
