---
name: workshop-mes-mobile-scan
description: >-
  Deliver and troubleshoot the Workshop MES phone camera QR scanning (H5 扫码报工)
  on customer sites. Covers the HTTPS + certificate-trust prerequisites, the
  jsQR (not qr-scanner) decoder decision that avoids page crashes on Baidu/国产
  browser engines, the ?scan-check diagnostic + 复制日志 evidence flow, and the
  phone-side acceptance checklist. Use when deploying 手机扫码/摄像头报工 to a
  customer phone, or debugging camera scan symptoms like 一闪而过, 闪退,
  打不开摄像头, 无权限提示, 识别不出二维码, or 页面重建/back_forward.
---

# 车间小工单：手机摄像头扫码交付与排障

来源：2026-09-22 真机定位（百度浏览器）。结论：摄像头、权限、HTTPS 全正常，**崩溃点只在 `qr-scanner` 启动识别 Worker 时**。已换 jsQR 修复，版本 0922-10。

## 一句话结论（先记住）

- 手机扫码必须 **HTTPS + 手机信任证书**；解码库必须用 **jsQR**（纯 JS、主线程），**不要用 qr-scanner**。
- `qr-scanner` 靠 `new Worker(URL.createObjectURL(new Blob(...)))` 建识别引擎，百度等国产浏览器内核直接崩页面（时间线在「视频已开始播放」之后断掉，导航类型 `back_forward`）。
- 独立页 `frontend/public/camera-check.html` 不加载扫码库，是判断「摄像头能力是否正常」的对照基线。

## 交付前提（部署到客户手机）

1. 手机访问地址是**手机能访问的局域网 HTTPS**，不能是 localhost / 127.0.0.1。
2. 证书 SAN 覆盖访问用的 IP/域名，且已在手机上信任。
3. 打印二维码里的报工链接，其「前端基址」要用上述 HTTPS 地址。
4. 内网 HTTP 通常无法开摄像头；「系统相机扫链接」≠「网页内开摄像头」，是两回事。

## 技术决策（改代码时别回退）

- 解码库：**jsqr**（本项目已用）。移除 qr-scanner 后，构建产物不应再有 `qr-scanner-worker.min-*.js`。
- 启动链路（`frontend/src/views/h5/scan.vue`）：`getUserMedia({ video: { facingMode: { ideal: 'environment' } } })`（OverconstrainedError 时回退 `video: true`）→ `video.srcObject = stream` → `await video.play()` → 进入 scanning → `setInterval`（120ms）循环 `drawImage` + `getImageData` + `jsQR()`，命中停流跳 `/h5/report?order=...`。
- 不要为「后置优先」在授权前加设备选择约束、也不要授权前更换 `video` 节点。
- 诊断记录存 `localStorage['h5.scan.diagnostics.local']`（崩溃/重载不丢），不要退回 sessionStorage。

## 现场验收清单

- [ ] 手机点「打开摄像头扫码」→ 允许权限 → 画面持续、不闪回
- [ ] 对准工单二维码 → 自动跳报工页（不自动提交）
- [ ] 后置摄像头能扫实际标签
- [ ] 拒绝权限 / 取消 / 超时 / 切后台均有明确提示，不残留摄像头占用
- [ ] 手动输入 + PDA 扫码枪模式不受影响

## 排障（先收证据，别瞎改）

1. 让用户打开 `https://<IP>:<port>/?scan-check=0922-10#/h5/scan`，点按钮后点「复制日志」把完整时间线发回来（不要再让用户截图）。
2. 日志能区分四类：
   - 权限拒绝：`NotAllowedError` / `PermissionDeniedError`
   - HTTPS 问题：`安全环境：否`
   - 页面崩溃：时间线在「视频已开始播放」后断掉、`页面进入方式：back_forward`
   - 识别不出：走到「识别器已就绪，等待二维码」但扫不到
3. 拿到日志再改，避免「换版本号—让用户再点—仍看旧现象」的循环。

## 症状对照表

| 症状 | 根因 | 处理 |
|---|---|---|
| 点按钮无反应、无权限弹窗 | HTTP 或证书未信任 | 换 HTTPS、信任证书 |
| 一闪而过、画面消失、页面重建 | 扫码库 Worker 崩内核 | 用 jsQR（本项目已换） |
| 画面正常但扫不出 | 朝向/距离/光线/标签 | 换后置、调距、补光 |
| 提示摄像头被占用 | 其它应用占用 | 关其它应用再试 |
| 手动能开、扫码不行 | 标签内容或扫码头 | 查标签内容、对扫码枪模式 |

## 手机侧给现场人员的话术

- 要用摄像头扫码，网址必须是管理员给的 **HTTPS** 地址，且手机已信任证书。
- 第一次点「打开摄像头扫码」时浏览器会问权限，选「允许」（不要开麦克风）。
- 打不开就点「手动输入工单号」，或让管理员查 HTTPS/证书。

## PWA 桌面图标（可选增强）

- 文件：`frontend/public/manifest.json`、`sw.js`、`icons/icon-*.png`；`index.html` 里挂 `link[rel=manifest]` 和 apple 系列 meta；`src/main.js` 里 `import.meta.env.PROD` 时 `navigator.serviceWorker.register('/sw.js')`。
- 图标由 `frontend/scripts/generate-icons.ps1` 生成（蓝底 #3370ff + 白色扫码框；`icon-maskable-512.png` 留 10% 安全区）。换 logo 时改脚本重跑即可。
- `start_url: /#/h5/scan`（面向工人，点图标直达扫码页）。
- Service Worker 缓存刻意保守：**HTML 导航 network-first、带 hash 的 /assets/ 资源 cache-first、/api/ 绝不缓存**。目的就是别让手机卡旧版（本项目曾被版本/缓存坑过）。
- 注意 PWA 走的是「添加时的浏览器内核」：Chrome/系统浏览器添加 → standalone 全屏走系统内核；百度浏览器添加 → 多数只是快捷方式、仍走百度内核（现已修复扫码，可用但非全屏）。

