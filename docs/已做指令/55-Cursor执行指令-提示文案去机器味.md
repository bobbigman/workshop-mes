# Cursor 执行指令 · 提示文案去机器味（演示顺畅化）

> 日期：2026-09-21｜状态：**已实现**（2026-09-21）。
> 背景：产品用于向客户演示。当前部分提示暴露追踪号、内部参数名、运维话术，客户看不懂；工人点到无权限按钮会弹 403，体验割裂。本次只改提示与入口，不动权限校验逻辑本身。
>
> **三条硬约束：**
> 1. 后端权限校验（`RoleGuardMiddleware` 拦截规则）**一律保留**，只改文案与前端入口展示；
> 2. 服务器日志的完整上下文、脱敏、`X-Trace-Id` **不变**；可改返回给用户的 `msg` 呈现；
> 3. **不新增配置项、不改表/接口契约**；技术栈不变。

## 执行纪律（必读）

- **逐条执行**：从上到下，一条改完并自验通过后，把该条 `- [ ]` 改为 `- [x]`，**再做下一条**。
- 每条含：改哪些文件 → 怎么改 → 怎么验收。验收不过不许打勾。
- 全部完成后跑第 9 条全量回归，并把本文件移动到 `docs/已做指令/`。

## 总清单

- [x] 1. 前端全局错误提示：追踪号只挂技术异常（且不二次拼接）
- [x] 2. 工单页：按角色隐藏管理动作（保留报工/详情）
- [x] 3. 路由守卫：补齐班组长；对齐 SideMenu（工人已有守卫）
- [x] 4. 后端无权限提示改人话
- [x] 5. 技术异常 `msg` 去追踪编号（运维靠响应头+日志；修订 2D/06）
- [x] 6. AI 助手后端错误：去技术词 + 用户 `msg` 不含 `nameof`
- [x] 7. AI 助手前端提示去技术词
- [x] 8. 登录/会话类 + PC 侧兜底杂项提示
- [x] 9. 全量回归验收 + 文档同步

> **不做（本单砍掉）：** 新增 `ShowTraceIdInMessage` 配置开关；MCP/豆包侧 `SalaryMcpTools` 文案（与 PC 演示主路径无关，另开单）。

---

## 1. 前端全局错误提示：追踪号只挂技术异常

**文件**：`frontend/src/api/http.js`

**现状**：
- 成功分支（HTTP 200 且 `body.code !== 0`）：几乎所有错误都拼 `（追踪：xxx）`，业务错误、403 也被拼；
- 错误分支：`err.response` 任意状态都拼追踪号。

**关键事实（勿写错验收）：**
- `ExceptionMiddleware` 技术异常是 **HTTP 200 + `body.code === 500`**（不是 HTTP 500）；
- `RoleGuardMiddleware` 无权限是 **HTTP 403 + `body.code === 403`**；
- 业务校验多为 **HTTP 200 + `body.code === 1`（或其它非 0 业务码）**。

**改法**：
- 成功分支：仅当 `body.code === 500` 时，若 `msg` 本身不含该追踪号，才允许展示追踪相关文案；**`code === 1` / `403` / 其它业务码一律不拼「追踪」**；
- 错误分支：仅当 `err.response?.status === 500` 时才考虑拼追踪（兜底）；403/401/其它 **不拼**；
- 与第 5 条配合后：即使用户侧 tip 已无「追踪编号」，响应头 `X-Trace-Id` 仍在，运维可查。

**验收**：
- 触发业务错误（如工单不存在）→ 提示不含「追踪」字样；
- worker 调无权限写接口 → 403 提示不含「追踪」；
- 触发技术异常（临时抛未处理异常）→ 按第 5 条：用户 tip 无追踪编号；响应头仍有 `X-Trace-Id`。

- [x] 完成

## 2. 工单页：按角色隐藏管理动作（保留报工/详情）

**文件**：`frontend/src/views/order/index.vue`（及同目录相关对话框若含写入口）

**现状**：工单页对工人可见，但「创建工单 / 编辑 / 删除 / 复制 / 开始·结束·撤回·取消 / 打印设置」等**未按角色过滤**。工人点「创建工单」→ POST `/api/WorkOrder` → RoleGuard 403（演示截图场景）。

**改法**：
- 参考 `frontend/src/views/report/index.vue`：`const isAdmin = Number(localStorage.getItem('role') || 0) === 1`；
- **仅管理员可见**：创建工单、编辑、删除、复制、状态流转（开始/结束/撤回/取消/恢复）、打印设置；
- **所有角色保留**：详情、报工（工人本就可 `POST /api/Report`，勿误伤）；
- **打印流转卡**：保留给所有登录角色（只读展示，不写基础数据）；若实现时发现打印依赖仅管理员可调的写接口，再改为仅管理员并在本条备注原因。

**验收**：
- worker：无创建/编辑/删除/复制/流转/打印设置；有详情与报工；点不到 403；
- admin：按钮与现网一致，功能不变。

- [x] 完成

## 3. 路由守卫：补齐班组长；对齐 SideMenu

**文件**：`frontend/src/router/index.js`

**现状（以代码为准，勿写「无守卫」）：**
- 工人（role=2）已有白名单重定向（`/order` `/report` `/stat` `/board` + H5）；直输 `/product` 会回 `/order`；
- AI 页已有 `meta.adminOnly`；
- **缺口**：班组长（role=3）菜单已藏基础数据，但路由未拦，直输 `/product` 等仍进页，加载接口即 403 弹窗。

**改法**：
- 为基础数据 / 系统配置 / 工资 / 排产评分等路由加 `meta: { roles: [1] }`（或等价 `adminOnly: true`，与现有 AI 一致即可，**一种写法吃到底**）；
- `beforeEach`：非管理员访问受限路由 → 静默 `next('/order')`（或班组长可去 `/review`，不要弹 Toast）；
- 保留现有：无 token → `/login`；工人白名单逻辑可收敛进 `meta.roles`，避免两套规则长期分叉（收敛时须回归工人仍能进工单/报工/报表/看板）。

**验收**：
- worker 直输 `/product` → 仍静默回工单页，无 403；
- leader 直输 `/product` → 静默离开，无 403；复核页 `/review` 正常；
- admin 不受影响。

- [x] 完成

## 4. 后端无权限提示改人话

**文件**：`backend/Common/RoleGuardMiddleware.cs`

**现状**：
- role=2：「无权限：生产人员只能查询和报工，不能改基础数据」
- role=3：「无权限：班组长不可操作基础数据」

**改法**：统一为：
- 「该功能仅管理员可用，如需使用请联系管理员」

不改 403 状态码、不改拦截白名单。前端藏入口后正常不触发，仍做人话兜底。

**验收**：`curl.exe` 带 worker token `POST /api/WorkOrder` → `msg` 为上述人话。

- [x] 完成

## 5. 技术异常 `msg` 去追踪编号（不新增配置）

**文件**：`backend/Common/ExceptionMiddleware.cs`；权威文档 `docs/2D-需求分析-服务器错误日志与追踪.md`、`docs/06-开发规约.md`、`.cursor/rules/api-conventions.mdc`（及 docs/33 若有同口径句）

**取舍（已拍板）：** 不增加 `ShowTraceIdInMessage`。演示与生产同一套呈现更简单。

**改法**：
- `SafeTechMsg` 改为不含编号，例如：「操作失败，请稍后重试；若多次出现请联系管理员」；
- 响应头 `X-Trace-Id`、服务器错误日志中的 TraceId **必须仍在**；
- **契约修订**：docs/2D §7、docs/06、api-conventions 中「技术异常 msg = 安全说明 + 追踪编号」改为「技术异常 msg = 安全说明；追踪号只在响应头 `X-Trace-Id` 与服务器日志」。

**验收**：
- 临时抛未处理异常 → 用户 tip 无「追踪」字样；响应头有 `X-Trace-Id`；日志能按该号搜到；
- 上述文档三处口径已改，无旧表述残留。

- [x] 完成

## 6. AI 助手后端错误：去技术词 + 用户 `msg` 不含 `nameof`

**文件**：`backend/Common/ThrowHelper.cs`、`backend/Services/AiToolDispatcher.cs`、`backend/Services/AiAssistantService.cs`

**关键事实（原指令写错了）：**
- `ThrowHelper.Biz(where, what)` 实际拼成 `$"{where}：{what}"` 并作为 `BusinessException.Message` **原样回前端**；
- `nameof(...)` **不是**「只进日志」。只改 `what` 仍会出现 `GetProgressAsync：请提供工单号`。

**改法**：
1. 在 `ThrowHelper` 增加面向用户的重载，例如 `BizUser(string what)` → `new BusinessException(what)`（不含 where）；定位靠中间件已有的 Path / TraceId 日志，不靠拼进 msg；
2. **本条范围内**（AiToolDispatcher / AiAssistantService）凡用户可见的校验，一律改用 `BizUser`（或等价只回中文 what）；
3. 文案替换（以源码为准扫一遍，下表为对照，勿盲改已人话的句子）：

| 若仍含技术词的现状（示例） | 改为 |
|---|---|
| 请提供 workOrderId 或 orderNo | 请提供工单号 |
| query 必填 | 请输入要搜索的内容 |
| query 最多 500 字符 | 搜索内容最多 500 字 |
| productCode 必填 | 请提供产品编号 |
| 请提供 filters | 缺少查询条件，请先通过对话筛选工单 |
| 请提供 conversationId | 会话信息缺失，请重新发起提问 |
| mode 只能是 progress、guide 或 support | 不支持的操作类型，请换个问法 |
| 工具参数不允许字段：{n} | 包含不支持的内容，请调整后重试 |
| 工具 {toolName} 不允许在 {mode} 模式使用 | 该功能在当前模式下不可用 |
| 报工时间格式无效：… | 报工时间格式不正确，请重试 |
| 日期格式无效：… | 日期格式不正确，请重试 |
| AI 助手未启用（Ai:Enabled=false） | AI 助手已关闭，不影响下单与报工 |
| 模型调用失败：…（含 traceId=…） | AI 服务暂时不可用，请稍后重试（traceId 只写日志） |
| 未配置密钥或模型，请联系部署人员 | AI 服务尚未配置完成，请联系管理员 |

**验收**：故意触发一次工具参数错误 → 用户侧 `msg` **无**英文字段名、**无**英文方法名（如 `GetProgressAsync`）、无 `traceId=`。

- [x] 完成

## 7. AI 助手前端提示去技术词

**文件**：`frontend/src/views/ai-assistant/index.vue`

**改法**：
- 连接成功类提示去掉 `elapsedMs` / `ms`（固定「连接成功」即可）；
- 选择产品：优先展示产品名称；若无名称则「选择产品编号 xxx」（产品编号是业务词，可保留）；
- 选择工序：保留业务名称，确认无技术英文字段直出。

**验收**：AI 助手界面无可读的 `ms` / `elapsedMs` 等技术单位。

- [x] 完成

## 8. 登录/会话类 + PC 侧兜底杂项提示

**文件**：`backend/Common/JwtHelper.cs`、`backend/Services/AuthService.cs`、`frontend/src/views/order/PrintLabelDialog.vue`、`backend/Controllers/ScheduleController.cs`、`backend/Services/PriceRuleService.cs`

**改法**：上表位置凡用户可见，与第 6 条一样走 `BizUser`（勿只改 what 仍留下 `LoginAsync：…`）。

| 位置 | 现状 | 改为 |
|---|---|---|
| JwtHelper（多处） | 未登录或 Token 缺少… | 登录状态已失效，请重新登录 |
| AuthService | 工厂代码与本实例不匹配 | 工厂代码不正确，请联系管理员确认 |
| AuthService | 本实例工厂未初始化，请联系运维 | 系统尚未初始化完成，请联系管理员 |
| AuthService | 实例未配置工厂代码 | 工厂代码缺失，请联系管理员 |
| AuthService | 手机号登录未配置短信服务… | 手机号登录暂未开放 |
| PrintLabelDialog | 直出 `e?.message` | 固定「加载打印数据失败，请重试」 |
| ScheduleController | weights 不是合法 JSON：{ex.Message} | 排产权重配置格式不正确 |
| PriceRuleService | 计价方式须为 1计件 / 2计时 / 3固定 | 请选择正确的计价方式 |

**验收**：上述场景提示无 Token/实例/短信服务/英文方法名；打印失败不直出浏览器异常原文。

- [x] 完成

## 9. 全量回归验收 + 文档同步

**验收**：
- [x] `backend`：`dotnet build` 通过；`backend.Tests` 既有测试不回归（若有断言旧 RoleGuard / SafeTechMsg / Biz 文案，同步改断言）；
- [x] `frontend`：`npm run build` 通过；
- [x] 三角色：
  - admin：入口/按钮齐全，功能不变；
  - worker：工单页无管理写按钮，有详情与报工；直输受限 URL 静默重定向；**无 403 弹窗**；
  - leader：无基础数据页；`/review` 正常；直输 `/product` 无 403；
- [x] AI 助手：用户可见提示无英文字段名/方法名；连接测试为「连接成功」；
- [x] 技术异常：用户 tip 无追踪编号；`X-Trace-Id` 头仍在；日志可检索；
- [x] 文档：2D / 06 / api-conventions（及 33 若涉及）已与第 5 条口径一致。

**收尾**：全部勾选后，将本文件移动到 `docs/已做指令/`。

- [x] 完成
