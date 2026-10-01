using System.Text.Json;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Mcp;
using ahu.MicrosoftMes.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace WorkshopMes.Tests;

/// <summary>docs/54：MCP SearchSupportDocs 包装层验收（复用 SupportDocumentService，不测检索算法本身）。</summary>
public class SupportDocsMcpToolsTests
{
    private sealed class FakeAuth : IMcpAuthService
    {
        public bool RequireCalled { get; private set; }
        public bool ShouldFail { get; set; }

        public Task<string> CreateCodeAsync(long factoryId) => Task.FromResult("x");
        public Task PeekCodeAsync(string code, long factoryId) => Task.CompletedTask;
        public Task<long> ResolveFactoryIdByCodeAsync(string code) => Task.FromResult(1L);
        public Task<McpAuthInfo?> ExchangeAsync(string code, KingdeeUserInfo user, long factoryId) => Task.FromResult<McpAuthInfo?>(null);
        public Task<McpAuthInfo?> ResolveAsync(string? authToken) => Task.FromResult<McpAuthInfo?>(null);

        public Task<McpAuthInfo> RequireAuthAsync(string? authToken, string toolName)
        {
            RequireCalled = true;
            if (ShouldFail || string.IsNullOrWhiteSpace(authToken))
                throw ThrowHelper.Biz(toolName, "未授权");
            return Task.FromResult(new McpAuthInfo { AuthToken = authToken! });
        }
    }

    private static string FindRepoSupportDocs()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null)
        {
            var c = Path.Combine(d.FullName, "SupportDocs");
            if (Directory.Exists(c)) return c;
            d = d.Parent;
        }
        throw new InvalidOperationException("未找到 SupportDocs 目录");
    }

    private static SupportDocsMcpTools Build(bool demoSkipAuth, SupportDocsOptions supportOpt, FakeAuth? auth = null)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mcp:DemoSkipAuth"] = demoSkipAuth ? "true" : "false"
            })
            .Build();
        var svc = new SupportDocumentService(
            Options.Create(new AiOptions { SupportDocs = supportOpt }),
            NullLogger<SupportDocumentService>.Instance);
        return new SupportDocsMcpTools(svc, auth ?? new FakeAuth(), cfg);
    }

    [Fact]
    public async Task EmptyQuery_throwsBiz()
    {
        var tools = Build(true, new SupportDocsOptions { Enabled = true, RootPath = FindRepoSupportDocs() });
        var ex = await Assert.ThrowsAsync<BusinessException>(() => tools.SearchSupportDocs("  "));
        Assert.Contains("query 必填", ex.Message);
    }

    [Fact]
    public async Task QueryOver500_throwsBiz()
    {
        var tools = Build(true, new SupportDocsOptions { Enabled = true, RootPath = FindRepoSupportDocs() });
        var ex = await Assert.ThrowsAsync<BusinessException>(() => tools.SearchSupportDocs(new string('扫', 501)));
        Assert.Contains("最多 500", ex.Message);
    }

    [Fact]
    public async Task Disabled_returnsState_noPathLeak()
    {
        var tools = Build(true, new SupportDocsOptions { Enabled = false, RootPath = FindRepoSupportDocs() });
        var json = await tools.SearchSupportDocs("扫码报工怎么开始");
        Assert.DoesNotContain("SupportDocs", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(":\\", json);
        Assert.DoesNotContain("RootPath", json, StringComparison.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("disabled", doc.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public async Task DemoSkipAuth_searchHits_noPathLeak()
    {
        var tools = Build(true, new SupportDocsOptions { Enabled = true, RootPath = FindRepoSupportDocs() });
        var json = await tools.SearchSupportDocs("扫码报工怎么开始");
        Assert.DoesNotContain(FindRepoSupportDocs().Replace("\\", "\\\\"), json);
        Assert.DoesNotContain(":\\", json);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ready", doc.RootElement.GetProperty("state").GetString());
        Assert.True(doc.RootElement.GetProperty("sources").GetArrayLength() > 0);
    }

    [Fact]
    public async Task DemoSkipAuth_false_withoutToken_rejects()
    {
        var auth = new FakeAuth { ShouldFail = false };
        var tools = Build(false, new SupportDocsOptions { Enabled = true, RootPath = FindRepoSupportDocs() }, auth);
        await Assert.ThrowsAsync<BusinessException>(() => tools.SearchSupportDocs("怎么补报", null));
        Assert.True(auth.RequireCalled);
    }

    [Fact]
    public async Task DemoSkipAuth_false_withToken_ok()
    {
        var auth = new FakeAuth();
        var tools = Build(false, new SupportDocsOptions { Enabled = true, RootPath = FindRepoSupportDocs() }, auth);
        var json = await tools.SearchSupportDocs("怎么补报", "tok");
        Assert.True(auth.RequireCalled);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ready", doc.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Nonsense_noMatch()
    {
        var tools = Build(true, new SupportDocsOptions { Enabled = true, RootPath = FindRepoSupportDocs() });
        var json = await tools.SearchSupportDocs("zzzxqwlkj_no_such_topic_999");
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("no_match", doc.RootElement.GetProperty("state").GetString());
        Assert.Equal(0, doc.RootElement.GetProperty("sources").GetArrayLength());
    }

    [Fact]
    public async Task 无报工权限_hits()
    {
        var tools = Build(true, new SupportDocsOptions { Enabled = true, RootPath = FindRepoSupportDocs() });
        var json = await tools.SearchSupportDocs("无报工权限");
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ready", doc.RootElement.GetProperty("state").GetString());
        var titles = doc.RootElement.GetProperty("sources").EnumerateArray()
            .Select(e => e.GetProperty("title").GetString()).ToList();
        Assert.Contains(titles, t => t is "常见问题" or "权限说明" or "基础资料");
    }
}
