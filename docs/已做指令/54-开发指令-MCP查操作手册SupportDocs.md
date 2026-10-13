# 开发指令：MCP 查操作手册（共用服务器 SupportDocs）

> 状态：**已完成（代码 + 单测 + 文档；扣子联调 E 待你本机验收）**  
> 完成日期：2026-09-20  
> 依据：会话结论（扣子问「扫码报工怎么开始」答非所问）；`SupportDocs/`、`docs/25`、`docs/51`、`docs/45`/`docs/47`/`docs/49`  
> 目标：扣子 Agent 与 PC 智能客服**共用服务器上一套**操作说明目录；用法类问题走 MCP 检索，禁止另开技术方案、禁止在扣子再传一份知识库当主副本。  
> 原则：复用现有 `SupportDocumentService`，不重写检索；不改表、不改报工业务、不改 PC AI 契约。

---

## 1. 一句话

给 MCP 增加「查帮助」工具，只读 `Ai:SupportDocs:RootPath` 目录；扣子问怎么扫码报工时调这个工具，而不是瞎编小程序方案。

---

## 2. 取舍（已定死，实现未改）

| 项 | 决定 |
|---|---|
| 知识放哪 | 只维护服务器 `SupportDocs` 一套 |
| 扣子知识库 | 不做主副本 |
| 实现 | MCP `SearchSupportDocs` → `SupportDocumentService.Search` |
| 鉴权 | 与 `query_work_order` 同（`DemoSkipAuth`） |

---

## 3. 实际改动

| 文件 | 说明 |
|---|---|
| `backend/Mcp/SupportDocsMcpTools.cs` | **新增** MCP 工具 |
| `backend/Services/SupportDocumentService.cs` | KeyPhrases/同义词补「扫码/报工/PDA」（利于 C1） |
| `backend.Tests/SupportDocsMcpToolsTests.cs` | **新增** 8 例包装层测试 |
| `docs/45`、`docs/47`、`docs/49` | 工具 7 个 + 人设「问怎么用」 |
| `SupportDocs/README.md` | 注明 MCP 与 PC 共用 |

---

## 4. 人设增量（已写入 docs/45）

见 `docs/45` 第 1 节完整人设（含菜单 6、【用法 / 怎么操作】）。

---

## 5. 验收清单

### A. 代码与单元边界

- [x] A1 新增 `SupportDocsMcpTools.cs`；`dotnet build` 通过（2026-09-20，旁路输出目录因本机 WorkshopMes.exe 占用默认 bin）。无新 NuGet、无 DDL。
- [x] A2 只调 `Search`；测试断言响应无 `:\\` / `RootPath` / 仓库路径。
- [x] A3 空 query / >500 → `BusinessException`（`SupportDocsMcpToolsTests`）。
- [x] A4 `Enabled=false` → `state=disabled`（同测）。
- [x] A5 `DemoSkipAuth=true` 无 token 可调；`false` 无 token 拒绝（同测）。

### B. 本地 MCP 工具列表

- [x] B1 类带 `[McpServerToolType]`，与现有工具同 `WithToolsFromAssembly` 装配；Description 含 `search_support_docs`。**扣子详情页见 E1。**
- [x] B2 未改动原 6 个 MCP 工具类；总数 = 原 + 1。

### C. 问法命中

- [x] C1～C5：`SupportDocsMcpToolsTests`（扫码报工 / 无报工权限 / nonsense）+ 原有 `SupportDocumentSearchTests`（补报 / 完成数等）共 19 例通过（2026-09-20）。

### D. 回归

- [x] D1 `SupportDocumentSearchTests` 11 例仍通过（检索服务行为保留）。
- [x] D2 未改 `WorkOrderMcpTools`；人设仍规定「查工单」只调 `query_work_order`。
- [x] D3 未改产品唯一 / 报工部门权限 / 删除倒序相关代码。

### E. 扣子联调（需你本机：重启后端 + cpolar + 删 MCP 重建）

- [ ] E1 删旧 MCP 重建并挂到「车间小工单助手v2」，工具列表含 `search_support_docs`（共 7 个）。
- [ ] E2 粘贴 `docs/45` 新人设；问「想试一下扫码报工，如何开始？」→ 调用 `search_support_docs` → 按手册答。
- [ ] E3 同会话「查工单」→ 仍走 `query_work_order`。

> **阻塞说明**：本机关着 WorkshopMes 占用了默认 bin，代码已编过；请你**重启后端**后再做 E。步骤见 `docs/45` / `docs/49`。

### F. 文档收尾

- [x] F1 `docs/45` 已更新。
- [x] F2 `docs/47`、`docs/49` 已更新。
- [x] F3 `SupportDocs/README.md` 已注明共用。
- [x] F4 本文件移入 `docs/已做指令/`（2026-09-20）。

---

## 6. 你这边还要做的 3 步

1. **重启后端**（加载新 MCP 工具；确认 `Ai:SupportDocs:Enabled=true` 且 RootPath 指向有资料的目录）。
2. 扣子：**删除**旧 `laohu-workshop` → 按 `docs/45` JSON **重建** → 挂到 Agent → 应见 **7** 个工具。
3. 人设整段换成 `docs/45` 第 1 节 → 新开对话测「扫码报工怎么开始」+「查工单」。
