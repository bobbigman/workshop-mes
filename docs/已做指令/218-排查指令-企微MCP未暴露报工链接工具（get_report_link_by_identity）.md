# 218-排查指令：企微机器人连的 MCP 未暴露 get_report_link_by_identity（报工链接工具）

> 状态：**已完成并归档（2026-10-09）**
> 性质：内部试用（企微机器人「老胡个人电脑的机器人」已接本机 MCP 服务，真实账套/权限）
> 目标：让【企微机器人】通过【MCP 插件列表】得到【`get_report_link_by_identity` 工具】→ 工人发「报工」能拿到专属免密链接进 H5 报工
> 依据/拍板：胡忠谦 2026-10-09 确认企微「API/MCP插件」页列表**没有** `get_report_link_by_identity`；用户口径生产已部署，但工具仍未暴露

## 取舍（已定 / 待确认）

| 项 | 方案及依据 | 状态 |
|---|---|---|
| 做/不做 | 排查并修复 MCP 工具未暴露；**不改** `get_report_link_by_identity` 的契约 | 已定 |
| 先确认编译、再重启 | 新工具是服务**启动时反射扫描 `[McpServerTool]`** 才进 `tools/list`；代码未编译进 DLL 或进程未重启，列表都不会出现 | 已定 |
| 复用还是另写 | 复用既有 `get_report_link_by_identity`（docs/215）；不另造工具、不重写前端 | 已定 |
| PC / H5 | 无界面改动 | 不适用 |

## 本次范围与落点

- **交付**：企微「API/MCP插件」页出现 `get_report_link_by_identity`，添加后工人发「报工」能回专属免密链接
- **已部署（代码层面）**：`McpReportLinkService`（`GetReportLinkByIdentityAsync`）+ `ReportLinkMcpTools`（`[McpServerTool(Name="get_report_link_by_identity")]`，`Program.cs:185` 已注册 `IMcpReportLinkService`）
- **缺口**：运行中 MCP 服务的 `tools/list` 不含该工具 → 需按顺序核查「编译 → 重启 → 扫描」
- **数据与契约**：不改表（复用 `sys_user.login_qr_secret`）、不改 docs/02、不改接口；identity=手机号(11位)/登录账号/唯一姓名，复用 150 `login_qr_secret` + `login-by-qr`
- **文档**：结论若指向需改代码/重启机制，另起指令；本指令只排查与落工具

## 行为与口径（分叉写成显式条件，禁止脑补）

- 当【`dotnet build`（backend）报错或产物不含新类型】→ 先修编译错误，确认 `ReportLinkMcpTools` / `McpReportLinkService` 进入输出的 DLL（未被排除/裁剪），再谈重启
- 当【编译通过但进程未重启】→ 本机后端 `backend` 目录：`stop.bat` → `start.bat`（或结束 `WorkshopMes.exe` 进程再 start）；重启后重查 `tools/list` 应出现 `get_report_link_by_identity`
- 当【重启后 `tools/list` 仍无该工具】→ 核对：① MCP 工具扫描是否反射 `[McpServerTool]`；② `Program.cs` 是否注册；③ 是否有按账套/档位/开关过滤工具的逻辑（`EnableMcp`、授权档）把新工具拦了；贴编译警告与扫描代码位置
- 当【工具出现】→ 到企微「API/MCP插件」页点 **+ 添加**，列表里选「微聚-老胡开发电脑 / get_report_link_by_identity」保存
- 当【企微加完工具仍不调】→ 另核查企微机器人侧是否有「报工」路由/人设把它指到该工具（此前 `docs/45` 是扣子人设，**企微机器人不适用**，需在企微机器人配置里另配）
- 当【工人发「报工」带手机号/账号/姓名】→ 应回专属报工链接卡片；缺凭证应先回「请发您的手机号（或登录账号/姓名）」

## 止步线

- 后置项：企微机器人「报工」路由/人设配置（工具暴露后仍不调时另起）；149 企微认人发卡路线；对话口述报工
- 后置项不预建接口、开关、表、按钮

## 验收（3～5 条，可独立判断）

- [ ] `dotnet build`（backend）通过，`ReportLinkMcpTools` / `McpReportLinkService` 确认在编译产物
- [ ] 重启后端后，MCP `tools/list` 出现 `get_report_link_by_identity`
- [ ] 企微「API/MCP插件」页 + 添加 能列出并保存该工具
- [ ] 企微发「报工」→ 回「请发手机号/账号/姓名」；发 11 位手机号 → 秒回专属报工链接，点开免密进工作台（真机联调）
- [ ] 权限：按「工序报工权限=部门」校验，无权限进不去（三条硬规则）

## 交付终点

- 附实际排查结论（编译 or 重启 or 扫描机制）、工具是否暴露、企微添加结果、剩余限制
- 真机企微全链路联调（发凭证→点链接→报工→看板回传）未完成单独标注，**不把「工具出现」写成「生产已验收」**
