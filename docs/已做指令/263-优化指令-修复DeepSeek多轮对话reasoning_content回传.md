# 263-优化指令：修复DeepSeek深度思考多轮对话HTTP 400（reasoning_content未回传）

> 状态：已做（2026-10-21；真实 API 与服务器现场验收待做）
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
- AiSessionStore 为进程内缓存，无数据库变更；重启后旧会话失效，须新建对话。修复不能恢复此前已丢失的 reasoning_content。

## 审核补充
- 官方依据：https://api-docs.deepseek.com/guides/thinking_mode/ 。携带 tools 时须完整回传历史 assistant 的 reasoning_content。
- 客服预检索自行构造的 assistant/tool 消息没有模型思考内容，明确设 ReasoningContent = ""，保留工具关联；空字符串原样发送，只有 null 省略。
- 模型返回的思考内容仅存内存并回传模型，不放进前端 DTO、日志或摘要。
- 本次交付增量文件夹，不自动部署服务器；真实 API 与服务器验收未完成时明确标记。

## 止步线
- 不改模型名、不改 BaseUrl、不改 tools、不动知识库；不为此加开关/配置键。
- 不扩改其它调用链（连接测试、其它模式）的 `reasoning_content` 之外逻辑。

## 验收
- [x] `ToApiMessages` 输出里，历史 assistant 消息带 `reasoning_content`（值为字符串时）。
- [ ] 连真实 DeepSeek key 做**连续多轮对话**（≥2 轮 + 至少 1 轮含工具调用），不再出现 `HTTP 400 ... reasoning_content` 报错。
- [ ] 连接测试仍通过；单轮首问正常。
- [x] 无数据库表结构变更；若 `AiSessionMessage` 是持久化实体而加了列，则 `docs/02-数据字典.md` 已同步。
- [x] 后端 Debug/Release 编译、前端构建及增量发布通过。

## 交付终点
- 代码完成 + 上述验证通过 → 走增量包（`publish-update`，不打 zip）→ 服务器 `apply-update.bat` 部署 → 复测多轮对话正常。

## 本次交付记录
- 已补齐模型响应→会话→请求字段链；客服主动工具消息发送空字符串；不输出思考正文到前端。
- 增量目录：`backend/publish-update`，未打 zip，未部署；已通过脚本的配置/启动文件禁入检查。
- 标准 dotnet test 启动器因 Windows 访问父进程被拒而中止，不标记为通过。
- 真实 API 连续多轮/连接测试、服务器 apply-update 与现场复测尚未执行。
- 独立断言入口执行 15 项回归通过（12 项会话 Fact + 原文/空字符串/null 三项实际 HTTP 报文测试）；不等同标准测试启动器通过。
