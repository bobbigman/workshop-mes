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

【本包功能 · 2026-10-21 · 版本 1.1.380】
- AI 助手电脑页签按角色视角重写：老板/车间主管/财务/车间师傅/AI 五个页签各 10 题，含电脑部门「业财打通三件事」问答（docs/259-261）。
- AI 助手题目与移动端体验：卖点左栏竖排、手机端标题与常见问题抽屉、聊天正文留白优化（docs/261-264）。
- 修复 DeepSeek 多轮对话 reasoning_content 回传（docs/263）。
- 检索与应答口径对齐 v2.4（docs/259、262）。
- 1.1.370 起已有的电脑工程师问答、AI 手机聊天、AI 工单进度顺序读取、定时企微简报（docs/251）等能力继续保留。
- 企微对话报工（docs/242-250）、H5 我的工资条（docs/241）、计时工资全链路（docs/240）、AI 综合风险简报（docs/251、252）等 1.1.352 已有能力继续保留。
- SupportDocs 支持文档/FAQ 不随本包更新。

【更新步骤】（一键）
1. 把本文件夹拷到服务器任意位置（如 D:\WorkShop\publish-update）
2. 双击 apply-update.bat（自动：停进程 → 更新 wwwroot/dll → 用安装目录原有 start.bat 拉起）
3. 浏览器 Ctrl+F5，看登录页版本号（应显示 v1.1.380）

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
