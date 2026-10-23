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

【本包功能 · 2026-10-23 · 版本 1.1.395】
- 工单加工选项计价（方案A，docs/280）：PC工序新增「加工计价」入口，按产品维护基础价/成套价、设置计价单位与报工数量换算、不良扣价；加工单选组与选项附加价，各组叠加；建单时选基础价和每组加工要求，服务端复算并锁定完整计价依据。工单锁价后调价不改历史，已有报工禁止重定价；普通/代报/多人分摊/批量补报、PC/H5 预览与报工快照统一读已锁价（docs/268）。
- 手机工作台改版（docs/278-279）：彩色八宫格、今日成果前置、直达代报补报、去掉「更多功能」直达管理操作、紧凑布局避让底栏；手机登录进入工作台并精简现场入口。
- 代报工手机分人分行填写并保护切换与重试（docs/272）；H5 报工数量移入首屏并明确提交行为。
- 手机更多功能与受限 AI 操作帮助（docs/273）；手机 H5 对齐电脑端：工作台/管理操作入口调整 + AI 操作帮助角色化（docs/277）；AI 操作帮助去重、问题面板默认 3 条并支持查看全部 10 条。
- 工序列表默认按编号升序并保持分页顺序稳定。
- 工资生成与重算沿用报工工价快照，避免调价追历史（docs/268）。
- 1.1.380 起已有的 AI 助手角色页签问答、AI 手机聊天、AI 工单进度顺序读取、定时企微简报、DeepSeek 多轮思考回传、检索应答口径 v2.4 等能力继续保留。
- 1.1.370 起已有的电脑工程师问答、企微对话报工（docs/242-250）、H5 我的工资条（docs/241）、计时工资全链路（docs/240）、AI 综合风险简报（docs/251、252）等能力继续保留。
- SupportDocs 支持文档/FAQ 不随本包更新。

【更新步骤】（一键）
1. 把本文件夹拷到服务器任意位置（如 D:\WorkShop\publish-update）
2. 双击 apply-update.bat（自动：停进程 → 更新 wwwroot/dll → 用安装目录原有 start.bat 拉起）
3. 浏览器 Ctrl+F5，看登录页版本号（应显示 v1.1.395）

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
