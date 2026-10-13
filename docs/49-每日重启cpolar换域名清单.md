# 49-每日重启 cpolar 换域名清单（开机照着走）

> **用途**：cpolar 免费版域名每次重启都会换新（24h 也变），微信/扣子/方舟链路全部依赖这个公网域名。本清单把「重启 cpolar + 同步改三处配置 + 验收」固定成 6 步，每天开机照做即可。
> **事实来源**：`docs/47`（扣子对接唯一事实来源）、`docs/40`、`docs/45`、`docs/MES 对接火山方舟 MCP 全流程操作明细笔记`、`backend/appsettings.json`。
> **状态**：2026-09-18 按当前隧道整理；域名以 `backend/appsettings.json` 的 `Mcp:AuthBaseUrl` 为准。

---

## 0. 先背下来的当前值（照抄）

| 项 | 值 |
|---|---|
| 后端 MCP 本地地址 | `http://localhost:8080/mcp` |
| 当前 cpolar 域名 | `https://789105f9.r16.cpolar.top`（**重启后必变**，以下方新域名为准） |
| 入口 Token | `laohu-demo-2026` |
| 扣子鉴权方式 | **Header**：`Authorization: Bearer laohu-demo-2026`（禁止 `?token=` 拼 URL） |
| 方舟鉴权方式 | URL 兜底：`https://新域名/mcp?token=laohu-demo-2026`（方舟界面填不了 Header） |
| 应有 7 个工具 | `start_auth`、`query_work_order`、`query_report`、`query_salary`、`calc_schedule_priority`、`query_production_guide`、`search_support_docs` |
| 对外渠道 | 微信机器人（新版 Agent 无公开 H5 / 分享链接） |

---

## 每日 6 步清单（1 → 6 顺序走）

### 1. 启动后端程序（.NET 8 MCP）

- 启动后端，确认本地 `http://localhost:8080/mcp` 可访问。
- 配置在 `backend/appsettings.json`：`DemoSkipAuth=true` 时查工单/查报工免授权，查工资仍需 start_auth 授权。

### 2. 重开 cpolar 隧道（拿新域名）

- 命令：`cpolar http 8080`
- 等 `Tunnel Status: online`。
- 复制 **https:// 开头的新域名**（免费版随机，形如 `https://xxxx.cpolar.top`）。
- ⚠️ **窗口别关**：隧道掉线 = 微信端全挂（后端 + cpolar 必须一直开着）。

### 3. 改后端 AuthBaseUrl → 重启后端

- 文件：`backend/appsettings.json` → `Mcp` 段
- 把 `"AuthBaseUrl": "https://旧域名"` 改成**新域名**（**末尾不带斜杠**）。
- 保存后重启后端。

### 4. 扣子：删旧 MCP → 重建（关键！）

- 扣子 MCP 页 → **删除**旧 `laohu-workshop`（不要编辑，旧失败状态会缓存，编辑往往不重新握手）。
- 新建自定义 MCP，粘贴下面 JSON（**url 换成新域名**）：

```json
{
  "mcpServers": {
    "laohu-workshop": {
      "type": "streamable-http",
      "url": "https://新域名/mcp",
      "headers": {
        "Authorization": "Bearer laohu-demo-2026"
      }
    }
  }
}
```

- ⚠️ ① token 必须放 Header，**禁止 `?token=` 拼 URL**；② MCP 详情页必须列出**全部 7 个工具**（含 `search_support_docs`）。

### 5. 把新 MCP 挂回 Agent + 同步方舟 URL

1. Agent 设置 → MCP → 添加 → 勾选 `laohu-workshop`，确认工具列表 7 个齐全（**最易漏**）。
2. 火山方舟（若还在用）：控制台 MCP URL 换成 `https://新域名/mcp?token=laohu-demo-2026`。

### 6. 验收（两套都过才算通）

- ✅ 扣子调试发「查工单」→ 出现调用 `query_work_order` 并返回真实工单数据。
- ✅ 扣子调试发「扫码报工怎么开始」→ 出现调用 `search_support_docs`（需 `Ai:SupportDocs` 已启用且 RootPath 有资料）。
- ✅ 微信给机器人发「查工单」→ 同样返回工单。
- ℹ️ 浏览器直接开 MCP 地址显示 **405 是正常现象**（MCP 只认 POST），别用浏览器测连通性。

---

## 常见卡点对照

| 现象 | 原因 | 怎么办 |
|---|---|---|
| MCP 页读不到工具 | token 拼在 URL 后；或旧 MCP 缓存失败 | 删旧 MCP 重建；token 放 Header Bearer |
| cpolar online 仍拉不出工具 | 后端没开；或旧 MCP 缓存 | 启动后端；删 MCP 重建触发握手 |
| 聊天只回文字、不调工具 | MCP 没挂到 Agent | 回第 5 步，确认勾选且 5 工具可见 |
| 浏览器开 MCP 返回 405 | MCP 只支持 POST | 正常；以扣子读工具为准 |
| 扣子调试通、微信调失败 | 本机关机 / cpolar 掉线 | 确认后端 + cpolar online |
| 公网打不开授权页 | 隧道地址变了 | 重跑 `cpolar http 8080`，重填 `AuthBaseUrl` 再重启 |

---

## 治本方向（可选，正式期再做）

cpolar 免费域名 24h 变、重启就换，这套 6 步每天都要走。正式上线建议：

1. 换**固定域名 / 固定服务器**（如客户内网固定 IP 服务器常驻），见 `docs/48` 正式阶段方案；
2. 或局域网内网部署 + 固定隧道（见 `docs/05-局域网部署与PDA扫码.md`）。
