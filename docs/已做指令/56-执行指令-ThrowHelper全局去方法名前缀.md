# 执行指令 · 统一去除业务提示的方法名前缀

> 日期：2026-09-21｜状态：本地实施与验收完成（2026-09-21），未部署。目标：删除“辣度”等业务失败提示不再自动附带 DeleteAsync 等内部方法名。

## 范围与约束

- 仅修改 ThrowHelper.Biz 的 Message 拼接、相关注释、规约和专项测试；保留 where 参数及既有调用点，保留 BizUser。
- 不改 General、Sql、Api、BusinessException.Code、HTTP 状态、异常中间件和前端 http.js；不改表、不部署、不操作线上业务数据。
- 排查 what 本身包含技术信息的调用点，仅列后续待办，不在本单顺手改写。
- 不保证所有提示变为纯中文：保留业务编号、Excel、PDA 等合理名称；本单只去除自动追加的方法名前缀。
- 日志保留 TraceId 与异常堆栈；源码行号依赖发布调试符号，不作为所有环境的硬性要求。where 不再作为显式日志字段保留，调用位置依靠实际抛出点的堆栈。
- 调用数量随代码变化，不写死；扫描排除 bin/obj。

## 执行纪律

逐项实施，自验通过后将该项打勾，再开始下一项。失败项保持未勾选并记录原因。全部本地验收通过后移至已做指令；线上验收另行记录，不能冒充已完成。

## 清单

- [x] 1. 修改 Biz 并做精确 Message 专项测试：字段“辣度”已有业务数据，不能删除；不含 DeleteAsync 前缀，默认业务码不变，BizUser 仍兼容。
- [x] 2. 扫描调用点和旧断言；抽查工单、产品、工价、报工、复核、基础数据；记录文案自身技术信息的后续待办，不修改调用点。
- [x] 3. 同步 ThrowHelper 注释、docs/06 和 error-handling 规约；补中间件测试，验证业务错误 HTTP 200、业务码、X-Trace-Id、技术错误安全提示、正常请求透传；通过真实日志文件验证 TraceId 和抛出方法堆栈保留。
- [x] 4. 后端构建与 backend.Tests 全量测试通过，记录结果；核对改动范围，归档本指令。

## 验收方式

专项测试直接调用真实 ThrowHelper 和 ExceptionMiddleware，不依赖线上账户，不通过删除真实数据验证。日志测试使用与生产一致的 Serilog CompactJsonFormatter 和 Warning 文件出口，在临时测试文件检查 TraceId 与异常堆栈。全量测试中的数据库测试使用现有独立测试库；环境不可用时如实保留未完成。

## 执行记录

1. Biz 已改为直接返回 what，保留签名与 BizUser；专项测试 1/1 通过。

## 部署后验收（本单不部署）

- [ ] 更新后在管理员界面触发有引用不能删等业务错误，确认没有方法名前缀。
- [ ] 抽查线上日志可通过响应头 TraceId 找到异常堆栈；发布包含调试符号时检查源码行号。


### 第 2 项扫描记录

扫描 backend 的 C# 源文件（排除 bin/obj），已抽查工单、产品、工价、报工、复核、部门、单位、自定义字段；自动前缀统一由 Biz 移除。backend.Tests 未发现依赖旧方法名前缀的断言，不需要批量替换。

后续文案待办（本单仅记录）：
- SupportDocumentService：配置键、完整目录路径、枚举文件/目录失败的 ex.Message。
- AiBusinessTime：时区异常的 ex.Message。
- DeepSeekClient、KnowledgeFileParser、McpTokenMiddleware：服务器配置键或配置文件名。
- AiReportEvidenceService、SupportDocsMcpTools：workOrderId、pageSize、query 等接口参数名；应按最终使用者区分用户提示与工具参数校验，不能机械删除英文。
- 以上技术异常若另行改写，须保留原异常和日志上下文；不能只抹掉 ex.Message 后丢失定位信息。

### 第 3 项验证记录

已同步规约与注释；ThrowHelperTests + ExceptionPresentationTests 共 6/6 通过，含真实 Serilog 文件日志的 TraceId 和抛出方法堆栈检查。技术异常与正常请求透传测试通过。
另补文案待办：ReportService 的 batch_no 参数提示。


### 第 4 项回归记录

- 后端构建通过：0 警告、0 错误；使用独立输出目录，避免覆盖运行中的服务程序。
- 全量测试 51/51 通过（0 失败、0 跳过），包含 6 项本次专项测试。
- 首次沙箱运行 SQL Server 加密连接失败；获准在沙箱外使用既有 WorkshopMes_Test 测试库后正常。
- 发现 55 号变更遗留的旧断言：AI 模型失败警告已为“AI 服务暂时不可用，请稍后重试”，测试仍期待“模型调用失败”。仅同步该条断言，真实证据与数量断言保留，重跑全量通过。
- 生产代码仅修改 ThrowHelper.Biz 的 Message 和文件注释；General/Sql/Api、调用点、中间件、业务码、前端和数据库结构未改。本次未部署，未修改线上“辣度”数据。
- 本目录未提供 Git 元数据，未执行提交；本单文件为 ThrowHelper.cs、两份专项测试、AiToolIntegrationTests.cs 单条旧断言、docs/06、error-handling.mdc 与本指令。
