# 37 - Clash Verge TUN 模式与微信/豆包冲突排障记录

> 场景：车间机上同时跑 Clash Verge（Ghelper 订阅，TUN 模式访问 ChatGPT）和本地车间系统。
> 问题：微信发图红叹号、微信/豆包 PC 客户端不能自动登录。
> 本文记录完整排查路径、根因与最终可用配置。

---

## 1. 现象

| # | 现象 | 触发条件 |
|---|---|---|
| 1 | 微信 PC 版发截图，消息全带**红色感叹号**（发送失败） | Clash Verge 开着 |
| 2 | 微信不能**自动登录**，每次要手机扫码 | 同上 |
| 3 | 豆包 PC 版**卡死**，登录界面转圈 | 同上 |
| 4 | 手动杀掉 `verge-mihomo.exe` 进程后，微信/豆包立刻恢复，但**几十秒内 mihomo 自动重新拉起** | — |

> 关键判据：杀掉 mihomo 就恢复 → 问题 100% 出在代理，不在微信/豆包/系统。

---

## 2. 根因

四个因素叠加：

1. **TUN 模式（虚拟网卡）接管全部流量**：`enable_tun_mode: true`，所有 TCP/UDP 被拽进 mihomo 内核，即使规则写 DIRECT 也要绕一圈虚拟网卡。
2. **TUN 栈是 `gvisor`**（用户态栈）：兼容性最差，对微信/豆包的长连接、登录信令、QUIC 处理不好，容易把连接搞死。
3. **系统代理也开着**：`enable_system_proxy: true` + TUN 双管齐下，流量被处理两次。
4. **规则不完整**：微信 CDN 域名（`qpic.cn` 等）没在 DIRECT 名单里，被兜底规则处理时 TUN 又不兼容，直接发图失败。

---

## 3. 排查过程（按顺序照做）

### 3.1 先排除"是不是断网了"
```powershell
# 系统代理状态
Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' | Select ProxyEnable, ProxyServer

# 外网连通性
Invoke-WebRequest https://www.baidu.com -UseBasicParsing
Invoke-WebRequest https://weixin.qq.com -UseBasicParsing

# 代理进程
Get-Process | ? { $_.ProcessName -match 'mihomo|clash|verge' }

# TUN 虚拟网卡
Get-NetIPAddress -AddressFamily IPv4 | ? { $_.IPAddress -like '198.18.*' }
```
> 结果：百度/微信官网都 200，说明**不是断网**；`verge-mihomo.exe` 在跑、`198.18.0.1` 虚拟网卡在 → 锁定代理。

### 3.2 杀进程发现杀不动
```powershell
taskkill /F /IM verge-mihomo.exe
# ERROR: Access is denied
```
> 原因：mihomo 由 `clash-verge-service` 系统服务拉起，进程权限高于普通管理员，硬杀没用。
> 且 Verge GUI 在跑时会**自动重启内核**，杀掉也白杀。

### 3.3 读 Verge 实际配置
配置目录：`%APPDATA%\io.github.clash-verge-rev.clash-verge-rev\`

关键文件：
- `verge.yaml` —— Verge 自身开关（TUN、系统代理、开机自启）
- `clash-verge.yaml` —— 合并后实际运行的 mihomo 配置
- `profiles\rUcv3QGwqI1C.yaml` —— 规则扩展（prepend/append/delete）

```yaml
# 病根就在这两行
tun:
  stack: gvisor        # ← 兼容性差，改成 system
  enable: true
```

---

## 4. 解决方案（最终可用配置）

### 4.1 规则补全：微信 + 字节系全部 DIRECT

文件：`profiles\rUcv3QGwqI1C.yaml`，在 `prepend` 最前面加：

```yaml
prepend:
  # ↓ 微信全家桶直连（qpic.cn 是图片 CDN，最关键）
  - 'DOMAIN-SUFFIX,weixin.qq.com,DIRECT'
  - 'DOMAIN-SUFFIX,wechat.com,DIRECT'
  - 'DOMAIN-SUFFIX,qpic.cn,DIRECT'
  - 'DOMAIN-SUFFIX,wx.qq.com,DIRECT'
  - 'DOMAIN-SUFFIX,wxapp.tc.qq.com,DIRECT'
  - 'DOMAIN-SUFFIX,weixinbridge.com,DIRECT'

  # ↓ 字节系（豆包/飞书）—— 原来已有，保留
  - 'DOMAIN-SUFFIX,zijieapi.com,DIRECT'
  - 'DOMAIN-SUFFIX,bytedance.com,DIRECT'
  - 'DOMAIN-SUFFIX,doubao.com,DIRECT'
  - 'DOMAIN-SUFFIX,feishu.cn,DIRECT'
  - 'DOMAIN-SUFFIX,feishu.com,DIRECT'
  - 'DOMAIN-SUFFIX,bytecdn.cn,DIRECT'
  - 'DOMAIN-SUFFIX,bytetos.com,DIRECT'

  # ↓ 翻墙的走 Ghelper（原样保留）
  - 'DOMAIN-SUFFIX,openai.com,Ghelper'
  - 'DOMAIN-SUFFIX,chatgpt.com,Ghelper'
  # ... 其他原有规则 ...
  - 'MATCH,DIRECT'
```

### 4.2 TUN 栈换成 system

文件：`profiles\mffnLb1TBpT0.yaml`（merge 扩展）：
```yaml
tun:
  stack: system
```

> `gvisor`（用户态）→ `system`（内核态）：兼容性天差地别，微信/豆包长连接不再被搞死。

### 4.3 系统代理保持开着（浏览器快）

`verge.yaml`：
```yaml
enable_system_proxy: true     # 浏览器走 127.0.0.1:7897，不绕 TUN，快
enable_tun_mode: true        # TUN 保留，管需要它的应用/ChatGPT
enable_auto_launch: false     # Verge 不开机自启
```

> 为什么系统代理要开着：关了它，浏览器流量全绕 TUN 虚拟网卡，**明显变慢**；开着时浏览器直连 mihomo 端口，速度正常。

### 4.4 改完必须重启 Verge

配置文件改了不会热生效，要：
1. 托盘 Verge 图标右键 → **退出**（连内核一起退）
2. 重新打开 Verge
3. **重启微信、豆包**（完全退出再开，清掉旧长连接）

---

## 5. 常见坑速查表

| 坑 | 现象 | 解法 |
|---|---|---|
| gvisor 栈 | 微信/豆包卡死、不能登录 | `tun.stack: system` |
| 微信图片 CDN 没规则 | 发图全红叹号 | 加 `DOMAIN-SUFFIX,qpic.cn,DIRECT` |
| 系统代理 + TUN 双开 | 奇怪卡顿 | 保留系统代理开着（浏览器快），TUN 也开，规则兜底 DIRECT |
| 杀不掉 mihomo | Access denied | 别硬杀，托盘退出 Verge 或重启电脑 |
| mihomo 杀了又起 | 几十秒自动回来 | Verge GUI 在守护，退出 GUI 才停 |
| 改配置不生效 | 改完文件没反应 | Verge 要完全退出重开，不是只重载 |

---

## 6. 与本系统其他改动的关系

本次同期还做了：
- **车间系统前端上 HTTPS**（mkcert，见 `frontend/vite.config.js` + `frontend/certs/`）：手机摄像头扫码要求 HTTPS，`https://192.168.3.7:5173/`。
- cpolar 隧道（`restart-tunnel.bat`）只管后端 8080 的 MCP 公网入口，**不要拿来给前端用**。

这两件事和 Clash Verge 排障相互独立，不要混。

---

## 7. 验证清单（改完逐项确认）

- [ ] 微信 PC 版发截图不再红叹号
- [ ] 微信重启后能自动登录（不用扫码）
- [ ] 豆包 PC 版能正常登录、不卡
- [ ] 浏览器访问 ChatGPT 正常
- [ ] 网页浏览速度正常（不慢）
- [ ] 车间系统 `https://192.168.3.7:5173` 正常
