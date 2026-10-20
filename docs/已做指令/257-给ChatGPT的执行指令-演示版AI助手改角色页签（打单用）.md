# 257-执行指令（给 ChatGPT）：演示版 AI 助手改角色页签，打单现场让客户说"我就想要这个"

> 状态：已做（2026-10-20）；后端编译及13项检查通过；前端构建受临时文件权限阻塞，真实模型/数据库现场验收未执行
> 性质：售前演示
> 本文件原用途：整段复制粘贴给 ChatGPT，让它输出改造后的代码。你（黑帽大人）拿代码贴回仓库。
> 背景：我们在卖一套车间管理系统（对标黑湖小工单，.NET 8 + Vue3/Element Plus + SQL Server）。下礼拜要去客户工厂演示，AI 助手这一页是**重头戏**——客户老板、车间主管、财务、车间师傅四个人轮流坐过来，每人点自己那张脸，点一下问题按钮，AI 必须秒出真数据，让客户当场拍桌子说"我就想要这个"。
> 现在这页有三个毛病：① 顶部一条橙色警告"AI 会把数据发到服务器、请勿输入工资密码"，像政府网站免责声明，太严肃打单；② 页签是"查进度/问工艺/智能客服"这种工程师视角，客户看不懂；③ AI 不会查设备异常，主管问"现在几条设备没修"它答不上来。

## 技术栈与仓库位置（你改代码时参考）

- 前端唯一文件：`frontend/src/views/ai-assistant/index.vue`（Vue3 `<script setup>` + Element Plus）
- 后端两个文件：
  - `backend/Services/AiToolDispatcher.cs`（白名单工具调度，要加新工具）
  - `backend/Services/AiAssistantService.cs`（不要改，只看一眼知道有哪三个 mode：progress/guide/support）
- 后端会话硬约束：一个会话绑死一个 mode，切 mode 必须新建对话。前端切页签时如果 mode 变了就清空聊天。
- 现有三个 mode 的工具能力（白名单，不要越界）：
  - `progress`：`search_work_orders`（关键词/产品编号/状态/交期）、`get_work_order_progress`（按工单号查各工序进度）
  - `support`：上面两个 + `search_support_docs`（搜操作文档）+ `get_work_order_report_evidence`（按工单号查报工证据：谁报的、良不良、复核状态、退回原因）
  - `guide`：工艺资料查询（本次不做页签）

## 三个改造点，一次性做完

### 改造一：删严肃警告条（20 分钟）

`index.vue` 现有代码：
- 模板里有个 `<el-alert v-if="!privacyAck" type="warning" title="使用说明" description="AI 会将本次问题及必要业务资料发送至服务器配置的模型服务。请勿输入工资、密码等敏感内容。本期仅支持查询，不能改数或下单。" />`
- 紧跟一个 `<el-button v-if="!privacyAck" type="primary" @click="ackPrivacy">我知道了</el-button>`
- script 里有 `const privacyAck = ref(false)`、`function ackPrivacy(){ privacyAck.value = true }`
- `canChat = computed(() => status.value.enabled && status.value.configured && privacyAck.value)`
- 示例问答区 `v-if="privacyAck"`

**要求**：
1. 警告条和"我知道了"按钮整对删除
2. `privacyAck` 整个 ref 删掉，`ackPrivacy` 函数删掉
3. `canChat` 改成 `computed(() => status.value.enabled && status.value.configured)`
4. 示例问答区的 `v-if="privacyAck"` 删掉，恒显示
5. 顶栏右侧的 `<span class="muted" v-if="status.model">模型已配置</span>` 和「连接测试」按钮也藏掉，只留「已就绪」绿标签和「新对话」按钮
6. **不要**新增任何开关或"演示模式"判断，就是永久删掉

### 改造二：页签改四角色，各 10 题点一下直接发（1 小时）

现有页签是三个 radio-button：`查进度 / 问工艺 / 智能客服`。改成四个：

| 页签显示 | value | 后端 mode |
|---|---|---|
| 老板 | `boss` | `progress` |
| 车间主管 | `supervisor` | `support` |
| 财务 | `finance` | `support` |
| 车间师傅 | `worker` | `support` |

**点题即发**：现在点示例按钮只是把文字填进输入框（`useExample(q) { input.value = q }`），改成点按钮直接把这句话发出去（把 input 设成 q 后立即调 `onSend()`），不用客户再点发送。

**切页签行为**：
- 老板 → 主管/财务/师傅：mode 从 progress 变 support，自动 `onNewChat()` 清空对话
- 主管 ↔ 财务 ↔ 师傅：mode 都是 support，**不清空对话**，只换下面的问题按钮列表

**40 道问题按钮（写死成前端常量，逐字照抄）**：

```js
const QUESTIONS = {
  boss: [
    '今天要交货的工单有哪几张？',
    '明天就要交的货，进度到哪了？',
    '现在有哪几张单已经逾期了？逾期多久？',
    '客户催货时，执行中的单里哪几张剩得最多？',
    '刚下的单怎么到现在还没人动？',
    '现在车间一共在做多少张单？',
    '临期 3 天内要交的单有哪些？',
    '被取消的工单有哪些？',
    '客户在微信上突然问起某单，我在手机上怎么一秒查到？',
    '做完的工单系统会自动结束吗？'
  ],
  supervisor: [
    '今天到期的单有几张在赶？',
    '逾期最久的是哪张？卡在哪道工序？',
    '现在有几条设备异常还没处理？',
    '拖最久的那条异常是谁报的、谁在修？',
    '工人不会用手机扫码报工怎么办？',
    '报工被退回了，工人来找我闹怎么办？',
    '待复核的报工我该怎么处理？',
    '工人说自己报错数了，能自己改吗？',
    '班组长在手机上派工，工人那边能看到吗？',
    '超报被系统硬拦了，我怎么放行？'
  ],
  finance: [
    '工单计划数和完成数对不上，差在哪？',
    '不良品会不会被算进完成数里？',
    '客户投诉了，这活是谁做的、什么时候做的？',
    '返工重做的件，算不算产量？',
    '两个人合作做一批，数量怎么分？',
    '临时工和正式工，系统里怎么区分标签？',
    '交期被改过，我在哪看记录？',
    '报工记录能导出 Excel 带去对账吗？',
    '被退回的报工，会不会被算进完成数里？',
    '月底对账时，良品+不良合计怎么对得上计划数？'
  ],
  worker: [
    '工人怎么用手机扫码报工？',
    'PDA 扫码枪怎么用？',
    '扫了二维码打不开页面怎么办？',
    '报错数量了，我自己能改吗？',
    '不良品怎么报？必须填什么？',
    '一道活分三次做，能分三次报吗？',
    '班组长派给我的活，手机上在哪看？',
    '没智能手机的工人怎么报工？',
    '设备坏了我要怎么上报？',
    '怎么看这道工序的作业指导书？'
  ]
}
```

### 改造三：后端加 `search_abnormal` 工具，让 AI 能查设备异常（1 小时）

我们数据库里已经有 `Abnormals` 表（员工手机上报设备故障/物料短缺/质量异常），字段包括：
- `AbnormalType` (1=设备故障 2=物料短缺 3=质量异常)
- `Description`、`ImagePath`、`WorkOrderId`、`ReportedBy`、`ReportedAt`
- `Status` (0=未处理 1=已处理)
- `AssignHandlerUserId`、`HandledBy`、`RecoveredAt`、`HandleNote`

但 AI 助手的工具白名单里没接这张表。请在 `AiToolDispatcher.cs` 里：

1. 新增工具定义 `SearchAbnormalTool()`：
   - 名称 `search_abnormal`
   - 描述："查询异常上报记录（设备故障/物料短缺/质量异常）。status=0 未处理，status=1 已处理，不传查全部。abnormalType：1设备故障 2物料短缺 3质量异常。"
   - 参数：`status`(int, 可选)、`abnormalType`(int, 可选)、`page`(int 默认1)、`pageSize`(int 默认10 最大20)

2. 把这个工具加进 `ToolsForMode("support")` 的返回数组，以及 `ModeTools["support"]` 白名单 HashSet

3. 在 `DispatchAsync` 的 switch 里加分支：
```csharp
"search_abnormal" => await SearchAbnormalAsync(root, session, ct),
```

4. 新增私有方法 `SearchAbnormalAsync`，要求：
   - 按 `session.FactoryId` 过滤（**绝对不能接受前端传 factoryId**，现有 `RejectForbiddenArgs` 已经拦了）
   - status / abnormalType 可选过滤
   - 排序：未处理按 `ReportedAt` 升序（最早拖最久的排最前）；已处理按 `RecoveredAt` 倒序
   - 联表 Users 取上报人姓名、处理人姓名；联表 WorkOrders 取 OrderNo
   - 返回 JSON：
     ```json
     {
       "total": 3,
       "list": [{
         "id": 12,
         "typeLabel": "设备故障",
         "description": "3号注塑机异响",
         "reporterName": "张三",
         "reportedAt": "2026-10-20 09:23",
         "status": 0,
         "handlerName": "李四",
         "recoveredAt": null,
         "minutesOpen": 142,
         "orderNo": "A0023",
         "handleNote": null
       }]
     }
     ```
   - `minutesOpen`：未处理 = (now - ReportedAt).TotalMinutes 取整；已处理 = (RecoveredAt - ReportedAt).TotalMinutes 取整。让大模型直接报"拖了 2 小时"，别让它自己算
   - 不返回 ImagePath（大模型不需要）
   - **只读**：不要暴露任何写操作（处理异常/关闭异常的 API 不要接进来）

## 不要做的事（止步线）

- 不改数据库表结构
- 不动企微预警链路、不动员工手机上报页、不动车间看板
- 不给 AI 开放写操作（不能让它处理异常、不能让它下单/改数）
- 不做"问工艺"guide 页签
- 不做问题管理后台（40 题写死前端常量就行）
- 不动 H5 端
- 老板页签（progress 模式）不要给它挂 search_abnormal，主管页（support）才挂

## 验收清单（我会逐条核对）

- [ ] 打开 AI 助手页，**没有橙色警告条**，没有"我知道了"蓝色整行按钮
- [ ] 页签是四个：老板/车间主管/财务/车间师傅
- [ ] 点任意问题按钮，**不用再点发送**，消息直接上屏
- [ ] 老板页点"今天要交货的工单有哪几张？"→ 返回工单列表，不报错
- [ ] 主管页点"现在有几条设备异常还没处理？"→ 后端真的查 Abnormals 表返回数字
- [ ] 主管/财务/师傅之间切换不弹"切换模式请新建对话"；老板→主管会自动新对话
- [ ] 财务 10 题逐字过一遍，**没有"工资/工价/多少钱"字眼**
- [ ] `dotnet build backend` 通过；`npm run build frontend` 通过
- [ ] 尝试传 `{"factoryId":999}` 调 search_abnormal 被拒

## 输出要求

请按以下顺序给我：
1. 改造后的完整 `frontend/src/views/ai-assistant/index.vue` 文件（把三个改造点全合进去）
2. `AiToolDispatcher.cs` 里新增/修改的代码片段（SearchAbnormalTool 定义、ToolsForMode 改动、ModeTools 改动、switch 分支、SearchAbnormalAsync 方法）——不用给整个文件，给 diff 风格的片段就行
3. 简短说明：你改了哪几处、有没有遇到我没说清楚的地方

谢了兄弟，这版打单就靠它了。

## 开工审核修正（2026-10-20）

- 直接在仓库实施，交付文件链接与验证结果，无需复制整份代码回仓库。
- 角色只是提问入口，继续仅管理员可用，不改变权限。老板两道操作说明题超出 progress 工具范围，保留原题；无证据应明确能力边界，不承诺40题全能答或秒回。
- 切换后端 mode 时先同步清空本地会话，再异步删除旧会话，防止新问题携带旧编号。同 support 角色不清空。
- 异常查询参数严格校验；page>=1，pageSize默认10、上限20。全部状态时未处理在前、最早上报在前，已处理按恢复时间倒序，同时间按ID稳定排序。
- handlerName：未处理取指定处理人，已处理取实际处理人；关联用户与工单均限定当前工厂，用左关联保留缺失关联的异常。
- 时间沿用现有异常服务的服务器本地 DateTime.Now；已处理缺恢复时间时 minutesOpen=null，不伪造耗时。异常ID可作为证据引用。
- 删除隐私确认门槛、模型提示和连接测试入口；保留关闭/未配置/失败的真实状态。
- 验证分为构建与自动检查、真实模型/数据库现场验收；后者未执行不得勾选。

## 本次实施与验证记录

- 已修改 PC 页面与 AiToolDispatcher；未修改 AiAssistantService、H5、数据库及部署配置。
- 前端专项检查通过：Vue script 编译、四角色各10题、财务无工资/工价/多少钱、mode切换、点题直发及忙碌防重；git diff --check通过。
- 新增 tests/AiAbnormalChecks，覆盖工具声明、factoryId注入、mode越权和非法参数；停止MES后重试13项全部通过。
- 前端构建第1次 spawn EPERM；沙箱外第2次在 PDF worker 构建清理临时文件时报 Access is denied，未通过。
- 后端检查第1次NuGet网络受限；沙箱外第2次 MSB3027/MSB3021，WorkshopMes.exe 被运行中进程33200占用，未通过。按规则停止重试，未停服务、未部署。
- 原验收清单保留未勾选：静态检查不等于浏览器现场验收。完整构建与真实模型/数据库问答仍待验证；因此本次尚未提交推送。

### 停止MES后重试（2026-10-20）

- `dotnet run --project tests/AiAbnormalChecks -p:SkipVersionBump=true` 成功：后端编译完成，13项工具声明、模式越权、factoryId注入与非法参数检查全部通过。
- 前端使用独立临时目录重跑构建，仍在 `src/workers/pdf.worker.js?worker&url` 阶段由 esbuild 删除临时文件时报 `Access is denied`，完整构建仍未通过。
- 未进行真实模型/数据库现场验收，未部署；本次代码仍未提交推送，等待前端构建阻塞解决。

## 编号与实现复审（2026-10-20）

- 原编号243与企微候选任务指令冲突，现改为257；企微候选任务仍为243，现有PRD和MCP接入说明中的docs/243继续指向企微功能。
- 角色页签只改变问题入口，管理员鉴权保持；support工具名单、工厂过滤、处理人及耗时口径已复核。
- 前端完整构建仍受PDF worker临时文件权限阻塞，不能记为通过；真实数据库与模型问答未现场验收。

- 本轮复审：Vue脚本与模板编译、40题、财务措辞、同mode保持/跨mode清空、点题发送及忙碌防重检查通过。异常工具13项检查沿用此前成功记录；后端本轮编译被进程38204占用阻塞，未重跑检查。

### 本轮最终验证（2026-10-20）

- 用户停止MES后，`dotnet run --no-restore --project tests/AiAbnormalChecks -p:SkipVersionBump=true` 编译及13项检查通过；前端脚本/模板及交互专项检查此前通过。
- 前端完整构建的PDF worker临时文件权限故障仍未解决，不标为通过；真实模型与数据库问答未验收，未部署。
