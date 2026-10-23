---
name: workshop-mes-phone-observe
description: >-
  安卓手机/PDA 真机验收：用 adb 截屏 + UI 树看画面，读 H5 用源码/同 URL/API（不要 OCR）。
  用户说手机、真机、荣耀、PDA、USB 调试、帮我看手机画面、页面遮挡/滚动/布局不对，
  或 HonorSuite/手机助理读屏失败时自动使用；无需用户点名技能或翻 docs/41。
  不用于普通电脑窗口操作。
---

# 手机真机 ADB 读屏（车间小工单）

权威手册：`docs/开发手册和方案/41-手机画面-ADB截屏与UI树（真机验证操作手册）.md`。技能已含核心步骤，不必先翻全文。

## 何时自动用

手机已 USB 连接且开了「USB 调试」时，要验 H5/PDA 真机画面、布局、弹窗、扫码观感 → **直接 adb 截屏**，不要让用户截图，也不要先死磕 HonorSuite 窗口捕获。

仅口头讨论设计、未授权真机操作时，不要擅自控手机。

## 先纠正：读 H5 不要 OCR

手机上的 H5 是本项目。读文字/按钮/金额用：

1. 读源码 `frontend/src/views/h5/*.vue`
2. 电脑浏览器开同一 dev server URL
3. 查后端 API / 读实现拿动态数值

OCR 多余且易错。少数例外：无源码的原生 App、或核对某个屏幕动态值（通常仍不如查 API）。

手机画面的价值 = **视觉验证**（分辨率/布局/弹窗/扫码/触控）。本质是看图：人看，或支持图像输入的模型看 PNG。不支持看图时，截屏只存文件给人看；**UI 树/OCR 替代不了视觉验证**。

## 本机 adb（按序找，不全盘搜）

1. `D:/workspace/pkub/bee/.tmp/adb/platform-tools/adb.exe`（优先）
2. `C:/Users/bob/platform-tools-dl/platform-tools/adb.exe`
3. 都没有再按手册下载到 `D:\adb`，设 `$adb = 'D:\adb\platform-tools\adb.exe'`

HonorSuite/HDB ≠ 标准 ADB。需手机开 USB 调试并授权；`unauthorized` 请用户点允许，不代点。

## 标准流程（同一次 PowerShell）

```powershell
$adb = 'D:/workspace/pkub/bee/.tmp/adb/platform-tools/adb.exe'
$temp = 'D:/workspace/pkub/bee/.tmp/adbtmp'
New-Item -ItemType Directory -Force -Path $temp, 'D:/workspace/pkub/bee/.tmp' | Out-Null
$env:TEMP = $temp; $env:TMP = $temp   # 否则 adb.log Permission denied，daemon 起不来

& $adb devices -l
# 只选状态 device 的；多设备用用户指定的 -s <serial>，不猜历史号

& $adb shell screencap -p /sdcard/screen.png
& $adb pull /sdcard/screen.png D:/workspace/pkub/bee/.tmp/phone-screen.png
# 禁止 PowerShell > 重定向接 PNG（二进制会坏）

& $adb shell uiautomator dump /sdcard/ui.xml
& $adb pull /sdcard/ui.xml D:/workspace/pkub/bee/.tmp/phone-ui.xml
# UI 树只读原生控件；web view/H5 内部读不到 → 回上面三条正路
```

支持图像输入时用 Read 看 PNG。截图放 `.tmp/`，不提交 Git。

## 坑

| 现象 | 处理 |
|---|---|
| `cannot open ...\adb.log: Permission denied` | 同上重定向 TEMP/TMP |
| `adb server version X doesn't match client Y` | `Get-Process adb \| Stop-Process -Force` 杀 HonorSuite/HiSuite 旧 server 后再连 |
| 只有 HDB/MTP，没有 ADB Interface | 装 platform-tools，开 USB 调试（不是只开 HDB） |
| 锁屏 / 通知栏挡住 | `KEYCODE_WAKEUP`；`cmd statusbar collapse`；人脸/密码锁请用户解锁，不猜密码 |

## 边界

- 截屏成功 ≠ 目标页已验收；区分「通道通了」和「页面验过」。
- 滑动/点击/输入仅在用户授权范围内；不自动提交业务。
- 不默认上传私人手机画面；无 adb/未授权时说清缺什么，停。
