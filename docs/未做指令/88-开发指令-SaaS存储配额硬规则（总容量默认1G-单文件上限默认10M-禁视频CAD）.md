# Cursor 开发指令 · SaaS 存储配额硬规则（每客户总容量默认1G可配 · 单文件上限默认10M可配 · 禁视频/CAD）

> 日期：2026-09-29｜状态：待开发（分析稿已核，待拍板开工）
> 定位：把推广定稿里的「配套硬规则」落到系统上传链路上：**SaaS 每客户总容量默认 1G、单文件上限默认 10M（两者均由 appsettings 配置、代码不写死）、文件类型白名单（图片/PDF/Word，禁止视频/压缩包/CAD 大文件）；超限唯一解法 = 升级私有买断版（私有版不限制）**。
> 依据：[推广方案/未来落地总规划（最终定稿）](../推广方案/未来落地总规划｜产品体系+定价+服务器规则+引流模式+渠道规则（最终定稿）.md)、[docs/02](02-数据字典.md)、现有 `KnowledgeFileService`/`KnowledgeFileController`。

---

## 一、已查证现状（开工前必须复核，不靠历史文档猜）

| 项 | 现状 | 证据/入口 |
|---|---|---|
| 客户文件上传唯一入口 | `KnowledgeFileService.UploadAsync`（挂产品/工序，存相对路径），前端 `KnowledgeFileController.Upload` | `backend/Services/KnowledgeFileService.cs` |
| 当前单文件上限 | **20MB 写死在代码**（`const long MaxFileBytes = 20 * 1024 * 1024`）→ 需改为**从 appsettings 读、代码不写死**，默认 10M | `KnowledgeFileService.cs:42` |
| 类型白名单 | 已含 `pdf/docx/xlsx/jpg/png/webp`（**天然无视频、无压缩包、无 CAD**） | `KnowledgeFileService.cs:202-214` |
| 存储布局 | 按 `factory_id/{guid}.ext` 存于 `KnowledgeBase:RootPath`，相对路径存表 | `KnowledgeFileService.cs:129-133` |
| 配额载体 | `factory_id`（一个客户=一个工厂） | `base_knowledge_file.factory_id` |
| 版本/套餐载体 | `sys_factory.license_tier`：`trial/enterprise/flagship`（另有云端授权 `license_expires_at_utc`） | `docs/02-数据字典.md:44-53` |
| 私有买断版识别 | **尚无**；`license_tier` 只有 trial/enterprise/flagship 三档，私有版需新增判定（见 §四） | 现库仅 1 条 `flagship`，多租户未落地 |
| 文件大小统计 | **`base_knowledge_file` 无文件大小字段**，无法按表聚合配额 → 需补 | `docs/02-数据字典.md:150-163` |
| 其他上传口 | `ImportController.Upload`（Excel 导入，临时文件）、`LoginSettingController.Upload`（登录图，限 png/jpg/webp） | 已核：不占客户知识库配额，但要防超大 |
| 一文件挂多处口径 | 一个磁盘文件被多 `ref_type+ref_id` 引用 = 插多条记录，`relative_path` 不唯一 | `docs/02-数据字典.md:163` |

**结论**：改的是「上传校验 + 配额统计」，不是重做存储。落点集中在 `KnowledgeFileService`，其余上传口只做防超大兜底。

---

## 二、技术栈与硬约束（不可变更）

- .NET 8 Web API + EF Core + SQL Server；命名空间 `ahu.MicrosoftMes`。
- 存储根目录 `KnowledgeBase:RootPath` 不变；相对路径结构 `factory_id/...` 不变。
- 异常统一 `Common/ThrowHelper.cs`，不吞、带上下文（工厂ID + 文件名 + 大小 + 上限 + 已用量）。
- `docs/02` 需**新增 1 个字段**（见步骤 2），其余表结构不动。
- 生产环境配额校验**可一键关闭**（配置开关，默认开），关闭时日志照记、不脱敏。

---

## 三、业务口径（规则定死，禁止另造）

| 套餐（license_tier 映射） | 总容量/客户 | 单文件上限 | 说明 |
|---|---|---|---|
| `trial`（体验账套） | 200M（默认） | 10M（默认） | 到期自动清理另走授权/账套过期逻辑，本指令只锁配额；容量以 appsettings 为准 |
| `enterprise` / `flagship`（SaaS 标准/企业/旗舰） | 1G（默认） | 10M（默认） | 主力对象；**总容量与单文件上限唯一事实源=appsettings**，可改 |
| 私有买断版（**以部署配置 `Deployment:Private=true` 识别为主**，网址仅辅助展示） | **不限制** | **不限制** | 「超限唯一解法=升级买断」即此档放行 |

- 文件类型白名单 = `pdf / docx / xlsx / jpg / png / webp`（**禁止**视频 mp4/mov/avi、压缩包 zip/rar/7z、CAD dwg/dxf 及一切大文件格式；现有白名单已满足，只需复核无漏网）。
- 配额统计口径（**关键，防一文件挂多处重复占配额**）：
  - 每客户已用容量 = `SELECT SUM(file_size) FROM (SELECT DISTINCT relative_path, file_size FROM base_knowledge_file WHERE factory_id = ?) t`。
  - 同一 `relative_path` 被多处引用时**只计一次**，与磁盘实际占用一致。
- 超限行为：**上传直接拒绝**，回明确文案（见步骤 3），不静默覆盖、不写入磁盘。
- 删除释放口径：删除记录时，**当**该 `relative_path` 已无任何引用 → 同步删磁盘文件并释放配额；**当**仍被其它记录引用 → 只删记录、保留磁盘（配额按去重口径自动仍计该文件一次）。

---

## 四、原子步骤（每步显式带条件判断）

### 步骤 1 · 复核所有上传口，确认无视频/CAD 漏网（Cursor 可闭环）

- 用 grep 找全项目内所有 `IFormFile` / `Request.Form.Files` 入口（现状：`KnowledgeFileController`、`ImportController`、`LoginSettingController`）。
- **当** 某入口接收并落盘用户自定义文件（含任意扩展名、或 ContentType 不受白名单限制）→ 列入改造清单（多为 KnowledgeFile 一个）。
- **当** 某入口只是系统内固定用途（Excel 导入、登录图）→ 不占客户配额，但按步骤 4 做防超大兜底。
- 验收：列出一份「上传口 × 是否走客户配额 × 现有白名单」对照表，确认无视频/压缩包/CAD 能进入知识库存储。

### 步骤 2 · `base_knowledge_file` 补 `file_size` 字段（DDL 变更，改 docs/02）

- 在 `docs/02-数据字典.md` 的 `base_knowledge_file` 增加：`file_size BIGINT NOT NULL DEFAULT 0`（字节），并在字段注释写明「配额统计用，去重口径见本指令 §三」。
- **当** 数据库尚未同步该字段 → 由 Cursor 出增量 SQL（`ALTER TABLE base_knowledge_file ADD file_size BIGINT NOT NULL DEFAULT 0`），并执行/交 DBA。
- **当** 有历史存量文件 → 迁移脚本按 `FileInfo.Length` 回填 `file_size`（按 `relative_path` 对磁盘文件取大小），一条路径一行，缺盘/丢文件的行记 `0` 并打日志（不脱敏），不中断上线。
- 同步更新 EF 实体 `BaseKnowledgeFile` 与 `AppDbContext` 映射。
- 验收：`docs/02` 字段已补；`dotnet build -c Release` 0 错 0 警；迁移脚本可重复执行不炸。

### 步骤 3 · 上传校验改造（核心）：单文件上限按套餐 + 配额拦截

- 读套餐：**以部署配置开关 `Deployment:Private` 为主识别**，网址仅辅助展示/审计（不参与权限判断）：
  - **当** `Deployment:Private = true`（私有买断版/局域网私有云）→ **直接放行**，无存储与单文件限制（只做非空/非 0 校验），不受 `StorageLimits` 约束。
  - **当** `Deployment:Private = false`（SaaS 云）→ 按 `sys_factory.license_tier`（经当前登录工厂 `factoryId` 取）映射：`trial`→200M（默认）/10M（默认）、`enterprise`/`flagship`→1G（默认）/10M（默认）（**最终值一律以 appsettings 为准，这里只给默认**）。
  - 网址辅助：`Request.Host` 与 `KnownCloudHosts`（自己云服务器域名白名单）比对，**仅**用于运维日志、界面标「私有部署」等展示/审计用途；**不得**作为放开存储限制的权限依据（防伪造 Host 头、防反代改写 host）。
- 单文件大小：`MaxFileBytes` 由代码常量 20MB **改为从 `appsettings` 的 `StorageLimits.<档>.MaxFileBytes` 读取（代码不写死 5M/10M 业务字面量）**，默认 **10M**：
  - **当** `Deployment:Private = false` 且 tier = `trial` / `enterprise` / `flagship` → 单文件上限 = 该档 `StorageLimits[tier].MaxFileBytes`（默认 10M，SaaS 各档默认一致、可分别配）。
  - **当** `Deployment:Private = true` → 已在「读套餐」放行，不判单文件大小（只做非空/非 0 校验）。
  - **当** `appsettings` 缺该档配置项 → 用代码常量默认 **10M** 兜底并打日志（唯一允许的写死=回退兜底，业务值以 appsettings 为唯一事实源）。
  - 超上限 → `ThrowHelper.Biz` 文案：「文件超过当前套餐单文件上限 {MaxFileBytes 可读格式}（默认 10M），如需大文件请升级私有买断版」，带文件大小。
- 类型白名单：沿用现有 `NormalizeFileType`（已满足），只复核；**当** 发现某入口可绕过 → 统一收敛到该白名单。
- 配额校验（在写盘**之前**）：
  1. 算该工厂已用容量（§三口径去重 SUM）。
  2. **当** `已用 + 本次大小 > 套餐总容量（TotalBytes，默认 1G）` → 拒绝，文案：「工厂存储已满（已用 XM / 上限 {TotalBytes 可读格式}，默认 1G），请清理旧文件或升级私有买断版」，XM 用 KB/MB 可读格式。
  3. **当** 未超 → 写盘、登记记录并写入 `file_size`。
- 存储与登记之间失败 → 异常带上下文抛，不留下"表有记录、磁盘没文件"的半残（有则回滚登记/补删磁盘）。
- 验收：按当前配置的单文件上限（默认 10M）构造**略低于上限**与**略超上限**的文件各试传一次（如 9M 与 11M）；构造"已用接近当前配置总容量（默认 1G）"的工厂再传 → 被拒且文案准确；**改 `appsettings` 的单文件上限与总容量各重测一次，确认跟随配置而非代码写死**；`dotnet build -c Release` 0 错 0 警。

### 步骤 4 · 其余上传口防超大兜底（不占客户配额，但要防炸盘）

- `ImportController.Upload`（Excel 导入）：**当** 文件 > 10M → 拒绝「导入文件过大」；不改其临时文件语义。
- `LoginSettingController.Upload`（登录图）：**当** 文件 > 2M → 拒绝「登录图过大」（现有已限 png/jpg/webp 类型，补大小即可）。
- 验收：两个口子超大文件均被拒，正常文件不受影响。

### 步骤 5 · 配额开关配置项（生产可一键关闭、日志完整不脱敏）

- 在 `appsettings*.json` 增：
  - `Deployment:Private`（bool，默认 `false`；**私有买断版部署时置 `true`，作唯一权威识别**）。
  - `KnowledgeBase:EnforceQuota`（bool，默认 `true`）+ `StorageLimits:{trial,enterprise,flagship}`（每档含 `TotalBytes` 总容量默认 1G、`MaxFileBytes` 单文件上限默认 10M，**均代码不写死**）+ `KnownCloudHosts`（自己云服务器域名白名单，网址辅助用）。
- **总容量与单文件上限唯一事实源**：`StorageLimits.*.TotalBytes` / `StorageLimits.*.MaxFileBytes`；业务代码不出现 1G/5M/10M 等业务字面量；缺项时用代码常量（总容量 1G、单文件 10M）兜底并打日志（见步骤 3）。
- **当** `Deployment:Private = true` → 配额与单文件限制整体放行（不受 `StorageLimits` 约束），存储不受总容量/单文件上限限制。
- **当** `Deployment:Private = false` 且 `EnforceQuota = true`（默认）→ 步骤 3 的按套餐配额与单文件上限全量生效。
- **当** `EnforceQuota = false` → 跳过配额与单文件上限校验，但**照常记录**：每次上传打日志（工厂ID + 文件名 + 大小 + 是否超限），供排查，不脱敏。
- **当** 部署更新 → 只发增量包，不覆盖服务器 `appsettings*.json`（见根目录 `DEPLOYMENT_NOTES.md`），默认值由代码常量兜底。
- 验收：开关两种状态下传行为各测一次；关闭时超限也留有日志证据。

### 步骤 6 · 前端体验与提示同步（PC + H5，避免前端后知后觉）

- PC（Element Plus `el-upload`）：`accept` 限定白名单扩展名；`before-upload` 前端先拦超**单文件上限（默认 10M，前端从配置接口取、不写死）**；提示文案「支持 图片/PDF/Word，单文件≤{上限}，总容量≤{TotalBytes 可读格式}/客户」。
- H5（Vant `Uploader` / 作业指导书上传）：同样加类型与单文件上限前端校验（上限同取配置，默认 10M）。
- **当** 后端拒绝 → 前端展示后端返回的明确文案（含已用量/上限），不吞错、不假成功。
- 验收：前端超类型、超大小均先拦；后端拒绝文案能透传到界面。

---

## 五、边界（对外口径，先跟客户说清）

- **「超限唯一解法=升级私有买断版」**：SaaS 各档不提供扩容购买；私有买断版无存储/上传限制。
- 视频、压缩包、CAD 大文件 **SaaS 一律不收**（不是"收但计费"，是白名单直接拒绝）；私有买断版可自行管理本地存储，不强制入库。
- 配额按"去重后的实际文件占用"计，与磁盘占用一致；删除引用不必然释放磁盘（见 §三口径）。

---

## 六、谁做哪步（避免 Cursor 闷头干业务/拍板）

| 步骤 | 谁 | 说明 |
|---|---|---|
| 1 上传口对照 + 步骤 2/3/4 代码与 DDL | **Cursor** | 本机可闭环 |
| 3 私有买断档识别（`Deployment:Private` 开关为主 + 网址辅助） | **Cursor** 实现；开关值由运维/用户按客户性质部署时定 | 私有版部署置 true，SaaS 版默认 false |
| 5 配置开关 + 部署增量包 | **Cursor**，服务器配置由用户/运维按规范执行 | 不覆盖服务器 `appsettings*.json` |
| 6 前端提示 | **Cursor** | PC+H5 |

---

## 七、验收清单（勾选用，做一项勾一项）

- [ ] 步骤 1：上传口对照表无视频/CAD 漏网
- [ ] 步骤 2：`docs/02` 已补 `file_size`；迁移回填完成；`dotnet build -c Release` 0 错 0 警
- [ ] 步骤 3：trial/enterprise/flagship 单文件上限与总容量（appsettings 配置，默认 10M / 1G）生效；配额拦截生效；文案准确
- [ ] 步骤 4：Import 10M、登录图 2M 防超大生效
- [ ] 步骤 5：`EnforceQuota` 开关两种状态各测一次，关闭时超限留日志
- [ ] 步骤 6：PC+H5 前端类型/单文件上限拦截 + 后端文案透传
- [ ] 私有买断档识别：`Deployment:Private=true` 时存储/单文件全放行；网址（`KnownCloudHosts`）仅辅助展示、不进权限判断

> 完成前复核：输入已读全（含 docs/02、最终定稿 §五）、约束未越界（只改上传校验+配额统计）、配额去重口径正确、异常带上下文不吞、开关可一键关闭且日志完整不脱敏。
