param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('tips', 'exit')]
    [string] $Mode,
    [string] $FrontUrl = '',
    [int] $ExitCode = 0
)

$OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

if ($Mode -eq 'tips') {
    Write-Host ''
    Write-Host '已启动，只保留这一个黑窗口:'
    Write-Host '  后端  https://localhost:8080'
    Write-Host "  前端  $FrontUrl"
    Write-Host '  本机也可 https://localhost:5173'
    Write-Host '  出个人码请用上面局域网地址'
    Write-Host '  演示账号 工厂 F001 / admin 或 zhangchu / Admin123'
    Write-Host ''
    Write-Host '提示:'
    Write-Host '  - 本窗口不要关；关掉或 Ctrl+C = 停后端'
    Write-Host '  - 停整套请双击 stop-dev.bat'
    Write-Host '  - 只重启后端请双击 restart-backend.bat'
    Write-Host '  - 用 Visual Studio 生成前请先双击 stop-dev.bat'
    Write-Host ''
    Write-Host '----------------------------------------'
    Write-Host ''
    return
}

Write-Host ''
Write-Host "后端已退出，退出码=$ExitCode。"
Write-Host '前端若还在跑，请双击 stop-dev.bat 一并停掉；或再开 start-dev 时会自动清理。'
Write-Host ''