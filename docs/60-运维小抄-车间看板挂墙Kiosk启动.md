# 60｜运维小抄：车间看板挂墙 Kiosk 启动

> 2026-10-10 记。场景：车间电视挂墙展示 `/board/screen` 大屏，开机自动全屏、免鼠标。

## 为什么需要这个

浏览器（Chrome/Edge）安全规定：网页不能自动调 `requestFullscreen()`，必须有人点一下。所以看板页那个"点击任意处进入全屏"遮罩，本质是**第一次手动点一次**。要彻底免点，用 Edge 的 Kiosk 模式启动——浏览器自己一上来就是全屏。

## 硬件

- 一台小主机（N100 盒子 / 闲置旧电脑都行），HDMI 接电视
- 接一次鼠标键盘配好，之后可拔
- 开机进 Windows 即可

## 启动命令（Edge Kiosk）

在桌面新建快捷方式，目标填：

```
"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" --kiosk "https://mes.webok.net:11302/#/board/screen" --edge-kiosk-type=fullscreen --noerrdialogs --disable-session-crashed-bubble
```

> 若 Edge 装在 `C:\Program Files\Microsoft\Edge\Application\msedge.exe`（64 位），把路径换成那个。

## 放进开机自启

1. `Win+R` 输 `shell:startup` 回车，打开启动文件夹
2. 把上面那个快捷方式扔进去
3. 重启验证：开机自动进 Edge → 自动全屏 → 直接看板

## 退出 Kiosk

- `Alt + F4` 关闭浏览器
- 或 `Ctrl + Alt + Del` 任务管理器结束 msedge 进程

## 备用方案（不用 Kiosk 时）

- 接鼠标，打开页面后**点一下中间遮罩**进全屏；或直接按 **F11**
- F11 退出全屏是 Esc
- 缺点：每次开机得手动点一下，不适合无人值守挂墙

## 注意

- 看板页 10 秒自动轮询一次数据，不用管
- 网络要通到 `mes.webok.net:11302`（内网或外网均可，按现场实际地址）
- 若电视分辨率不是 1920×1080，大屏页已做自适应，拉伸即可
