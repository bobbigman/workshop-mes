# 41-手机画面：ADB 截屏与 UI 树（真机验证操作手册）

> 一句话：用 adb 让 AI 获取安卓手机画面信息（截屏 + UI 结构树），用于真机验证。
> 适用：Windows 电脑 + 安卓手机 USB 连接；手机开了「USB 调试」。
> 结论先行：**能截屏、能导 UI 树**；但「看图」受模型是否支持图像输入限制。**H5/网页内容不要 OCR——读源码、电脑开同一 URL、查 API 更直接可靠。**

---

## 一、先纠正一个误区：读 H5 内容不需要 OCR

手机此刻显示的 H5 报工端，是**我们自己的项目**（dev server `192.168.3.7:5173`）。要「读」它上面的文字/按钮/金额，有比 OCR 可靠得多的三条路：

1. **读源码**：`frontend/src/views/h5/*.vue` 直接定义了页面长啥样、有哪些文字、按钮文案。
2. **电脑浏览器开同一 URL**：同一个 dev server，电脑浏览器渲染的是同一页，可看 DOM / 读渲染后的文字。
3. **查 API / 读逻辑**：页面上的动态数字（如「今日计件 ¥1.50」）来自后端接口，直接查接口、读 `getTodayPieceSummary` 的实现就知道值是多少。

**OCR 只做「从图片里抽文字」**，而 H5 的文字本来就在源码/电脑浏览器里，OCR 是多余的，还容易读错。它只在少数场景才用（见 §六）。

---

## 二、手机画面真正的价值是「视觉验证」，不是「读文字」

- 手机画面的核心价值 = 确认**真机渲染对不对**：分辨率、布局、弹窗观感、扫码识别、触控。
- 这一步本质是「看图」：要么人看，要么**支持图像输入的模型**看截图。
- 当前模型若不支持图像输入，截屏只能存文件给人看；**UI 树和 OCR 都替代不了「视觉验证」**。

---

## 三、能拿到什么

| 产物 | 命令 | 用途 |
|---|---|---|
| 截图 PNG | `screencap` + `pull` | 给人看 / 给图像模型看 |
| UI 结构树 XML | `uiautomator dump` + `pull` | 读**原生 App** 控件的文字/坐标/resource-id/包名；**web view（H5）内部读不到** |

---

## 四、前置条件

1. 安卓手机 USB 连电脑，开发者选项里开了 **「USB 调试」**（不是只开 HDB）。
2. 电脑有 `adb`（没有就按 §五 下载 platform-tools）。
3. 手机首次连接弹「允许 USB 调试吗？」→ 点允许（否则 `adb devices` 显示 `unauthorized`）。

> 判断 USB 调试是否开了：`Get-PnpDevice` 里能看到 **「Android Composite ADB Interface」**（而不是只有 HDB/MTP）。华为/荣耀连 HiSuite/HonorSuite 走的是 HDB，和 ADB 是两回事。

---

## 五、完整步骤（Windows PowerShell）

### 1. 准备 adb（没有时）

```powershell
curl.exe -L -sS -o platform-tools.zip https://dl.google.com/android/repository/platform-tools-latest-windows.zip
Expand-Archive platform-tools.zip -DestinationPath D:\adb
$adb = 'D:\adb\platform-tools\adb.exe'
```

### 2. ⚠️ 关键：重定向临时目录（否则 adb 起不来）

adb 守护进程要写 `%TEMP%\adb.log`，在受限/沙箱环境会报 `cannot open ...\adb.log: Permission denied`，daemon 起不来。**把 TEMP 指到可写目录再跑**：

```powershell
$env:TEMP = 'D:\workspace\pkub\bee\.tmp\adbtmp'
$env:TMP  = $env:TEMP
New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null
```

### 3. 确认设备

```powershell
& $adb devices
# 期望：<设备号>  device   （看到 unauthorized 就是没授权）
```

> 若报 `adb server version (xx) doesn't match this client (yy)`：有旧 adb server（如 HonorSuite/HiSuite 自带的 v39）在跑。先 `Get-Process adb | Stop-Process -Force` 杀掉旧 server，再重跑。

### 4. 截屏（两步走，别用 `>` 重定向）

PowerShell 的 `>` 是文本重定向，会破坏二进制 PNG。**用 `shell screencap` + `pull` 两步**：

```powershell
& $adb shell screencap -p /sdcard/screen.png
& $adb pull /sdcard/screen.png D:\workspace\pkub\bee\.tmp\phone-screen.png
```

### 5. 导 UI 结构树（读原生 App 控件）

```powershell
& $adb shell uiautomator dump /sdcard/ui.xml
& $adb pull /sdcard/ui.xml D:\workspace\pkub\bee\.tmp\phone-ui.xml
```

---

## 六、OCR 什么时候才用（少数场景）

默认**不需要**。只有这些情况才值得装 tesseract（含中文包）：

1. 读**没有源码的原生 App** 屏上的文字；
2. 想核对屏幕上某个**动态值**是否按预期显示（但通常查 API 更准，不靠 OCR）。

```powershell
tesseract D:\workspace\pkub\bee\.tmp\phone-screen.png out -l chi_sim
Get-Content out.txt
```

---

## 七、给其它 AI 的一键复现指令（直接复制）

> 按目标机替换 `$adb` 和目录路径。

```
任务：用 adb 获取安卓手机画面信息，用于真机验证。
环境：Windows + 安卓手机 USB 连接，手机已开「USB 调试」。

步骤（PowerShell）：
1. 没有 adb 就下载解压：
   curl.exe -L -sS -o platform-tools.zip https://dl.google.com/android/repository/platform-tools-latest-windows.zip
   Expand-Archive platform-tools.zip -DestinationPath D:\adb
   $adb = 'D:\adb\platform-tools\adb.exe'
2. 必须先把临时目录重定向到可写目录（否则 adb 守护进程写 %TEMP%\adb.log 报 Permission denied 起不来）：
   $env:TEMP = '<可写目录>'; $env:TMP = $env:TEMP
3. 确认设备：& $adb devices   （<设备号> device 即连上；unauthorized 需手机点允许）
4. 截屏（两步走，不要用 PowerShell 的 > 重定向，会破坏二进制）：
   & $adb shell screencap -p /sdcard/screen.png
   & $adb pull /sdcard/screen.png <本地路径>.png
5. UI 结构树（只读原生 App 控件；web view/H5 内部读不到）：
   & $adb shell uiautomator dump /sdcard/ui.xml
   & $adb pull /sdcard/ui.xml <本地路径>.xml

读 H5/网页内容：不要 OCR。优先①读项目源码（h5/*.vue 定义了页面文字）；②电脑浏览器开同一 dev server URL；③查后端 API 拿动态数值。OCR 只用于没有源码的原生 App 或核对某个屏幕动态值。

注意：
- adb 版本冲突报 mismatch 时，先 Get-Process adb | Stop-Process -Force 杀掉旧 server（HonorSuite/HiSuite 常自带旧版）。
- 若模型本身不支持图像输入，截屏只能存文件给人看；「视觉验证」要靠图像模型或人，UI 树/OCR 替代不了。
```

---

## 八、实测记录（2026-10-23）

- 设备：HONOR 50 SE，分辨率 1080×2177，设备号 `AMFU6R1A28001619`。
- HonorSuite 装的是 HDB（`hdbtransport.exe`），不带标准 adb；系统也本无 adb，故下载 platform-tools v41。
- 坑 1：adb.log 权限 → 重定向 TEMP 到 `D:\workspace\pkub\bee\.tmp\adbtmp` 后 daemon 正常启动。
- 坑 2：HonorSuite 自带 adb server v39 与新 v41 冲突 → 杀旧进程解决。
- 结果：`adb devices` 显示 `device`；截屏 279KB；UI 树读到手机在 `https://192.168.3.7:5173/#/h5/home`（H5 报工端工作台页）。
- 结论：H5 内部文字靠 UI 树读不到（web view 不透明），但**不需要 OCR**——直接读 `h5/*.vue` 源码 + 电脑开同 URL 即可；OCR 路线已废弃。
