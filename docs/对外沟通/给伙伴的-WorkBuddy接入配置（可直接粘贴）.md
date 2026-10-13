# 给伙伴：贴进 WorkBuddy 的配置（可直接粘贴）

> 日期：2026-09-28｜用途：伙伴自己动手接，不用开发。
> 配置值**全部来自真实文件**，不是示意：
> - `backend/appsettings.json`（开发/演示实例，用户指定）
> - `deploy/instance/.secrets/appsettings.8080.json` / `appsettings.8081.json`（给客户的实例）
> - `backend/Mcp/McpTokenMiddleware.cs`、`backend/Common/InstanceMiddleware.cs`（鉴权与开关的真实行为）
> - `backend/Mcp/*.cs`（工具名逐个来自方法名）

---

## 0. 三个真实值（照抄，别改）

| 项 | 真实值 | 出处 |
|---|---|---|
| 鉴权密钥 | `laohu-demo-2026` | `backend/appsettings.json` → `Mcp:Token` |
| 公网地址（异地用） | `https://39d6fafc.r16.cpolar.top` | 同上 → `Mcp:AuthBaseUrl` |
| 内网地址（现场用） | `http://<装 MES 那台机器的IP>:8080` | 同上 → `Instance:PublicBaseUrl` = `http://localhost:8080` |
| MCP 开关 | **已开**（`Instance:EnableMcp` = `true`） | 同上 |

**MCP 地址 = 上面地址 + `/mcp`**（末尾这个 `/mcp` 不能少，路由就在这：`app.MapMcp("/mcp")`）。

> ⚠️ 实测现状（2026-09-28 10:5x）：**本机 8080 当前没有监听，cpolar 隧道探测返回 404**——就是服务和隧道都没在跑。所以下面配置粘上去灯不绿是正常的，先把后端和隧道起起来（`docs/37` 有步骤）。我不是拿"应该能通"糊弄你。

---

## 一、粘这一段（异地 / 演示用，公网隧道）

WorkBuddy → 侧边栏「插件」→ 右上角「MCP 服务器」→「配置 MCP」，整段粘：

```json
{
  "mcpServers": {
    "workshop-mes": {
      "type": "streamable-http",
      "url": "https://mes.webok.net:11302/mcp",
      "headers": {
        "Authorization": "Bearer laohu-demo-2026"
      }
    }
  }
}
```

## 二、粘这一段（现场 / 同局域网，直连 MES）

```json
{
  "mcpServers": {
    "workshop-mes": {
      "type": "streamable-http",
      "url": "https://mes.webok.net:11302/mcp",
      "headers": {
        "Authorization": "Bearer laohu-demo-2026"
      }
    }
  }
}
```

**只需改一处**：`192.168.x.x` 换成装了 MES 那台机器的局域网 IP（用浏览器打开 `http://那个IP:8080/` 能出登录页就对）。密钥不用改，就是 `laohu-demo-2026`。

**如果 WorkBuddy 和 MES 装在同一台机器**，直接用 `http://localhost:8080/mcp`。

**几个死规矩：**
- `Bearer` 和密钥之间**有一个空格**，别删；用 Header 传，**不要把 token 拼在网址里**（新版扣子就是这么失败过，见 `docs/40` 勘误表）。
- 隧道域名**会变**。免费 cpolar 每次重启换域名，历史就是 `209c3f5f.r19.vip.cpolar.cn` → `789105f9.r16.cpolar.top` → 现在的 `39d6fafc.r16.cpolar.top`。**域名以后以服务器上 `Mcp:AuthBaseUrl` 为准**，变了整串换掉，并**删掉旧的 MCP 重建成新的**。

---

## 三、存完怎么确认通了

1. 保存后看状态灯：**绿 = 通**，红 = 看第五节。
2. 点开这条连接器，应能列出 **8 个工具**（名字来自代码里的方法名，逐个核过）：

   | 工具名 | 干什么 |
   |---|---|
   | `start_auth` | 发起授权，给一个 H5 链接，登完拿授权码 |
   | `query_work_order` | 查工单列表（关键词/状态） |
   | `query_work_order_progress` | 查单张进度（做到哪了 / 完成% / 距交期） |
   | `query_report` | 查报工记录 |
   | `query_salary` | 查工资单（**财务敏感，仅财务账号**） |
   | `calc_schedule_priority` | 算排产优先级 |
   | `query_production_guide` | 查某产品怎么生产（工艺路线 + 工艺文件） |
   | `search_support_docs` | 查操作手册 / 常见问题 |

   列不出来 → **删掉这条重新新建**（失败状态会被缓存，光改不容易重新握手）。
3. 界面上出现「信任」就点一下启用（不点相当于没接）。
4. 直接在对话里说人话试：「查一下这个厂的工单」「GDxxx 做到哪了」「哪些工单超期了」「扫码报工怎么操作」。

**怎么算真通了**：它回的是你系统里**真实存在的工单号、真实数量**。开始编 = 没连上。

**授权码这件事要跟伙伴讲清楚**（`Mcp:DemoSkipAuth` = `true` 的实际效果）：
- 演示实例下，**查工单、查进度、查报工、查生产指导、查手册：不用授权码**，直接问。
- **查工资、查排产：仍要先 `start_auth` 拿授权码**（工具说明里写死"需先金蝶授权"），这是权限设计，不是故障。

---

## 四、配置放在哪一级

| 级别 | 文件 | 什么时候用 |
|---|---|---|
| 用户级 | `~/.workbuddy/mcp.json` | 只有一家客户、你自己常用 |
| 项目级 | `<项目目录>/.workbuddy/mcp.json` | 管多家客户 → **每家单独一个项目文件夹**，各配各的，别串号 |

---

## 五、红灯排错（按顺序查）

| 现象 | 多半是 | 怎么办 |
|---|---|---|
| 状态红、工具列不出 | 地址或端口不对 / 服务没起 | 浏览器开 `http://地址:8080/`，出登录页才算通 |
| `/mcp` 返回 **404 且提示「MCP 未启用」** | 那套实例的 `Instance:EnableMcp` 是 `false` | 见第六节第 1 条 |
| 抛出「**MCP 未配置静态 token**」 | `Mcp:Token` 是空的 | 见第六节第 1 条 |
| 返回 **401** | 密钥不匹配 / Bearer 后少空格 | 拿服务器 `Mcp:Token` 逐字对 |
| 能开网页但 `/mcp` 404 | 地址打到前端端口了 | 换 MES 后端地址；隧道域名以 `Mcp:AuthBaseUrl` 为准 |
| 人在外地连不上 | 服务器在内网 | 厂内同网用；或开公网隧道 |
| 昨天还好今天不行 | 免费 cpolar 域名变了 | 换新域名，**删旧 MCP 重建** |

> 这些不是猜的：404「MCP 未启用」来自 `McpGateMiddleware`，抛 token 异常来自 `McpTokenMiddleware`，两处都逐行看过。

---

## 六、给客户的实例：默认真的连不上，要多做两步

**真实证据**——`deploy/instance/.secrets/appsettings.8080.json`（甲）和 `appsettings.8081.json`（乙）里都是：

```json
"Instance": { "EnableMcp": false, ... },
"Mcp": { "Token": "", "FactoryId": 0, "DemoSkipAuth": false }
```

也就是说，**客户实例照抄上面的配置粘过去，一定连不上**——一个报 404「MCP 未启用」，一个抛「未配置静态 token」。要开必须手工两步：

1. `Instance:EnableMcp` 改 `true`；
2. `Mcp:Token` 填一把**只属于这家**的密钥，`Mcp:FactoryId` 填对应该实例的工厂 ID（现在配置里是 `0`，要填真实值），重启服务。

⚠️ 改的是服务器上的 `appsettings.json`，**增量包永远不会覆盖它，必须手工改**（`appsettings.json` 里多处 `_comment` 都写着"增量包勿覆盖"）。
⚠️ **一客一钥**，别多家共用一把 token、更别共用 `Jwt:Secret`（`远程8080-8081.txt` 和 `开通与运维清单.md` 都写死了这条）。

**要不要开授权码**：演示实例可以 `DemoSkipAuth=true` 图省事；正式给客户的实例建议 `false`——先在对话里说「发起授权」，登一次拿码，再查。卖点说法：**谁查的、查了什么都认人，不是一把钥匙人人用。**

---

## 七、现在能问什么、不能问什么（别承诺错）

**能问**：工单和进度、报工记录、工资单（仅财务账号）、排产评分、产品工艺资料、系统操作怎么用。

**还不能问**：金蝶的料价、成本、BOM。金蝶那个查询口子目前只开给了系统里的「建议报价」页面，**没开给 AI**（`backend/Services/Kingdee/` 那套客户端是给业务服务用的，不是 MCP 工具）。
想要"在 WorkBuddy 里一句话把两边数据摆一起看"，要另补一个金蝶 MCP 工具，工作量不大——**伙伴先别对客户承诺**。
