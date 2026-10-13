# 豆包 MCP 演示开发清单（老胡小工单 × 豆包方舟）

> 版本：v1（2026-09-16）｜ 定位：**演示级**，跑通「豆包 → 本机工单数据」的查询闭环，含一次金蝶授权 + 敏感数据拦截的演示。

## 1. 结论先行（关键边界，决定整套设计）

豆包方舟（火山方舟）的 Remote MCP 有两个硬边界，任何方案都不能违反：

1. **方舟不告诉你「提问者是谁」**。它调你的 MCP 时，只带「固定 token + 工具参数」，没有用户标识、没有会话 ID、没有 OAuth 回调。
2. **手机豆包 App 不支持自定义 MCP**，只能从方舟控制台/体验中心/Responses API 接入（电脑端为主）。

因此「按会话存金蝶授权凭证、自动绑定」在方舟上**无法实现**。本清单采用**配对码方案**（用户 H5 授权后，把一次性凭证手动捎回豆包对话），这是方舟上唯一能演示「授权→按人查权限→拦敏感查询」闭环的可行方式。

## 2. 链路

```
电脑浏览器 → 火山方舟（填 cpolar 的 MCP 地址 + 静态 Bearer token）
    └─(固定 token)→ cpolar HTTPS 隧道 → 本机 MCP Server(.NET8, Streamable HTTP, /mcp)
            ├─ 查工单/报工/工资 ──→ 复用 backend 现有 Services（WorkOrder/Report/Salary）
            └─ H5 授权页 ──→ 金蝶账号验证(IKingdeeAuthService，当前占位) → 生成 auth_token
```

## 3. 第一步：本机 MCP Server

- 复用现有 `backend` 项目，加 `ModelContextProtocol.AspNetCore` 包，在 `Program.cs` 里 `MapMcp("/mcp")`。
- **入口鉴权**：静态 `Authorization: Bearer <token>`（`McpTokenMiddleware` 校验，挡公网乱调）。
- 工具（M1 只做第一个）：

| 工具 | 作用 | 关键入参 | 涉及表 |
|---|---|---|---|
| `query_work_order` | 查工单 | 关键词/状态 | `prod_work_order` |
| `query_report` | 查报工 | 工单/人/时间 | `prod_report` |
| `query_salary` 🔒 | 查工资（敏感） | 人/周期 | `salary_statement`、`salary_statement_item` |

> 数据口径以 `docs/02-数据字典.md` 为准：报工金额看 `prod_report.wage_amount`（`review_status=1`）；工资金额公式见字典第 330 行。

## 4. 第二步：H5 授权 + 配对码

1. 模型调 `start_auth` → 生成一次性 code（5 分钟过期），返回 H5 链接 `https://xxx.cpolar.cn/auth?code=ABC123`。
2. 用户点开 H5 → 输入金蝶账号密码 → 服务端调 `IKingdeeAuthService.VerifyAsync` 验证（当前为假验证占位）。
3. 服务器把 code 换成 `auth_token`（绑定金蝶用户 ID + 角色，2 小时过期），H5 页大字显示，提示「复制回豆包」。
4. 用户回豆包贴 token → 后续查询都带 `auth_token`，服务端凭它认出是谁、什么权限。

**存储**：新增 `sys_mcp_auth` 表（code、auth_token、金蝶用户 ID、角色、过期时间、一次性标记）。表结构改动先读 `docs/02-数据字典.md` 并同步。

## 5. 权限拦截（双层，都在服务端）

1. **金蝶账号原生权限**：拿 `auth_token` 里的金蝶用户调金蝶接口查角色。
2. **MCP 服务端额外拦截**：`query_salary` 等 `salary_` 敏感查询，校验「金蝶用户是否财务角色」，非财务直接返回「无权限」。

## 6. cpolar + 方舟配置

- `cpolar http <端口>` → MCP 地址 = `https://xxx.cpolar.cn/mcp`。
- 方舟侧填 `server_url` + `headers.Authorization` 静态 token，调用加 `ark-beta-mcp: true`（测试期要求）。
- **演示完关隧道、轮换 token**；cpolar 免费地址重启会变，重配即可。

## 7. 里程碑（先打通，再补肉）

| 里程碑 | 内容 | 验收 |
|---|---|---|
| M1 | `query_work_order` + 假登录页，跑通「方舟→cpolar→本机→返回数据」 | 方舟里一句话查到工单 |
| M2 | 铺满 `query_report` / `query_salary` | 三类数据都能查 |
| M3 | 接真金蝶登录 + `auth_token` + 财务拦截 | 未授权/非财务查工资被拦 |

> **关键解耦：M1、M2 用假登录页打通链路，M3 再接真金蝶 OAuth**，别一上来卡在金蝶对接。

## 8. 待确认（不阻塞 M1，M3 前定）

1. ~~金蝶云星空授权方式~~ **已查清（2026-09-16）**：金蝶云星空**没有**标准 OAuth2 授权码回调，只有「第三方系统登录授权(SSO)」——生成免密登录 URL 把你**送进金蝶**，方向反了、无回调。现实可行的是 **金蝶 WebAPI 凭证验证**（H5 输入账号密码 → 服务端代验）。M3 已按此做成 `IKingdeeAuthService` 接口 + 假验证占位，真金蝶对接时替换实现即可。
2. 方舟入口形态：体验中心网页版，还是必须写 Responses API 代码。
3. 演示终端：手机豆包 App 无法自定义 MCP，演示大概率在电脑浏览器打开方舟体验中心。

## 9. M3 现状（2026-09-16 已实现）

配对码授权 + 财务拦截已落地，代码可编译。落地清单：

| 层 | 文件 | 说明 |
|---|---|---|
| 表 | `sys_mcp_auth` | 存 code / auth_token / 金蝶用户 / is_finance / 过期，已同步 `docs/02` |
| 实体/上下文 | `Models/SysModels.cs`、`Data/AppDbContext.cs`、`Data/DbCompat.cs` | 幂等建表 |
| 配对码服务 | `Services/McpAuthService.cs` | code 生成 / code→token 交换 / token 校验 |
| 金蝶验证 | `Services/KingdeeAuthService.cs` | `IKingdeeAuthService` 接口 + `FakeKingdeeAuthService` 占位 |
| 工具 | `Mcp/AuthMcpTools.cs` | `start_auth` 返回 H5 链接 |
| 工具 | `Mcp/WorkOrderMcpTools.cs`、`ReportMcpTools.cs` | 加 `authToken` 必填 |
| 工具 | `Mcp/SalaryMcpTools.cs` | `authToken` + 财务角色拦截 |
| H5 | `Controllers/McpAuthController.cs` | `GET/POST /mcp-auth` 表单 + 显示授权码 |
| 配置 | `appsettings.json` | `Mcp:AuthBaseUrl`、`CodeExpireMinutes`、`TokenExpireHours`、`FinanceKingdeeAccounts` |

**上线前必须配**：`Mcp:AuthBaseUrl` = cpolar 公网地址（如 `https://xxx.cpolar.cn`）。

**金蝶真对接待办**：替换 `FakeKingdeeAuthService`，调金蝶 WebAPI 登录接口验账号密码、查角色回填 `IsFinance`。演示期用 `Mcp:FinanceKingdeeAccounts` 配置财务账号列表即可演示拦截。
