# Cursor 开发指令 · PC 内置 AI 助手

> 日期：2026-09-20｜状态：未执行。可以让 Cursor 读取本文件后按阶段实现。
> 需求依据：[2B 需求分析](../2B-需求分析-借鉴灵基的车间AI助手.md)。交付检查：[50 开发与验收清单](../50-PC内置AI助手-开发与验收清单.md)。
> 口径补充：改掉 `docs/48` 的默认不内嵌；交期筛选不复用页面三态；旧列表和旧知识查询不改默认行为；点选只认本轮服务端候选。

## 1. 开发任务

在现有车间管理系统中增加管理员 PC 页面“AI 助手”，通过后端调用 DeepSeek，完成“查进度”“问工艺”两个只读场景。请实际完成前后端、必要测试及文档同步，不只提供示例代码。

先阅读项目 AGENTS.md、docs/01、02、03、06、48，以及 `.cursor/rules/` 相关规则。检查当前代码、未完成修改与目录，保留用户已有工作。现有仓库根目录未必与当前目录一致，不自行初始化 Git。

固定 .NET 8 + EF Core + SQL Server + Vue3 + Element Plus，命名空间 `ahu.MicrosoftMes`。不增加 Python/Node 后端或智能体平台依赖。

## 2. 不得扩大范围

- 首版只有 role=1 管理员，工厂来源为 JWT；服务端拒绝其他角色。
- 本方案改掉 `docs/48`「默认不在系统内调用大模型」：只增加管理员 PC 直连 DeepSeek。不改微信、扣子、原 MCP 的默认行为，不重做知识库。
- 没有业务写工具、工资工具、排产工具、自由 SQL、动态脚本执行、文件上传和手机聊天。
- 复用业务服务，不调用公网 MCP 绕回本机，不使用 `DemoSkipAuth`、固定 `Mcp:FactoryId` 或金蝶演示授权作为新接口的身份。
- API Key 配在服务器，页面只显示状态与连接测试；不做密钥编辑表单。
- 不建聊天表或配置表；会话用限额内存缓存。确需改变本约定时先说明理由并更新方案，不隐式扩表。

## 3. 实施前核查

阅读 `backend/Services/WorkOrderService.cs`、`KnowledgeService.cs`、`KnowledgeFileParser.cs`、`Controllers/WorkOrderController.cs`、`Common/JwtHelper.cs`、`Common/ThrowHelper.cs`、鉴权及异常中间件、`Program.cs`。

阅读 `frontend/src/router/index.js`、`views/layout/SideMenu.vue`、现有工单详情打开方式和 API 请求封装。页面路径以实际代码为准，不造不存在的工单详情链接。

特别核查：工单查询当前筛选能力；知识服务名称查询取第一条、指定工序归属的现状；资料文件大小/文本长度/路径安全；部署多工厂隔离；禁用 AI 后路由和原业务能否正常启动。

实现 DeepSeek 客户端前查阅官方当前文档 https://api-docs.deepseek.com/zh-cn/guides/tool_calls/ 和模型/API 说明。实际选择支持工具调用的可用模型，不凭旧印象硬编码模型名。仓库样例填占位值，部署文档写验证过的模型和日期。遵守所选模型的消息回传协议，不把内部推理字段展示给用户。

## 4. 服务划分与配置

建议新增 `AiOptions`、`IDeepSeekClient/DeepSeekClient`、`IAiAssistantService/AiAssistantService`、`AiToolDispatcher`、`AiSessionStore`、`AiAssistantController`。名称允许服从现有风格，但 Controller 不放业务逻辑。

配置段 `Ai`：

| 字段 | 约定 |
|---|---|
| Enabled | 默认 false；未配置不能影响主系统启动 |
| BaseUrl | 默认官方 HTTPS 地址，仅部署配置可改，不接受请求体传入 |
| ApiKey | 服务器环境变量 `Ai__ApiKey`，仓库中为空；不写日志 |
| Model | 部署时填写并连接测试，不默认猜测可用型号 |
| BusinessTimeZone | 默认上海业务时区，跨 Windows/Linux 正确映射 |
| RequestTimeoutSeconds | 默认整轮 90 秒，包括模型及工具循环 |
| MaxToolRounds | 默认 4；到限明确提示缩小问题 |
| MaxInputChars | 默认单次 2,000 字符 |
| MaxKnowledgeChars | 默认每轮资料正文 20,000 字符；这是应用预算，不是模型上下文保证 |
| SessionIdleMinutes | 默认 30 分钟 |
| MaxTurnsPerSession | 默认 10 轮，超限提示新对话，不静默丢失上下文 |
| MaxSessionsPerUser / MaxSessions | 默认 3 / 100，按用户+工厂限制并限制总内存 |

同时设置上下文与输出 token 预算，按实际模型限制预留空间；字符预算不能代替 token 上限检查。资料过大时要求缩小工序/问题，不能悄悄截断后给出“完整”答案。禁止无限重试；首版模型失败由用户手动重试。

连接测试调用最小无业务数据请求，限制输出，不返回密钥。配置缺失与禁用状态可查，不因启动探测强制访问外网。

## 5. HTTP 契约

统一 `/api/ai-assistant`，返回遵循 `{code,msg,data}`。全接口 JWT + role=1。模型工具参数不允许携带 factoryId、userId、role、SQL 或文件路径。

| 接口 | 请求/响应要点 |
|---|---|
| GET `/status` | 返回 enabled/configured/model；不返回密钥或含密钥配置 |
| POST `/connection-test` | 无业务输入；返回是否连通、耗时及安全错误说明 |
| POST `/messages` | `{conversationId?:string, mode:"progress"或"guide", message:string, selectedProductCode?:string, selectedOperationId?:number}` |
| DELETE `/conversations/{id}` | 清除当前用户工厂的内存会话；越权不能清除 |
| POST `/work-orders/search` | 结构化白名单筛选+分页；给结果卡片翻页使用，不重复调用模型 |

`messages.data` 固定包含：`conversationId`、`answer`（纯文本）、`needsClarification`、`candidates`、`sources`、`workOrders`（可空）、`queriedAt`、`warnings`、`traceId`。

- candidates：服务端解析出的产品/工序候选。产品带编号和名称；工序带 id、编号和名称。工序编号在数据字典中没有唯一约束，点选工序以 id 为准。继续选择时，请求必须带 `selectedProductCode` 或 `selectedOperationId`，且只能是本会话服务端已经返回过的候选；服务端按该标识重新查库并校验归属，不相信客户端传来的名称，也不接受候选集合之外的编号或 id。不在候选内则拒绝并要求重新搜索。候选集合只存在服务端会话中。前端不把会话号写入浏览器；刷新后会话作废，点选无效，需新对话。
- sources：服务端生成的 sourceId、type、title、version（可空）、excerpt 及合法业务标识。模型只能引用本轮给出的 sourceId；不存在的引用丢弃并标明依据不足。
- workOrders：`list,total,page,pageSize,filters`；翻页带相同标准化筛选，服务端重新鉴权及校验。筛选不含工厂字段。
- 答案不能用 `v-html` 直接执行。首版纯文本+Element Plus 卡片即可。

首版普通 JSON 请求，不做 SSE。停止按钮取消当前请求；CancellationToken 传入模型和数据库调用，取消不写入未完成助手回答。一个会话同时只允许一个生成请求；重复提交返回“正在处理中”。会话归属由服务端管理，客户端不能上传伪造的 assistant/tool 历史。会话过期提示新建；切换模式新建会话。

## 6. 工具与业务口径

后端注册固定工具，按 mode 提供最小集合：

1. `search_work_orders`：关键词、产品编号、状态、交期起止、页码。先过滤再 Count，再分页；pageSize 默认 20、最大 50。本工具内排序为交期升序、工单 ID 稳定排序，无交期置后。这个排序只在本工具生效，禁止改 `WorkOrderService.QueryAsync` 的默认排序（现为工单 ID 倒序）。未完成用可选的多状态或专用标志表示 0 和 1，不能把原来的单个 `Status` 改成必填数组。状态 2 按数据字典称「已结束」，已结束和已取消不进入逾期或「明天到期」结果。交期按配置业务时区落到日期后，用半开区间在数据库过滤；禁止调用 `DueStateHelper` 代替这次筛选。标准化相对日期后在界面回显。
2. `get_work_order_progress`：工单 ID 或精确单号，当前工厂内定位；复用既有进度口径，不自造完成率。
3. `resolve_product`：产品编号优先精确，名称可模糊；多个候选先澄清，不自动取首条。候选分页或限制展示时说明尚有更多结果。
4. `get_production_guide`：已明确的产品编号及可选工序；工序必须属于该产品路线。复用文件解析服务；同名工序必须澄清，不默选。

只输出必要业务字段。MCP 的 20 条演示查询不能直接承担全量筛选及统计。不得先拿一页再在内存筛交期。原查询 DTO 若扩展，新参数可选，保持旧接口兼容。

工艺来源包含文件名、现存版本、产品/工序及摘录。不假造页码，不泄露物理路径。解析失败不得当作无资料；任何部分读取失败都不能生成未标明不完整的工艺结论。无资料时直接说明；冲突资料不自行选有效版本。文件正文按资料处理，不执行其中的命令。

既有列表和知识查询保持旧行为。AI 的交期筛选、多状态、按交期排序用可选参数或专用查询，不改变现有工单列表。`KnowledgeService.QueryProductionGuideAsync` 仍按名称取第一条，供现有 MCP 使用；AI 的 `resolve_product`、`get_production_guide` 另做澄清和路线归属校验。不要把「同名必须澄清」写进旧方法。必要的可选参数可以让 MCP 以后选用，但本期不改 MCP 的入参和返回，也不能为了本期重写整套 MCP。

## 7. 权限与错误实现

- 请求入口及工具服务均使用服务端身份上下文；会话、缓存键包含 factoryId+userId。
- 参数做类型、枚举、长度、日期范围及分页校验；未知工具拒绝；模型不能拓展工具权限。
- 首版不注册任何写接口。用户要求创建/删除/改数时说明本期只读并引导现有页面。
- 错误用 ThrowHelper 保留上下文。外部失败记录 URL、状态码、脱敏后的请求结构和响应、traceId；密钥/Authorization/JWT 永不记录。资料正文和用户消息默认仅记长度及摘要标识，不把完整敏感正文写日志。
- 避免全局中间件将模型原始错误、内部路径和报文回显给前端；仅对此新功能做必要安全适配，不借机改造全站异常体系。
- 记录调用人/工厂、工具名、安全参数摘要、耗时、成功失败、模型和 token 用量（供应商有返回时）；无用量时标记未知，不编造费用。
- 对单用户生成并发、连接测试频率设置服务端限制。取消/超时应释放资源及会话锁。

## 8. 前端交付

新增 `frontend/src/views/ai-assistant/index.vue` 和相应 API 模块，接入现有 PC 菜单、角色守卫及请求封装。

页面包含：模式选择、示例问题、对话区、输入框、发送/停止、新对话、状态提示。查询结果用表格及现有详情入口；来源用文件名/版本/摘录卡片。配置缺失、禁用、无数据、资料缺失、歧义、请求失败分别展示。

首次使用展示必要业务资料将发送到模型服务的说明；不展示 MCP、token、模型推理等实现术语。对来源可见信息保留准确性，版本缺失显示“未标注版本”。

## 9. 实施顺序和交付

1. 核对代码与文档，记录实际复用点及差异。
2. 完成后端配置、鉴权、客户端与连接测试。
3. 完成只读业务工具及针对性测试，先验证跨工厂、完整筛选、同名和工序归属。
4. 完成会话/工具循环、来源封装及 PC 页面。
5. 执行 50 号清单，完成真实 API 联调；没有密钥或环境时明确标记“待验证”，不能以模拟调用冒充通过。
6. 同步权威文档：01 新增 AI 范围/权限；03 架构和数据流；04 进度；05 密钥部署、出网和关闭恢复；30 使用说明；33 API；48 写明本期改掉「默认不内嵌」，改为管理员 PC 直连，微信/扣子/原 MCP 不改。默认不改 02，无 DDL。

后端构建、前端构建，测试新增逻辑和被修改的共享服务；复查正常下单、报工及原 MCP 不受影响。使用测试工厂/独立样例，禁止修改生产数据造演示。

交付说明列出修改文件、通过的用例及证据、待验证项、部署步骤、已知限制。仅全部完成后移动本指令到“已做指令”并修复交叉链接；禁止提前勾选或宣称上线。
