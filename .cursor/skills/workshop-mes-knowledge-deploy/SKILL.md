---
name: workshop-mes-knowledge-deploy
description: >-
  整理 Workshop MES（车间小工单）智能客服知识库（SupportDocs），并把它打
  进增量部署包一并交付给客户服务器。Use when the user asks 整理知识库,
  知识库, SupportDocs, 智能客服, MCP 检索, search_support_docs, 超报比例,
  上道不良, 把知识库打进更新包, 或要同步到 apply-update.bat 自动拷贝.
  与 workshop-mes-deploy（打程序增量包）互补；本技能只负责知识库内容整理
  与随包交付，程序本体仍走 workshop-mes-deploy。
---

# 车间小工单：知识库整理 + 打进增量部署包

本技能固定一条链路：**整理 SupportDocs → 校验 → 复制进 publish-update → 让 apply-update.bat 自动拷贝到服务器知识库路径**。
知识库是「智能客服 / 企业微信 MCP search_support_docs」唯一数据源（只读 `SupportDocs`，不读仓库 docs 根）。

## 一、先分清两件事（别混）

- **知识库整理**（本技能）：改 `D:\workspace\pkub\bee\SupportDocs\` 下的手册/问答/`_retrieval.json`。
- **程序增量包**（`workshop-mes-deploy` / `pack-update.ps1`）：更新 `wwwroot` + `*.dll`。

客户机 `appsettings.json` 里 `Ai:SupportDocs:RootPath` 指向知识库根目录（如 `D:\WorkShop\SupportDocs`），更新时该文件**绝不覆盖**。

## 二、后端对知识库的硬约束（决定整理边界）

- 检索 `SupportDocumentService` 用 `Directory.EnumerateFiles(root)` **不递归子目录** → 文档**不能**分到子目录，否则检索不到。
- `FaqFileName = "10-系统能力50问.md"` 被代码写死（PC 常见问题页依赖）→ 文件名不能改，内容可动。
- 索引只看 `.md` / `.txt`；`_retrieval.json` 与手册**同目录**，热加载（坏 JSON 则回退 builtin），改完不用重启。
- **检索按标题切分、文件名无关**（除 10 号）→ 重编号对 MCP 命中无益、却会破坏文档间交叉引用与 README 基准，**不要重编号**。

## 三、整理知识库（逐篇核对 → 补新 → 去重 → 刷元信息）

顺序固定，别跳：

1. **定位新功能缺口**：先看最近上线的开发指令（如 `docs/已做指令/230-*.md`）有没有**没写进知识库**的能力。这是「答案最新」的最大缺口——客户从企微问不到就丢单。把新功能按真实契约补进对应分册（如 04-报工与复核）＋ `10-系统能力50问` ＋ `00-系统能力总地图`（加能力行 + 维护日志）。
2. **补检索词表**：新功能的关键词 + 同义词加进 `_retrieval.json`（`keyPhrases` / `synonyms` / 可选 `boosts`），把 `version` 递增（如 `2026-10-14.1`）。**必须用 JSON 校验器确认合法**，坏 JSON 会让检索回退 builtin。
3. **去真冗余（不是去有意分场景）**：只在「同一问答两处逐字重复、会改一处忘一处」时，把次要那处精简为一句指向权威文档的指引。像 `01/02`、`06/07` 这类**有意分场景**的重复是不同检索入口，**不删**。
4. **刷新 front matter**：每篇 `updatedAt → 当天`、`systemVersion` 同步、`version +1`；缺 `updatedAt` 的补齐。抽查确认无遗漏。
5. **README 同步**：`SupportDocs\README.md` 的目录表、真实测试问法基准、维护日志随内容更新。

## 四、打进增量部署包

```powershell
# 1. 先打程序增量包（产物在 backend\publish-update）
powershell -NoProfile -ExecutionPolicy Bypass -File backend\pack-update.ps1
```

```powershell
# 2. 把整理后的知识库复制进包（覆盖旧版）
$src = "D:\workspace\pkub\bee\SupportDocs"
$dst = "D:\workspace\pkub\bee\backend\publish-update\SupportDocs"
if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
Copy-Item $src $dst -Recurse
```

```powershell
# 3. 让 apply-update.bat 自动拷贝知识库（若脚本还没这个步骤）
#    脚本顶部加：set KNOWLEDGE=D:\WorkShop\SupportDocs
#    [2/3] 拷贝后、[3/3] 启动前加一段：包内有 SupportDocs 就 robocopy 到 %KNOWLEDGE%
#    目标目录不存在先 mkdir；失败 goto FAILKB 不阻断已完成的程序更新
```

```powershell
# 4. 把改好的 apply-update.bat 与 update-readme.txt 同步进包
Copy-Item backend\apply-update.bat        backend\publish-update\apply-update.bat
Copy-Item backend\update-readme.txt       backend\publish-update\README-update.txt
```

> **部署说明别放知识库目录内**：`SupportDocs` 里 `.txt` 会被检索索引，污染答案。部署说明放包根目录（如 `README-SupportDocs-DEPLOY.txt`）。

## 五、验收（交付前逐项勾）

- [ ] 知识库文档无敏感文件被带进包（`appsettings*` / `start.bat` / exe / runtimeconfig / deps）——运行 `pack-update.ps1` 自带闸门，复检一遍。
- [ ] `_retrieval.json` JSON 合法、`version` 递增、新功能词表已含。
- [ ] 包内 `SupportDocs\` 为最新（与源一致，抽查 updatedAt / 文件数）。
- [ ] 包内 `apply-update.bat` 含知识库自动拷贝段，`KNOWLEDGE` 指向服务器知识库路径。
- [ ] `README-update.txt` / `README-SupportDocs-DEPLOY.txt` 反映「自动拷贝 + 手动备选」。
- [ ] 部署后企微 MCP 再查，返回 `retrievalVersion` 对上新版、`retrievalSource=file`。

## 六、止步线（不做）

- 不重编号、不分子目录（后端不递归）。
- 不删「有意分场景」的重复章节。
- 不编造未上线能力（`docs/已做指令` 里没有的，不许写进知识库当已上线）。
- 不动服务器 `appsettings.json` / `start.bat`；不整文件覆盖。
