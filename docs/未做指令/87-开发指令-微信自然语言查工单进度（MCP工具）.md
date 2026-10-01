# Cursor 开发指令 · 微信自然语言查工单进度（MCP 工具）

> 日期：2026-09-29｜状态：待开发（分析稿已核；**2026-09-29 Cursor 复核后已补条件分支**，待拍板开工）
> 定位：给老板一条**手机微信自然语言查工单进度**的路，复用已有链路与已验证口径，只新增一个 MCP 工具，不重造自然语言引擎。
> 依据：[2B](2B-需求分析-借鉴灵基的车间AI助手.md)、[26](26-开发指令-PC内置AI助手.md)、[48](48-制造知识库-文档问答-系统分析.md)、[25](已做指令/25-开发指令-AI智能客服与数据解释.md)、[51](51-AI智能客服-逐项执行与验收清单.md)

---

## 一、已查证现状（开工前必须复核，不靠历史文档猜）

| 能力 | 现状 | 入口 |
|---|---|---|
| 微信自然语言**问工艺做法** | **已通**（docs/48 已实现） | 微信 → 扣子 → MCP `query_production_guide` → 后端知识库 |
| 自然语言**查工单进度** | **已实现但仅 PC 管理员端**（docs/25/51，真实 DeepSeek 联调通过） | `/api/ai-assistant` 内工具 `get_work_order_progress`，role=1；手机/H5 首版未开放 |
| MCP 已有 `query_work_order` | **列表摘要**（关键词/状态，最多 20 条），含 DoneQty / ProgressPercent / Ops，但**不是** PC 助手那种「单张详进度」 | `WorkOrderMcpTools.QueryWorkOrder` |
| 进度口径 | 已验证：doneQty=各工序有效良品最小值、复核双轨 0+1/1 | `WorkOrderProgressCalculator` / `WorkOrderService` / PC `AiToolDispatcher.GetProgressAsync` |

**结论**：缺的是把「单张详进度」接到手机微信 MCP，不是新造引擎。走现有微信→扣子→MCP 链路，后端新增一个 MCP 工具即可。

**与现有工具分工（必写进扣子人设，否则意图打架）**：

| 用户说法 | 调哪个 | 不调哪个 |
|---|---|---|
| 「查工单 / 今天有哪些单 / 列表」 | `query_work_order` | 不要用新工具顶掉列表 |
| 「GDxxx 做到哪了 / XXX 单进度 / 第几道了」 | **`query_work_order_progress`（新）** | 不要只调列表工具糊弄 |

> 现状人设（`docs/45`）把「工单进度」绑在 `query_work_order` 上——步骤 2 **必须改人设分流**，不能只补示例。

---

## 二、技术栈与硬约束（不可变更）

- .NET 8 Web API + EF Core + SQL Server；命名空间 `ahu.MicrosoftMes`。
- **不建业务表、不扩大角色、不重写原 MCP/微信/扣子功能**。
- 只读；权限/工厂/用户隔离沿用现有 MCP 授权与 JWT 机制（`DemoSkipAuth` / `RequireAuthAsync` / `Mcp:FactoryId` 同现有工具）。
- 异常统一 `Common/ThrowHelper.cs`，不吞、带上下文。
- `docs/02` 无 DDL 变更则不改。
- 出参**不吐**：工资、单价、密码、硬盘路径、连接串、Assignee 敏感细节可省略（进度工具不需要派工人）。

---

## 三、业务口径（复用 PC 助手已验证，禁止另造）

- `doneQty` = 各工序有效良品**最小值**（`Min`），不得累加。对齐 `WorkOrderProgressCalculator` / PC `get_work_order_progress`。
- 复核双轨：进度算 0+1（排除退回 2），报表/工资只算 1（见 `docs/02` §3）。`GetByOrderNoAsync` / `GetAsync` 已按此汇总，**直接复用，勿另写 SQL**。
- 「未完成」= 状态 0/1；交期差天用 `AiBusinessTime` + `Ai:BusinessTimeZone`（默认 Asia/Shanghai），**禁止**用 `DateTime.Now` 直接减。
- 只回进度与交期等必要字段给模型；模型只负责语言表达，不猜总数、不算金额。

---

## 四、原子步骤（每步显式带条件判断）

### 步骤 1 · 后端新增 MCP 工具 `query_work_order_progress`（Cursor 可闭环）

- 写法对齐 `KnowledgeMcpTools` / `WorkOrderMcpTools`：`[McpServerToolType]` + `authToken` + `DemoSkipAuth`。
- **落点**：优先加在 `WorkOrderMcpTools.cs`（同文件第二工具）；若文件过长再拆，默认不拆。
- **口径复用**：内部调 `IWorkOrderService.GetByOrderNoAsync` / `GetAsync`（或先 Query 再 Detail），整单 `doneQty = Tasks.Min(DoneQty)`，与 PC `GetProgressAsync` 一致。
- 入参：
  - `authToken`（同现有）
  - `orderNo?`、`productCode?`、`productName?`、`operationName?`
- 出参（单张命中时）：工单号、产品编号/名称、计划数 qty、整体 doneQty / remainQty / progressPercent（有则给）、交期 dueDate、距交期天数 daysToDue（可负=逾期）、状态 status、各工序（seq / name / planQty / doneQty；**当**有 `operationName` → 只返回名称匹配的那几道，整单 doneQty 仍按**全工序** Min 算，避免局部工序误导整单完成数）。
- **当** `McpTools` 通过 `WithToolsFromAssembly` 自动扫描（`Program.cs` 已确认）→ 加完工具自动可见，无需额外注册。
- **当** 是手动注册列表 → 显式加一行。（现状不是，本条作兜底。）
- **当** 与 PC 助手口径不一致 → 以已验证口径为准（`doneQty=Min`、双轨 0+1/1）。
- **解析规则（缺则易脑补，必须按此）**：
  - **当** 未给任何 `orderNo` / `productCode` / `productName` → 拒绝：「请给工单号或产品编号/名称」。
  - **当** 只给 `orderNo` → 按单号精确查（工厂内）；找不到 → `found=false`，文案「未找到该单」。
  - **当** 只给产品（code 和/或 name）→ 工厂内筛未完成（status 0/1）工单：
    - 命中 **0** → `found=false`，「未找到相关工单」。
    - 命中 **1** → 直接返回该单详进度。
    - 命中 **多条** → **不取第一条**；返回 `found=false, candidates=[...]`（最多 10 条：orderNo/productCode/productName/status/dueDate），提示「请给具体工单号」。
  - **当** 同时给 `orderNo` 与产品条件 → 先按单号取单；**若**该单产品与给定 code/name 不符 → 拒绝，「单号与产品不一致」，不取其它单。
  - **当** `productCode` 与 `productName` 同时给且指向不同产品 → 拒绝，不取第一条。
- 验收：MCP `tools/list` 出现新工具（总数 7→**8**）；`dotnet build -c Release` 0 错 0 警。

### 步骤 2 · 扣子 Agent 挂载与人设分流（需用户在扣子控制台操作）

- **当** 扣子 MCP 插件已连接 → 刷新/重建 MCP 后智能体启用新工具（应看到 8 个）。
- **当** 现人设把「工单进度」绑在 `query_work_order` → **必须改人设**：
  - 「查工单 / 列表 / 今天有哪些单」→ `query_work_order`
  - 「做到哪了 / 进度 / 第几道」→ `query_work_order_progress`
- **当** 需识别「查进度」意图 → 补自然语言示例：「查一下 GDxxx 做到哪了」「XXX 单进度」。
- **当** 示例可复用已有意图菜单 → 可在开场菜单加一项「查进度」，或说明「直接说单号+进度」；不加新对话流。
- 同步改 `docs/45` 人设段落（权威照抄源）。
- 验收：扣子侧工具列表可见新工具；测试问法调度到新工具，不误调列表工具。

### 步骤 3 · 微信实测验收（四类必测，需用户侧留证）

- **当** 走通 → 微信发「GDxxx 做到哪了」，回「第 N 道工序、完成 X%、距交期 X 天」（表述由模型组织，数据必须来自工具）。
- **当** 查不到/模糊多单 → 回「未找到该单 / 请给单号」，**不捏造**（对齐 docs/48 E05）。
- **当** 涉及敏感字段 → 确认只回进度交期，不吐工资/路径/密钥。
- **当** 断网/无资料/模型失败 → 明确失败原因；追踪号在响应头/日志，不假成功（对齐 docs/51 E08）。
- 验收：四类场景各有真实证据；不静默造假。

### 步骤 4 · 文档同步（Cursor 可做文案；定价句由业务拍板）

- **当** 步骤 1 通 → 更新 `docs/48`、`docs/45`、接口/MCP 说明：微信可查「进度 + 做法」两类；工具数 8。
- **当** 放进入门款 → 作钩子；企业版/旗舰版全量。（定价句是否写入对外稿，**开工前问一句**；默认本步只改技术文档。）
- **当** `docs/02` 无 DDL 变更 → 不改。
- 验收：引用链接有效、口径与 PC 助手一致、人设分流已写进 `docs/45`。

---

## 五、边界（对外口径，先跟客户说清）

- **查进度/问做法 = 联网增值**（微信服务器需能访问客户服务器/穿透）。
- **报工/看板 = 断网核心**，断外网照用。两者不混为一个卖点。
- 不承诺未闭环能力；不承诺在线时长。

---

## 六、谁做哪步（避免 Cursor 闷头干扣子/微信）

| 步骤 | 谁 | 说明 |
|---|---|---|
| 1 后端 MCP 工具 + build | **Cursor** | 本机可闭环 |
| 2 扣子挂载 + 改人设 | **用户**（Cursor 可改 `docs/45` 文案供粘贴） | 需登录扣子 |
| 3 微信四类实测 | **用户** | 需穿透 + 微信渠道 |
| 4 技术文档同步 | **Cursor**；定价对外句可选 | |

---

## 七、验收清单（勾选用，做一项勾一项）

- [x] 步骤 1：`query_work_order_progress` 已加 + `dotnet build -c Release` 0 错 0 警（2026-09-29，v1.1.115）
- [ ] 步骤 2：扣子挂载 + **人设分流**（进度 ≠ 列表）+ 意图调度（人设文案已写入 `docs/45`，待控制台粘贴/刷新 MCP）
- [ ] 步骤 3：微信实测四类场景（成功/查不到/敏感/断网）均留证据
- [x] 步骤 4：技术文档已同步（`docs/45` 人设+8 工具、`docs/48` 进度/做法分流、WorkBuddy 接入表）；定价不写

> 完成前复核：输入已读全、约束未越界、口径与 PC 助手一致、实测留证、文档同步、人设不再把「进度」绑死在 `query_work_order`。
