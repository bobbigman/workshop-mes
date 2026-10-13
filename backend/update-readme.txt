车间小工单 · 更新包（增量）

适用：已用整包 publish-win64 装过一次的客户机。
不要用本包做首次安装。
已上线机禁止用整包 publish-win64 覆盖安装目录（会冲掉 HTTPS/连接串）。

本包更新内容：wwwroot、*.dll（及明确需要的web.config）；另附apply-update、说明和scripts供部署使用。
本包不含且 apply-update 绝不覆盖：
- appsettings*.json
- start.bat / stop.bat
- WorkshopMes.exe、*.runtimeconfig.json、*.deps.json
- certs\、logs\

（exe / runtimeconfig / deps 若被覆盖，会提示 You must install .NET）

【本包功能 · 2026-10-05】
- 工作区当前代码整体构建（含未提交改动）；没有部署到客户服务器。
- 出品署名：服务器 appsettings 可加可选键 Instance:CreditPartner（伙伴名）。缺该键或为空，署名仍是「小蜜蜂报工・胡工」，不必改也能启动。
- 微信预警（141）：正常推送仅群机器人 Webhook；交期临期+超期每天汇总一条；启动幂等升级 129→131→141。库账号须有 ALTER/CREATE INDEX/UPDATE。保留原 CronToken；服务器需能出站访问 qyapi.weixin.qq.com。
- scripts 目录不由 apply-update 复制；任务脚本/SQL 按需单独复制。
- 无强制新配置项。勿整文件覆盖 appsettings。

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
