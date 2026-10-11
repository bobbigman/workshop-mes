using System.Reflection;
using System.Text.Json;
using ahu.MicrosoftMes.Mcp;
using Xunit;

namespace WorkshopMes.Tests;

public class McpKeyConnectionTests
{
    [Theory]
    [InlineData("HY伞业 (HY001)")]
    [InlineData("工厂\"甲\"\\分厂\n车间 (TEST)")]
    public void ConnectionJson_PreservesAccountUrlAndBearer(string factoryLabel)
    {
        const string url = "https://example.invalid:8080/mcp?name=\"甲\"&path=\\test";
        const string token = "test-only-token";
        var method = typeof(McpKeyService).GetMethod("BuildConnectionText", BindingFlags.NonPublic | BindingFlags.Static)!;
        var text = Assert.IsType<string>(method.Invoke(null, new object[] { factoryLabel, url, token }));

        using var json = JsonDocument.Parse(text);
        var servers = json.RootElement.GetProperty("mcpServers");
        Assert.Single(servers.EnumerateObject());
        var server = servers.GetProperty(factoryLabel);
        Assert.Equal("streamable-http", server.GetProperty("type").GetString());
        Assert.Equal(url, server.GetProperty("url").GetString());
        Assert.Equal($"Bearer {token}", server.GetProperty("headers").GetProperty("Authorization").GetString());
    }
}
