# 231-执行指令：SupportDocs 知识库整理 + 打进增量包 + apply-update 自动拷贝（技能固化）

> 写给：Cursor（及后续任何做部署/AI）· 长期遵守
> 日期：2026-10-14 · 已落地
> 技能：`.cursor/skills/workshop-mes-knowledge-deploy`（权威可复用流程，本次按它执行）

## 项目
WorkshopMes 车间小工单 —— **智能客服知识库（SupportDocs）整理与随包部署**。

权威出处：`DEPLOYMENT_NOTES.md`、`docs/59-执行指令-部署更新铁律-appsettings不覆盖.md`；程序增量包脚本 `backend/pack-update.ps1`、`backend/apply-update.bat`；本技能 `workshop-mes-knowledge-deploy` 与 `workshop-mes-deploy` 互补。

## 一、本次做了什么

### 1. 整理 SupportDocs（方案3：补新 → 去重 → 刷元信息）
- **补 docs/230 新功能**（超报比例 / 上道不良计不计入）到 `04-报工与复核.md`（新增「报工数量设置」操作节）、`10-系统能力50问.md`（+1 条）、`00-系统能力总地图.md`（+能力行、+维护日志）。这是「答案最新」的关键——新功能不写进知识库，企微 MCP 就答不出。
- **补检索词表**：`_retrieval.json` 加 `超报/超报比例/报工数量设置/上道不良/可报上限/数量异常` 等关键词 + 同义词；`version 2026-10-13.1 → 2026-10-14.1`；JSON 已校验合法。
- **去真冗余**：`05-常见问题` 末尾 14 段与 `08-扫码报工问答` Q1-Q15 逐字重复，精简为一句指向 08 的指引（口径只留一处）。
- **刷 front matter**：18 篇全部 `updatedAt→2026-10-14`、`systemVersion` 同步、`version +1`。
- **README 同步**：目录表加「报工数量设置」、测试问法加 64a、维护日志加当天记录。

### 2. 打进增量部署包
- 复制 `SupportDocs\`（20 文件 + images）到 `backend\publish-update\SupportDocs`。
- `apply-update.bat` 加 **自动拷贝知识库**步骤：`[2/3]` 拷贝后 `[3/3]` 启动前，若包内有 SupportDocs 则 robocopy 到 `set KNOWLEDGE=D:\WorkShop\SupportDocs`（服务器知识库路径，路径不同改这行）；目标不存在先 mkdir；失败 `goto FAILKB` 不阻断程序更新。
- 部署说明 `README-SupportDocs-DEPLOY.txt` 放**包根目录**（不放知识库内，避免 .txt 被检索索引污染答案）。
- `README-update.txt` 同步提示「知识库自动拷贝」。

### 3. 技能固化（本次核心）
新建 `.cursor/skills/workshop-mes-knowledge-deploy/SKILL.md`，把「整理 → 校验 → 打进增量包 → apply-update 自动拷贝」整条链路固化成可复用技能，供 Cursor 等 AI 调用。
在 `.cursor/rules/deploy-update-iron.mdc` 与 `docs/59` 补该技能入口。

## 二、硬约束（整理边界，长期遵守）

- 检索 `Directory.EnumerateFiles(root)` **不递归子目录** → 文档**不能**分子目录。
- `10-系统能力50问.md` 文件名被 `SupportDocumentService` 写死 → 不改名。
- 检索按标题切分、文件名无关（除 10 号）→ **不重编号**（会破坏交叉引用与 README 基准，且对 MCP 命中无益）。
- `_retrieval.json` 与手册同目录、热加载；坏 JSON 回退 builtin → 改完必校验。
- 索引只看 `.md`/`.txt` → 部署说明别放知识库目录内。
- 只补 `docs/已做指令` 里真实上线的能力，不编造未上线项。

## 三、验收（已勾）

- [x] `_retrieval.json` JSON 合法、version=2026-10-14.1、含新功能词表
- [x] 包内 `SupportDocs\` 为最新（20 文件，抽查 updatedAt=2026-10-14）
- [x] 包内 `apply-update.bat` 含知识库自动拷贝段，KNOWLEDGE=D:\WorkShop\SupportDocs
- [x] 包内无敏感文件（appsettings / start.bat / exe / runtimeconfig / deps，pack 闸门 + 复检 PASS）
- [x] README-update / README-SupportDocs-DEPLOY 反映自动拷贝 + 手动备选
- [x] 技能 `workshop-mes-knowledge-deploy/SKILL.md` 已建，规则与 docs/59 已登记

## 四、部署效果

服务器拷 `publish-update` 整夹 → 双击 `apply-update.bat`：停进程 → 更新 wwwroot/dll → **自动更新知识库到 D:\WorkShop\SupportDocs** → 用原有 start.bat 拉起。MCP 检索热加载，拷完即生效（retrievalVersion=2026-10-14.1、retrievalSource=file）。

## 五、止步线（不做）

- 不重编号、不分子目录。
- 不删「有意分场景」的重复（01/02、06/07 是不同检索入口）。
- 不动服务器 appsettings.json / start.bat；不整文件覆盖。
