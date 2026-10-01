# 来源索引（供审核用，不对车间念类名）

> 各篇正文里已不贴来源，全部集中在本表。对用户口述时用大白话，不必念内部文件名；审核时用本表回溯。

## 权威文档

| 文档 | 用到的口径 |
|---|---|
| `AGENTS.md` | 技术栈、三条硬规则、交付节奏 |
| `docs/01-PRD需求规格说明书.md` | 模块范围、报工流、AI 助手、硬规则 |
| `docs/02-数据字典.md` | 工单状态、交期、报工字段、复核双轨 |
| `docs/03-技术方案.md` | 架构、AI 可选能力 |
| `docs/05-局域网部署与PDA扫码.md` | 扫码、HTTPS、AI 配置与回退 |
| `docs/06-开发规约.md` | 报工权限、删除顺序 |
| `docs/48-制造知识库-文档问答-系统分析.md` | 知识库思路、只读查询 |
| `docs/50` / `docs/2B` | PC AI 助手口径（交期筛选与页面三态区分等） |

## 后端代码（审核用）

| 区域 | 文件/符号 | 对应主题 |
|---|---|---|
| 工单进度 | `WorkOrderService`：`FillListProgressAsync`、`LoadTasksAsync`、`GetAsync`、`RecalcStatusAsync` | 完成数取最小、无任务回退、状态 |
| 交期三态 | `DueStateHelper.Calc` | 正常/预警/延期 |
| 报工 | `ReportService.SubmitAsync` 等 | 部门权限、超量、良品不良 |
| 报表 | `ReportStatService` | 报表只算已通过；看板汇总 |
| 知识 | `KnowledgeService.QueryProductionGuideAsync`、`KnowledgeFileParser` | 问工艺旧入口行为 |
| AI | `AiAssistantService`、`AiToolDispatcher`、`AiBusinessTime`、`AiAssistantController` | 模式、工具、相对交期、管理员 |
| 鉴权 | JWT 取工厂/用户/角色 | 工厂隔离 |

## 前端代码（审核用）

| 文件 | 对应主题 |
|---|---|
| `frontend/src/views/layout/SideMenu.vue` | 真实菜单名与角色显隐 |
| `frontend/src/router/index.js` | 路由、`adminOnly` |
| `frontend/src/views/ai-assistant/index.vue` | AI 开关提示、出网说明、详情打开方式 |

## 明确未采用为「本系统事实」的内容

| 主题 | 说明 |
|---|---|
| 本系统库存数量 | 未实现库存账；金蝶库存属外部系统 |
| 延期自动归因（设备故障等） | 无对应主数据字段 |
| 把 AI 同名澄清写进旧 Knowledge 方法 | 禁止；旧行为保持不变 |

## 2026-09-20 操作手册核对补充

用户正文：docs/30、docs/31、docs/manual/第一次使用.md、docs/manual/遇到问题.md。对应 AI 操作文档通过 frontend/scripts/sync-manual.mjs 生成；变更和验收证据见 docs/52-操作手册-实施与验收清单.md。

已逐项核对 login、user、dept、operation、routing、product、order、report、review、h5/scan、h5/report 页面及 ReportService、ReviewService、WorkOrderService、ReportStatService。PRD 中旧登录方式、拖动排序与当前界面有差异，手册不把未出现的入口当成可用功能；业务规则仍以 PRD 和相应权威规约为准。
