# MCP Server 对接信息・车间管理系统（微聚）→ 扣子 Coze

> 2026-10-05・供把系统能力接入扣子（Coze）等支持 MCP 的 Agent 平台使用。
> 结论：系统已内置可用的 MCP Server（远程 Streamable HTTP），线上已开启，无需新写服务，
>
> **只差生成一把 Key 并在扣子里填地址**
>
> 。

## 一、MCP Server 基本信息



| 项        | 值                                                                   |
| -------- | ------------------------------------------------------------------- |
| 服务类型     | **远程 MCP 服务**（非本地 stdio）                                            |
| 传输协议     | **Streamable HTTP**（无状态 Stateless，ModelContextProtocol.AspNetCore）  |
| 服务地址 URL | `https://mes.webok.net:11302/mcp`                                   |
| 鉴权方式     | **Bearer Token（MCP Key）**，明文钥匙                                      |
| 请求头      | `Authorization: Bearer <MCP Key>`（也支持 `?token=` / `?access_token=`） |
| 厂隔离      | 一把 Key 绑定一个厂，所有工具只读写该厂数据（HY001 的 factoryId = **4**）                 |

## 二、如何获取 MCP Key（三选一，推荐 a）

### a. 系统管理员后台生成（推荐）

管理员登录 HY001 → 调接口建 Key（若有对应菜单则在界面上操作，没有则走接口）：



```
POST https://mes.webok.net:11302/api/McpKey
Header: Authorization: Bearer <管理员登录token>
Body:   { "factoryId": 4, "keyAlias": "coze-hy001", "expireAt": null, "remark": "扣子对接" }
返回：  { "data": { "keyId": 1, "keyAlias": "coze-hy001", "apiKey": "一串明文Key" } }
```

> `apiKey`
>
>  明文
>
> **只返回这一次**
>
> ，请立即复制；后台不存明文（存的是 BCrypt 哈希）。

### b. 服务器配置万能钥匙

部署端在 `appsettings*.json` 配 `Mcp:Token`（可多厂共用，配合 `Mcp:FactoryId`）。适合自己内部用，不适合给客户各厂单独发。

### c. WorkBuddy 专用钥匙

`Mcp:WorkBuddyToken`，只给 WorkBuddy 集成用，对接扣子用 a 即可。

## 三、可用工具清单（对接扣子后自动暴露；工资拆分见 docs/222）



| 工具名                         | 用途                        | 备注                  |
| --------------------------- | ------------------------- | ------------------- |
| `query_work_order`          | 查工单列表（按工单号 / 产品 / 状态筛选）   | 演示模式可空授权码           |
| `query_work_order_progress` | 查单张工单进度（做到哪 / 百分比 / 距交期）  | 演示模式可空授权码           |
| `query_report`              | 查报工记录（按工单 / 产品 / 复核状态）    | 演示模式可空授权码           |
| `query_production_guide`    | 查产品「如何生产」制造知识库（顺工艺路线读指导书） | 演示模式可空授权码           |
| `search_support_docs`       | 检索操作说明 / 常见问题 / 数据口径      | 演示模式可空授权码           |
| `calc_schedule_priority`    | 计算工单排产优先级 / 建议开工顺序        | 演示模式可空授权码           |
| `query_salary`              | 查已生成工资单；含 summary 本期扣款拆分  | 生产人员/班组长仅本人（148）   |
| `query_daily_piecework`     | 当日计件试算（只读不落库）             | 「今天工资」用；需绑定本人       |
| `get_work_order_payable`    | 按工单累计应付（docs/420/421）         | 仅管理员；口语计件=payableTotal |
| `get_completed_work_orders_payable` | 已完工工单应付台账与总合计     | 仅管理员；「完成工单要付多少」   |
| `get_report_link_by_identity` | 按手机号/账号/姓名发免密报工链接       | docs/215            |
| `start_auth`                | 发起金蝶账号授权（返回 H5 授权链接）      | 非演示需先手机号绑定          |
| `bind_wechat`               | 用手机号绑定当前会话到本厂账号           | 非演示模式调用前需绑定         |
| `unbind_wechat`             | 解绑会话与账号                   | —                   |

> 工具均为
>
> **只读查询**
>
> （查工单 / 进度 / 报工 / 工资 / 知识库 / 排产），系统不开放写操作给 MCP，安全可控。

## 四、授权模式（决定工具要不要授权码）



| 模式   | 开关                            | 效果                                                    |
| ---- | ----------------------------- | ----------------------------------------------------- |
| 演示模式 | 部署端 `Mcp:DemoSkipAuth = true` | 工具可空授权码直接查，适合售前演示                                     |
| 正式模式 | 默认（关闭 DemoSkipAuth）           | 需先 `bind_wechat` 绑定手机号 → `start_auth` 走金蝶授权，非管理员不可查工资 |

## 五、扣子（Coze）接入步骤



1. 扣子控制台 → **插件** → 新建 / 添加 → **MCP 服务**。

2. 选「**远程服务**」，服务地址填 `https://mes.webok.net:11302/mcp`。

3. 鉴权选 **API Key / Bearer**，粘贴第二步拿到的 MCP Key。

4. 保存并**连接测试**：应能拉取到上面 10 个工具清单（tools/list 返回正常）。

5. 回到智能体，添加该插件为工具，即可让 Agent 调用查工单 / 进度 / 报工 / 工资等。

6. 调用时让用户先问「今天有哪些工单」验证连通；涉及工资先走 `bind_wechat` + `start_auth`。

## 六、注意事项 / 前置条件



* 线上 `/mcp` 已开启（无 Key 访问返回 `{"code":401,"msg":"unauthorized"}`，说明端点活、鉴权生效）。

* 钥匙**按厂隔离**：HY001 的钥匙只能查 HY001 数据，别把别的厂钥匙混用。

* 钥匙可**吊销**：`POST /api/McpKey/{id}/revoke`，吊销后立即失效；建议为扣子单独建一把，出问题可单独吊销。

* **明文 Key 只显示一次**，丢了不能找回，只能重发一把。

* 若扣子报「不支持该协议」：本项目是 **Streamable HTTP**（非 SSE），扣子支持；个别老版本平台只认 SSE 时需在接入处确认。

* 数据口径：工价 / 排产中性分在系统内维护（非金蝶），MCP 查的是系统实时数据。

## 七、mcpServers JSON 示例（可直接粘贴到支持该格式的客户端 / 配置文件）



```
{
  "mcpServers": {
    "coze-hy001": {
      "type": "streamable_http",
      "url": "https://mes.webok.net:11302/mcp",
      "headers": {
        "Authorization": "Bearer REDACTED_MCP_TOKEN"
      }
    }
  }
}
```

> 说明：示例里的 Key（别名 
>
> `coze-hy001`
>
> 、绑定 HY001/factoryId=4）为 2026-10-05 已创建并验证有效的钥匙；明文仅此一次，丢失需重发并吊销旧钥匙。