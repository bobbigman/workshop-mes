# 415 MCP 通用 SQL 维护

日期：2026-10-25。用户明确选择允许查询与写入、不限价目表、独立维护权限。
状态：已做（实现与权限验证完成）；未部署，未执行服务器SQL。

## 契约与范围

- 在现有 `/mcp` 增加 `prepare_maintenance_sql`、`execute_maintenance_sql`，不改变 `/api/diag/sql` 只读约束。工具由服务器执行SQL，本机不需要直连数据库。
- 默认关闭；独立 `Mcp:Maintenance:Enabled` 和至少32字符随机 `Mcp:Maintenance:Key`。客户端在现有MCP认证之外增加 `X-Maintenance-Key` 请求头，不将维护密钥放在工具参数中。普通账套钥匙、手机号绑定和DemoSkipAuth均不能代替维护密钥。
- 维护权限为整个当前数据库的运维权限，**不受账套隔离**；只发给系统维护者，不能发给客户/工人。SQL实际权限受服务器数据库登录权限约束；不承诺通过字符串过滤限制任意SQL的服务器访问范围。
- Prepare仅展示SQL及当前服务器/数据库，不执行提交的SQL；生成5分钟有效的一次性确认凭据，绑定SQL、数据库连接、维护密钥和当前MCP厂别。执行只收该凭据，不接收替换SQL；并发重复执行仅允许一次。
- 任意SQL允许自带事务。工具不声称能自动回滚所有任意SQL；脚本应自行使用XACT_ABORT、TRY/CATCH和显式事务。失败/断连可能已有语句提交，不自动重试；先查结果。
- 支持多语句和多个结果集，不支持SSMS/sqlcmd的GO等客户端指令；上限64000字符、30秒、返回最多5个结果集各200行/64列，每格最多4000字符（binary为2000字节十六进制），总文本约100万字符。截断仅影响返回，不停止后续SQL执行；未提交事务会被回滚并报告失败。`recordsAffected=-1`表示驱动未提供计数，精确行数应在SQL中`SELECT @@ROWCOUNT`。
- 日志记录当前MCP钥匙别名、厂别、SQL SHA256、目标服务器/库、执行开始与成功/失败；不记录维护密钥和完整SQL正文。连接与SQL错误按ThrowHelper抛出上下文，返回前脱敏。
- 不自动部署，不修改服务器配置，不自动执行414删除。完成后交付配置方法和调用步骤；服务器更新且配置启用后才可实际使用。

## 验收

- [x] 默认关闭、错误密钥、普通MCP权限不能执行（含演示免绑定场景）。
- [x] 确认凭据过期/跨厂/密钥变化/连接变化拒绝；重复或并发调用最多执行一次（计划仓库断言）。
- [x] 后端构建通过；独立检查34项通过（新增维护权限及计划检查、已有DiagSqlService和McpDataAccess回归）。
- [ ] SQL Server真实查询、写入、返回截断、事务回滚及远端连接验证：尚未实测。

## 服务器与本机配置（部署后由维护者配置）

不另开公网端口，沿用现有MCP入口。服务器合并以下配置到原有Mcp节，不覆盖原文件或旧值；不要把密钥提交Git：

```json
"Maintenance": {
  "Enabled": true,
  "Key": "替换为维护者本机生成的至少32字符随机密钥"
}
```

也可使用进程环境变量 `Mcp__Maintenance__Enabled=true` 和 `Mcp__Maintenance__Key=随机密钥`。生成示例：`[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))`（PowerShell 7）。重启或部署遵守增量更新铁律，本次未执行。

本机MCP客户端为维护连接单独起名，例如 `workshop-maintenance`，使用服务器现有MCP地址和普通入口Bearer，并额外添加请求头 `X-Maintenance-Key: 上述维护密钥`。这份连接配置只保留在维护者本机，不能给普通客户。维护权限与普通账套钥匙均须有效。

## 调用顺序

1. `prepare_maintenance_sql`，参数 `{"sql":"SELECT DB_NAME() AS database_name;"}`。返回当前数据库和待执行SQL；此步没有执行该查询。
2. 核对返回的服务器、库名和完整SQL后，用返回的 `confirmationToken` 调用 `execute_maintenance_sql`，参数 `{"confirmationToken":"准备步骤返回的凭据"}`。每次查询/写入均按这两步执行。
3. 414清理：先用本工具读取实际库名与8张旧表状态，保存所需备份；将414脚本核实的服务器/数据库填好，`@Execute=0`运行预检，再另行准备和执行`@Execute=1`版本。JSON结果可能截断，不可用截断结果充当完整备份。任一引用/状态/数量异常即停止。
4. 凭据5分钟过期或进程重启后失效。执行失败/超时先查询实际结果，不得自动重新准备同一删除SQL并再次执行。

## 验证记录

### 2026-10-25 远程首次启用后的连接修复

远程入口和独立维护密钥验证已通过，Prepare阶段曾报sa登录失败，没有执行待提交SQL。检查发现维护工具与只读诊断接口均从当前EF连接读取ConnectionString；该连接已被MCP入口用于查询账套，SQL客户端默认Persist Security Info=False会在打开连接后移除密码，复制该字符串新开连接会丢失凭据。修复为统一从后端有效IConfiguration的ConnectionStrings:Default新建连接，保留环境配置覆盖，不开启Persist Security Info、不改服务器密码或登录权限。新增3项凭据保留、配置覆盖与缺配置拒绝检查，共37项检查通过。服务器须更新修复包后再验证；此时尚未完成远程SQL执行。

`backend.Tests`完整工程目前被既有`OperationPricingTests`引用已删除的`ProcessingConfig/ProcessingChoice`阻断，与本次维护工具无关，未声称全套测试通过。独立入口 `tests/MaintenanceSqlChecks`直接运行同一批xUnit断言，避开本机VSTest监视父进程时的Win32拒绝访问；构建后运行 `dotnet tests/MaintenanceSqlChecks/bin/Debug/net8.0/MaintenanceSqlChecks.dll`，结果34通过、0失败。
