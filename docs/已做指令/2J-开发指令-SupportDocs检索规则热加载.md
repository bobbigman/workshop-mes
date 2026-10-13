# 2J-开发指令：SupportDocs 检索规则热加载（`_retrieval.json`）

> 状态：已做（本机 `dotnet build` 通过；热加载/企微问法未现场验收）  
> 性质：内部试用 — 检索可调数据外置，改规则不编译  
> 目标：词表 / 同义词 / 排除标题 / 问句加权 放 `SupportDocs/_retrieval.json`，随目录指纹热加载；算法仍在 C#。  
> 依据：2026-10-13 拍板方案 A（对话：检索逻辑外置，日常不重编）。

---

## 取舍（已定）

| 项 | 方案 | 状态 |
|---|---|---|
| 做/不做 | 做热加载配置；**不**把评分公式做成脚本/DSL | 已定 |
| 文件位置 | `Ai:SupportDocs:RootPath` 下 `_retrieval.json`（与手册同目录） | 已定 |
| 缺文件 / 坏 JSON | 回落代码内默认（与当前词表一致），检索不中断；日志 Warning | 已定 |
| 热加载 | 并入现有 `EnsureIndex` 指纹（含该文件长度+mtime）；改文件下次 Search 生效，无需重启 | 已定 |
| PC / H5 | 无界面；PC 客服与 MCP 共用 | 已定 |

---

## 落点

- `backend/Services/SupportDocumentService.cs`：加载规则；`ExtractTerms` / `Score` / `ExcerptAround` / 排除标题用规则；`boosts` 命中则章节加分。
- `SupportDocs/_retrieval.json`：种子（当前词表 + `excludeTitles:["README"]` + 工时比例→Q13/多人分摊加权）。
- `SupportDocs/README.md`：一行说明维护方式。
- 不改 `docs/02`、不改 appsettings 契约。

## 验收

- [ ] 无 `_retrieval.json` → 行为与现默认一致，不报 failed。
- [ ] 改 json 词表/加权后**不重启**，下一问 `search_support_docs` 已用新规则。
- [ ] 「能按员工的工时比例，自动分配报工数量吗？」前排含 Q13/按工时分摊，README 不进 sources。
- [ ] 坏 JSON → Warning + 默认词表，检索仍 ready。
- [x] `dotnet build` 0 错误。

## 止步线

- 不做向量检索、不做 DSL 脚本引擎、不把 MaxResults 挪进 json（仍 appsettings）。
