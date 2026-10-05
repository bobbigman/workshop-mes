# 车间小工单系统 · 喂 AI 助手知识库

> 用途：给 AI 助手 / 智能体 / 扣子或豆包知识库直接引用。AI 回答问题时，据此给出「表名、字段、接口、规则」的准确口径，不靠猜、不靠记忆。
> 数据来源：docs/01-PRD、02-数据字典、03-技术方案、33-接口对接说明。
> 说明：源码不断在改，本文件是稳定契约（表结构、接口、业务规则）；若与代码冲突，以最新代码 + 本文件为准。

---

## 0. 系统一句话定位

面向中小工厂/车间的轻量级 MES（生产执行系统）。局域网 H5 + PDA 扫码，不依赖公网/微信。
主链路：**电脑下单出二维码 → 工人扫码报工 → 复核 → 老板大屏看进度**。
技术栈：.NET 8 Web API + EF Core + SQL Server；前端 Vue3（PC 后台 Element Plus / H5 报工 Vant / 大屏看板 ECharts）；鉴权 JWT + BCrypt。

---

## 1. 角色与权限

| 角色 | role | 权限 |
|---|---|---|
| 管理员 | 1 | 全部权限，可创建账号 |
| 生产人员（工人） | 2 | 电脑可查、手机报工；不能改基础数据 |
| 班组长 | 3 | 电脑复核 + 可报工；只读工单/报表/看板；不可改基础数据 |

- **工序报工权限 = 部门**（不是人）：报工前校验当前用户部门是否属于该工序的报工权限部门。
- 管理员不受部门报工权限限制。

---

## 2. 三条业务硬规则（红线，任何功能不得违反）

1. **产品编号唯一**（base_product.code，相当于身份证号），名称可重复。
2. **工序报工权限 = 部门**：报工前校验当前用户部门 ∈ 该工序报权部门。
3. **删除依赖倒序**：工单 → 产品 → 工艺路线 → 工序 → 不良品项 → 单位 → 部门；有下级的对象禁止直接删除。

---

## 3. 核心业务流程（一单到底）

1. **电脑下单出码**：创建工单（引用产品+工艺路线），生成工单号二维码 / 流转卡。
2. **扫码报工**：PDA 扫码头 / 手机摄像头 / 手动输入工单号 / 电脑「+报工」；选工序填良品数、不良数提交。
3. **部门权限校验**：当前用户部门 ∈ 该工序报权部门，否则拒绝（管理员不受限）。
4. **复核（双轨）**：班组长 / 管理员复核；复核通过才进入生产报表与工资口径。
5. **进度实时更新**：大屏 / 工单列表按工序实时累计进度；异常走 prod_abnormal。

> 报工提交后进度即实时更新；复核是「入账/工资」关口，两条轨分开。

---

## 4. 表结构（数据库：SQL Server）

统一说明：除注明外，每表都有 `id`（IDENTITY 主键）；业务表均带 `factory_id`（多租户隔离）。

### 4.1 系统 sys_

- **sys_factory** 工厂（多租户）：`factory_code`(唯一), `factory_name`, `license_tier`(合同授权档：trial入门/enterprise企业/flagship旗舰，默认trial，见docs/88), `license_expires_at_utc`(云端授权到期，空=未激活，docs/89), `last_license_ok_utc`(最近一次成功云校验的服务端UTC), `last_license_seen_utc`(最近发起校验的本地UTC，防拨回时钟), `trial_expires_at_utc`(体验账套到期，空=不进清理队列，docs/90), `trial_cleaned_at_utc`(体验账套已清场时间，防重复清理), `created_at`
- **sys_user** 用户：`factory_id`, `account`(可填手机号), `phone`, `name`, `role`(1管理/2生产/3班组长), `password`(加密), `status`(1启用/0停用)；UNIQUE(factory_id, account)
- **sys_department** 部门：`factory_id`, `code`, `name`
- **sys_department_user** 部门-用户关联：`department_id`, `user_id`；UNIQUE(department_id, user_id)
- **sys_print_setting** 工单打印标签设置（工厂级一条）：`label_size`(Label30x40|Label80x60), `qr_mode`(OrderNo|ReportUrl), `base_url`, `show_product_code/name/qty/ops`, `show_custom_field_ids`(JSON)；UNIQUE(factory_id)
- **sys_mcp_auth** MCP 授权（豆包演示）：`auth_code`(一次性配对码), `auth_token`, `kingdee_user_id/name`, `is_finance`(财务角色敏感拦截), `code_used`, `code_expire`, `token_expire`

### 4.2 基础数据 base_

- **base_unit** 单位：`name`（如"只"）
- **base_defect_item** 不良品项：`name`（如"粘锅"）
- **base_operation** 工序：`code`(工序编号), `name`(工序名称)
- **base_operation_department** 工序-报工权限部门：`operation_id`, `department_id`；UNIQUE —— **仅该部门人员可报工**
- **base_operation_defect** 工序-不良品项关联：`operation_id`, `defect_id`
- **base_routing** 工艺路线：`code`, `name`
- **base_routing_step** 工艺路线-工序明细：`routing_id`, `operation_id`, `seq`(工序顺序，从1开始)；UNIQUE(routing_id, seq)
- **base_product** 产品定义：`code`(产品编号，唯一), `name`(产品名称，可重复), `unit_id`, `routing_id`, `supplier`, `price`(**扩展属性，不是工价**)；UNIQUE(factory_id, code)
- **base_knowledge_file** 制造知识库文件：`file_name`(显示名), `relative_path`(相对路径，不存绝对路径), `file_type`(pdf/docx/xlsx/jpg/png/webp), `ref_type`(product/operation), `ref_id`, `version`；一个文件挂多处=插多条记录；图片(jpg/png/webp)供手机端预览，不参与 AI 文字问答
- **base_price_rule** 工价规则：`product_id`/`operation_id`/`department_id`/`user_id` 四维**均可空**(NULL=该维度不限), `price_type`(1计件/2计时/3固定), `unit_price`(计件=元/件,计时=元/小时,固定=单笔额), `deduct_price`(不良扣款元/件,NULL=不扣), `effective_from/to`, `priority`；匹配取"命中维度最多(最具体)者"，具体度相同时 priority 小者优先

### 4.3 生产 prod_

- **prod_work_order** 工单：`order_no`(工单编号), `product_id`, `qty`(下单数量), `status`(0未开始/1执行中/2已结束/3已取消), `due_date`(计划交期，可空；空=未设交期), `created_by`；UNIQUE(factory_id, order_no)
- **prod_work_order_operation** 工单工序任务：`work_order_id`, `operation_id`, `seq`, `plan_qty`(工序计划数), `assignee_user_id`(派工执行人，空=未派工)；UNIQUE(work_order_id, operation_id) 和 (work_order_id, seq)
- **prod_report** 报工记录：`order_id`, `operation_id`, `user_id`, `good_qty`(良品数), `defect_qty`(不良数), `defect_id`(不良原因), `duration_minutes`(报工时长分钟), `batch_no`(批量补报批次号), `product_id`/`department_id`/`unit_price`/`wage_amount`(**快照**), `settled_flag`(0未结算可改/1已结算冻结), `review_status`(0待复核/1已通过/2已退回), `reviewed_by/at`, `reject_reason`, `report_time`
- **prod_abnormal** 工人异常上报：`abnormal_type`(1设备故障/2物料短缺/3质量异常), `description`, `image_path`(相对路径), `work_order_id`(可空), `reported_by`(上报人), `reported_at`(上报时间), `status`(0待处理/1已恢复), `handled_by`, `recovered_at`, `handle_note`

### 4.4 工资 salary_

- **salary_statement** 工资单：`user_id`(领薪人), `period_type`(1月/2周), `period_value`(月'2026-09'/周'2026-W37'), `total_amount`(应发合计), `status`(0草稿/1已确认/2已发放), `confirmed_at`；手工列（docs/73，仅草稿可改）：`base_salary` 底薪、`meal_allowance` 餐补、`other_allowance` 其他补贴、`social_tax` 社保个税（填正数系统扣）、`other_deduction` 其他扣款（填正数系统扣）；UNIQUE(factory_id, user_id, period_type, period_value)
- **salary_statement_item** 工资明细：`statement_id`, `report_id`(**全局唯一：一条报工最多进一张工资单**), `rule_id`(命中的工价规则ID,追溯), `calc_qty`(计件=良品数/计时=0/固定=1), `unit_price`(结算单价快照), `amount`(小计,扣款前), `deduct_amount`(不良扣款小计)；UNIQUE(report_id)

### 4.5 扩展 · 自定义字段

- **sys_custom_field** 字段定义：`target`(归属对象 work_order/product/operation), `field_name`, `field_type`(single单选/text文本/number数字), `show_in_order_list`(仅 work_order 可开；1=PC 工单列表显示该列)
- **sys_custom_field_option** 字段选项：`field_id`, `label`(如"一号桌"), `seq`
- **sys_custom_field_value** 字段值：`field_id`, `target_id`(归属对象记录ID), `value`；UNIQUE(field_id, target_id)

### 4.6 企业微信预警（默认关闭，正式期再开）

- **sys_wechat_alert_setting** 工厂级一条：`enabled`(默认0), `corp_id`, `secret`, `agent_id`, `to_user`, `daily_limit`(默认20), `notice_seen`(首次说明是否已看)；UNIQUE(factory_id)
- **sys_wechat_alert_log** 推送去重日志：`alert_key`(如 overdue:2026-09-14), `send_date`, `content`；UNIQUE(factory_id, alert_key, send_date) —— 同工厂同键同天只推一次

---

## 5. 关键字段口径（AI 回答必看）

- `base_product.code` = 身份证号（唯一，一般填图号/物料编码）；`base_product.name` = 名字（可重复）。
- 报工权限通过 `base_operation_department` 关联部门实现。
- `base_price_rule` 四维列均可空（NULL=通配不限）；匹配取"命中维度最多者"，具体度相同时 `priority` 小者优先。
- `base_price_rule.price_type`：1计件(unit_price=元/件)、2计时(元/小时)、3固定(单笔固定额)；`deduct_price`=不良扣款(元/件)。
- **`base_product.price` 不是工价**，是产品扩展属性；工价一律走 `base_price_rule`。
- `prod_report.department_id`：报工时部门快照，取值=用户所属部门 ∩ 该工序报权部门，取 id 最小者；交集空（如管理员代报）=NULL。
- `prod_report.unit_price`/`wage_amount`：提交时尽力匹配工价回填（实时预览）；结算时以 `salary_statement_item` 固化为准，单价涨跌不覆盖已结算历史。
- `prod_report.settled_flag`：0未结算(可改)、1已结算(冻结,禁改/禁删)；确认工资单时置1；删除**草稿**工资单、或 `POST /api/Salary/{id}/revoke` 撤回**已确认**单时回滚0（已发放不可撤）。
- `prod_report.review_status`：0待复核/1已通过/2已退回。双轨：进度/防超额/工单状态算 0+1（不含已退回）；**生产报表与工资只算 1**；**不良品报表（docs/207）算 0+1**（质量分析看现场全貌，与生产报表用途不同、不强求同数）。
- `prod_work_order.due_date`：可空；空=未设交期(不参与临期/超期判断)。三态：正常/预警(临期≤3天)/延期(超期)，仅未结束工单参与。
- `prod_abnormal.abnormal_type`：官方仅三类（1设备/2物料/3质量），无第四类；看板只播 status=0。
- **金额公式**（salary_statement_item）：计件 `amount=good×unit_price`；计时 `amount=(duration_minutes/60)×unit_price`；固定 `amount=unit_price`；扣款 `deduct=defect×deduct_price`(可空)；计件应发小计=amount−deduct。
- **应发合计**（salary_statement.total_amount）：计件明细求和 + base_salary + meal_allowance + other_allowance − social_tax − other_deduction；五列手工默认 0，只录 ≥0；**不自动算社保费率、不负责银行代发**。

### 5.5 相似件建议报价（金蝶主数据 + 报工实绩）

**卖点 / 解决的痛点**：金蝶已有的物料、BOM、标准工艺不用在小工单里再录第二遍；人工成本按车间**真实干多久**估，不拍脑袋。功能不绑架流程（不自动拆单、不写回金蝶报价；比选现场能算、能导出带走，日常仍按老习惯拍板）。

- 相似件：人工勾选 1~N 个本厂 `base_product`（搜编号/名称），不做向量/图片相似。
- 材料：经金蝶适配器（`Kingdee:Mode=Mock|K3Cloud`）读物料成本价 + BOM 材料费合计；无 BOM 只算物料价；连不上 → 材料区标红、仅「人工建议」仍可出结果。
- 人工建议 = 平均单件工时 × 人工单价；平均单件工时 = `SUM(duration_minutes)/SUM(good_qty)`，仅取 `review_status=1` 且 `duration_minutes>0` 的报工（缺时长的计件不计入分母，页面提示「部分报工缺时长」）。
- 人工单价：优先相似产品在 `base_price_rule` 命中的**计时**单价（price_type=2，取匹配分最高）；否则用配置 `Kingdee:DefaultLaborRatePerHour`。
- 建议总价 =（材料建议 + 人工建议）× (1 + markupPct%)，另可手动加不良预留 defectReservePct%。
- 首版**只算不存**（无报价单表）；不写回金蝶销售报价；金蝶标准工艺只读展示、不自动拆单建工序。
- 仅 `role=1`（管理员）；工人/班组长 403。

### 5.6 产品优势速览与界面价值提示（演示口径，docs/76·77）

- 入口：**帮助 → 产品优势速览**（`/advantages`，登录前也可开）。页顶横幅：「本地部署・局域网运行・数据不出厂」。
- A 区对外：三大麻烦怎么解、六步闭环、三大差异（本地部署头号）、金蝶扩展（建议报价/排产/「能和金蝶打通，数据不重复录」）；**A 区不点名竞品**。
- B 区销售备忘：话术杀招、竞品对比表；演示时默认收起。
- 业务页顶部一行浅色价值提示（非弹窗）：工单/报工/看板/工资/企微预警/建议报价/排产/AI；文案统一在 `frontend/src/constants/valueTips.js`。
- 「断外网也能用」限定局域网核心功能；推送依赖企微，不是断网也能推。工资对外只讲计件算清楚，不把银行代发说成系统全包。

### 5.7 授权档（LicenseTier）与体验账套（AI 回答必看，docs/88·89·90）

**三档授权（docs/88）**：一套代码按工厂 `license_tier` 开放功能子集，一个工厂一档。

| 功能键 LicenseFeature | 中文 | trial | enterprise | flagship |
|---|---|---|---|---|
| （三档共享） | 下工单 / 派工到人 / 手机派工报工 / 车间看板 | ✓ | ✓ | ✓ |
| `PieceWage` | 计件工资（可重算）+ 工价表 | ✗ | ✓ | ✓ |
| `PrintLabel` | 打印工单/条码 | ✗ | ✓ | ✓ |
| `ScanReport` | 扫码报工 | ✗ | ✓ | ✓ |
| `WechatDueAlert` | 交期预警 + 企微推送 | ✗ | ✓ | ✓ |
| `ReportReview` | 报工复核 | ✗ | ✓ | ✓ |
| `MultiWorkshop` | 多车间 | ✗ | ✗ | ✓ |
| `KingdeeMcp` | 接金蝶（建议报价/MCP） | ✗ | ✗ | ✓ |
| `DataScreen` | 数据大屏 | ✗ | ✗ | ✓ |

- **校验**：后端必须真校验（`EnsureFeature`，拒绝文案可读，如「当前为入门款，该功能需企业版」），前端只隐藏体验；tier 按工厂、role 按用户，两维正交，互不替代。改档（SQL/授权）后**重新登录**生效，数据不重录。新建账套默认 `trial`。
- **`sys_factory.license_tier` 存合同档**；**有效档由 `ILicenseTierService.GetTierAsync` 运行时计算**（配合 89 云激活）。

**轻量授权云激活（docs/89）**：`LicenseCloud:Enabled`（默认 false，开发/未开通客户=仅本地档位）时联网校验授权。
- 联网成功写缓存列；断网用宽限期（`GraceDays` 默认 7）；超期/宽限用尽 → **有效档临时降 trial**（不改库内 `license_tier`），报工/看板仍可用，续费后重新登录恢复。
- 只发非业务标识（`factory_code`/档位/时间戳），不泄露工单/报工/工资；拨回本地时钟超过容差（5 分钟）→ 视为异常，有效档 trial。

**体验账套到期自动清理（docs/90）**：`TrialCleanup` 默认体验期 7 天、间隔 60 分钟、`GraceMinutes` 0（到期即清，可配）。
- **进清理队列硬判据** = `license_tier='trial'` 且 `trial_expires_at_utc` 非空且 `trial_cleaned_at_utc` 为 NULL 且已过期；到期按删除依赖倒序清空该 factory 全部资料 + 删附件磁盘 `RootPath/{factory_id}/` + 禁用登录，**不可逆**。
- `trial_expires_at_utc=NULL` 永不进清理（历史库、私有 `Deployment:Private=true`、未标体验一律安全）；SaaS 新建账套写到期=创建+`DurationDays`；转正式版（enterprise/flagship/私有）清空该列。
- **与 88/89/90 分工**：88 管「合同档 `license_tier` → 功能开关」（入门/企业/旗舰开放哪些能力）；89 管「云激活/宽限期 → 有效档」（超期有效档临时降 trial，不删数据，续费后重登恢复）；90 管「体验账套试玩结束后清场」。**体验账套到期 ≠ 授权降档，是清场**。存储配额/单文件上限另属未上线项，勿当现能力回答。

---

## 6. 接口清单（HTTP API）

**统一约定**：返回格式 `{ "code":0, "msg":"ok", "data":{...} }`；code=0 成功，非 0 失败原因在 msg。
**分页**：列表接口用 `page`(默认1)、`pageSize`(默认20)，返回 `data={list, total}`。
**多工厂**：数据按工厂隔离，工厂由登录账号的 factoryId 决定，接口不传工厂参数。
**鉴权**：除登录外所有接口带 `Authorization: Bearer <token>`；token 有效期 12 小时。

登录：`POST /api/Auth/login`，请求体 `{factoryCode, account, password}` → 返回 `data.token`。

### 6.1 基础资料（只读查询，GET）

| 接口 | 关键参数 | 说明 |
|---|---|---|
| /api/Product | keyword,page,pageSize | 产品列表（keyword 模糊匹配编号/名称） |
| /api/Product/{id} | — | 产品详情 |
| /api/Operation | page,pageSize | 工序列表 |
| /api/Operation/{id} | — | 工序详情 |
| /api/Department | page,pageSize | 部门列表 |
| /api/Department/{id} | — | 部门详情 |
| /api/Unit | page,pageSize | 单位列表 |
| /api/DefectItem | page,pageSize | 不良品项列表 |
| /api/Routing | page,pageSize | 工艺路线列表 |
| /api/Routing/{id} | — | 工艺路线详情（含工序明细） |
| /api/User | page,pageSize | 用户列表 |
| /api/PriceRule | page,pageSize | 工价表（计价规则） |
| /api/KnowledgeFile | refType,refId | 制造知识库文件列表（作业指导书/图纸/图片，挂产品或工序） |
| /api/KnowledgeFile/by-order/{orderId} | — | 按工单取该单产品+工序关联的知识库文件（H5 报工界面查看用） |

### 6.2 单据 / 统计（只读查询，除标注外为 GET）

| 接口 | 关键参数 | 说明 |
|---|---|---|
| /api/WorkOrder | keyword,status,excludeCancelled,page,pageSize；可选 customFieldId/customFieldValue/customFieldMatch(exact\|contains) | 工单列表；行内 ext 仅含"开启列表显示"的工单字段 |
| /api/WorkOrder/{id} | — | 工单详情 |
| /api/WorkOrder/by-no | orderNo | 按单号查工单 |
| /api/WorkOrder/status-counts | — | 各状态工单数量 |
| /api/Report | orderNo,productCode,productName,reviewStatus,page,pageSize | 报工记录列表 |
| /api/Report/defects/{operationId} | — | 某工序可选的不良品项 |
| /api/Review/pending | page,pageSize | 待复核报工 |
| /api/Salary | periodType,periodValue,userId,status,page,pageSize | 工资单列表（含底薪等手工列） |
| /api/Salary/{id} | — | 工资单详情（计件合计 + 手工列 + 明细） |
| PUT /api/Salary/{id}/components | baseSalary,mealAllowance,otherAllowance,socialTax,otherDeduction | 草稿保存手工列并重算应发合计（仅管理员） |
| POST /api/Salary/{id}/revoke | — | 撤回已确认工资单（作废重算；已发放不可撤；仅管理员） |
| /api/Salary/export | periodType,periodValue | 导出工资 CSV |
| /api/ReportStat/production | 见 Swagger | 生产报表 |
| /api/ReportStat/defect | period, keyword? | 不良品报表三表（docs/207）：分布/汇总/明细；口径待复核+已通过，不含退回；按原报工时间 |
| /api/ReportStat/board | 见 Swagger | 看板统计；含 openAbnormalCount、abnormalTicker；工单 doneQty/progressPercent 为工序达成口径（非跨工序求和） |
| /api/ExecutionMonitor | keyword,status,dueFrom,dueTo,page,pageSize(≤100) | PC 执行监控聚合（见下） |
| /api/Abnormal | status,page,pageSize | 异常上报列表 |
| POST /api/Abnormal | multipart: type,description,workOrderId?,image? | 工人上报异常 |
| POST /api/Abnormal/{id}/resolve | handleNote? | 标记恢复（管理员/班组长） |
| GET /api/KnowledgeFile/{fileId}/raw | — | 返回原始文件流（预览/下载 docx/pdf/图片；需登录） |
| POST /api/KnowledgeFile | multipart: refType,refId,version?,file | 上传作业指导书/图纸/图片（管理员；白名单 pdf/docx/xlsx/jpg/png/webp，≤20MB） |
| DELETE /api/KnowledgeFile/{fileId} | — | 删除登记记录（磁盘文件保留） |
| POST /api/QuoteSuggest/preview | similarProductIds[],kingdeeMaterialCode?,markupPct?,defectReservePct? | 相似件建议报价预览（仅管理员 role=1）：金蝶材料+BOM + 本厂已复核报工时长估人工 → 建议总价 |
| GET /api/QuoteSuggest/products | keyword,page,pageSize | 本厂产品下拉（相似件选择） |
| GET /api/QuoteSuggest/kingdee/material | code= | 试连金蝶/Mock 物料（名称+成本价+BOM 材料费） |
| POST /api/QuoteSuggest/export | 同 preview body | 导出建议报价 xlsx |

**GET /api/ExecutionMonitor 口径**：
- summary：按 keyword+交期筛选后全集统计（不含已取消），忽略当前 status；含未开始/执行中/已结束/临期/超期/待处理异常数。临期超期仅未开始与执行中。
- orders：进度链同一分页；默认未结束在前，再按超期、有待处理异常、交期升序、ID；progressPercent 为工序达成进度。
- abnormals：当前厂待处理异常全集计数，明细最多 10 条，不随工单关键词/状态/交期筛选，含未关联工单的异常。
- overdueOrders：随关键词/交期过滤的超期摘要，最多 10 条+total。
- todos：pendingReviewCount 仅管理员/班组长（班组长限本组）；unassignedOpCount 仅派工授权角色；无权限对应字段为 null，不伪造 0。

---

## 7. AI 助手（PC 内置，仅管理员 role=1，默认关闭）

**数据流**：PC 页面 → POST /api/ai-assistant → DeepSeek 工具调用 → 本机白名单业务工具 → 答案 + 来源卡片。不经公网 MCP 绕回。

| 接口 | 说明 |
|---|---|
| GET /api/ai-assistant/status | enabled / configured / model；不返回密钥 |
| POST /api/ai-assistant/connection-test | 最小连通探测 |
| POST /api/ai-assistant/messages | 对话：mode=progress(查进度) 或 guide(问工艺)；可选 selectedProductCode / selectedOperationId |
| DELETE /api/ai-assistant/conversations/{id} | 清除本用户本工厂会话 |
| POST /api/ai-assistant/work-orders/search | 结果翻页（白名单筛选，不调模型） |
| POST /api/ai-assistant/report-evidence/search | 报工证据翻页（白名单字段，不读工资/单价） |

- **本机白名单工具**：`search_support_docs`（查服务器 .md/.txt 客服/操作说明）、`get_work_order_report_evidence`（查报工证据，只投影白名单字段，**不读工资/单价**）。
- 密钥放环境变量 `Ai__ApiKey`，不进仓库/浏览器；客服文档内容只是证据、不改变权限。

---

## 8. 错误追踪

- 每个 HTTP 响应可带响应头 **`X-Trace-Id`**（服务端生成，勿信任客户端自带关联值）；CORS 已暴露，浏览器 JS 可读。
- 统一返回仍为 `{code,msg,data}`：业务校验 msg 为可读提示（如"账号或密码错误"）；技术故障 msg 为安全说明（不含追踪号/SQL/堆栈/密钥），运维用响应头 `X-Trace-Id` 在服务器 ErrorLogging 目录日志检索。
- 前端公共封装 frontend/src/api/http.js 展示后端 msg，不把追踪号拼进用户提示。

---

## 9. 删除顺序（外键依赖倒序，有下级禁止直接删）

```
base_knowledge_file（知识库文件 → 引用产品/工序，先删）
→ salary_statement_item（工资明细 → 引用报工）
→ salary_statement（工资单 → 引用用户）
→ prod_report / prod_work_order
→ base_price_rule（工价表 → 引用产品/工序/部门/用户）
→ base_product
→ base_routing / base_routing_step
→ base_operation (及关联表)
→ base_defect_item → base_unit → sys_department
```

---

*本文件为稳定契约版，与 docs/02-数据字典.md、docs/33-接口对接说明.md 保持一致；源码变化时优先更新上述权威文档再同步本文件。*
