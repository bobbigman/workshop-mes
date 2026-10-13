# 扣子（Coze）重搭 + 重接 MCP + 发布到微信 · 照着点

> ⚠️ **状态：已被 `docs/47` 取代（2026-09-18 实测）**  
> 本文保留作历史依据与公众号/客服渠道备忘。**日常操作请直接看 `docs/47`**，不要再按本文旧步骤做。

## 相对 47 的三处关键勘误（必读）

| 旧结论（本文原稿） | 实测结论（以 47 为准） |
|---|---|
| 方式 A「URL 带 `?token=`」最省事 | **新版扣子 Streamable-HTTP 禁止 token 拼 URL**，握手失败；必须 Header `Authorization: Bearer` |
| 先发「网页版 / 分享链接 / Web SDK」 | **新版 Agent 没有公开 H5 / 分享链接渠道**；陌拜对外走 **微信机器人**（扫码绑定） |
| 域名 `209c3f5f.r19.vip.cpolar.cn` | 以当时 `appsettings.json` 的 `Mcp:AuthBaseUrl` 为准；示例已变为 `789105f9.r16.cpolar.top` |

> 后端仍支持 Header 与 `?token=` 两种鉴权（方舟等可用）；**仅扣子新版 MCP 接入必须 Header**。

---

## 0. 当前照抄值（与 47 一致）

| 项 | 值 |
|---|---|
| MCP URL | `https://789105f9.r16.cpolar.top/mcp`（域名以 `Mcp:AuthBaseUrl` 为准） |
| 鉴权 | Header：`Authorization: Bearer laohu-demo-2026`（Bearer 后有空格） |
| 传输 | Streamable HTTP（JSON 里字段名用 `type: streamable-http`） |
| 应有工具 | `start_auth`、`query_work_order`、`query_report`、`query_salary`、`calc_schedule_priority` |

推荐 JSON（与 47 相同）：

```json
{
  "mcpServers": {
    "laohu-workshop": {
      "type": "streamable-http",
      "url": "https://789105f9.r16.cpolar.top/mcp",
      "headers": {
        "Authorization": "Bearer laohu-demo-2026"
      }
    }
  }
}
```

---

## 1～3. 建 Agent / 接 MCP / 调试

**整段流程以 `docs/47` 第二节为准**，此处不重复，避免再分叉。

要点摘要：

1. 建普通 Agent（不要低代码 / 对话流）。
2. 自定义 MCP 粘上面 JSON → 详情页必须能列出 5 个工具；列不出就**删重建**。
3. Agent 设置里把 MCP **挂载**上去（最常漏）。
4. 调试发「查工单」，必须出现「调用工具 query_work_order」。

---

## 4. 发布（按实测渠道更新）

### 4.1 微信机器人（陌拜首选，见 docs/47 步骤 5）

1. Agent 设置 →【渠道】→ 添加 →【微信】。
2. 「去授权」→ 微信扫码绑定。
3. 微信里给机器人发「查工单」验收。

> ~~旧稿 §4.1「网页版 / 分享链接」~~：**新版 Agent 无此渠道，勿再找。**  
> 若以后扣子重新开放网页分享，再补文档；当前以 47 为准。

### 4.1-bis 关于「发布到豆包 App」——已下线，别走

⚠️ **豆包 App 的自定义智能体功能已于 2026-07-15 永久下线**（见[官方文档](https://docs.coze.cn/guides_publish_to_doubao)）。官方替代「猫箱」不适合车间工单场景。

| 想在哪问 | 可行做法（2026-09 实测） |
|---|---|
| 微信里问 | **微信机器人渠道**（§4.1 / docs/47） |
| 浏览器点开问 | 新版 Agent **暂无**公开 H5；勿承诺「发个链接点开」 |
| 公众号 / 企业客服 | 见下方 §4.2 / §4.3（长期商用再做） |

### 4.2 发布到微信公众号（服务号/订阅号）

1. 需要先有一个**已认证的公众号**（个人订阅号也可，认证过的服务号最好）。
2. 去微信公众平台拿 **AppID**。
3. 扣子「发布」→ 选「微信公众号（服务号/订阅号）」→ 点「配置」→ 填 AppID。
4. 用公众号管理员微信号**扫码授权**。
5. 回扣子勾选该渠道 → 发布。之后在公众号对话框发消息，机器人就回。

> 官方文档：[发布到微信服务号](https://docs.coze.cn/guides_publish_app_to_wechat_serviceAccount)、[发布到微信订阅号](https://docs.coze.cn/guides_wechat_subscription)

### 4.3 发布到微信客服（企业）

1. 需企业认证 + 开通微信客服。
2. 从微信客服平台拿 **企业 ID、Token、EncodingAESKey**。
3. 扣子「发布」→「微信客服」→ 填这些 → 复制 webhook 地址 → 回微信客服平台填回调地址 → 复制 secret 和客服账号 → 回扣子保存 → 发布。

> 官方文档：[发布到微信客服](https://docs.coze.cn/guides_publish_app_to_wechat_customerService)

---

## 5. 建议顺序（已按实测改）

| 顺序 | 动作 | 说明 |
|---|---|---|
| 1 | 建 Agent + Header 方式接 MCP | 见 docs/47 |
| 2 | 挂载 MCP，调试「查工单」 | 必须见工具调用 |
| 3 | 加微信渠道，扫码测 | **陌拜入口** |
| 4 | 公众号 / 客服 | 确认长期要用了再做 |

---

## 6. 提醒

- **日常以 `docs/47` 为准**；本文公众号/客服段落仍可参考。
- **演示免授权**：`Mcp:DemoSkipAuth=true` 时查工单/查报工免授权码；查工资仍要授权 + 财务角色。
- **cpolar 域名会变**：变了就更新 `Mcp:AuthBaseUrl`、MCP JSON、文档示例三处，并**删旧 MCP 重建**。
- **个人微信灰产机器人 = 没官方接口**，别用；走扣子官方微信渠道或 §4.2/4.3。
- **别让对话里的 Bot「帮你做看板网页再发布」**——那是跑偏；发布是控制台「渠道」配置。
