# 开发指令：管理员只读诊断 SQL

> 状态：**已完成（代码 + 门禁单测）**  
> 完成日期：2026-09-23  
> 目标：查远程伙伴机问题时，可用管理员 JWT 直接跑只读 SQL，避免「丢脚本 → 人跑 → 贴结果」半自动。

---

## 1. 一句话

新增 `POST /api/diag/sql`：仅 role=1，只允许 SELECT，结果有行数/超时上限；**不**挂 MCP、不给豆包。

---

## 2. 取舍（已定）

| 项 | 决定 |
|---|---|
| 入口 | `POST /api/diag/sql`，JWT 鉴权 |
| 谁可调 | 仅管理员 role=1（控制器再拦一层；role=2/3 本就不在 RoleGuard 白名单） |
| 语句 | 仅 `SELECT` / `WITH…SELECT`；禁写库与多语句 |
| 上限 | 默认最多 200 行、命令超时 15s、SQL 最长 8000 字符 |
| MCP | **不做**；与 PRD「客服不可执行 SQL」一致 |
| 表结构 | 不改 |

---

## 3. 契约

请求：

```json
{ "sql": "SELECT TOP 20 Id, OrderNo FROM prod_work_order ORDER BY Id DESC" }
```

成功 `data`：

```json
{
  "columns": ["Id", "OrderNo"],
  "rows": [[1, "WO001"]],
  "rowCount": 1,
  "truncated": false,
  "elapsedMs": 12
}
```

失败：业务提示（非 SELECT / 超时 / 空 SQL 等）；技术失败走现有异常中间件 + 日志（SQL 原文进服务器日志）。

### 调用示例（本机或已部署站点）

```powershell
# 1) 登录拿 token（账号按目标环境）
$login = curl.exe -s -X POST "http://mes.webok.net:11302/api/Auth/login" `
  -H "Content-Type: application/json" `
  -d "{\"account\":\"admin\",\"password\":\"你的密码\"}"
# 从返回 JSON 取 data.token

# 2) 只读查询
curl.exe -s -X POST "http://mes.webok.net:11302/api/diag/sql" `
  -H "Authorization: Bearer <token>" `
  -H "Content-Type: application/json" `
  -d "{\"sql\":\"SELECT TOP 20 TABLE_NAME FROM INFORMATION_SCHEMA.TABLES ORDER BY 1\"}"
```

---

## 4. 实际改动

| 文件 | 说明 |
|---|---|
| `backend/Controllers/DiagController.cs` | **新增** |
| `backend/Services/DiagSqlService.cs` | **新增** 校验 + 执行 + 截断 |
| `backend/Program.cs` | 注册 `IDiagSqlService` |
| `backend.Tests/DiagSqlServiceTests.cs` | **新增** 门禁 9 例 |

---

## 5. 验收清单

- [x] A1 `dotnet build` 通过（旁路输出 `_build_diag_out`）
- [ ] A2 管理员登录后 POST 合法 SELECT → `code=0`，有 columns/rows（**需目标机部署本版后测**）
- [x] A3 `UPDATE` / `DELETE` / 多语句 → 业务拒绝（`DiagSqlServiceTests` 9/9）
- [ ] A4 非管理员（role=2）→ 403 或业务拒绝（**需目标机部署后测**）
- [x] A5 未新增任何 MCP Tool
- [x] A6 未改产品唯一 / 报工部门 / 删除倒序

## 6. 远程生效

本机改完后，须把更新包/整包部署到 `mes.webok.net:11302` 对应进程后，才能对该站远程查库。A2/A4 勾选留到部署后。
