# Stop leftover "dotnet run" processes that target port 8080.
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
    Where-Object { $_.CommandLine -match 'dotnet\s+run' -and $_.CommandLine -match '8080' } |
    ForEach-Object {
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }
