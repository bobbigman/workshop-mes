# Cursor 执行指令 · 服务器错误日志与追踪（短开工单）

> 日期：2026-09-20｜状态：**已实现**（2026-09-20）。  
> **唯一方案源：** [`docs/2D-需求分析-服务器错误日志与追踪.md`](../2D-需求分析-服务器错误日志与追踪.md)。  
> 验收证据目录（可删）：`_verify_runtime_logs/`、`_verify_errlog*`（若仍在仓库旁，仅本地验证残留）。

## 覆盖扫描（开工时）

| 类别 | 结论 |
|---|---|
| SQL / ADO | 均经 EF Core（含 `ExecuteSqlRaw`）；**无**直接 `SqlConnection`/`SqlCommand` |
| HttpClient | 仅 `deepseek`、`wecom` 两命名客户端 |
| 业务校验 vs 技术故障 | `BusinessException`→Warning；其余→Error；启动失败→Fatal |

## 5. 验收清单

### 阶段 A

- [x] **5.A 日志** `dotnet build` 通过；Serilog Warning+ → `mes-error-*.log` JSONL；目录探测写删；写入器不递归掩盖业务异常。  
- [x] **5.B 追踪** 中间件验证：技术异常 `msg` 含追踪编号、无 SQL/密钥；响应头 `X-Trace-Id` 同号；业务异常保留「账号或密码错误」类提示。  
- [x] **5.B2 并发不串线** 20 并行请求 TraceId 互异（`_verify_middleware`）。  
- [x] **5.C 脱敏** URL/JSON/文本中 password、corpsecret、Bearer、token 均不可见明文（`_verify_redact` + SQL/HTTP 日志抽检）。  
- [x] **5.D 清理不误删** 固定时钟 2026-09-20：删 09-06、留 09-07/今天/noise/他命名文件（`ErrorLogCleanupService.CleanupOnce`）。

### 阶段 B

- [x] **5.E SQL 参数** Interceptor 已注册；本机 SQL Server：语法错误日志含真实 SQL、`@p0=123 (Int32)`、SqlNumber=208、TraceId/CommandId（`_verify_sql_http`）。同步/异步失败入口均挂钩。  
- [x] **5.F HTTP 请求/响应** 本地 `HttpListener`：non_2xx 与 200 业务失败均有 `http_error`；password 字段脱敏为 `***`；AI omit 路径已接入 DeepSeek。

### 阶段 C

- [x] **5.G 前端** 仅改 `frontend/src/api/http.js`；无日志管理页。  
- [x] **5.H 收尾** docs/03、05、06、33、2D、error-handling/api-conventions、deploy 模板与运维清单已同步。

## 主要改动文件

- 新增：`ErrorLoggingOptions`、`LogRedactor`、`TraceIdMiddleware`、`ErrorLogCleanupService`、`SqlCommandErrorInterceptor`、`OutboundHttpErrorLogger`、`OutboundHttpLoggingHandler`、`ErrorLoggingBootstrap`
- 修改：`Program.cs`、`ExceptionMiddleware`、`ThrowHelper`、`DeepSeekClient`、`WechatAlertService`、`WorkshopMes.csproj`、`appsettings.json`、`http.js`、实例模板

## 配置要点

```json
"ErrorLogging": {
  "Directory": "D:\\MesTrials\\client-a\\logs",
  "RetentionDays": 14,
  "CleanupIntervalMinutes": 60,
  "FileSizeLimitBytes": 52428800,
  "MaxBodyBytes": 65536,
  "MaxParameterBytes": 4096
}
```

生产用绝对路径 + 实例隔离；开发可用相对 `logs`。
