# 263-优化指令：修复DeepSeek深度思考多轮对话HTTP 400（reasoning_content未回传）

> 状态：待做
> 性质：正式生产（改 AI 模型调用契约）
> 本次交付：代码与必要验证 + 增量包
> 目标：让 DeepSeek 深度思考（thinking）**多轮对话不再返回 HTTP 400**（`reasoning_content` 必须原样回传），AI 助手连续提问能正常出答案
> 依据：服务器日志（`D:\WorkShop\backend\publish-win64\logs`）报 `HTTP 400: The reasoning_content in the thinking mode must be passed back to the API`；黑帽大人 2026-10-21 部署后 AI 助手报"模型调用失败，未能生成解释；AI 服务暂时不可用"。

## 问题根因（已定位，非配置）
- `DeepSeekChatMessage`（`backend/Services/DeepSeekClient.cs` 行 31-47）**没有 `reasoning_content` 字段**。
- DeepSeek 深度思考模型返回的 assistant 消息含 `reasoning_content`，官方要求**多轮时原样回传**；当前代码解析响应时丢弃、会话不存、`ToApiMessages` 组装不回传 → 第二轮起 400。

## 取舍（已定）
| 项 | 方案及依据 | 状态 |
|---|---|---|
| 修法 | 在消息模型/结果/会话/组装四条链补 `reasoning_content`，随 assistant 消息原样回传 | 已定（DeepSeek 官方要求，无替代） |
| 会话存储 | `AiSessionMessage` 加 `ReasoningContent` 字段；**先核实**该对象是内存会话还是持久化实体——内存则加字段即可，持久化则需加列并同步 `docs/02` | 待核实 |

## 本次范围与落点
动 4 处：
1. `DeepSeekChatMessage` 加 `ReasoningContent`（`[JsonPropertyName("reasoning_content")]`）。
2. `DeepSeekChatResult` 加 `ReasoningContent`。
3. `DeepSeekClient.ChatInternalAsync` 响应解析（约行 224-244）：读 `reasoning_content`（值为字符串时）存入 `DeepSeekChatResult.ReasoningContent`。
4. `AiAssistantService`：
   - `AiSessionMessage` 加 `ReasoningContent`；
   - 存 assistant 消息处（约行 186、244-249）：带 `modelResult.ReasoningContent`（tool 轮 assistant 同样带）；
   - `ToApiMessages` 组装时，assistant 消息带 `ReasoningContent` 回传。

不改知识库、不改前端、不改 model/BaseUrl/tools。

## 行为与口径（分叉）
- 当多轮对话继续 → 上一轮 assistant 的 `reasoning_content` 出现在本轮请求 messages → DeepSeek 不再 400，正常出答案。
- 当 `reasoning_content` 为空/无 → 按现逻辑不传该字段（`DefaultIgnoreCondition=WhenWritingNull` 已生效），行为不变。
- 修复后新会话与既有会话继续对话都应正常（消息动态组装，重启即生效，无需清库）。

## 止步线
- 不改模型名、不改 BaseUrl、不改 tools、不动知识库；不为此加开关/配置键。
- 不扩改其它调用链（连接测试、其它模式）的 `reasoning_content` 之外逻辑。

## 验收
- [ ] `ToApiMessages` 输出里，历史 assistant 消息带 `reasoning_content`（值为字符串时）。
- [ ] 连真实 DeepSeek key 做**连续多轮对话**（≥2 轮 + 至少 1 轮含工具调用），不再出现 `HTTP 400 ... reasoning_content` 报错。
- [ ] 连接测试仍通过；单轮首问正常。
- [ ] 无数据库表结构变更；若 `AiSessionMessage` 是持久化实体而加了列，则 `docs/02-数据字典.md` 已同步。
- [ ] `dotnet build` 通过。

## 交付终点
- 代码完成 + 上述验证通过 → 走增量包（`publish-update`，不打 zip）→ 服务器 `apply-update.bat` 部署 → 复测多轮对话正常。
