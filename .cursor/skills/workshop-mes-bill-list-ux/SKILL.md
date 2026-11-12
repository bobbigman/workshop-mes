---
name: workshop-mes-bill-list-ux
description: >-
  Migrates Workshop MES PC business-bill list pages to Kingdee-style UX:
  field+op+value quick filter, selection + toolbar actions, no row 操作 column.
  Use when aligning 工单/报工/复核/异常/工资/价目表列表, 快捷过滤, 顶栏按钮,
  FilterField/FilterOp/FilterValue, or user says 单据列表对齐/比照工单报工改.
---

# 业务单据列表：对齐工单/报工 UX

配合规则：`.cursor/rules/bill-list-ux.mdc`（只业务单据，不含基础资料）。

**范本（只读对照，不要整文件复制粘贴）：**

- 前端：`frontend/src/views/order/index.vue`、`frontend/src/views/report/index.vue`
- 后端：`WorkOrderService` / `ReportService` 的 `FilterField` + `ApplyQuickFilter*`

## 何时用

用户要「单据列表比照工单/报工」「快捷过滤」「去掉操作列改顶栏」时启用。  
一次只改**一页**（或用户点名的一组），改完编译再下一页。

## 范围清单（业务单据）

| 页面 | 路径 | 状态（改完请更新） |
|---|---|---|
| 工单 | `views/order/index.vue` + `form.vue` | 已对齐；单据独立页签 `/order/new`、`/order/:id` |
| 报工 | `views/report/index.vue` | 已对齐 |
| 报工复核 | `views/review/index.vue` | 已对齐 |
| 异常 | `views/abnormal/index.vue` | 已对齐 |
| 工资 | `views/salary/index.vue` | 顶栏命令+去操作列已对齐；快捷过滤 FilterField 仍待做 |
| 计件/SKU 工资相关 | `views/salary-sku/index.vue` | 待核是否单据列表 |
| 工序价目表 | `views/operation-price-list/index.vue` + `form.vue` | 已对齐；单据独立页签 `/operation-price-list/new`、`/:id` |
| 排产等其它单据列表 | 视菜单 | 待核 |

**默认不做：** `dept` / `unit` / `defect` / `user` / `custom-field` 等基础资料（单据技能范围外）。  
**基础资料已对齐产品模式（另指令，非本技能）：** `product`（docs/440–441）、`operation` / `routing`（docs/442：列表 UX + 审核/反审/禁用/反禁用 + 人时）。

## 迁移步骤（按页）

1. **摸现状（限时）**  
   只读目标 `index.vue` 工具栏 + 表格操作列 + 列表 API；后端对应 `*QueryDto` + `QueryAsync`。够判就停。

2. **前端工具栏**  
   - 字段下拉 + 比较符 + 值控件（按 `valueType`：text / number / date / 枚举 select）。  
   - 产品编号/名称：`filterable` + `allow-create`，复用本页或 `getProductList`（约 200 条即可）。  
   - 工单号等已有选项列表可同样做成可搜可选手输。  
   - 查询 / 重置；原「新增、代办、批量」等入口按钮保留在过滤右侧。

3. **前端勾选与顶栏动作**  
   - `el-table-column type="selection"`；`@selection-change` + 行点击单选（参照工单 `onRowClick`）。  
   - 原操作列按钮挪到工具栏；`requireOne`；**不要**按选中条数/状态给顶栏 `:disabled`（多选时反灰原因含糊）。点下去再提示业务原因。  
   - 删除主表「操作」列（弹窗内子表除外）。

4. **后端快捷过滤**  
   - `*QueryDto` 增加 `FilterField` / `FilterOp` / `FilterValue`。  
   - `FilterField` 有值时走 `ApplyQuickFilter`，旧散装参数作兼容（MCP/旧客户端）。  
   - 文本默认 `contains`，数字/日期/枚举默认 `eq`；值空不筛；非法抛 `ThrowHelper.Biz`。  
   - 字段集合按该单据真实列设计，不要照抄工单自定义字段（除非该单真有）。

5. **验证**  
   - 后端 `dotnet build`（锁 exe 则输出到 `.tmp/build-*`）。  
   - 前端改动按项目效率规则构建；不主动部署除非用户要求。  
   - 验收：过滤生效；勾选后顶栏可改/可看记录；无主表操作列；旧 API 参数仍可用（若有 MCP）。

6. **收尾**  
   - 更新本技能表格「状态」列。  
   - 改表才动 `docs/02`；纯查询参数一般不必。

## 开单据不卸列表页签（#22）

原则：打开全屏单据**不得**卸掉或盖死列表页签。

1. **拆路由**：列表一路由（如 `/order`），单据另路由（`/order/new`、`/order/:id`；价目表同理）。
2. **PcTabs**：`meta.title` 登记页签；单据加载后 `setPcTabTitle(path, 工单号|单据号)`；`meta.billTab` 防止被默认标题盖回；`meta.menuPath` 侧栏高亮列表菜单。
3. **小弹窗保留**：调价、行详情、删除确认、改数等继续用 `el-dialog` / `ElMessageBox`，不强制新页签。
4. **禁止**：用 fullscreen `el-dialog` 盖在列表上当单据编辑壳。

范本：`order/index.vue`↔`order/form.vue`，`operation-price-list/index.vue`↔`form.vue`。

## 明确不做

- 完整 F7 / 搜索按钮弹窗选基础资料。  
- 两组条件并排、过滤方案胶囊（另立需求）。  
- 为对齐去改 H5（本技能默认 PC）。

## 反例

```text
# ❌ 基础资料产品列表也拆掉操作列上快捷过滤
# ❌ 只改前端假过滤、后端仍只认旧 orderNo 参数
# ❌ 一次改五个页面不编译
# ❌ 全屏 dialog 盖死列表当单据
# ✅ 对照 order/report → 改一页前后端 → build → 勾状态表
# ✅ 列表签仍在；单据另签可来回切
```
