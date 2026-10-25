using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Mcp;
using ahu.MicrosoftMes.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using Xunit;

namespace WorkshopMes.Tests;

public class MaintenanceSqlTests
{
    private const string Key = "test-only-maintenance-key-32-characters";
    private static IConfiguration Config(bool enabled, string? key = Key, bool demo = false) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mcp:Maintenance:Enabled"] = enabled.ToString(), ["Mcp:Maintenance:Key"] = key,
            ["Mcp:DemoSkipAuth"] = demo.ToString()
        }).Build();

    [Theory]
    [InlineData(false, Key, Key, false)]
    [InlineData(false, Key, Key, true)]
    [InlineData(true, null, Key, false)]
    [InlineData(true, "short-key", "short-key", false)]
    [InlineData(true, Key, null, false)]
    [InlineData(true, Key, "wrong-key", true)]
    public void Permission_DeniesDisabledMissingWeakOrWrongKeys(bool enabled, string? configured, string? presented, bool demo)
        => Assert.Throws<BusinessException>(() => MaintenanceSqlMcpTools.RequireMaintenance(Config(enabled, configured, demo), presented));

    [Fact]
    public void Permission_SeparateKeyAllowsMaintenance() => MaintenanceSqlMcpTools.RequireMaintenance(Config(true), Key);

    [Fact]
    public async Task Tool_DemoAndFactoryContextCannotReplaceMaintenancePermission()
    {
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var tools = new MaintenanceSqlMcpTools(null!, Config(true, demo: true), http,
            new McpRequestContext { CurrentFactoryId = 4, KeyAlias = "config-master" });
        var result = JsonDocument.Parse(await tools.ExecuteMaintenanceSql(new string('A', 64)));
        Assert.Equal(1, result.RootElement.GetProperty("code").GetInt32());
        Assert.Contains("维护权限", result.RootElement.GetProperty("msg").GetString());
    }

    [Fact]
    public void MaintenanceHeader_IsRedacted()
    {
        var result = JsonSerializer.Serialize(LogRedactor.RedactHeaders(new Dictionary<string, IEnumerable<string>>
            { ["X-Maintenance-Key"] = new[] { Key } }));
        Assert.DoesNotContain(Key, result);
        Assert.Contains("***", result);
    }

    [Fact]
    public void Plan_BindsExactSqlAndScope_AndIsSingleUse()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new MaintenanceSqlPlans(cache);
        const string sql = "DELETE FROM example WHERE id=17;";
        var plan = new MaintenanceSqlPlan(sql, MaintenanceSqlService.Hash(sql), "factory4:key:connectionA", "server", "database", DateTimeOffset.UtcNow.AddMinutes(5));
        var token = store.Add(plan);
        Assert.Throws<BusinessException>(() => store.Take(token, "factory5:key:connectionA"));
        Assert.Throws<BusinessException>(() => store.Take(token, "factory4:new-key:connectionA"));
        Assert.Throws<BusinessException>(() => store.Take(token, "factory4:key:connectionB"));
        Assert.Equal(plan, store.Take(token, plan.Scope));
        Assert.Throws<BusinessException>(() => store.Take(token, plan.Scope));
    }

    [Fact]
    public void Plan_ExpiredOrUnknownCannotExecute()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new MaintenanceSqlPlans(cache);
        var token = store.Add(new("SELECT 1", "hash", "scope", "s", "d", DateTimeOffset.UtcNow.AddSeconds(-1)));
        Assert.Throws<BusinessException>(() => store.Take(token, "scope"));
        Assert.Throws<BusinessException>(() => store.Take("unknown", "scope"));
    }

    [Fact]
    public async Task Plan_ConcurrentExecutionHasOneWinner()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new MaintenanceSqlPlans(cache);
        var token = store.Add(new("DELETE FROM example", "hash", "scope", "s", "d", DateTimeOffset.UtcNow.AddMinutes(5)));
        var success = 0;
        var rejected = 0;
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
        {
            try { store.Take(token, "scope"); Interlocked.Increment(ref success); }
            catch (BusinessException) { Interlocked.Increment(ref rejected); }
        })));
        Assert.Equal(1, success);
        Assert.Equal(19, rejected);
    }
}
