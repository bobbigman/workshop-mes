namespace ahu.MicrosoftMes.Mcp;

/// <summary>当前 MCP 请求的厂上下文（由 McpTokenMiddleware 按钥匙写入）。</summary>
public interface IMcpRequestContext
{
    long? CurrentFactoryId { get; set; }
    string? KeyAlias { get; set; }
    string? FactoryName { get; set; }

    /// <summary>取当前厂；未定厂则抛业务错，禁止用 0/1 兜底。</summary>
    long RequireFactoryId(string where);
}

public class McpRequestContext : IMcpRequestContext
{
    public long? CurrentFactoryId { get; set; }
    public string? KeyAlias { get; set; }
    public string? FactoryName { get; set; }

    public long RequireFactoryId(string where)
    {
        if (CurrentFactoryId is null or <= 0)
            throw Common.ThrowHelper.Biz(where, "MCP 缺少租户上下文，禁止查询");
        return CurrentFactoryId.Value;
    }
}
