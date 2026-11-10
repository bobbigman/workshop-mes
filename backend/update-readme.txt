车间小工单 · 更新包（增量）

交付方式：直接交付整个 publish-update 文件夹（不打 zip）。

适用：已用整包 publish-win64 装过一次的客户机。
不要用本包做首次安装。
已上线机禁止用整包 publish-win64 覆盖安装目录（会冲掉 HTTPS/连接串）。

本包更新内容：wwwroot、*.dll（及明确需要的web.config）；另附apply-update、说明和scripts供部署使用。
本包不含 SupportDocs 智能客服知识库；知识库变更走独立部署流程，不随程序增量包发布。
本包不含且 apply-update 绝不覆盖：
- appsettings*.json
- start.bat / stop.bat
- WorkshopMes.exe、*.runtimeconfig.json、*.deps.json
- certs\、logs\

（exe / runtimeconfig / deps 若被覆盖，会提示 You must install .NET）

【本包功能 · 2026-11-10 · 版本 1.1.528】
- 上道复核数量不够时提示写清件数（2026-11-10，扫码报工）：
  上一道复核通过的数量不够时，不管是直接拦下不让报工，还是弹窗问「确定要继续吗」，提示都写明三组数：上一道复核通过多少件、本道已报多少、这次再报多少（合计多少）。多道工序一起批量报工时逐道列出。以前只说「数量不太够」，现场还得跑回去问班组长。
- 只有一道工序时自动选中（2026-11-10，扫码报工）：
  工单只有一道可选工序（比如只有品检）时，扫码进报工页会自动选中这道工序并带出单价，不用再手动点一次；有两道以上仍是原来的手选。
- 沿用上一版（1.1.513）已上线的能力：基础资料审核 / 禁用体系（docs/440、441、442）、建单弹窗去审核并铺满（444）、取价缺字段红感叹定位（445）、重新取价挪底栏（446）、工单批次手输（443）、工单列表审核后保持勾选（447）；1.1.479 起工单暂存 / 保存 / 审核三档与详情系统信息、通用表格列宽拖动记忆与右键复制、PC 内置 AI 与外部 MCP 多账套隔离、工单「作废」口径统一；1.1.424 起按工单查计件应付与本月工资受控追溯重算；1.1.410 起统一「工序价目表」取价与锁价。

【数据库迁移前置】本次为纯程序更新，不改表结构，无新增列。
（上一版 1.1.513 的审核 / 禁用字段由服务启动自动补建；已装过 1.1.513 的机器无需任何处理。）
建议：仍先备份数据库再更新，便于回滚。
（若服务器停留在 1.1.410 之前的旧工价结构，须先完成 413 迁移并至少升级到 1.1.424 后再装本包；正常在 1.1.424 及以上的机器无需处理。）

【更新步骤】（一键）
1. 把本文件夹拷到服务器任意位置（如 D:\WorkShop\publish-update）
2. 双击 apply-update.bat（自动：停进程 → 更新 wwwroot/dll → 用安装目录原有 start.bat 拉起）
3. 浏览器 Ctrl+F5，看登录页版本号（应显示 v1.1.528）

目标目录写死：D:\WorkShop\backend\publish-win64（不覆盖 appsettings / start.bat）

【若新版本新增了配置项】
不要整文件覆盖 appsettings.json。对比开发机与服务器键差，只把新键合并进服务器那份，旧值不动。
本包新增配置键（1.1.367 起）：
- `Ai` 段（AI 智能客服）：`Enabled`（默认 true，设 false 可关闭）、`BaseUrl`（默认 https://api.deepseek.com）、`ApiKey`（服务器须填自己的 key）、`Model`（deepseek-flash）、`BusinessTimeZone`、`RequestTimeoutSeconds`、`MaxToolRounds`、`MaxInputChars`、`MaxKnowledgeChars`、`SessionIdleMinutes`、`MaxTurnsPerSession`、`MaxSessionsPerUser`、`MaxSessions`；子段 `Ai:SupportDocs`（`Enabled`/`RootPath`/`MaxFiles`/`MaxFileBytes`/`MaxTotalBytes`/`MaxResults`/`MaxEvidenceChars`）。`Ai:SupportDocs:RootPath` 服务器须指向本机知识库目录。
- `KnowledgeBase:RootPath`：知识库检索根路径，服务器改为本机知识库目录。
（较早的服务器若还没有 `WecomReport` 段，补 `ApiToken` / `FactoryId` / `ConfirmExpireMinutes`，旧值不动。）

【若已误覆盖出现 “You must install .NET”】
用完整包 publish-win64 里的这两个文件盖回去（不要动 appsettings.json / start.bat）：
- WorkshopMes.runtimeconfig.json
- WorkshopMes.deps.json
必要时再盖 WorkshopMes.exe，并确认同目录有 coreclr.dll。
