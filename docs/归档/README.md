# 归档目录

这里放「已经完成使命、不再活跃维护，但需要留着回溯」的文档。

## ai-support（2026-09-24 归档）

`ai-support/` 是 **PC 内置 AI 助手 + 智能客服** 的开发侧源稿与审核索引，**不是知识库**，服务器从不读它。

### 为什么归档

- 客户服务器（mes.webok.net）只读部署目录 `Ai:SupportDocs:RootPath`（发布物，仓库内对应 `SupportDocs/`），读不到本机 `docs/ai-support`。
- `ai-support` 里的 `work-order / reporting / quantity / permissions / faq` 已通过 `npm run manual:sync` 同步进 `SupportDocs/`，源稿使命完成。
- 剩下 `ai-assistant / production-guide / due-date` 是 AI 功能本身的口径说明，`sources.md` 是审核索引（口径来自哪段代码，只给开发者回溯，不能进用户知识库）。

### 结论

它不能当知识库用；真正上服务器当知识库的是 `SupportDocs/`。所以把源稿归档收起，避免以后被误当成「要部署的知识库」。

### 怎么恢复

把 `归档/ai-support/` 移回 `docs/ai-support/` 即可；不改 SupportDocs、不影响线上客服。线上客服内容要变，需改 `SupportDocs/` 后重新部署到服务器并刷新/重启。

## 2026-10-03 归档（SupportDocs 移出物 + 手册同步台账）

这批是"知识库净化"时从 `SupportDocs/` 移出的历史物 + 手册同步台账，同样**不参与 AI 检索**（服务器只读部署的 `SupportDocs/`，不读本目录），仅留档回溯：

| 文件 | 是什么 | 注意 |
|---|---|---|
| `2026-10-03-手册权威源同步总账.md` | docs/30/31/manual 三轮同步（1.7→1.8）改动总账 | 复核用；现行口径以手册为准 |
| `SupportDocs-更新日志.md` | SupportDocs 历次知识库同步 / 扒回 / 清理台账（原 README 台账部分移出） | 追溯用，不喂 AI |
| `车间小工单-喂AI知识库（旧版）.md` | 旧版喂 AI 知识库（表结构 / 接口契约版） | ⚠️ 工资手工列口径已过时（docs/136 已移除），勿作现行依据；01-10 已取代 |
| `安装到手机桌面.mhtml` | PWA 添加到主屏幕说明页 | 非 md，AI 本就检索不到 |
| `车间小工单-系统架构流程表关系三合一地图.html` | 系统架构 / 流程 / 表关系三合一图 | 非 md，AI 本就检索不到 |

**维护原则**：归档物不喂 AI；需要更新知识时改 `docs/30/31/manual` 权威源 → `npm run manual:sync` → 记入 `SupportDocs-更新日志.md`。
