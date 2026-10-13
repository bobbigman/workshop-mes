# 79-排障记录-WorkBuddy 连不上 laohu-workshop MCP 的坑

> 日期：2026-09-28｜场景：WorkBuddy 里配了 `laohu-workshop` 连接器，会话里调不到工具；绕过 UI 直接 HTTP 打 MCP 端点，工单查出来了。
> 全文结论：**端点、密钥、服务都没问题，坑集中在「会话加载、域名漂移、协议细节」三层。** 所有数据都是当天实测，不是推测。

---

## 0. 现场还原（当天时间线）

| 时间 | 事件 | 结果 |
|---|---|---|
| 10:5x | 《给伙伴的-WorkBuddy接入配置》定稿，写明当前域名 `39d6fafc.r16.cpolar.top`，并记录"本机 8080 没监听、隧道 404" | — |
| 12:21 | WorkBuddy 对话里说"用连接器 laohu-workshop 查工单" | 会话里**搜不到任何该连接器的工具** |
| 12:22 | 绕过 UI，直接 curl `https://6fbaad7a.r19.vip.cpolar.cn/mcp` | initialize 通（应答 `serverInfo: WorkshopMes v1.1.106.0`）→ tools/list 列出 7 个工具 → query_work_order 返回 **11 张真实工单** |
| 12:44 | 反过来探测上午文档里的"当前域名" `39d6fafc.r16.cpolar.top` | **cpolar 平台 404「domain doesn't exist」，域名已死** |

两条教训先放这：
1. **文档结论有保质期**——上午写的"8080 没监听、域名是 X"，中午就双双过期。一切以当下探活为准。
2. **mcp.json 里存着的域名也别默认信**——它只是"最后一次配置成功的值"，不是"当前值"。

---

## 坑1：mcp.json 配了 ≠ 会话里有工具

**现象**：`C:\Users\bob\.workbuddy\mcp.json` 里配置完好（`disabled: false`），但对话里 Agent 搜不到 laohu-workshop 的任何工具，直接说"没有这个连接器"。

**根因**：MCP 连接器是**会话启动时加载**的。配置是后存的、没点「信任」、或上次握手失败状态被缓存——本会话里就是没有。

**解决**（按顺序）：
1. 连接器管理页看状态灯，出现「信任」就点（不点等于没接）；
2. 还不行 → **删掉这条连接器重新新建**（失败状态会缓存，光改配置不容易重新握手）；
3. 新开会话再试。

**旁证**：和《给伙伴的-WorkBuddy接入配置》第三节"列不出来 → 删旧重建"是同一个坑的两面。

小注：mcp.json 里名字叫 `laohu-workshop`，给伙伴文档示例叫 `workshop-mes`——**名字不影响连接**，排查时别按名字死磕。

---

## 坑2：cpolar 域名漂移——两个"权威"当场打架

| 来源 | 域名 | 实测（12:22~12:44） |
|---|---|---|
| mcp.json 存量配置 | `6fbaad7a.r19.vip.cpolar.cn` | ✅ 通，WorkshopMes v1.1.106.0 应答 |
| 当天 10:5x 的"给伙伴"文档（自称当前值） | `39d6fafc.r16.cpolar.top` | ❌ cpolar 平台 404，域名不存在 |

**教训**：域名漂移不是天级是**小时级**。谁也别默认信，逐个 initialize 探活：

```bash
curl -s --max-time 15 -X POST "https://<候选域名>/mcp" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -H "Authorization: Bearer laohu-demo-2026" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"probe","version":"1.0"}}}'
```

**认 `serverInfo` 里的 `WorkshopMes`，不要只看 HTTP 状态码。** 两种 404 要分清：

| 404 长相 | 含义 | 动作 |
|---|---|---|
| cpolar 蓝色错误页「The page you were looking for domain doesn't exist」 | **域名死了**（隧道重启换名） | 换域名，mcp.json 和所有文档同步改 |
| 后端 JSON「MCP 未启用」（来自 `McpGateMiddleware`） | 域名活着，但该实例 `EnableMcp=false` | 去服务器改配置，见"给伙伴"文档第六节 |

制度性根源：免费 cpolar 每次重启换域名（见 `49-每日重启cpolar换域名清单.md`）。域名以服务器 `Mcp:AuthBaseUrl` 为准。

---

## 坑3：直调 MCP 端点的协议细节（绕过客户端排查时必看）

1. **Accept 头必须带 `text/event-stream`**：
   `Accept: application/json, text/event-stream`
   streamable-http 的硬要求，只发 `application/json` 部分服务器直接拒。
2. **响应是 SSE 不是裸 JSON**：格式是 `event: message` 换行 `data: {...}`。要解析 `data:` 那一行，别把整个响应体当 JSON parse。
3. **服务端不下发 `Mcp-Session-Id`** → 无状态。initialize 一次确认活之后，tools/list、tools/call 直接发，不用带会话头。
4. **走系统代理时**响应第一行是 `HTTP/1.1 200 Connection Established`（CONNECT 隧道建立），真正的 HTTP 响应头在其后。排障时加 `-i` 看全，别被第一行骗了。
5. **必带 `--max-time`**（实测 15~30s），cpolar 隧道卡死时不会把终端挂死。
6. Windows 下用 Git Bash 发，JSON 用单引号包；别用 cmd（转义地狱）。

---

## 坑4：返回体要剥两层

外层是 JSON-RPC，业务数据藏在 `result.content[0].text`，而且它**还是个字符串**（内含 unicode 转义），要再 parse 一次：

```
外层: {"result":{"content":[{"type":"text","text":"{\"code\":0,\"total\":11,\"list\":[...}]"}]}}
内层: {"code":0, "total":11, "list":[{Id, OrderNo, ProductName, Qty, DoneQty, ProgressPercent, Status, DueDate, DueState, Ops[], ...}]}
```

状态字典（解析返回时对照）：
- 工单 `Status`：0 未开始 / 1 执行中 / 2 已完成 / 3 已取消
- 报工 `ReviewStatus`：0 待复核 / 1 已通过 / 2 已退回
- 交期 `DueState`：normal / **overdue（已逾期，对话里必须点名报出来）**

---

## 坑5：授权码——哪些工具要、哪些不要

| 工具 | 演示模式（DemoSkipAuth=true） | 正式实例 |
|---|---|---|
| query_work_order / query_report / query_production_guide / search_support_docs | `authToken` 传 null 直接查 | 需先 `start_auth` 拿码 |
| query_salary / calc_schedule_priority | **必须先 `start_auth` 拿金蝶授权码**（写死在工具说明里） | 同左 |

工资、排产查不了是**权限设计不是故障**，别在这上面浪费排障时间。

---

## 附：三段可粘贴的 curl（URL 换成探活通过的当前域名）

```bash
BASE="https://6fbaad7a.r19.vip.cpolar.cn/mcp"   # ← 每次先探活再填
H1="Content-Type: application/json"
H2="Accept: application/json, text/event-stream"
H3="Authorization: Bearer laohu-demo-2026"

# 1) 握手
curl -s --max-time 30 -X POST "$BASE" -H "$H1" -H "$H2" -H "$H3" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"cli","version":"1.0"}}}'

# 2) 列工具（7 个 = 通）
curl -s --max-time 30 -X POST "$BASE" -H "$H1" -H "$H2" -H "$H3" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'

# 3) 查工单（演示模式免授权码）
curl -s --max-time 30 -X POST "$BASE" -H "$H1" -H "$H2" -H "$H3" \
  -d '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"query_work_order","arguments":{"authToken":null,"keyword":null,"status":null}}}'
```

---

## 一分钟体检顺序（下次连不上照这个走）

1. 服务活着？→ `curl http://<MES机器IP>:8080/` 出登录页才算活
2. 域名探活？→ 逐个 curl initialize，**认 serverInfo=WorkshopMes**
3. 401？→ `Bearer` 后有一个空格、token 逐字对（`Mcp:Token`）
4. `/mcp` 404 且报「MCP 未启用」？→ 该实例 `EnableMcp=false`，改服务器配置
5. 客户端搜不到工具？→ 点信任 → 删旧重建 → 新开会话
6. 通的标志：tools/list 列出 7 个工具；说人话查询回的是**真实工单号**，开始编数 = 没连上

---

## 关联文档

- 《给伙伴的-WorkBuddy接入配置（可直接粘贴）.md》——接入总纲。⚠️ 其第一、二节里的"当前域名"已再次过期（本文坑2），**贴给伙伴前先探活换域名**
- `49-每日重启cpolar换域名清单.md`——域名漂移的制度性根源
- `37-ClashVerge-TUN排障记录.md`——本机代理层排障（连不上先分清是代理还是隧道）
- `35-MCP上线待办清单.md` / `36-金蝶MCP排产工具清单.md` / `40-扣子重搭重接MCP并发布微信网页.md`——MCP 上线与历史踩坑
