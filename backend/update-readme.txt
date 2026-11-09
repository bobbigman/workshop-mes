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

【本包功能 · 2026-11-04 · 版本 1.1.479】
- 工单「暂存 / 保存 / 审核」三档（docs/425、435）：暂存=草稿，可缺工艺属性、不锁价、不可报工；保存=待审核（status=5）；【审核】通过才锁价并进入未开始，可报工。工单详情底部新增系统信息：创建人/创建时间、审核人/审核时间（docs/436）。
- 建单取价体验（docs/432、433、434）：取价失败提示改为短句、带产品编号与属性名（书名号），指向正确补全入口；取价必填工艺字段标「*」，缺字段在输入旁标红。
- 提示去内部 Id（docs/433）：报工、工资、调价、业务时区等回传给用户的提示一律不再展示数据库自增主键数字 Id 与裸配置键名，改用工单号/产品编号/工序名等业务编号；排障定位改走服务器日志。
- 通用表格改进（docs/431、60f4d36）：全部业务表格支持列宽拖动并记忆（按浏览器/厂/账号隔离）、悬停查看全文、表格底部「恢复默认列宽」；工单表格支持右键复制单元格显示内容及内网 HTTP 复制。
- PC 内置 AI 与外部 MCP 多账套隔离加固（docs/426）：查询锁定本人/本厂/当前权限，厂列表与关联对象收口；外部查询引入本人网页验证的短期查询凭证，关闭手机号直绑扩权与演示免绑定；原有特殊跨厂报工与独立 SQL 维护权限保留。
- 报工弹窗帮助（docs/430）：新增报工说明「？」浮层（大白话）；非管理员看到「计时补报（管理员用）」只读说明链接，后端权限红线不变。
- 工单状态按钮口径统一（docs/427、428、429）：「取消」统一改为「作废」，撤回两跳提示、恢复、删除区别在帮助浮层/手册/AI 知识库一致；状态码与流转规则不变。
- 1.1.424 起已具备的能力继续保留：按工单查计件应付、本月工资受控追溯重算；1.1.410 起的统一「工序价目表」取价与锁价继续保留。

【数据库迁移前置】（本次为自动，正常执行更新步骤即可）
本次新增 prod_work_order.audited_by / audited_at 两列，由服务启动时 DbCompat 幂等自动补建（加列，并给历史已审核单补审核人=创建人、审核时间=创建时间），无需手工执行迁移脚本。
建议：仍先备份数据库再更新，便于回滚。
（若服务器停留在 1.1.410 之前的旧工价结构，须先完成 413 迁移并至少升级到 1.1.424 后再装本包；正常在 1.1.424 及以上的机器无需处理。）

【更新步骤】（一键）
1. 把本文件夹拷到服务器任意位置（如 D:\WorkShop\publish-update）
2. 双击 apply-update.bat（自动：停进程 → 更新 wwwroot/dll → 用安装目录原有 start.bat 拉起）
3. 浏览器 Ctrl+F5，看登录页版本号（应显示 v1.1.479）

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
