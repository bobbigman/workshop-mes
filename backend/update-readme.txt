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

【本包功能 · 2026-10-20 · 版本 1.1.370】
- 电脑端 AI 智能客服/助理问答（docs/259、260）：PC 新增「电脑工程师」问答入口与 AI 能力问答，覆盖 AI 当前能力、WorkBuddy 区别与演示、MES 查询和报表、Excel 导入准备、金蝶前提、已交付边界、老板/财务/现场价值、云模型数据边界等；AI 按「关键发现 + 简要细节」结构作答，安全 Markdown 渲染，贴心建议卡片，仅首次回复问候；空闲会话自动回收、失败首请求自动清理。
- 检索与措辞对齐 v2.4（docs/259）：AI 答案措辞、工程师角色与 IT/计算机部门焦点问题、检索章节与知识映射同步修正。
- AI 手机聊天（H5）界面优化：手机端 AI 对话正文留出空间、收起常见问题，更清爽（092af2c）。
- AI 工单进度读取改为共享 DbContext 顺序读取，避免并发冲突。
- 定时企微简报复用既有风险聚合逻辑（docs/251 能力继续保留）。
- 企微对话报工（docs/242-250）、H5 我的工资条（docs/241）、计时工资全链路（docs/240）、AI 综合风险简报（docs/251、252）等 1.1.352 已有能力继续保留。
- SupportDocs 支持文档/FAQ 不随本包更新。

【更新步骤】（一键）
1. 把本文件夹拷到服务器任意位置（如 D:\WorkShop\publish-update）
2. 双击 apply-update.bat（自动：停进程 → 更新 wwwroot/dll → 用安装目录原有 start.bat 拉起）
3. 浏览器 Ctrl+F5，看登录页版本号（应显示 v1.1.370）

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
