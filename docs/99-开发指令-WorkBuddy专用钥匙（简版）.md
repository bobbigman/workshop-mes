# 99-开发指令-WorkBuddy专用钥匙（简版）

> 交付对象：Cursor（写代码）。这是【简版】：只给 WorkBuddy 发一把独立的随机串专用钥匙。
> **前置：必须先完成 docs/96**（中间件已按钥匙查表定厂）。本指令只在 96 定型的中间件上**再放行一把配置钥匙**。
> 明确【不做】：不绑厂到 `sys_mcp_key`、不控工资、不做审计。

---

## 一、目标（人话）

WorkBuddy 原先可能共用演示 `Mcp:Token`。改成单独一把随机串 `Mcp:WorkBuddyToken`，配置里写上，WorkBuddy 用它连 `/mcp`。

定厂规则（与 96 对齐）：
- **表内厂钥匙**（`sys_mcp_key`）→ 用该行的 `factory_id`；
- **`Mcp:Token`（若配置）或 `Mcp:WorkBuddyToken`** → 用配置的 `Mcp:FactoryId` 定厂（排障/WorkBuddy 单厂演示用，不进钥匙表）。

---

## 二、要改的两处

**第 1 处：`backend/appsettings.json` 的 `Mcp` 段加一行**
```json
"WorkBuddyToken": "<32位随机串>"
```
必须工具生成，不手写 workbuddy/demo 单词。

**第 2 处：`backend/Mcp/McpTokenMiddleware.cs`（96 已改过的版本）**
在「配置万能钥匙」分支里，放行条件改为：
- 请求钥匙 == `Mcp:Token`（且 Token 非空）→ 用 `Mcp:FactoryId`；
- **或** 请求钥匙 == `Mcp:WorkBuddyToken`（且非空）→ 用 `Mcp:FactoryId`，`key_alias` 记为 `workbuddy-config`；
- 否则再走 `sys_mcp_key` 表比对；
- 全不中 → 401。

---

## 三、原子步骤

1. 生成 32 位随机串，写入 `appsettings.json` 的 `Mcp:WorkBuddyToken`。
2. 改中间件比对逻辑（只动配置钥匙分支，表查逻辑不动）。
3. `dotnet build` 通过。
4. WorkBuddy 的 mcp.json：`Authorization: Bearer <新随机串>`。

---

## 四、验证清单

1. WorkBuddy 新钥匙查工单 → 真数据，非 401。
2. 随便编钥匙 → 401。

---

## 五、铁律

- 只加配置钥匙放行；不绑厂表、不控工资、不做审计。
- 增量包勿覆盖服务器 `appsettings*.json`（docs/59）。
