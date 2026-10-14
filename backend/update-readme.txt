车间小工单 · 更新包（增量）

适用：已用整包 publish-win64 装过一次的客户机。
不要用本包做首次安装。
已上线机禁止用整包 publish-win64 覆盖安装目录（会冲掉 HTTPS/连接串）。

本包更新内容：wwwroot、*.dll（及明确需要的web.config）；另附apply-update、说明和scripts供部署使用。
本包另附 **SupportDocs 智能客服知识库**（含 _retrieval.json）：apply-update.bat 已内置自动拷贝，双击即自动更新到 `D:\WorkShop\SupportDocs`（脚本顶部 KNOWLEDGE 变量，路径不同先改这行）。
本包不含且 apply-update 绝不覆盖：
- appsettings*.json
- start.bat / stop.bat
- WorkshopMes.exe、*.runtimeconfig.json、*.deps.json
- certs\、logs\

（exe / runtimeconfig / deps 若被覆盖，会提示 You must install .NET）

【本包功能 · 2026-10-14 · 版本 1.1.322】
- 小批量复制（docs/…）：产品、工艺路线、工序支持「复制已有资料 → 改差异 → 保存」，减少小批量建号/建工艺的录入步骤；仍遵守产品编号唯一、工序报工按部门、删除依赖倒序等硬规则。
- 智能客服检索规则热加载：SupportDocs 检索规则可外置到 `SupportDocs/_retrieval.json`（关键词/同义词/加权/排除），改文件即生效、缺文件回退内置默认。
- 登录背景：加入微聚轻 MES 生产看板背景并压缩至 132KB；登录页记住所选工厂背景，减少手机端图片加载（前端增量）。
- 报工复核·工序前置数量校验（docs/230）：复核页按工艺路线上工序已复核良品算出本工序可报上限，列表新增「上工序合格库存/可报上限」列；报工数量超上限的行浅黄高亮并挂橙色【数量异常】标签；批量通过自动跳过异常单据；单点通过弹出特批确认弹窗（备注不落库）。PC 端与 H5 端一次做完，全厂通用、所有账套开启。
- 报工数量口径设置（docs/230）：新增工厂级配置 sys_report_qty_setting（每工厂一条），允许超报比例 0～999%（默认 0）、上道不良是否计入下道可报（默认不计入）；仅管理员可改。首次启动 DbCompat 自动幂等建表，无需手工执行 SQL。
- SupportDocs 支持文档/FAQ 同步更新（系统能力总图、常见问题、扫码报工问答、50问等）。
- scripts 目录不由 apply-update 复制；任务脚本/SQL 按需单独复制。
- 新增配置键：`Instance:CreditPartner`（空=胡工单方版；填伙伴名如「沈工」=胡工、沈工联合出品）、`Instance:Url`（空即可）。服务器 appsettings 只**手工加这两个新键、旧值不动**，勿整文件覆盖。

【更新步骤】（一键）
1. 把本文件夹拷到服务器任意位置（如 D:\WorkShop\publish-update）
2. 双击 apply-update.bat（自动：停进程 → 更新 wwwroot/dll → 用安装目录原有 start.bat 拉起）
3. 浏览器 Ctrl+F5，看登录页版本号

目标目录写死：D:\WorkShop\backend\publish-win64（不覆盖 appsettings / start.bat）

【若新版本新增了配置项】
不要整文件覆盖 appsettings.json。对比开发机与服务器键差，只把新键合并进服务器那份，旧值不动。
本包新增配置键：`Instance:CreditPartner`、`Instance:Url`。仅手工把这两个新键合并进服务器 appsettings（旧值不动），勿整文件覆盖。

【若已误覆盖出现 “You must install .NET”】
用完整包 publish-win64 里的这两个文件盖回去（不要动 appsettings.json / start.bat）：
- WorkshopMes.runtimeconfig.json
- WorkshopMes.deps.json
必要时再盖 WorkshopMes.exe，并确认同目录有 coreclr.dll。
