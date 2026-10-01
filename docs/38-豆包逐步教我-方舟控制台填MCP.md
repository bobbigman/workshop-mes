# 发给豆包：一步步教我在火山方舟填 MCP

> 用途：把下面「复制区」整段贴给豆包（或方舟里的助手），让它当向导，一步一步带着你点控制台。  
> 前置：本机后端 + `cpolar http 8080` 已通（见 `docs/30` / `docs/35` §二）。  
> 官方参考：[云部署 MCP / Remote MCP](https://www.volcengine.com/docs/82379/1827534)（界面会改版，以控制台实际为准）。

---

## 复制区（从下一行起，整段发给豆包）

```text
你是向导，不是程序员。请用大白话，一次只教我做一步；每步结束用一句话问我「做好了吗？」，等我回「好了」或贴截图/报错后再给下一步。不要让我改代码、不要提 /mcp/rpc、不要让我往数据库插 token。

目标：在火山方舟控制台，把我们车间 MES 的远程 MCP 配上，点「测试连接」能列出工具。

【已经准备好的值——照抄，不要改】
- 【重要】不要用旧域名 2e2c5459（那是前端页面「车间管理系统」）；必须用 209c3f5f
- 若界面能填请求头：
  - MCP 地址：https://209c3f5f.r19.vip.cpolar.cn/mcp
  - Header Key：Authorization
  - Header Value：Bearer laohu-demo-2026（Bearer 后有空格）
- 若界面根本没有「请求头 / Headers / 高级」可填（很常见）：
  - 只填这一行 URL（token 已拼在地址里，不用再找 Header）：
    https://209c3f5f.r19.vip.cpolar.cn/mcp?token=laohu-demo-2026
- 传输方式：Streamable HTTP（有的界面只写「HTTP」，选这个；不要选 SSE，除非页面强制）
- 若有测试期/beta：可再加 Key=ark-beta-mcp，Value=true
- 成功时工具名应包含：start_auth、query_work_order、query_report、query_salary、calc_schedule_priority

【你教我时的总顺序】
1）打开控制台并找到「智能体 / Agent」配置页
2）找到「添加 MCP / 远程 MCP / 云部署 MCP」入口
3）选传输方式（Streamable HTTP）
4）先改 MCP 地址：删掉 2e2c5459，换成上面「带 ?token=」那一整行（界面没 Header 时用这一招）
5）若页面上有「请求头 / Headers / 高级 / JSON 配置」再教填 Authorization；没有就跳过，不要让我满页找
6）保存并点「测试连接」
7）确认工具列表出来
8）在对话里说「查工单」，走授权演示（这一步最后做）

【每一步你要怎么教】
- 先告诉我：打开哪个网址、点哪里（左侧菜单叫什么、按钮叫什么）
- 若界面名称和你说的不一样：让我把页面上相近的几个菜单名念给你，你再帮我对上号
- 填值时：把要粘贴的内容用单独一行写出来，方便我复制
- 若失败：先问我状态码/提示原文，再按下面排查，仍不要改代码

【失败时优先排查（按序）】
1. 地址是不是少了 /mcp，或写成了 /mcp/rpc → 改回 …/mcp
2. Authorization 是不是没带 Bearer+空格 → 整段重贴：Bearer laohu-demo-2026
3. 浏览器打开 https://209c3f5f.r19.vip.cpolar.cn/mcp-auth?code=probe
   - 应看到「金蝶账号授权」表单
   - 若看到「车间管理系统」或带 vite 字样的空白壳子 → 隧道打错到前端了，先停下，让我去电脑上重开 cpolar 指 8080，不要继续填方舟
4. 本机后端是否还在跑、cpolar 窗口是否还开着（免费域名会变；变了地址整串要换）

【禁止你做的事】
- 不要让我新建 /mcp/rpc 或改 Program.cs
- 不要让我把 laohu-demo-2026 插入数据库
- 不要一次甩给我超过一步的操作
- 不要用一堆英文缩写吓我；必须用缩写时用括号补一句人话

现在从第 1 步开始：告诉我打开哪个网址，登录后点哪里。
```

---

## 给人看的速查（豆包卡住时你自己对照）

| 项 | 填什么 |
|---|---|
| 控制台 | [https://console.volcengine.com/ark](https://console.volcengine.com/ark) |
| MCP URL | `https://209c3f5f.r19.vip.cpolar.cn/mcp` |
| Header | `Authorization` = `Bearer laohu-demo-2026` |
| 传输 | Streamable HTTP |
| 合格 | 测试连接列出上述 5 个工具 |

隧道域名若变了：以 `backend/appsettings.json` 里 `Mcp:AuthBaseUrl` 为准，后面只加 `/mcp`。更细的点选说明见 `docs/37`；总待办见 `docs/35` §四。
