using System.Reflection;
using System.Text.Json;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Mcp;
using ahu.MicrosoftMes.Models;
using ahu.MicrosoftMes.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace WorkshopMes.Tests;

public class McpDataAccessTests
{
    public sealed class Bind : IMcpWxBindService
    {
        public SysUser? User = new() { Id = 17, FactoryId = 8, Role = 2, Status = 1, Name = "员工\"甲" };
        public Task<SysUser?> ResolveBoundUserAsync(string? c, string? s, CancellationToken ct = default) => Task.FromResult(User);
        public Task<string?> RequireBoundOrHintAsync(string? c, string? s, string t, CancellationToken ct = default) => throw new InvalidOperationException("旧权限路径");
        public Task<string> BindAsync(string p, string? c, string? s, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> UnbindAsync(string? c, string? s, CancellationToken ct = default) => throw new NotSupportedException();
    }
    public class ServiceProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Call = (_, _) => throw new InvalidOperationException("不应调用");
        protected override object? Invoke(MethodInfo? m, object?[]? a) => Call(m!, a!);
    }
    private static T Service<T>(Func<MethodInfo, object?[], object?> call) where T : class
    {
        var s = DispatchProxy.Create<T, ServiceProxy>(); ((ServiceProxy)(object)s).Call = call; return s;
    }
    public static IConfiguration Config(bool demo = false) => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["Mcp:DemoSkipAuth"] = demo.ToString(), ["Mcp:FinanceKingdeeAccounts"] = "worker" }).Build();
    private static McpRequestContext Context() => new() { CurrentFactoryId = 8 };

    [Theory]
    [InlineData(1, false, null)]
    [InlineData(2, false, 17L)]
    [InlineData(3, false, 17L)]
    [InlineData(2, true, null)]
    public async Task Salary_UsesBoundScope_NotFinanceToken(byte role, bool demo, long? expected)
    {
        var bind = new Bind(); bind.User!.Role = role; bind.User.Account = "worker";
        var called = false;
        var svc = Service<ISalaryService>((m,a) => {
            Assert.Equal("QueryAsync", m.Name); var q = Assert.IsType<SalaryQueryDto>(a[0]);
            Assert.Equal(expected,q.UserId); Assert.Equal("2026-10",q.PeriodValue); Assert.Equal(8L,a[1]); called=true;
            return Task.FromResult(ApiResult<PageResult<SalaryListDto>>.Ok(new() { Total=0, List=new() }));
        });
        var tool = new SalaryMcpTools(svc, null!, Context(), bind, Config(demo));
        using var json = JsonDocument.Parse(await tool.QuerySalary(periodValue:"2026-10"));
        Assert.True(called); Assert.Equal(0,json.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Report_PreservesFilters_AndHidesSensitiveFields()
    {
        var svc = Service<IReportService>((m,a) => {
            Assert.Equal(17L,a[2]); var q = Assert.IsType<ReportQueryDto>(a[0]);
            Assert.Equal("P1",q.ProductCode); Assert.Equal("产品",q.ProductName); Assert.Equal((byte)1,q.ReviewStatus);
            return Task.FromResult(ApiResult<PageResult<ReportListDto>>.Ok(new() { Total=1, List=new() { new() { Id=9, UnitPrice=999, WageAmount=999, Ext=new() { [1]="秘密" } } } }));
        });
        var tool = new ReportMcpTools(svc,null!,Context(),new Bind(),Config());
        var json = await tool.QueryReport(productCode:"P1",productName:"产品",reviewStatus:1);
        Assert.DoesNotContain("UnitPrice",json); Assert.DoesNotContain("WageAmount",json); Assert.DoesNotContain("秘密",json);
    }

    [Fact]
    public async Task WorkOrder_EmployeeHidesCustomFields_DemoPreservesThem()
    {
        var svc = Service<IWorkOrderService>((_,a) => {
            Assert.Equal(8L,a[1]);
            return Task.FromResult(ApiResult<PageResult<WorkOrderListDto>>.Ok(new()
            { Total=1, List=new() { new() { OrderNo="WO1", Ext=new() { [1]="客户秘密" } } } }));
        });
        var worker = new WorkOrderMcpTools(svc,null!,Context(),new Bind(),Config());
        var demo = new WorkOrderMcpTools(svc,null!,Context(),new Bind(),Config(true));
        using var w = JsonDocument.Parse(await worker.QueryWorkOrder());
        using var d = JsonDocument.Parse(await demo.QueryWorkOrder());
        Assert.False(w.RootElement.GetProperty("list")[0].TryGetProperty("ext",out _));
        Assert.Equal("客户秘密",d.RootElement.GetProperty("list")[0].GetProperty("ext").GetProperty("1").GetString());
    }

    [Fact]
    public async Task Schedule_WorkerDenied_BeforeQuery()
    {
        var svc = Service<IScheduleScoreService>((_,_) => throw new InvalidOperationException("泄露排产"));
        var tool = new ScheduleMcpTools(svc,null!,Context(),new Bind(),Config());
        using var json = JsonDocument.Parse(await tool.CalcSchedulePriority());
        Assert.Equal(1,json.RootElement.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Salary_UnboundAndUnknownRole_DoNotQuery()
    {
        var svc = Service<ISalaryService>((_,_) => throw new InvalidOperationException("不应查询工资"));
        var bind = new Bind();
        var tool = new SalaryMcpTools(svc,null!,Context(),bind,Config());
        bind.User=null;
        using var unbound = JsonDocument.Parse(await tool.QuerySalary(authToken:"财务授权不能替代绑定"));
        Assert.Equal(1,unbound.RootElement.GetProperty("code").GetInt32());
        bind.User=new() { Id=17,Role=9,Status=1,Name="未知角色" };
        using var unknown = JsonDocument.Parse(await tool.QuerySalary());
        Assert.Equal(1,unknown.RootElement.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task BindingAndRoleChanges_AreReadEachRequest()
    {
        var b = new Bind();
        Assert.Null((await McpDataAccess.ResolveAsync(b,Config(),null,null)).Hint);
        b.User!.Role=9; Assert.NotNull((await McpDataAccess.ResolveAsync(b,Config(),null,null)).Hint);
        b.User.Role=1; Assert.Null((await McpDataAccess.ResolveAsync(b,Config(),null,null,true)).Hint);
        b.User.Status=0; Assert.NotNull((await McpDataAccess.ResolveAsync(b,Config(),null,null)).Hint);
        b.User=null; Assert.NotNull((await McpDataAccess.ResolveAsync(b,Config(),null,null)).Hint);
        Assert.Null((await McpDataAccess.ResolveAsync(b,Config(true),null,null)).Hint);
    }
}
