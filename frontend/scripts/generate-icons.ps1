# 生成 PWA 图标（蓝底 + 白色扫码框），输出到 frontend/public/icons/
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File scripts/generate-icons.ps1
# 依赖：Windows 自带 System.Drawing，无需第三方库。
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "public\icons"
New-Item -ItemType Directory -Force -Path $out | Out-Null

$bgColor = [System.Drawing.ColorTranslator]::FromHtml("#3370ff")
$fgColor = [System.Drawing.Color]::White

function New-Icon([int]$size, [string]$file, [double]$margin) {
  $bmp = New-Object System.Drawing.Bitmap($size, $size)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.Clear([System.Drawing.Color]::Transparent)

  # 背景：全幅圆角方块（Android/Chrome 会用系统形状再裁剪，这里画满即可）
  $bgBrush = New-Object System.Drawing.SolidBrush($bgColor)
  $g.FillRectangle($bgBrush, 0, 0, $size, $size)

  # 白色扫码框图案（四个 L 形角标 + 中心方块）
  $fgBrush = New-Object System.Drawing.SolidBrush($fgColor)
  $u = [double]$size
  $m = [double]($u * $margin)          # 外边距
  $t = [double]($u * 0.085)            # 角标棒粗
  $s = [double]($u * 0.24)             # 角标外边长
  $c = [double]($u * 0.18)             # 中心方块边长

  # 四个角的 L 形（横棒 + 竖棒）
  $corners = @(
    @( $m, $m, 1, 1 ),                                  # 左上
    @( ($u - $m - $s), $m, -1, 1 ),                     # 右上
    @( $m, ($u - $m - $s), 1, -1 ),                     # 左下
    @( ($u - $m - $s), ($u - $m - $s), -1, -1 )         # 右下
  )
  foreach ($c0 in $corners) {
    $x = $c0[0]; $y = $c0[1]; $dx = $c0[2]; $dy = $c0[3]
    # 横棒：从角点沿 x 方向延伸 s，厚 t
    $hx = if ($dx -gt 0) { $x } else { $x + $s - $t }
    $hy = if ($dy -gt 0) { $y } else { $y + $s - $t }
    $g.FillRectangle($fgBrush, [float]$hx, [float]$hy, [float]$s, [float]$t)
    $g.FillRectangle($fgBrush, [float]$hx, [float]$hy, [float]$t, [float]$s)
  }

  # 中心方块
  $cx = [float](($u - $c) / 2)
  $g.FillRectangle($fgBrush, $cx, $cx, [float]$c, [float]$c)

  $g.Dispose()
  $bmp.Save((Join-Path $out $file), [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  Write-Host "generated $file ($size x $size)"
}

New-Icon 192 "icon-192.png" 0.06
New-Icon 512 "icon-512.png" 0.06
# maskable：图案四周留 10% 安全区，避免被系统圆形遮罩裁掉
New-Icon 512 "icon-maskable-512.png" 0.10
