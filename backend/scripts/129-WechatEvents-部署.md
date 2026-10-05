# 129 企微事件消息部署

1. 已上线实例使用现有 `pack-update.ps1` / `apply-update.bat` 增量更新，只更新前端及 DLL。不得覆盖 `appsettings*.json`、`start.bat`、`stop.bat`、证书或日志。发布包含 `scripts/` 中本功能的任务脚本、SQL 与本说明；原 apply-update 不拷脚本，须将新的任务脚本单独放进实例的 scripts 目录。
2. 启动时 `DbCompat` 自动幂等创建三张新表并将原成功日志计入额度；数据库账号须有建表权限。若由 DBA 升级，先在目标 SQL Server 库顺序执行 `129-wechat-events.sql`、`131-wechat-group-webhook.sql`、`141-wechat-webhook-only.sql`，再启动。脚本可重复执行；141仅停用旧session规则、处理旧通道未完成投递并加限流哈希字段，不删除业务或历史数据；更新前按既有流程备份库。首次安装由 EF 创建，契约以 docs/02 为准。本项目目前使用 EnsureCreated+DbCompat，不引入并行 Migrations 体系。
3. 沿用服务器现有 `WechatAlert:CronToken`。若未配置，只合并这一个键，不替换整份配置。设置任务运行账号的 `MES_BASE_URL`（如 http://127.0.0.1:8080）与 `MES_WECHAT_CRON_TOKEN`；不同实例使用各自地址和 Token，不将 Token 写入命令示例或日志。
4. Windows 任务计划程序新建事件任务：每 1 分钟重复执行，运行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File "D:\Mes\scripts\wechat-events-cron.ps1"`（替换为实际路径），配置“如果任务已运行：不启动新实例”。使用可读上述环境变量的专用任务账号。保留原交期预警每日任务，不把 `cron-push` 改为每分钟扫描。
5. PC「微信预警」保存内部群机器人Webhook及默认@成员，再开启总开关、新建群规则；仅有独立Webhook时，先保存设置（关闭），启用独立规则后再开总开关。正式授权沿用WechatDueAlert；填写企微UserId，@成员须在目标群中。默认成员只用于测试、交期，事件规则留空只群通知。服务器须能出站HTTPS访问qyapi.weixin.qq.com，MES不必暴露公网。
6. 提交一条报工，确认发送记录出现待发；正常网络下下一轮变为“企微已接受”。这是 API 接受，不表示已读。断网/超时、key失效、限额和规则停用都通过记录处理；结果未知先核实。额度按服务器部署时区自然日，生产机应设为客户当地时区（本项目通常为 Asia/Shanghai）。测试消息不计额度。

## 验证与故障处理

- 独立自动测试：`dotnet test tests/wechat-events/WechatEvents.Tests.csproj -p:SkipVersionBump=true`。默认在 localhost SQL Server 自动创建随机的 `WorkshopMes_129_Test_*` 专用库，用完删除；可用 MES_TEST_DB 指定测试服务器的连接配置，仍另建随机库，绝不复用业务库。无需真实企微凭证，HTTP 使用模拟响应。
- 任务 HTTP 或业务 code 非 0 时脚本退出 1。服务按条记录失败并给汇总，检查发送记录和服务器脱敏错误日志；规则/消息数据库错误不会假装报工成功。
- 租约为 2 分钟，发送阶段超时上限 40 秒；未进入发送阶段的过期领取可恢复，已进入发送阶段的过期领取转结果未知。未知结果不自动重发，确认未发送后才能排队。
- 旧手动/交期入口也使用统一工厂锁与额度账本，明确失败可重新尝试；超时或未知结果会阻止手动入口盲目整批重发，需按企微接收情况核实。
- 现场待验：客户纯内部群、机器人配置、真实接收和@效果、手机通知权限、Windows任务账号与分钟周期。本地模拟测试不能代替这一步。

## 141 升级与联网检查

- 升级前备份数据库；本次沿用旧配置，无新appsettings键。旧应用字段和值保留，旧session规则停用；管理员查看原模板/成员后重新建立群规则，旧待发不转群。旧发送阶段未知记录先核实，不能重新排队。
- 启动会自动按129→131→141幂等升级；141在现有尝试账本添加robot_key_hash及索引，数据库账号须具备ALTER/CREATE INDEX/UPDATE权限。DBA可按上述顺序先升级。`apply-update.bat`不复制scripts，新脚本须单独复制到服务器scripts目录；不覆盖任何服务器配置或启动脚本。
- 事件任务仍每分钟运行wechat-events-cron.ps1，交期任务仍每日运行wechat-alert-cron.ps1（按客户时间，如08:00），任务均禁止重叠实例。保留现有X-Cron-Token，结果非0查发送记录和脱敏日志。
- 服务器运行 `Test-NetConnection qyapi.weixin.qq.com -Port 443` 检查DNS及出站TCP443；该检查不发送群消息。连通不等于机器人有效，最终以真实群测试为准。
- 同实例同key跨工厂共用20次/分钟尝试限额，测试、失败也计数；事件延后，测试/交期提示稍后重试。不同实例建议各用独立机器人，不能凭各实例计数保证共用机器人的总额度。
- 本机自动回归采用随机独立SQL Server测试库和模拟HTTP，不发送真实群消息、不修改客户库。现场群接收、@、锁屏通知与客户任务执行须另验，不把构建完成写成生产已验收。
