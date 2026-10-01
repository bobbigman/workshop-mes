# Cursor 开发指令 · LicenseTier 三档授权与前端显隐

> 日期：2026-09-29｜状态：已完成
> 定位：一套代码 + 工厂级"档位"字段（LicenseTier）+ 功能权限映射 + 前后端两级控制。不做三套系统。
> 依据：[产品三档定盘与入门款功能清单](推广方案/产品三档定盘与入门款功能清单.md)、`docs/01/02/03/06`、`.cursor/rules/` 对应规则。
> 配套：轻量授权（云激活 + 宽限期，防白用/复用）另立指令，不并入本指令。

---

## 一、目标与原则

- **一套代码**，按 `factory` 级 `license_tier` 开放功能子集，一个工厂一个档。
- 三档：`trial`（入门 3500，引流钩子） / `enterprise`（企业 8800，主力） / `flagship`（旗舰 11800，差异化）。
- **升级平滑**：改 `factory.license_tier` 即生效，同一套表，数据**天然不重录**。
- 技术栈不可变更：.NET 8 + EF Core + SQL Server；Vue3 + Element Plus（PC）/ Vant（H5）；命名空间 `ahu.MicrosoftMes`。

## 二、功能权限映射（单点维护，集中定义）

| 功能键 `LicenseFeature` | 中文 | trial | enterprise | flagship |
|---|---|---|---|---|
| （无键，三档共享） | 下单出工单 / 派工到人 / 手机派工报工 / 车间看板 | ✓ | ✓ | ✓ |
| `PieceWage` | 计件工资（可重算）+ 工价表 | ✗ | ✓ | ✓ |
| `PrintLabel` | 打印工单/条码 | ✗ | ✓ | ✓ |
| `ScanReport` | 扫码报工 | ✗ | ✓ | ✓ |
| `WechatDueAlert` | 交期预警 + 企微推送 | ✗ | ✓ | ✓ |
| `ReportReview` | 报工复核 | ✗ | ✓ | ✓ |
| `MultiWorkshop` | 多车间 | ✗ | ✗ | ✓ |
| `KingdeeMcp` | 接金蝶（MCP） | ✗ | ✗ | ✓ |
| `DataScreen` | 数据大屏 | ✗ | ✗ | ✓ |
| （本指令不做） | 定制 / 外挂 | ✗ | 加购 | 含 / 按需 |

> 映射集中在 `LicenseTierPolicy`（后端）+ 同名常量（前端 `licenseTier.js`），加购/升级只改一处。

### 2.1 功能键 ↔ 现有菜单 / 路由 / API（对照现状拍板）

> 表名是 `sys_factory`（指令口语「factory」指此表）。现状无 `license_tier` 字段、无换厂会话接口（换厂=重新登录选账套）。

| 功能键 | 最低档 | PC 菜单/入口 | 路由 | 后端需真校验的入口 | 备注 |
|---|---|---|---|---|---|
| （共享） | trial | 工单、执行监控、报工、生产报表、车间看板；基础数据除工价表外；用户/部门/自定义字段/登录页/AI/异常/帮助 | `/order` `/execution-monitor` `/report` `/stat` `/board` 等 | 不叠加 tier | `/board` = **车间看板**，三档都开 |
| （共享） | trial | H5：工单、任务（派给我的）、异常 | `/h5/orders` `/h5/tasks` `/h5/abnormal`；`Assign/my-tasks`；`Report` 提交 | 不叠加 tier | 入门款报工主路径 |
| `PieceWage` | enterprise | 菜单「工价表」「工资报表」 | `/price-rule` `/salary` | `PriceRuleController`*、`SalaryController`* | 工价是计件前提，一并锁 |
| `PrintLabel` | enterprise | 工单页「打印」「打印设置」按钮（非独立菜单） | 无独立路由 | `PrintSettingController`* | trial 隐藏按钮即可 |
| `ScanReport` | enterprise | H5 TabBar「扫码」 | `/h5/scan`（及进 `/h5/report` 的扫码入口） | **无独立扫码 API**；本轮以前端路由+Tab 显隐为主；`Report` 提交不因扫码路径加锁（派工报工共用） | trial 禁止进扫码页 |
| `WechatDueAlert` | enterprise | 菜单「微信预警」 | `/wechat-alert` | `WechatAlertController`*（含 push/cron） | |
| `ReportReview` | enterprise | 菜单「报工复核」 | `/review` | `ReviewController`* | 仍叠加 role（管理员/班组长） |
| `KingdeeMcp` | flagship | 菜单「建议报价」；MCP 授权相关 | `/quote-suggest`；`/mcp`；`mcp-auth` | `QuoteSuggestController`*、`McpAuthController`*；MCP 工具入口按厂读 tier 拒绝 | 排产评分 `/schedule` **本轮不锁**（矩阵未列） |
| `MultiWorkshop` | flagship | **现状无页面** | — | 仅登记功能键；有接口再挂 `EnsureFeature` | 本轮无 UI 可藏 |
| `DataScreen` | flagship | **现状无独立大屏页**（≠ `/board`） | — | 同上，仅登记功能键 | 勿把车间看板当成数据大屏关掉 |

\* 写操作与读配置均校验；列表/查询一并拦，避免 trial 用接口扒高级数据。

### 2.2 本轮明确不做 / 不锁

- 云激活、宽限期、防复用 → 另立授权指令。
- 改档 UI：本轮**不提供**管理端改 `license_tier` 的页面；改档用 SQL / 后续授权指令。新建账套默认 `trial`。
- 矩阵未列且已有的能力（AI 助手、排产评分、异常、生产报表、新手入门等）→ **不锁**。
- 定制/外挂加购 → 不实现开关。

## 三、硬约束（红线）

- tier 按**工厂**、role 按**用户**，两维正交，都要校验，不叠加错位。
- **后端必须真校验**（不能只靠前端隐藏防绕过）；例外仅 `ScanReport`（无独立 API，见 2.1）。
- 改表先更新 `docs/02-数据字典.md`（DDL 权威），不得凭记忆另写。表名 `sys_factory`。
- 异常统一 `Common/ThrowHelper.cs`，不吞、带上下文（拒绝时带 factoryId / tier / feature）。
- 三条业务硬规则（产品编号唯一、报工权限=部门、删除倒序）不得回归。

## 四、原子步骤（每步显式带条件判断）

### 步骤 1 · 数据层：`sys_factory` 加 `license_tier`
- 更新 `docs/02`：`sys_factory` 加 `license_tier NVARCHAR(16) NOT NULL`，默认 `'trial'`；取值仅 `trial` / `enterprise` / `flagship`。
- 加实体字段 + EF 映射 + `DbCompat` 幂等建列（`IF NOT EXISTS … sys.columns`），存量行 `UPDATE` 为 `trial`。
- **当** `docs/02` 尚无该字段 → 先改权威文档再加代码；
- **当** 已有同类字段 → 复用，不重复加（现状：无）；
- **当** 新建账套（`FactoryService.Create`）→ 写死默认 `trial`。
- 验收：`docs/02` 已同步；建列幂等；实体映射正确；`dotnet build -c Release` 0 错 0 警。

### 步骤 2 · 后端：枚举 + Policy + 鉴权
- 定义 `LicenseTier`、`LicenseFeature`、`LicenseTierPolicy`（功能→最低档，单点维护）。
- 提供 `ILicenseTierService`（按 `factoryId` 读档）+ `EnsureFeature(factoryId, feature)`（拒用 `ThrowHelper.BizUser`，文案可读，如「当前为入门款，该功能需企业版」；日志带 factory/tier/feature）。
- **当** 功能属 2.1「共享」→ 不调用 Ensure；
- **当** 功能属 2.1 有键且有 Controller → 在对应 Controller 入口叠加 Ensure（不删原有 role 校验）；
- **当** `MultiWorkshop` / `DataScreen` 尚无页面/API → 只登记 Policy，不加空调用；
- **当** MCP `/mcp` 请求 → 解析到工厂后按 `KingdeeMcp` 拒绝（实现点跟现有 MCP 工厂解析走，不另造租户模型）。
- 验收：映射单点；用 trial 厂调 `Salary`/`Review`/`WechatAlert`/`PrintSetting`/`QuoteSuggest` 被拒；role 逻辑无回归。

### 步骤 3 · 登录返回当前工厂 tier
- `LoginResultDto` 增加 `licenseTier`（字符串）；登录时从所选 `sys_factory` 读取写入。
- 前端登录成功后 `localStorage.setItem('licenseTier', …)`，与 `role`/`factoryCode` 同生命周期。
- **当** 多账套登录选厂 → 返回该厂 tier；
- **当** 用户改选另一账套重新登录 → 覆盖 localStorage，不残留旧档；
- **当** 现状无「登录后换厂」接口 → **不新做换厂 API**；验收按「重新登录」计。
- 验收：登录响应含 tier；换账套重登后前端读到新值。

### 步骤 4 · 前端：菜单 / 路由 / 按钮显隐
- 新增 `frontend/src/utils/licenseTier.js`（与后端 Policy 同表：feature → 最低档；`canUse(tier, feature)`）。
- `SideMenu.vue`：在现有 `role` 判断上叠加 `v-if="canUse(tier, …)"`（工资/工价/微信预警/复核）。
- `router.beforeEach`：在现有 `adminOnly`/`leaderOk`/role=2 逻辑之后，按 `meta.feature`（或路径表）拦无档路由，回退到 `/order` 或 `/h5/tasks`。
- 工单页：打印按钮 / 打印设置 → `PrintLabel`。
- H5 `TabBar`：trial 隐藏「扫码」；直开 `/h5/scan` 被路由拦回任务页。
- **当** 功能需按档隐藏 → 前端隐藏（体验）；
- **当** 后端已拦截 → 前端隐藏不作为安全依据；
- **当** DB 改档后用户仍持旧 localStorage → 以**下次登录**刷新为准（本轮不做心跳拉档）；验收改档后重新登录看菜单。
- 验收：trial 看不到工资/工价/复核/微信预警/打印/扫码/建议报价；enterprise 看不到建议报价（金蝶）；flagship 全开（多车间/大屏仍无页则无可点入口）。

### 步骤 5 · 升级平滑验证
- SQL 将某厂 `license_tier` 从 `trial` → `enterprise`，该厂用户**重新登录**后功能即开，数据不重录。
- **当** 升档 → 原报工/工单数据完整；升到 enterprise 后工资/复核等入口出现；
- **当** 降档 → 高级入口消失，工单/报工数据不受损（历史工资行保留，仅入口/API 不可用）。
- 验收：升/降档往返，数据无损，功能边界正确。

### 步骤 6 · 回归 + 文档同步
- 三条硬规则无回归；`docs/02` 已同步；`docs/01` 或 `docs/03` 补一句三档与 `license_tier`（点到为止，不写长文）。
- **当** 产品优势速览需对外讲档 → 可补三档差异一小段；**本轮非必须**（避免和定价文案打架则跳过）。
- **当** `docs/02` 无 DDL 变更 → 不改（本轮有变更，必须改）。
- 验收：后端 `dotnet build -c Release`、前端 `npm run build` 通过。

## 五、验收清单（勾选用，做一项勾一项）

- [x] 步骤 1：`docs/02` 同步 + 建列幂等 + build 通过
- [x] 步骤 2：Policy 单点 + Controller/`McpGate` 挂 Ensure（运行时越权留证待部署后验）
- [x] 步骤 3：登录返回 `licenseTier`；换账套重登刷新
- [x] 步骤 4：菜单/路由/按钮按 role+tier；trial 无高级入口；H5 无扫码 Tab
- [ ] 步骤 5：SQL 升/降档 + 重登，数据无损、边界正确（需部署后 SQL 改档实测）
- [x] 步骤 6：回归 + `docs/02`（及 01/03 一句）同步；后端/前端 build 通过

> 完成前复核：输入已读全、约束未越界、后端真校验、数据字典已同步、实测留证。
