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

【本包功能 · 2026-10-25 · 版本 1.1.410】
- ⚠️ 本版本含破坏性数据库结构变更（删表删列），必须先备份数据库并执行迁移脚本，否则服务启动即报错退出。详见下方【数据库迁移前置】。
- 彻底删除旧工价表，统一工序价目表取价与工资计算（docs/413）：全系统只保留「工序价目表」一处工价配置与取价来源，删除旧工价表 base_price_rule、双模式开关表 base_product_operation_pricing、旧加工选价列 base_operation.pricing_config、旧工价来源列 salary_statement_item.rule_id；新增价目表计价方式 base_operation_price_list.pricing_type（1计件/2计时/3单笔固定）与 salary_statement_item.price_list_id。
- 报工、补报、开工计时、工资生成与重算、报价建议、AI/MCP 试算全部改读「工单任务锁价 + 报工快照」，不再按当前价格重新取价；调价不改已保存单据。缺表、缺价、缺关键属性、重复命中、缺锁价一律明确报错，不回退旧工价、不把缺价记成 0。
- 前端删除「工价表」页面与菜单、加工选价编辑器、产品工序计价模式切换；工序价目表增加计价方式；建单/改单改为传 quoteTokens 由服务端取价锁价；帮助、引导与补价链接统一指向工序价目表。
- 接口收口：/api/PriceRule 及旧工价导入、旧计价模式切换接口已移除（旧 URL 不再提供）。
- 1.1.395 起的工单加工选项计价、手机工作台改版、代报工分人分行、手机 H5 与 AI 操作帮助等能力继续保留。
- 1.1.380 起已有的 AI 助手角色页签问答、AI 手机聊天、AI 工单进度顺序读取、定时企微简报、DeepSeek 多轮思考回传、检索应答口径 v2.4 等能力继续保留。
- 1.1.370 起已有的电脑工程师问答、企微对话报工（docs/242-250）、H5 我的工资条（docs/241）、计时工资全链路（docs/240）、AI 综合风险简报（docs/251、252）等能力继续保留。
- SupportDocs 支持文档/FAQ 不随本包更新。

【数据库迁移前置】（本版本必做，不做则服务起不来）
本版本启动时会检测旧工价结构，检测到即抛错拒绝启动：
「检测到旧工价结构（base_price_rule / base_product_operation_pricing / pricing_config / rule_id），请先执行 413 清理脚本，再重启。」

正确顺序：
1. 先完整备份服务器数据库（本迁移删表删列，不可逆，只能靠备份回滚）。
2. 停止 WorkshopMes 服务（可先跑 apply-update.bat，它会自动停进程；若启动步骤报错属预期，继续第 3 步）。
3. 在服务器数据库上执行本包 scripts\413-drop-legacy-price-rule.sql（脚本幂等，可重复执行）。
   sqlcmd -S localhost -d WorkshopMes -i scripts\413-drop-legacy-price-rule.sql
   或用 SSMS 打开该脚本，确认连接的是生产库后执行。
4. 执行安装目录原有 start.bat 重新启动，确认无上述报错。

注意：脚本会删除 base_price_rule、base_product_operation_pricing 两张表及
base_operation.pricing_config、salary_statement_item.rule_id 两列。
工资单表、工资明细表、工单、报工、基础资料均保留，脚本不动这些数据。
本地开发库已完成同样迁移并核对（旧对象已删、新列已加）。

【更新步骤】（一键）
1. 把本文件夹拷到服务器任意位置（如 D:\WorkShop\publish-update）
2. 先完成上方【数据库迁移前置】的备份与脚本执行
3. 双击 apply-update.bat（自动：停进程 → 更新 wwwroot/dll → 用安装目录原有 start.bat 拉起）
4. 浏览器 Ctrl+F5，看登录页版本号（应显示 v1.1.410）

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
