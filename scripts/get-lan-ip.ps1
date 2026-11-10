# 取当前可给手机访问的局域网 IPv4（优先有网关的已连接网卡）
$ErrorActionPreference = 'SilentlyContinue'
$skip = 'vEthernet|Loopback|WSL|Hyper-V|VPN|Virtual|Docker|Bluetooth|VMware|VirtualBox'

$nics = [System.Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces() |
  Where-Object {
    $_.OperationalStatus -eq 'Up' -and
    $_.NetworkInterfaceType -ne 'Loopback' -and
    $_.Name -notmatch $skip -and
    $_.Description -notmatch $skip
  }

$withGw = @()
$noGw = @()
foreach ($nic in $nics) {
  $props = $nic.GetIPProperties()
  $hasGw = @($props.GatewayAddresses | Where-Object { $_.Address -and $_.Address.AddressFamily -eq 'InterNetwork' }).Count -gt 0
  foreach ($ua in $props.UnicastAddresses) {
    if ($ua.Address.AddressFamily -ne 'InterNetwork') { continue }
    $ip = $ua.Address.ToString()
    if ($ip -match '^(127\.|169\.254\.)') { continue }
    if ($hasGw) { $withGw += $ip } else { $noGw += $ip }
  }
}

$pick = if ($withGw.Count -gt 0) { $withGw[0] } elseif ($noGw.Count -gt 0) { $noGw[0] } else { '127.0.0.1' }
[Console]::Out.WriteLine($pick)
exit 0
