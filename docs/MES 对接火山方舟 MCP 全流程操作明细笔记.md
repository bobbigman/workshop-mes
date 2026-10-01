# MES 对接火山方舟 MCP · 全流程操作明细笔记

> 版本：v2.0（2026-09-17）｜ 状态：**已打通，方舟对话已查到工单**
> 本笔记取代旧版（旧版误判「MCP 未实现、需手写 Streamable-HTTP」，见末尾「历史误区」）。
> 事实来源：`backend/Program.cs`、`backend/Mcp/*`、`backend/appsettings.json`，以及 2026-09-17 实测。
> 相关文档：`docs/30-火山打通指令`、`docs/35-MCP上线待办清单`、`docs/37-上线操作手册`、`docs/38-豆包逐步教我填方舟`。

---

## 一、结论先行（别再当「没接上」）

| 项 | 事实 |
|---|---|
| MCP 服务 | **代码早已就绪**，不是待开发 |
| 协议栈 | `ModelContextProtocol.AspNetCore`（官方包），Streamable HTTP |
| 入口 | `POST /mcp`，路径固定是 **`/mcp`**，不是 `/mcp/rpc` |
| 工具 | `start_auth` / `query_work_order` / `query_report` / `query_salary` / `calc_schedule_priority`（共 5 个） |
| 鉴权 | 两层：①`Mcp:Token` 进门口令；②`sys_mcp_auth` 配对码 → 授权码（业务授权） |
| 最终结果 | 方舟对话「查工单」已返回 10 条工单 ✅ |

**一句话：** 这次真正的活儿是「隧道指对端口 + 方舟填对 URL + 走通授权闭环」，**没有新增任何 MCP 协议代码**。

---

## 二、真实架构（当前代码现状）

### 2.1 Program.cs（已存在，无需改）

```csharp
// MCP Server（豆包方舟演示）：Streamable HTTP，工具从 [McpServerToolType] 类自动扫描
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
    .WithToolsFromAssembly();
// ...
app.UseMiddleware<McpTokenMiddleware>();   // 入口 token 校验
// ...
app.MapMcp("/mcp");
```

- 工具靠 `[McpServerToolType]` / `[McpServerTool]` 特性自动扫描，**不手写 `list_tools` / `call_tool`**。
- `MapMcp("/mcp")` 就是标准 Streamable HTTP 端点。

### 2.2 工具文件（`backend/Mcp/`）

| 文件 | 工具 | 作用 |
|---|---|---|
| `AuthMcpTools.cs` | `start_auth` | 生成一次性配对码，返回 H5 授权链接 |
| `WorkOrderMcpTools.cs` | `query_work_order` | 查工单（需授权码） |
| `ReportMcpTools.cs` | `query_report` | 查报工（需授权码） |
| `SalaryMcpTools.cs` | `query_salary` | 查工资（需授权码 + 财务角色） |
| `ScheduleMcpTools.cs` | `calc_schedule_priority` | 排产优先级（需授权码） |

### 2.3 两层鉴权（别混）

| 层 | 在哪 | 校验什么 | 失败表现 |
|---|---|---|---|
| ① 进门口令 | `McpTokenMiddleware` | `Authorization: Bearer {Mcp:Token}` 或 URL `?token=` | 401 `unauthorized` |
| ② 业务授权 | `McpAuthService` + `sys_mcp_auth` | 配对码换授权码 → 工具带授权码 | 抛「未授权/授权码无效/已过期」 |

> 进门口令是静态串（`appsettings` 的 `Mcp:Token`）；配对码/授权码是数据库 `sys_mcp_auth` 记录。**两者不是一回事，别把 Token 插进 `sys_mcp_auth`。**

### 2.4 当前配置（`backend/appsettings.json` → `Mcp` 段）

```json
"Mcp": {
  "Token": "laohu-demo-2026",                        // 进门口令（演示完轮换）
  "FactoryId": 1,
  "AuthBaseUrl": "https://209c3f5f.r19.vip.cpolar.cn", // cpolar 公网根，必须指后端 8080
  "AuthLocalBaseUrl": "http://localhost:8080",
  "CodeExpireMinutes": 30,                            // 配对码有效期（已从 5 调到 30）
  "TokenExpireHours": 2,                              // 授权码有效期
  "FinanceKingdeeAccounts": "boss"                    // 可查工资的账号名单
}
```

---

## 三、全流程实际操作记录（这次真实做了什么）

### 3.1 本地验证（✅ 通过）

| 探测 | 结果 | 说明 |
|---|---|---|
| `GET /mcp-auth?code=probe` | 200 | 授权页能开 |
| `POST /mcp` 正确 Bearer | 400 | 空 `{}` 不是合法 JSON-RPC，入口在 |
| `POST /mcp` 错误 Bearer | 401 | 鉴权在 |
| `POST /mcp/rpc` | 404 | 路径不存在，**别用 `/rpc`** |

### 3.2 定位「方舟拉不到工具」的根因

- 旧公网域名 `https://2e2c5459.r19.vip.cpolar.cn` 打开返回的是 **Vite 前端页**（带 `/@vite/client`、`<title>车间管理系统</title>`）。
- 判断：cpolar 当时把流量打到了 **前端 5173**，不是后端 8080。所以 `POST /mcp` 在旧域名上 404。
- 处理：重跑 `cpolar http 8080`，拿到新域名 `https://209c3f5f.r19.vip.cpolar.cn`（已确认指后端 Kestrel）。
- 更新 `Mcp:AuthBaseUrl` 为新域名，重启后端。

### 3.3 方舟界面填不了 Header 的兜底

方舟远程 MCP 弹窗**没有自定义请求头**输入位。为不卡在这，`McpTokenMiddleware` 增加 URL 兜底：

```csharp
// 优先 Header；方舟界面填不了自定义头时，允许 URL ?token= 兜底
var queryToken = context.Request.Query["token"].ToString();
// ... headerOk || queryOk 即通过
```

于是方舟里 URL 直接填带 token 的形式：

```
https://209c3f5f.r19.vip.cpolar.cn/mcp?token=laohu-demo-2026
```

### 3.4 授权闭环踩的坑（配对码过期）

- 现象：方舟给旧链接 `code=DSGPMWD4`，用户点开填 `boss` 后报「授权码无效」。
- 根因：配对码默认 **5 分钟过期**，用户 12:02 过期后 12:30 才填（库里 `code_expire` 已过）。
- 修复：
  1. `CodeExpireMinutes` 5 → **30**；
  2. `McpAuthService` 新增 `PeekCodeAsync`，打开授权页时**先预检配对码**，过期直接提示，不再让人填完账号才报错；
  3. 授权页文案从「金蝶账号授权」改为「车间系统授权」，副标题写明「演示期账号密码非空即可」。

### 3.5 最终结果（✅）

- 方舟对话说「查工单」→ 调 `start_auth` 出新链接 → 填 `boss` + 任意密码 → 拿授权码贴回 → 返回 **10 条工单**（其中 2026-09-14 创建 8 条）。
- 工资拦截：`boss` 可查、非财务账号（如 `worker1`）被拦「仅财务人员可查询」。

---

## 四、给 Cursor / 后续维护者的核对要点

1. **不要**新建 `AddMcpRpc` / `UseMcpRpc` / 自造 JSON-RPC 中间件（协议栈已有）。
2. **不要**把路由改成 `/mcp/rpc`（端点就是 `/mcp`）。
3. **不要**把 `Mcp:Token` 插进 `sys_mcp_auth` 当入口 token（两层鉴权分开）。
4. 打通方舟**不改工单/报工/工资业务逻辑**。
5. 若方舟连不上：先查「隧道是否指 8080 后端」和「URL 是不是 `/mcp`」，再查 Bearer/`?token=`，**最后才怀疑代码**。
6. cpolar 免费域名**会变**；变了要同步 `Mcp:AuthBaseUrl` 并重启后端。

### 验证命令（本地）

```powershell
# 入口鉴权：对 → 400（空报文非 JSON-RPC）；错 → 401
curl.exe -s -w "`nHTTP:%{http_code}`n" -X POST "http://localhost:8080/mcp" `
  -H "Authorization: Bearer laohu-demo-2026" -H "Content-Type: application/json" -d "{}"

# URL token 兜底（方舟无 Header 时用）
curl.exe -s -w "`nHTTP:%{http_code}`n" -X POST "http://localhost:8080/mcp?token=laohu-demo-2026" `
  -H "Content-Type: application/json" -d "{}"
```

---

## 五、历史误区（为什么旧版笔记是错的）

旧版（以及 `docs/未做指令/29`、`31`）的共同误判：

| 误区 | 事实 |
|---|---|
| 「后端没实现 MCP、要手写 Streamable-HTTP」 | `ModelContextProtocol.AspNetCore` 已实现，`MapMcp("/mcp")` 在 |
| 「要写 `list_tools`/`call_tool`」 | 标准是 `tools/list`/`tools/call`，由框架自动处理 |
| 「GET `/mcp` 是前端页、POST 要做 MCP，前后路由隔离」 | 前后端分离：页面在 5173，MCP 在 8080；问题在隧道指错端口，不在路由 |
| 「方舟不能填 Header → 只能写死鉴权在后端」 | 鉴权本来就在后端；加 `?token=` 兜底即可，不必手写协议 |
| 「公网 `2e2c5459` 是对的」 | 该域名当时打到前端 5173，`/mcp` 404 |

**正确链路：** 后端 8080 `MapMcp("/mcp")` ← cpolar 隧道指 8080 ← 方舟填 `https://<新域名>/mcp`（带 Bearer 或 `?token=`）← 授权闭环 ← 查工单。
