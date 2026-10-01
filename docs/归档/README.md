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
