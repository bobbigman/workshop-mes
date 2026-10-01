# AI 助手 API Key 部署配置说明（新服务器照此配置）

> 用途：部署新服务器 / 新账套时，给小蜜蜂报工系统的「PC 内置 AI 助手」接上大模型（默认 DeepSeek）。
> 适用版本：后端 .NET 8 Web API。依据代码 
>
> `backend/Common/AiOptions.cs`
>
> 、
>
> `backend/appsettings.json`
>
> 、开发指令 
>
> `docs/26`
>
> 。
> 一句话：
>
> **密钥只配在服务器端，前台页面没有填密钥的地方，只有状态展示和连接测试。**



***

## 1. 密钥配在哪里

密钥位于后端配置段 `Ai`（对应文件 `backend/appsettings.json`）。关键字段：



| 字段        | 含义           | 本仓库默认值                     | 部署时怎么定                                   |
| --------- | ------------ | -------------------------- | ---------------------------------------- |
| `Enabled` | 总开关          | `true`                     | 保持开；关了页面显示「已关闭」                          |
| `BaseUrl` | 大模型接口地址      | `https://api.deepseek.com` | 默认官方；走火山方舟等改这里                           |
| `ApiKey`  | **你的密钥（重点）** | 空                          | **服务器环境变量&#x20;**`Ai__ApiKey`**&#x20;填** |
| `Model`   | 模型名          | `deepseek-flash`           | 部署时填验证过的真实可用型号                           |

> 铁律（来自 
>
> `docs/26`
>
>  与 AGENTS.md）：密钥
>
> **只走服务器配置**
>
> ，页面只显示「已配置 / 未配置 / 模型」状态和连接测试，
>
> **不做密钥编辑表单**
>
> ；密钥
>
> **禁止写日志、禁止返回前端**
>
> （有 
>
> `LogRedactor`
>
>  兜底）。



***

## 2. 两种配置方式

### 方式 A：环境变量（生产推荐，不碰服务器 appsettings）

.NET 会自动把环境变量 `Ai__ApiKey` 映射到配置段 `Ai:ApiKey`。同理可配 `Ai__BaseUrl`、`Ai__Model`。

Windows 服务器（管理员 PowerShell）永久设置：



```
setx Ai\_\_ApiKey "sk-你的DeepSeekKey"

setx Ai\_\_Model "deepseek-chat"      # 按部署时验证过的模型名填
```

> 注意：
>
> `setx`
>
>  只影响
>
> **之后新启动**
>
> 的进程。
>
> **必须重启后端进程 / 服务**
>
> （Windows 服务则重启服务；用 
>
> `start.bat`
>
>  则关掉重开）才生效。

### 方式 B：改配置文件（仅本地调试，勿用于上线）

编辑 `backend/appsettings.json`，在 `Ai` 节里填：



```
"Ai": {

&#x20; "Enabled": true,

&#x20; "BaseUrl": "https://api.deepseek.com",

&#x20; "ApiKey": "sk-你的DeepSeekKey",

&#x20; "Model": "deepseek-chat"

}
```

> 警告：仓库里 ApiKey 保持为空、示例配置别提交真实密钥。上线服务器
>
> **只用增量包，绝不覆盖服务器上的&#x20;**
>
> `appsettings*.json`
>
> **&#x20;/&#x20;**
>
> `start.bat`
>
> （见 AGENTS.md 部署铁律、
>
> `docs/59`
>
> ）。



***

## 3. 密钥从哪里来



* DeepSeek 官方：登录 DeepSeek 开放平台（[platform.deepseek.com](https://platform.deepseek.com)）→ 创建 API Key。

* 若公司走火山方舟 / 其他兼容 OpenAI 协议的服务：改 `BaseUrl` + `Model`，密钥同样走环境变量。



***

## 4. 配好之后怎么验证（三步）



1. **重启后端**：让新环境变量生效（方式 A 必须重启）。

2. **进前台**：登录 →「AI 助手」页。顶部状态应显示「已配置 / 模型已配置」（原来是「未配置」）。

3. **点连接测试**：按钮能通、返回连通，说明密钥可用。

> 若前台仍显示「未配置完成（缺密钥或模型）」，就是环境变量没生效或模型名填错 —— 先确认是否重启了后端，再核对 
>
> `Ai__Model`
>
>  是否为真实可用型号。



***

## 5. 排障速查



| 现象        | 原因                        | 处理                                      |
| --------- | ------------------------- | --------------------------------------- |
| 页面显示「未配置」 | 缺密钥或模型                    | 确认 `Ai__ApiKey` / `Ai__Model` 环境变量已设并重启 |
| 连接测试失败    | Key 无效 / BaseUrl 错 / 网络不通 | 核对 Key、BaseUrl；内网服务器确认能访问外网             |
| 页面显示「已关闭」 | `Ai.Enabled=false`        | 服务器配置改回 `true` 并重启                      |