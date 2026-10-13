# MCP 演示上线待办清单

> 前置：M3 代码已完成、编译通过。这份清单列的是**你接下来要亲手做的事**。
> 代码部分不用再改（除了最后一节「金蝶真对接」可选）。
> 📖 要照着点着做，见 **[docs/37-上线操作手册-接豆包方舟.md](./37-上线操作手册-接豆包方舟.md)**（把本清单的 §二/§四/§五/§六 拆成了分步步骤 + 常见坑）。

---

## 〇、配置速查（所有要填的值都在这）

位置：`backend/appsettings.json` 的 `Mcp` 段

| 配置项 | 干什么 | 现在状态 | 你要做的 |
|---|---|---|---|
| `Token` | 方舟调 MCP 的固定 token | `laohu-demo-2026` | 演示完轮换 |
| `FactoryId` | 工厂 ID | `1` | 一般不用动 |
| `AuthBaseUrl` | cpolar 公网地址 | `https://209c3f5f.r19.vip.cpolar.cn` | 隧道变了再改；须指后端 8080 |
| `AuthLocalBaseUrl` | 没配公网时的本地回退 | `http://localhost:8080` | 不用动 |
| `CodeExpireMinutes` | 配对码有效期（分钟） | `5` | 不用动 |
| `TokenExpireHours` | 授权码有效期（小时） | `2` | 不用动 |
| `FinanceKingdeeAccounts` | 财务账号名单（逗号分隔） | 空 | 填一个，如 `boss` |

> 说明：`AuthBaseUrl` 为空时，授权链接会自动回退到本地地址，方便先自测。

---

## 一、本地先跑通（不依赖 cpolar、不依赖金蝶）

- [x] 启动后端：`cd backend` 后 `dotnet run`
- [x] 确认服务起在 `http://localhost:8080`，不报错
- [x] 确认数据库自动多出 `sys_mcp_auth` 表
- [x] 浏览器打开 `http://localhost:8080/mcp-auth?code=test123`，能看到金蝶授权表单页

**验收**：不报错、表建出来、页面能打开。

> ✅ 已实测通过（2026-09-16）。后端起在 8080，`start_auth` 能生成配对码写入 `sys_mcp_auth`，H5 表单页正常渲染。

---

## 二、开隧道 + 配公网地址（接方舟前做）

- [x] 安装/启动 cpolar，把本地 `8080` 端口映射出去
- [x] 拿到 `https://xxxx.cpolar.cn` 这样的公网地址
- [x] 填进 `backend/appsettings.json` 的 `Mcp:AuthBaseUrl`
- [x] 重启后端

**验收**：浏览器打开 `https://xxxx.cpolar.cn/mcp-auth?code=test` 能打开。

> ✅ 已实测通过（2026-09-17）。正确隧道：`https://209c3f5f.r19.vip.cpolar.cn` → **后端 8080**（Kestrel）。  
> 公网 `POST /mcp` + Bearer → 400（入口在）；错 Token → 401；授权页返回「金蝶账号授权」。  
> ⚠️ 旧域名 `2e2c5459...` 当时打到 **Vite 5173**，`/mcp` 会 404——方舟不要用旧域名。免费版地址会变，变了重配本段。

---

## 三、配财务账号（演示工资拦截用）

- [x] 在 `Mcp:FinanceKingdeeAccounts` 填一个财务账号，如 `boss`
- [x] 重启后端（改配置后重启最稳）

**验收**：名单里的账号查工资成功，名单外的账号查工资被拦。

> ✅ 已实测通过（2026-09-16）。已填 `boss` 作演示占位；`boss` 查工资成功、`worker1` 查工资被拦并提示「无权限：仅财务人员可查询工资数据」。**上线前把 `boss` 换成真实财务账号。**

> 当前是假验证：任意非空账号密码都通过；「是否财务」只看这个名单。

---

## 四、接豆包方舟

- [x] 火山方舟控制台填 MCP 地址：`https://209c3f5f.r19.vip.cpolar.cn/mcp`（当前隧道；变了以 `Mcp:AuthBaseUrl`+/mcp 为准）
- [x] 配置请求头 `Authorization` = `Bearer laohu-demo-2026`（与 `Mcp:Token` 一致；演示完轮换）
- [x] 测试期加 `ark-beta-mcp: true`
- [x] 试一句话「查工单」

**验收**：方舟里能查到工单数据。

> ✅ 已实测通过（2026-09-23）：方舟控制台已配通，测试连接可列出 5 个 MCP 工具，对话可查工单。

---

## 五、走一遍完整演示闭环

- [x] 方舟里说「查工资」→ 触发 `start_auth`，拿到 H5 授权链接 　*(2026-09-23 方舟内已验)*
- [x] 打开 H5，输金蝶账号密码 → 页面显示授权码 　*(本地已验)*
- [x] 把授权码贴回对话 → 查工单 / 查报工 　*(本地已验：用授权码调 `query_work_order`/`query_report` 成功返回数据)*
- [x] 用财务账号（如 `boss`）授权 → 查工资成功 　*(本地已验：`boss` 通过)*
- [x] 用非财务账号授权 → 查工资被拦，提示「仅财务人员可查询」 　*(本地已验：`worker1` 被拦)*

**验收**：授权 → 查询 → 工资拦截，整条链路走通。

> 🔵 本地闭环已实测走通（2026-09-16，不经方舟、直接用 MCP JSON-RPC）：`start_auth` → H5 拿授权码 → `query_work_order`/`query_report` 查数 → `boss` 查工资成功 → `worker1` 查工资被拦。剩「方舟里」这一句，等 §二/§四 开好 cpolar + 方舟配置后再验。

---

## 六、演示完收尾

- [ ] 关 cpolar 隧道
- [ ] 轮换 `Mcp:Token`（改个新值，防公网残留调用）

---

## 七、后续（可选）：金蝶真对接

等金蝶 K3 WebAPI 的 skill 维护好后，再把假验证换成真的：

- [ ] 用 skill 拿到金蝶登录结构（`stru_k3login` 等）
- [ ] 新写 `KingdeeAuthService` 实现 `IKingdeeAuthService`，调金蝶登录接口验账号密码、查角色回填 `IsFinance`
- [ ] `Program.cs` 里把 `FakeKingdeeAuthService` 换成 `KingdeeAuthService`（一行）
- [ ] 编译 + 按第一节本地自测

> 只动这一个实现类，其余（配对码、H5、财务拦截、查询工具）一行不用改。
