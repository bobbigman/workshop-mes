# 执行指令：修复 SupportDocs 中文检索失效（扫码报工查不到）

> 状态：**已完成（2026-09-23）**
> 依据：豆包（2026-09-23）对真实运行环境 `localhost:8080` 的完整诊断；`docs/已做指令/54-开发指令-MCP查操作手册SupportDocs.md`、`SupportDocs/README.md`
> 目标：让 `search_support_docs` 对中文问句正常命中，恢复“扫码报工怎么开始”等查询。
> 分工约定：本指令由 **Cursor** 改代码、自测、部署、跑验收脚本；**豆包**负责锁死判据并对运行结果独立复核。

---

## 1. 一句话

运行中的 WorkshopMes（`localhost:8080`）**中文检索本身正常**。此前观察到的“纯中文关键词一律 `no_match`、纯英文命中”是**验收脚本用 Windows PowerShell 5.1 `Invoke-RestMethod -Body [string]` 发送中文 body 时按系统 ANSI（GBK）编码、服务端按 UTF-8 解析成乱码导致的“假 no_match”**，并非服务或检索 bug。修复=验收脚本改用 curl.exe + UTF-8 请求体（判据不变）；同时顺带核对运行 DLL 与源码一致。按锁定判据黑盒验收。

---

## 2. 已确诊证据（按此验收，勿推翻）

- 用 **curl.exe + UTF-8 请求体**实测：`扫码 / 二维码 / 流转卡 / 扫码枪 / 一键补报 / 扫码报工怎么开始` → 全部 `ready` 命中；`PDA` 照常。
- 用 PowerShell 5.1 `Invoke-RestMethod -Body [string]` 发中文 → **假 `no_match`**（中文按 ANSI 发送、服务端按 UTF-8 解成乱码）；ASCII/PDA 不受影响。**此为客户端编码坑，非服务 bug。**
- 临时探针文件（UTF-8）可被立即命中 → **知识库加载正常、`Ai:SupportDocs:RootPath` 正确指向仓库 `SupportDocs`**。
- 已排除：服务中文检索 bug、配置错误、知识库没料、文件编码（01~06 均合法 UTF-8 无 BOM）。
- **结论**：`SupportDocumentService` 检索逻辑（字符级 `IndexOf`）本就正常，**无需改检索源码**；只需让验收/调用方以 UTF-8 正确发送中文 body。
- **源码现状提示**：工作区 `CountOccurrences` 已是 `string.IndexOf(..., OrdinalIgnoreCase)`（字符级）。勿先入为主改 IndexOf。

---

## 3. 修复任务（红线：不改表 / 不改报工业务 / 不改 MCP 契约 / 知识库保持仓库唯一来源）

### 步骤 1（必做）：完整 rebuild 并部署到运行目录

```powershell
# 仓库根目录执行；旁路输出，避免 WorkshopMes.exe 锁默认 bin
dotnet build "backend\WorkshopMes.csproj" -c Debug -o "backend\_build_out"

# 先停 8080 上的 WorkshopMes 进程，再拷回运行目录（至少 DLL/deps/runtimeconfig；与 _build_out 对齐）
$run = "backend\bin\Debug\net8.0"
Copy-Item "backend\_build_out\WorkshopMes.dll" $run -Force
Copy-Item "backend\_build_out\WorkshopMes.pdb" $run -Force -ErrorAction SilentlyContinue
Copy-Item "backend\_build_out\WorkshopMes.deps.json" $run -Force
Copy-Item "backend\_build_out\WorkshopMes.runtimeconfig.json" $run -Force
# 若 _build_out 有更新的依赖 DLL，一并覆盖（勿删 logs、appsettings）
```

若默认 `bin` 未被占用，也可直接 `dotnet build` 输出到 `backend\bin\Debug\net8.0`，仍须确认进程已停再覆盖。

### 步骤 2（仅步骤 1 验收仍 FAIL 时）：查检索逻辑

排查 `backend/Services/SupportDocumentService.cs`：

- `ExtractTerms`（关键短语、中文二元词、≤32 字符全文）；
- `CountOccurrences`：`IndexOf` 必须为**字符级**，禁止按 UTF-8 字节搜索中文；

修复后重新走步骤 1 的 build + 部署。

### 禁止

改表结构、改报工业务、改 MCP 工具契约、改动知识库目录结构；新增 NuGet；产生 DDL。

---

## 4. 自测要求（Cursor 必做）

- `dotnet build` 通过；
- `backend.Tests` 下 **`SupportDocumentSearchTests` + `SupportDocsMcpToolsTests` 全部通过**（合计约 19 例，见 `docs/已做指令/54` C1～C5 / D1，勿与 `docs/54-手机摄像头扫码交付与排障.md` 混淆）；
- 不得新增 NuGet 依赖、不得产生 DDL 变更。

---

## 5. 部署与重启（Debug 框架依赖，勿照搬 start.bat 自检）

当前 8080 进程工作目录是 **`backend\bin\Debug\net8.0`**，为 **Debug 框架依赖** 布局（通常**没有** `coreclr.dll`）。`backend\start.bat` 面向自包含发布包（会查 `coreclr.dll` / `includedFrameworks`），**原样在 Debug 目录跑会自检失败**。

正确做法（只借鉴 bat 的环境变量）：

```powershell
# 停旧进程
Stop-Process -Name WorkshopMes -Force -ErrorAction SilentlyContinue

# 部署 DLL 后，在运行目录启动
cd backend\bin\Debug\net8.0
$env:ASPNETCORE_URLS = "http://0.0.0.0:8080"
Start-Process -FilePath ".\WorkshopMes.exe" -WorkingDirectory (Get-Location)
# 确认 8080 重新 LISTENING
```

URL 必须是明文 `http://0.0.0.0:8080`，禁止 Markdown 链接写法。

---

## 6. 黑盒验收（判据锁定，勿改）

- 脚本：`backend/scripts/verify-supportdocs-chinese.ps1`
- 顺序：**修复前先跑一次，应 FAIL（复现旧 bug）**；修复部署重启后再跑，须 **ALL PASS**。
- 固定判据（每词 `state=ready` 且 `totalMatched>0`）：
  - 中文必命中：`扫码报工怎么开始`、`扫码`、`二维码`、`流转卡`、`一键补报`
  - 英文照常：`PDA`
- **任何一条 FAIL = 验收失败，不得交付。**
- **脚本传输注意（不改判据）**：Windows PowerShell 5.1 的 `Invoke-RestMethod -Body [string]` 默认按系统 ANSI（中文机常为 GBK）发请求，服务端按 UTF-8 解 → 中文 query 乱码 → 假 `no_match`（ASCII/`PDA` 不受影响）。验收脚本须用 **curl.exe + UTF-8 无 BOM 请求体**；查询词用 Unicode 码点构造，避免脚本文件编码坑。**判据词表与 PASS 条件不变。**

---

## 7. 结果回传（必做）

- 把验收脚本的**完整原始输出**（每个词 `state` / `matched` / 命中的 `title / chapter`）**原样**贴回，供豆包复核。
- 说明：改了哪些代码（若仅对齐 DLL 则写明「未改源码」）、build 输出目录、部署重启方式、验收最终结果。

---

## 8. 执行记录（2026-09-23 Cursor）

### 结论

- **未改** `SupportDocumentService.cs` / MCP 契约 / 知识库 / 表结构。工作区检索本已是字符级 `IndexOf`。
- **已做**：旁路 `dotnet build -o backend\_build_out` → 停进程 → 部署到 `backend\bin\Debug\net8.0` → `ASPNETCORE_URLS=http://0.0.0.0:8080` 重启（Debug 框架依赖，未跑 start.bat 自检）。
- **自测**：`SupportDocumentSearchTests` + `SupportDocsMcpToolsTests` **19/19 通过**。
- **真因补充**：对齐 DLL 后，用 curl UTF-8 直打 `扫码` 已 `ready/matched=39`；原验收脚本经 `Invoke-RestMethod` 发 ANSI 体会造成中文假 FAIL。已把脚本改为 curl + UTF-8（**判据未改**）。
- **验收**：修复前基线 FAIL；修复后 **ALL PASS**。

### 修复前基线（应 FAIL）

```
=== SupportDocs 中文检索验收（真实 localhost:8080）===
[FAIL] 扫码报工怎么开始 -> state=no_match matched=0
[FAIL] 扫码 -> state=no_match matched=0
[FAIL] 二维码 -> state=no_match matched=0
[FAIL] 流转卡 -> state=no_match matched=0
[FAIL] 一键补报 -> state=no_match matched=0
[PASS] PDA -> state=ready matched=10 | 报工与复核 / 用 PDA 扫码枪，或手动输入; 常见问题 / PDA / 摄像头没反应、工单不存在; 报工与复核 / 用手机摄像头扫码; 工单 / 打印二维码流转卡; 操作入门 / 先看懂每天要做什么

== FAIL: 仍有中文检索问题，勿交付 ==
```

### 修复后验收（ALL PASS）

```
=== SupportDocs 中文检索验收（真实 localhost:8080）===
[PASS] 扫码报工怎么开始 -> state=ready matched=39 | 报工与复核 / 用 PDA 扫码枪，或手动输入; 报工与复核 / 用手机摄像头扫码; 常见问题 / PDA / 摄像头没反应、工单不存在; 报工与复核 / 电脑报工; 工单 / 改工单、结束、撤回和取消
[PASS] 扫码 -> state=ready matched=39 | 报工与复核 / 用 PDA 扫码枪，或手动输入; 报工与复核 / 用手机摄像头扫码; 常见问题 / PDA / 摄像头没反应、工单不存在; 报工与复核 / 电脑报工; README / 三、真实测试问法（≥10 条）与预期命中章节
[PASS] 二维码 -> state=ready matched=5 | 工单 / 打印二维码流转卡; 报工与复核 / 用手机摄像头扫码; 操作入门 / 先看懂每天要做什么; 常见问题 / PDA / 摄像头没反应、工单不存在; README / 三、真实测试问法（≥10 条）与预期命中章节
[PASS] 流转卡 -> state=ready matched=4 | 工单 / 打印二维码流转卡; README / 三、真实测试问法（≥10 条）与预期命中章节; 报工与复核 / 用手机摄像头扫码; README / 二、目录内容
[PASS] 一键补报 -> state=ready matched=9 | 报工与复核 / 一键补报未完工序; 常见问题 / 补报与改数有什么区别; README / 三、真实测试问法（≥10 条）与预期命中章节; 报工与复核 / 电脑报工; README / 二、目录内容
[PASS] PDA -> state=ready matched=10 | 报工与复核 / 用 PDA 扫码枪，或手动输入; 常见问题 / PDA / 摄像头没反应、工单不存在; 报工与复核 / 用手机摄像头扫码; 工单 / 打印二维码流转卡; 操作入门 / 先看懂每天要做什么

== ALL PASS: SupportDocs 中文检索已恢复 ==
```

---

## 验收脚本（backend/scripts/verify-supportdocs-chinese.ps1）

以仓库内脚本为准（curl.exe + UTF-8；判据词表不变）。
