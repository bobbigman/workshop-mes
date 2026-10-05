# 133-执行指令：回滚 Pushplus 推送（推送整体搁置）

> 状态：已做（代码回滚完成；后端 `dotnet build` 因本机 `WorkshopMes.exe` 占用未能拷贝输出）
> 性质：正式代码回滚
> 目标：全量移除 Pushplus 推送改动，推送功能整体搁置；H5 异常上报保留、上报后不推送
> 历史备注：Pushplus 走微信服务号，消息折叠、无锁屏强弹窗，不满足车间告警"主动弹窗"诉求，已弃用；企业微信自建应用需客户域名备案、小客户不会做备案，此路**不可行**；且客户尚未同意试用产品，推送整体搁置，待客户确认试用后再评估合适渠道。

## 取舍（定死）
| 项 | 定案 |
|---|---|
| Pushplus | **全量回滚删除**：不保留任何代码、字段、界面、脚本 |
| 企业微信自建应用 | **不可行**：需客户域名备案，小客户不会做备案；不做 |
| H5 工人异常上报 | **保留**上报业务，删除"上报后触发推送"的调用 |
| 微信预警菜单 | 保留入口，页面仅显示提示，无任何配置表单/按钮 |

## 本次范围与落点（回滚清单）
- **前端**：`frontend/src/views/wechat-alert/index.vue` 移除全部 Pushplus 表单与测试按钮，页面只显示"消息推送功能暂未开放，敬请期待"；删除对应 API 调用逻辑。
- **后端**：
  - 删除 `backend/Data/WechatPushplusSchema.cs`，并移除 `DbCompat` 中对该 Schema 的调用；
  - 删除 `backend/scripts/133-pushplus.sql`；
  - 删除 Pushplus 推送服务、测试推送接口及相关代码；
  - `sys_wechat_alert_setting` / `sys_wechat_alert_rule` 回滚本次新增字段（channel / pushplus_token / pushplus_tokens / due_warn_days / overdue_repeat_hours 及规则扩展列）；
  - 删除异常上报完成后触发推送的调用。
- **文档**：`docs/02-数据字典.md` 复原两张表原始定义，删掉 Pushplus 字段记录；本文件（133）留档历史备注。

## 行为与口径（分叉写成显式条件）
- 当【进入微信预警页】→ 仅显示"消息推送功能暂未开放，敬请期待"，无表单、无按钮。
- 当【H5 工人上报异常】→ 正常落库、看板红标，**不触发任何推送**。
- 当【工单临期/超期】→ 仅系统内记录，**不推送**。
- 当【报工/工单/看板主流程】→ 完全不受影响，回归不坏。

## 止步线
- 企业微信自建应用（需客户域名备案）**不可行、不做**；不建任何新推送通道；推送整体待客户确认试用后再评估；不留"预留"代码。

## 验收（≤5 条，可观察，集中验收）
- [x] Pushplus 前端/后端/脚本/数据字典全部移除，微信预警页为纯提示。
- [x] H5 异常上报可用，上报后无推送调用。
- [x] 两张表字段已回滚，`docs/02` 已复原。
- [x] 前端 `npm run build` 通过。后端无 C# 编译错误；`dotnet build` 两次均因本机 `WorkshopMes.exe` 占用未能拷贝输出。
- [ ] 工人上报、工单主业务回归不坏（未部署、未跑现场）。

## 回滚核对清单
### 删除
- `backend/Data/WechatPushplusSchema.cs`
- `backend/Services/PushplusMessageSender.cs`
- `backend/scripts/133-pushplus.sql`

### 改回 / 去掉 Pushplus 依赖
- `backend/Services/WechatAlertService.cs`：恢复仓库 HEAD 企微实现（无 Pushplus / 无上报后推送）
- `backend/Services/AbnormalService.cs`：上报落库后不再调推送
- `backend/Program.cs`：去掉 `PushplusMessageSender` 与 `pushplus` HttpClient
- `backend/Models/SysModels.cs`、`backend/Data/AppDbContext.cs`：去掉 133 字段映射
- `backend/Models/WechatEventModels.cs`：去掉规则扩展列
- `backend/Services/WechatEventService.cs`：去掉 `channel=wecom` 判断（列已删）
- `backend/Data/DbCompat.cs`：改为幂等 DROP 133 列 / 恢复规则 CHECK
- `backend/Common/DueStateHelper.cs`：临期仍固定 3 天
- `frontend/src/views/wechat-alert/index.vue`：纯提示页
- `frontend/src/help/pageHelp.js`、`frontend/src/views/advantages/index.vue`
- `docs/02-数据字典.md`：两张表定义与口径复原

## 交付终点
- 输出回滚核对清单（删了什么、改了哪些文件）；本次不改部署、不动客户配置。
