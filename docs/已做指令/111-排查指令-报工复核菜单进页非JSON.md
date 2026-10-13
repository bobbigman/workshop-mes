# 111-排查指令 · 报工复核进页必现「非 JSON / 静态页兜底」

> 日期：2026-10-01｜状态：**已修复并验收**。根因：浏览器缓存了旧 `/api/Review/order-options` 首页 HTML，持续复用同一旧 TraceId，未再请求当前后端。
> 前序：`docs/已做指令/110-排查修复指令-报工复核业务失败.md`（原「业务失败」）。
> 执行人：GPT。Windows + PowerShell；前端 `https://localhost:5173`，后端 `http://127.0.0.1:8080`。

## 一、用户最新现象（**每次必现**，按此复现）

> **用户已明确纠正：不是偶发。** 每次进入复核页都会弹错。

1. 已登录管理员。
2. **任意一种进入方式都会中**：
   - 地址栏直接打开 / 刷新：`https://localhost:5173/#/review`
   - 或点左侧菜单「生产管理 → 报工复核」
3. **每次**弹红字（示例）：
   `接口返回非 JSON（可能路由未注册或静态页兜底）（追踪号 00-2ac089f194ef893b93ff67ae4692f110-2d0ea011522ceacf-00）`
4. 该文案来自 **`frontend/src/api/http.js` 近期加固**（原先会显示笼统「业务失败」）。  
   **有 TraceId = 请求打到了后端中间件链路**（`TraceIdMiddleware`），不是纯前端瞎提示。

**禁止**再按「偶发 / 缓存 / 再试一次就好」口径排查；按**稳定复现缺陷**处理。

## 二、Cursor 已核实（勿推翻，除非你拿到相反抓包）

| 项 | 结果 |
|---|---|
| 8080 进程 | `WorkshopMes.exe`，路径 `backend\bin\Debug\net8.0\`，启动约 10:28，DLL 同刻 |
| 无 token 直连 | `GET /api/Review/pending` → **401**；`GET /api/Review/order-options` → **401**（**两条路由都在**） |
| 经 5173 代理无 token | 同上，两接口均 **401**（代理正常） |
| 带 token（curl / 浏览器 fetch） | 两接口均 **200 + application/json + code:0** |
| 源码 | `ReviewController` 有 `[HttpGet("order-options")]`；`ReviewService.OrderOptionsAsync` 已实现 |
| `vite` 代理 | `/api` → `127.0.0.1:8080`（非 B 档） |
| `bin\Debug\net8.0\wwwroot` | **不存在**；`backend\wwwroot` **存在**。历史日志多次 `WebRootPath was not found` |
| 当日 error 日志 | `bin\Debug\net8.0\logs` 最新文件停在 **2026-09-27**，**查不到**用户 TraceId `2ac089f1…`（HTML 兜底通常不写 error 日志） |
| Cursor 自动化 | 打开 `#/review`、带 token fetch：**本机自动化未弹错**（与用户浏览器**不一致**——优先采信用户侧 Network） |

历史抓包（110）：曾出现 **pending=JSON 正常、order-options=200 text/html=前端 index.html**（`MapFallbackToFile`）。与「同控制器一条命中一条不命中」一致。  
**矛盾**：工具侧带 token 已拿到 JSON；**用户浏览器每次进 `#/review` 仍报非 JSON** → 必须在**用户正在用的那个浏览器会话**里抓 XHR，不能只用 curl/自动化代替。

## 三、核心矛盾（必须回答）

> 用户每次进 `https://localhost:5173/#/review` 时，**到底是哪一个 XHR** 触发了提示：URL、HTTP 状态、`Content-Type`、响应头 `X-Trace-Id`、body 前 200 字符？

在未拿到 Network 行之前，禁止改业务逻辑「猜修」。

## 四、推测（按概率，逐条证伪）

- **A（高）进页时某个请求回了 HTML**  
  候选：`GET /api/Review/order-options`（历史实锤过）；也可能是同屏其它 `/api/*`。  
  **当** DevTools 里该行 `Content-Type: text/html` 且 body 以 `<!` 开头 → 确认仍是兜底/未命中路由。  
  **当** 只有这一条失败、pending 正常 → 回到「为何单 action 未映射」（编译产物/多实例/路由冲突/用户会话打到的并非工具测到的进程）。

- **B（中）axios 把响应当成 string，被 http.js 报「非 JSON」**  
  Cursor 已把判断收成：**仅 `trimStart().startsWith('<')` 才报「非 JSON」**；其它 string 先 `JSON.parse`。  
  **当** 用户仍报「非 JSON」→ body 真是 HTML，不是误判。  
  **当** 改成「无法解析的文本」→ Content-Type/代理把正文弄成非标准文本。

- **C（中）Debug 输出目录无 wwwroot，Fallback 行为怪异**  
  运行目录 `bin\Debug\net8.0\wwwroot` 缺失。  
  **当** 未命中 action 时：可能回 `backend\wwwroot\index.html`，或 404，或其它；需对照实际响应。  
  **不做**：为「消错」删掉 `MapFallbackToFile`（会伤 SPA 托管）。

- **D（中）用户浏览器打到的后端 ≠ Cursor curl 打到的后端**  
  查 Network Remote Address / 代理目标；是否另有 8081、旧发布目录、另一 `WorkshopMes`。  
  **当** 用户会话远程地址或进程与工具侧不一致 → 先对齐再谈路由表。

- **E（低）许可证 / RoleGuard**  
  档位不足应返回 **JSON** `{code,msg}`，不是 HTML；RoleGuard 403 也是 JSON。除非中间件异常短路——需堆栈，概率低。

## 五、执行步骤（原子 + 条件）

### 步骤 1：在用户必现浏览器里抓 Network（最高优先）

- **当**：需要锁定失败请求  
- **做**：用**用户复现用的同一浏览器** → DevTools → Network → Preserve log → 清空 → 打开或刷新 `https://localhost:5173/#/review`（或点菜单进复核）→ 筛 `Review` 与 `api`  
- **登记**每条：Name、Status、Type、`content-type`、`x-trace-id`、Response 前 200 字  
- **当**：某条 body 以 `<!DOCTYPE`/`<html` 开头 → 记 URL，进步骤 2  
- **当**：全部为 JSON 且 code=0，但页面仍弹错 → 进步骤 4（前端误报/旧包未刷新）  
- **当**：工具自动化不弹错、用户弹错 → **只信用户浏览器抓包**，不要用「工具侧正常」结案

### 步骤 2：对照直连后端（同一 URL + 同一 Bearer）

- **做**：对步骤 1 失败的 path，分别请求：  
  - `http://127.0.0.1:8080<该path>`（带**用户浏览器里同一个** Bearer）  
  - `https://localhost:5173<该path>`（经代理，同一 Bearer）  
- **当**：8080=JSON 且 5173=HTML → **Vite 代理/开发服务器问题**（查 `vite.config.js`、代理错误日志、是否打到别的 port）  
- **当**：两者皆 HTML → **后端未映射该路由**（步骤 3）  
- **当**：两者皆 JSON，但页面进 `#/review` 仍弹错 → 查是否另有失败请求、或前端旧拦截器；**不要**当成偶发放过

### 步骤 3：确认运行中程序集是否含 OrderOptions

- **做**：  
  1. `Get-NetTCPConnection -LocalPort 8080 -State Listen` → PID  
  2. `Get-CimInstance Win32_Process -Filter "ProcessId=<pid>"` → ExecutablePath（**勿用 PowerShell 变量名 `$pid`，只读冲突**）  
  3. 对该目录 `WorkshopMes.dll` 用 ildasm / `strings` / 反射，确认含 `OrderOptions` 或路由 `order-options`  
  4. 对比源码 `ReviewController.cs` 修改时间 vs dll `LastWriteTime` vs 进程 `StartTime`  
- **当**：dll 无 OrderOptions 或 StartTime < 源码修改时间 → **重新 `dotnet build` 后必须用新 dll 启动**，再验收  
- **当**：dll 有、仍 HTML → 查路由冲突（同路径其它 action、`[HttpGet("{id}")]` 抢匹配等）；开 Swagger 或端点列表核对

### 步骤 4：前端 http.js / 缓存

- **当**：Network 全是 JSON 仍弹「非 JSON」  
- **做**：确认浏览器加载的是当前 `http.js`（强刷；看 Sources）；读拦截器是否仍对任意 string 误报  
- **当**：HMR 未更新 → 重启 `npm run dev` 后再进 `#/review`

### 步骤 5：wwwroot 缺失（仅作旁证，莫当主修）

- **当**：确认是 Fallback HTML  
- **做**：记录 `Test-Path backend\bin\Debug\net8.0\wwwroot` 与 `backend\wwwroot`；说明 Fallback 从哪读到 index.html  
- **不要**为消错去改 SPA Fallback，除非步骤 2/3 证明必须

## 六、验收

- [x] 直接打开 / 刷新 `https://localhost:5173/#/review` **连续 5 次**不弹「非 JSON / 业务失败」  
- [x] 菜单点「报工复核」同样连续 5 次不弹  
- [x] Network：`/api/Review/pending`、`/api/Review/order-options` 均为 `application/json` 且 `code=0`  
- [x] 根因一句话写入本文件状态栏；若与 A–E 都不同，补「新发现」节  
- [x] 修完把本文件移到 `docs/已做指令/`；相关结论可回写 110

## 七、边界

- 不覆盖服务器 `appsettings*.json` / `start.bat`（docs/59）  
- 异常不吞；不为「不报错」静默 catch  
- 不改表、不改无关功能  
- **先抓包再改代码**；禁止未拿到用户侧 Network 就结案或大重构路由  
- **禁止**用「偶发 / 再试一次」口径结案

## 八、给 GPT 的最短路径

1. **先做步骤 1**（用户浏览器 + `#/review` + Network），没有失败行就不要改后端。  
2. 有 HTML 行 → 步骤 2 分清代理 vs 后端。  
3. 后端 HTML → 步骤 3 编译产物 / 对齐用户打到的进程。  
4. 全 JSON 仍弹 → 步骤 4。  
5. 把根因 + 关键抓包贴回本指令。

## 九、GPT 本轮排查记录（2026-10-01 11:26，未完成）

- 已核对指令、开发规约、前端复核页/API 封装、HTTP 拦截器、Vite 代理与后端控制器。指令“先用户会话抓包再修复”的顺序合理。
- 页面 mounted 同时请求 `/api/Review/order-options` 与 `/api/Review/pending`；源码路由均存在，HTML 判定确实为字符串 trimStart 后以 `<` 开头。
- netstat 确认 5173 监听 PID 30584、8080 监听 PID 25864（WorkshopMes，启动 10:28:39）。Get-NetTCPConnection 本轮未给出结果，不能据此认定服务未运行。
- Debug WorkshopMes.dll 修改时间 10:28:37；ReviewController.cs 修改时间 08:16:42。时间证据未显示旧编译产物，但尚未验证运行程序集内容。
- 11:26 无 Bearer 直连两接口均 HTTP 401，Server=Kestrel，附 X-Trace-Id；与前序工具侧结果一致，不能替代用户失败会话证据。
- 当前浏览器连接仅暴露 Codex 内置浏览器（无标签页），未暴露用户 Chrome/Edge 会话；原生应用控制在当前工具接口中禁用。因此尚未获得用户侧失败请求的 URL、状态、Content-Type、TraceId 与正文。
- 下一步：用户提供该出错会话 Network 的失败请求记录或脱敏 HAR，再对照同一路径直连与代理响应。尚未修改业务代码、重启服务或完成验收，本指令保留在“未做指令”。

### 补充分支：用户会话不可直接连接时的临时代理抓包

用户已授权自行抓包并继续排查电脑端 `#/review`。当工具无法连接用户 Chrome 会话，则在开发 Vite `/api` 代理记录所有 Review 响应及其它 API 的 HTML 响应，保存到 `artifacts/review-network-111.jsonl`。只记方法、路径（去掉查询参数）、状态、Content-Type、TraceId、缓存响应头、上游地址及正文前 200 字；禁止记录 Authorization、Cookie、登录接口正文。响应照常透传，不改业务逻辑。Vite 配置变更由开发服务器自动重载，不重启后端。记录就绪后用户只需刷新一次出错的电脑端页面；抓到失败行再按步骤 2–4 定位，修复验收后移除临时记录。

## 十、新发现与修复指令（2026-10-01 11:35）

用户原电脑浏览器的客户端自动抓包（记录在 artifacts/review-network-111.jsonl）：

- `03:35:15.297Z`，`GET /api/Review/order-options`，HTTP 200，`text/html`，TraceId=`00-2ac089f194ef893b93ff67ae4692f110-2d0ea011522ceacf-00`，正文开头 `<!DOCTYPE html>`，首页标题“小蜜蜂报工”，serviceWorker 为空。
- 同次 `/api/Review/pending` 为 200 JSON code=0，TraceId=`00-64688f6bf92af48b897e378498d35b58-aebf03c1abb22fef-00`，与代理同次记录匹配；失败 order-options 没有对应代理请求记录。
- 工具会话同一路径为 JSON code=0，客户端与代理 TraceId 一一对应。
- 旁证：后端不存在的 `/api/__diagnostic111_missing` 当前返回 200 首页 HTML，含 ETag，没有 Cache-Control；现有正常 API JSON 也没有 Cache-Control。

结论：用户浏览器持续复用先前路由未注册时缓存的 order-options 首页 HTML（旧 TraceId 原样返回，未到代理），是稳定的浏览器 HTTP 缓存污染，不是偶发，也不是当前 action 缺失。指令“A–E”缺少“已缓存 API HTML、不再发起网络请求”分支。

修复范围和验收：

1. 前端统一 API GET 添加 `Cache-Control: no-cache` 和 `Pragma: no-cache`，强制重新校验已缓存响应；不吞错误、不自动把 HTML 当成功。
2. 后端所有 `/api` 响应添加 `Cache-Control: no-store`，涵盖鉴权失败和误落到静态兜底的响应，防止再存入浏览器缓存；保留 SPA Fallback，不改业务或表结构。
3. 当用户侧重校验后拿到 JSON → 连续刷新/菜单各 5 次验证并检查 TraceId 与代理对应；当仍拿到 HTML → 保留诊断，按实际新记录继续排查，不能结案。
4. 构建验证 API 成功/失败/未知路径的 no-store 及普通首页托管仍正常；验收后移除临时诊断代码，保存脱敏抓包证据。

## 十一、最终交付与验收

- 实际修复：`frontend/src/api/http.js` 对统一 API GET 设置 `Cache-Control: no-cache`、`Pragma: no-cache`，使已存的旧响应重新走网络；`backend/Program.cs` 在鉴权/静态文件之前对 `/api` 响应统一设置 `Cache-Control: no-store`，包括 API 失败和误命中静态兜底。保留 SPA Fallback，不改表、业务权限或接口正文。
- 用户原浏览器修复后抓包：`2026-10-01 11:39:35` 两接口均 200 JSON code=0；order-options TraceId=`00-45e559bf560e9e2f063ac06cc5191366-63a1e76617a1e321-00`，pending TraceId=`00-9c24419319315cfa33e73f9ba8544478-1418d512f5247814-00`，客户端与代理均匹配。用户明确回复“问题没有了”。
- 电脑端已登录管理员会话：普通刷新连续 5 次、通过生产管理菜单进入复核连续 5 次均未弹错。后端更新后的代理证据中两接口各 12 次 HTTP 200 application/json、code=0、Cache-Control=no-store；可逐条按 TraceId 对照客户端记录。不是以工具侧成功代替用户侧失败证据。
- 后端独立构建通过：`dotnet build backend/WorkshopMes.csproj --no-restore -p:SkipVersionBump=true -o artifacts/review111-backend`，0 错误/0 警告。只更新本地 Debug 的 WorkshopMes.dll/pdb，原件保存在 artifacts/review111-backup；运行 DLL 与验证构建 SHA256 一致。没有覆盖 appsettings/start.bat、没有部署生产服务器。
- 本地后端初次在受限环境启动出现 SQL Server 连接失败，已在原权限环境重新启动成功并验证监听 8080；未修改连接配置或降低数据库安全设置。启动输出保存在 artifacts/review111-backend.stdout.log。
- 后端接口未登录返回 401 no-store；未知 `/api`、大小写 `/API` 路径兜底 HTML 也带 no-store；普通首页仍 200 HTML 且不添加 API 专用缓存标记。
- 正式前端构建 `npm run build` 通过；有现有大体积 chunk 提示，无构建错误。临时代理/客户端诊断代码已全部移除；移除后再次刷新复核页正常。脱敏抓包保留 `artifacts/review-network-111.jsonl`，未记录 Bearer/Cookie。
- 已同步 docs/06 与 .cursor/rules/api-conventions.mdc 的实时 API 缓存约定。
- 用户提议的登录页“清旧缓存”按钮：本次没有增加。它可作为手动恢复入口，但不能替代 API 禁止缓存；仅删除 localStorage/CacheStorage 无法保证清除本次 HTTP 缓存，还可能误删登录信息。当前修复普通刷新即恢复，无需人工清缓存。

补充辨析：响应携带 X-Trace-Id 只能证明该响应生成时经过了后端，不能证明本次浏览器读到它时又请求了后端。本次重复的旧 TraceId 与缺失的代理请求，正是区分缓存响应和实时响应的关键。