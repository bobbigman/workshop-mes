namespace ahu.MicrosoftMes.Common;

/// <summary>
/// PC 内置 AI 助手配置（docs/26、docs/2B）。默认关闭；密钥仅服务器环境变量。
/// </summary>
public class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>总开关。false 或不配置时不影响主系统启动，页面显示已关闭。</summary>
    public bool Enabled { get; set; }

    /// <summary>DeepSeek API 基址。仅部署配置可改，不接受请求体传入。</summary>
    public string BaseUrl { get; set; } = "https://api.deepseek.com";

    /// <summary>
    /// API Key。仓库与示例配置必须为空；部署用环境变量 Ai__ApiKey。
    /// 禁止写入日志或返回前端。
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>部署时填写并连接测试；不默认猜测可用型号。</summary>
    public string Model { get; set; } = "";

    /// <summary>业务时区。Linux 用 IANA（如 Asia/Shanghai）；Windows 映射到对应时区 ID。</summary>
    public string BusinessTimeZone { get; set; } = "Asia/Shanghai";

    /// <summary>整轮超时（秒），含模型与工具循环。</summary>
    public int RequestTimeoutSeconds { get; set; } = 90;

    /// <summary>单轮对话最多模型往返次数（含最后一次强制收口）。默认 6。</summary>
    public int MaxToolRounds { get; set; } = 6;

    /// <summary>用户单次输入最大字符数。</summary>
    public int MaxInputChars { get; set; } = 2000;

    /// <summary>每轮送入模型的工艺资料正文预算（字符）。不是模型上下文保证。</summary>
    public int MaxKnowledgeChars { get; set; } = 20000;

    /// <summary>会话空闲过期（分钟）。</summary>
    public int SessionIdleMinutes { get; set; } = 30;

    /// <summary>单会话最多用户轮次；超限提示新对话。</summary>
    public int MaxTurnsPerSession { get; set; } = 10;

    /// <summary>同一用户+工厂最多并存会话数。</summary>
    public int MaxSessionsPerUser { get; set; } = 3;

    /// <summary>进程内总会话上限。</summary>
    public int MaxSessions { get; set; } = 100;

    /// <summary>智能客服知识目录配置（docs/25 §3）。默认关闭。</summary>
    public SupportDocsOptions SupportDocs { get; set; } = new();

    /// <summary>是否已配置可用密钥（不返回密钥本身）。</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>是否已填写模型名。</summary>
    public bool HasModel => !string.IsNullOrWhiteSpace(Model);
}
