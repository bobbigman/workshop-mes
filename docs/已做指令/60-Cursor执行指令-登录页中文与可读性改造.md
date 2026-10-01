# Cursor 执行指令 · 登录页中文与可读性改造

> 日期：2026-09-24｜状态：**已实现**（2026-09-24）  
> **唯一方案源：** [`docs/60-登录界面分析与改造建议.md`](../60-登录界面分析与改造建议.md)  
> 相关：`docs/未做指令/23-开发指令-客户独立部署试用.md`（DisplayName = 客户显示名）、`docs/59-执行指令-部署更新铁律-appsettings不覆盖.md`

## 执行纪律（必读）

- **逐条执行**：从上到下，一条改完并自验通过后，把该条 `- [ ]` 改为 `- [x]`，**再做下一条**。
- 每条含：改哪些文件 → 怎么改 → 怎么验收。验收不过不许打勾。
- 全部完成后跑最后一条全量回归，并把**本文件**从 `docs/未做指令/` 移到 `docs/已做指令/`；修正分析文档 `docs/60` 里对本指令的链接。
- 遵守 AGENTS.md：异常不吞；不改三条业务硬规则；已上线更新不覆盖客户 `appsettings*.json`。

## 硬约束（勿改偏）

1. **保留**登录成功后的跳转逻辑：`redirect` 以 `/h5/` 开头（且非 `//`）→ `router.replace(redirect)`；否则进 `/order`。
2. **保留**密码框 `show-password`（已有）；不要删掉再「重做」。
3. **不要**新增登录前老板/员工选择；不要加厂房大图/科技光效。
4. **不要**改登录 API 契约、表结构、JWT。
5. 只改仓库内开发/模板配置的 `DisplayName` 默认值；**禁止**把「改客户机 DisplayName」做成覆盖整个 appsettings 的更新步骤。
6. 配色继续用 `frontend/src/styles/theme.css` 令牌，不另起紫色主题。

## 总清单

- [x] 1. 仓库默认 `Instance:DisplayName` 改为中文
- [x] 2. 登录页：厂名眉标 + 固定产品名 + 业务副标题
- [x] 3. 表单：标签置顶、字号与控件高度、帮助文案
- [x] 4. 版本号移出卡片；窄屏宽度
- [x] 5. 布局页/标题兜底与英文占位清洗一致
- [x] 6. 部署/试用文档补一句 DisplayName 口径
- [x] 7. 全量回归验收 + 勾选归档

> **不做（本单砍掉）：** 独立 H5 登录页；多语言；登录页换肤开关；自动远程改客户机 appsettings。

---

## 1. 仓库默认 `Instance:DisplayName` 改为中文

**已做：**

- `backend/appsettings.json` → `"DisplayName": "车间小工单系统"`
- `backend/appsettings.WorkshopMesB.json` → `"DisplayName": "演示工厂B"`
- 未改 publish 产物与客户机配置

- [x] 完成

---

## 2. 登录页：厂名眉标 + 固定产品名 + 业务副标题

**已做：** `frontend/src/views/login/index.vue` + 共享 `frontend/src/utils/instanceDisplay.js`

- 产品名固定「车间小工单系统」；`WorkShop`/`Workshop`/`WorkshopB` 及与产品名相同者不显示厂名眉标
- 副标题 PC：`生产进度 · 工单管理 · 报工统计`；窄屏：`扫码报工，查看生产任务`
- `document.title` / `localStorage.instanceName` 经清洗函数写入
- 保留 `getInstanceInfo`、版本号、H5 `redirect` 跳转

- [x] 完成

---

## 3. 表单：标签置顶、字号与控件高度、帮助文案

**已做：** `label-position="top"`；输入约 48px；按钮 50px；标签 16px；保留 `show-password`；提示「账号或密码有问题，请联系管理员」；「操作手册」保留

- [x] 完成

---

## 4. 版本号移出卡片；窄屏宽度

**已做：** 版本号在 `.login-box` 外；卡片宽 400px；`max-width: 100%` + 窄屏 padding 收紧

- [x] 完成

---

## 5. 布局页/标题兜底与英文占位清洗一致

**已做：** `layout/index.vue` 读 localStorage / 接口时经 `resolveInstanceLabel`，旧 `WorkShop` 显示为「车间小工单系统」

- [x] 完成

---

## 6. 部署/试用文档补一句 DisplayName 口径

**已做：**

- `docs/未做指令/23-开发指令-客户独立部署试用.md` DisplayName 行补充中文/禁止英文/手工合并
- `DEPLOYMENT_NOTES.md` 补充 DisplayName 口径（与 docs/59 不覆盖铁律一致）

- [x] 完成

---

## 7. 全量回归验收 + 勾选归档

**验收记录（2026-09-24）：**

- [x] 代码：`redirect` 仍仅 `/h5/` 且非 `//` → `replace`；否则 `/order`
- [x] 代码：密码 `show-password` 仍在
- [x] 单元：`instanceDisplay` 对 WorkShop / WorkshopB / 车间小工单系统 / 中文厂名清洗符合预期
- [x] 配置：仓库 appsettings DisplayName 已为中文（UTF-8）
- [x] 本文件已迁入 `docs/已做指令/`；`docs/60` 链接已更新
- 浏览器目视登录/错密/手册：需本机重启前后端后刷新确认（开工时 `localhost:8080` 未在跑）

- [x] 完成

---

## 主要改动文件

- 新增：`frontend/src/utils/instanceDisplay.js`
- 修改：`frontend/src/views/login/index.vue`、`frontend/src/views/layout/index.vue`
- 修改：`backend/appsettings.json`、`backend/appsettings.WorkshopMesB.json`
- 文档：`docs/60`、`docs/未做指令/23`、`DEPLOYMENT_NOTES.md`
