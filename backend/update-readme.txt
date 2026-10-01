车间小工单 · 更新包（增量）

适用：已用整包 publish-win64 装过一次的客户机。
不要用本包做首次安装。
已上线机禁止用整包 publish-win64 覆盖安装目录（会冲掉 HTTPS/连接串）。

本包只含：wwwroot、*.dll（及明确需要的 web.config）。
本包不含且 apply-update 绝不覆盖：
- appsettings*.json
- start.bat / stop.bat
- WorkshopMes.exe、*.runtimeconfig.json、*.deps.json
- certs\、logs\

（exe / runtimeconfig / deps 若被覆盖，会提示 You must install .NET）

【本包功能 · 2026-09-27】
- 帮助 → 关于：版本、版权署名、使用说明、免责与责任限制（赔偿上限=该客户已付软件费）
- 登录页 / 帮助 / 优势速览：联合出品署名（胡忠谦、沈域诚；产品研发 / 客户成功经理）
- P0：报工提交拦截良品/不良负数，避免列表「报工汇总数量非法」
- P1：自动工单号改「当天最大尾号+1」，删单后再建不再撞号 500
- 无新表，可不改库

【更新步骤】（一键）
1. 把本文件夹拷到服务器任意位置（如 D:\WorkShop\publish-update）
2. 双击 apply-update.bat（自动：停进程 → 更新 wwwroot/dll → 用安装目录原有 start.bat 拉起）
3. 浏览器 Ctrl+F5，看登录页版本号与页脚署名

目标目录写死：D:\WorkShop\backend\publish-win64（不覆盖 appsettings / start.bat）

【若新版本新增了配置项】
不要整文件覆盖 appsettings.json。对比开发机与服务器键差，只把新键合并进服务器那份，旧值不动。
本包无强制新配置项。

【若已误覆盖出现 “You must install .NET”】
用完整包 publish-win64 里的这两个文件盖回去（不要动 appsettings.json / start.bat）：
- WorkshopMes.runtimeconfig.json
- WorkshopMes.deps.json
必要时再盖 WorkshopMes.exe，并确认同目录有 coreclr.dll。
