---
name: workshop-mes-phone-observe
description: >-
  Observe the connected Honor phone from this Windows PC over USB debugging,
  without asking the user for screenshots. Use when the user says 手机、真机、荣耀、
  USB调试、不用截图、帮我看手机画面, or an H5 page looks wrong on the phone.
  Covers adb location, lock screen, notification shade, screencap, and Honor
  browser DevTools. Also a standalone note other assistants (豆包) can follow.
---

# 用数据线看荣耀手机画面（不用用户截图）

给本机 Cursor，也给豆包等其它助手。没有这台电脑的 shell、手机又没插上时，做不到，改口头让用户解锁或看屏幕。

## 什么时候用

用户报手机上的页面不对，且手机已用数据线连上这台电脑、并已允许 USB 调试。助手自己截屏、自己读浏览器网址，不要再让用户截图。

## 这台电脑上的事实

- 系统里没有全局 `adb` 命令。不要全盘搜索 `adb.exe`（会扫很久）。
- 本机 adb：`C:\Users\bob\platform-tools-dl\platform-tools\adb.exe`（Android platform-tools）。下面命令里的 `adb` 都指这个完整路径。
- 荣耀手机助手在 `C:\Program Files (x86)\HonorSuite`。那里有 `fastboot.exe` 和 `AdbWinApi.dll`，没有 `adb.exe`。
- 已授权过的手机：荣耀 **JLH-AN00**。以 `adb devices -l` 为准；状态必须是 `device`。`unauthorized` 要用户在手机上点「允许」。列表为空就是没连上或调试没开。
- 车间页面开发地址：`https://192.168.3.7:5173`。手机浏览器是荣耀浏览器 `com.honor.browser`。

## 看画面

1. `adb devices -l`。不是 `device` 就停，告诉用户差在授权还是没连上。
2. 点亮：`adb shell input keyevent KEYCODE_WAKEUP`。
3. 看焦点：`adb shell dumpsys window`，找 `mCurrentFocus` 和 `mDreamingLockscreen`。
4. 截屏不要用 PowerShell 重定向 `adb exec-out screencap`（PNG 会坏）。用：

```powershell
adb shell screencap -p /sdcard/cursor-screen.png
adb pull /sdcard/cursor-screen.png .tmp/phone-screen.png
adb shell rm /sdcard/cursor-screen.png
```

截图放仓库 `.tmp/`，不要提交。

## 锁屏和通知栏

- `mDreamingLockscreen=true`，或画面写着「双击屏幕进行人脸识别」：调试命令关不掉这道锁，`wm dismiss-keyguard` 也没用。不要猜密码。请用户看着手机解锁。
- 焦点是 `NotificationShade` 时，通知栏盖住锁屏，人脸预览不会出现。先 `adb shell cmd statusbar collapse`，再请用户看手机。
- 用户说「看不到人脸、没有解锁提示」，先截屏。手机若已经在桌面或浏览器里，锁屏提示本来就不会有。

看页面期间避免马上又锁上：先记下原来的熄屏时间，再执行

```powershell
adb shell settings get system screen_off_timeout
adb shell svc power stayon true
adb shell settings put system screen_off_timeout 600000
```

看完恢复原来的超时，并 `adb shell svc power stayon false`。改过要告诉用户。

## 看浏览器里的页面（比截图准）

`am start -a android.intent.action.VIEW -d <网址>` 可能新开一个标签，眼前仍是旧页。不要以为网址已经打开。

读真正在前台的页：

```powershell
adb shell cat /proc/net/unix
```

找 `@webview_devtools_remote_<pid>`，然后：

```powershell
adb forward tcp:9333 localabstract:webview_devtools_remote_<pid>
curl.exe -s http://127.0.0.1:9333/json
```

`9222` 常被占用，换端口。JSON 里 `"attached":true` 的那条才是眼前这一页，看它的 `url`。

车间 H5 的工单列表是 `https://192.168.3.7:5173/h5/report#/order`，上面有「报工端」。`多人共同报工` 不在这一页，在点进某张工单后的报工表单里（`frontend/src/views/h5/report.vue`）。

## 做不到的

- 人脸锁、密码锁未解开时，看不到锁屏后面的页面。
- 手机没插线，或 USB 调试不是「已授权」。
- 助手不在这台电脑上（例如豆包只有对话）：只能按上面的步骤说明，不能自己截到屏幕。
