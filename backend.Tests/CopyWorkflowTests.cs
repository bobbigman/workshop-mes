using System.Security.Claims;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;
using ahu.MicrosoftMes.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace WorkshopMes.Tests;

// SQLite only exercises service transactions and isolation; deployment remains SQL Server.
public class CopyWorkflowTests
{
    private sealed class FailProductInsert : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (e.Context!.ChangeTracker.Entries<BaseProduct>().Any(x => x.State == EntityState.Added && x.Entity.Code == "FAIL"))
                throw ThrowHelper.General(nameof(FailProductInsert), "模拟产品保存失败");
            if (e.Context.ChangeTracker.Entries<ProdWorkOrderOperation>().Any(x => x.State == EntityState.Added && x.Entity.PlanQty == 13))
                throw ThrowHelper.General(nameof(FailProductInsert), "模拟工单计划保存失败");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        public AppDbContext Db { get; private set; } = null!;
        public async Task InitializeAsync()
        {
            await _connection.OpenAsync();
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection)
                .AddInterceptors(new FailProductInsert()).Options);
            await Db.Database.EnsureCreatedAsync();
            Db.Operations.AddRange(new BaseOperation { Id = 1, FactoryId = 1, Code = "OP1", Name = "下料" },
                new BaseOperation { Id = 2, FactoryId = 1, Code = "OP2", Name = "检验" },
                new BaseOperation { Id = 3, FactoryId = 2, Code = "OTHER", Name = "外厂" });
            Db.Routings.Add(new BaseRouting { Id = 1, FactoryId = 1, Code = "R1", Name = "原路线" });
            Db.RoutingSteps.AddRange(new BaseRoutingStep { RoutingId = 1, OperationId = 1, Seq = 1 },
                new BaseRoutingStep { RoutingId = 1, OperationId = 2, Seq = 2 });
            Db.Products.Add(new BaseProduct { Id = 1, FactoryId = 1, Code = "P1", Name = "支架", RoutingId = 1, Price = 12 });
            Db.WorkOrders.Add(new ProdWorkOrder { Id = 1, FactoryId = 1, OrderNo = "OLD", ProductId = 1, Qty = 100, Status = 2, CreatedBy = 10 });
            Db.WorkOrderOperations.Add(new ProdWorkOrderOperation { WorkOrderId = 1, OperationId = 1, Seq = 1, PlanQty = 77, AssigneeUserId = 10 });
            Db.CustomFields.AddRange(new SysCustomField { Id = 1, FactoryId = 1, Target = "work_order", FieldName = "客户", FieldType = "text" },
                new SysCustomField { Id = 2, FactoryId = 1, Target = "product", FieldName = "产品属性", FieldType = "text" },
                new SysCustomField { Id = 3, FactoryId = 2, Target = "work_order", FieldName = "外厂", FieldType = "text" });
            Db.CustomFieldValues.AddRange(new SysCustomFieldValue { FieldId = 1, TargetId = 1, Value = "客户A" },
                new SysCustomFieldValue { FieldId = 2, TargetId = 1, Value = "产品秘密" },
                new SysCustomFieldValue { FieldId = 3, TargetId = 1, Value = "外厂秘密" });
            await Db.SaveChangesAsync();
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await _connection.DisposeAsync(); }
    }

    private static RoutingCreateDto NewRoute() => new()
    {
        CopyFromId = 1, Code = "R2", Name = "新路线",
        Steps = new() { new() { OperationId = 2 }, new() { OperationId = 1 } }
    };

    [Fact]
    public async Task CopyProduct_WithNewRoute_IsIndependent_AndDuplicateCodeRejected()
    {
        await using var f = new Fixture(); await f.InitializeAsync();
        await new ProductService(f.Db).CreateAsync(new() { CopyFromId = 1, Code = "P2", Name = "支架", Price = 12, RoutingId = 1, NewRouting = NewRoute() }, 1);
        var copy = await f.Db.Products.SingleAsync(p => p.Code == "P2");
        Assert.NotEqual(1, copy.RoutingId);
        Assert.Equal(1, (await f.Db.Products.FindAsync(1L))!.RoutingId);
        Assert.Equal(new long[] { 2, 1 }, await f.Db.RoutingSteps.Where(s => s.RoutingId == copy.RoutingId).OrderBy(s => s.Seq).Select(s => s.OperationId).ToArrayAsync());
        Assert.Equal(3, await f.Db.Operations.CountAsync());
        await Assert.ThrowsAsync<BusinessException>(() => new ProductService(f.Db).CreateAsync(new() { Code = "P2", Name = "不同名", NewRouting = NewRoute() }, 1));
        Assert.Equal(2, await f.Db.Routings.CountAsync());
    }

    [Fact]
    public async Task ProductFailure_RollsBackNewRouteAndSteps()
    {
        await using var f = new Fixture(); await f.InitializeAsync();
        await Assert.ThrowsAsync<Exception>(() => new ProductService(f.Db).CreateAsync(new() { Code = "FAIL", Name = "失败", NewRouting = NewRoute() }, 1));
        f.Db.ChangeTracker.Clear();
        Assert.Equal(1, await f.Db.Routings.CountAsync());
        Assert.Equal(2, await f.Db.RoutingSteps.CountAsync());
        Assert.Equal(1, await f.Db.Products.CountAsync());
    }

    [Fact]
    public async Task CopyOrder_RebuildsPlans_UsesCurrentUser_AndFiltersFields()
    {
        await using var f = new Fixture(); await f.InitializeAsync();
        var service = new WorkOrderService(f.Db);
        await service.CopyAsync(1, 99, 1);
        var copy = await f.Db.WorkOrders.SingleAsync(o => o.Id != 1);
        Assert.Equal(99, copy.CreatedBy); Assert.Equal(0, copy.Status); Assert.Null(copy.DueDate);
        var tasks = await f.Db.WorkOrderOperations.Where(t => t.WorkOrderId == copy.Id).ToListAsync();
        Assert.Equal(2, tasks.Count); Assert.All(tasks, t => { Assert.Equal(100, t.PlanQty); Assert.Null(t.AssigneeUserId); });
        Assert.Equal("客户A", Assert.Single(await f.Db.CustomFieldValues.Where(v => v.TargetId == copy.Id).ToListAsync()).Value);
        // Detail reads must also exclude product fields with the same numeric target ID.
        var detail = await service.GetAsync(1, 1);
        Assert.Equal("客户A", Assert.Single(detail.Data!.Ext).Value);
        await service.CreateAsync(new() { CopyFromId = 1, ProductId = 1, Qty = 7, DueDate = new DateTime(2026, 11, 1) }, 99, 1);
        var edited = await f.Db.WorkOrders.SingleAsync(o => o.Id != 1 && o.Id != copy.Id);
        Assert.All(await f.Db.WorkOrderOperations.Where(t => t.WorkOrderId == edited.Id).ToListAsync(), t => Assert.Equal(7, t.PlanQty));
        Assert.Equal(new DateTime(2026, 11, 1), edited.DueDate);
    }

    [Fact]
    public async Task CrossFactorySourcesReferencesAndWrongFields_AreRejectedWithoutWrites()
    {
        await using var f = new Fixture(); await f.InitializeAsync();
        await Assert.ThrowsAsync<BusinessException>(() => new ProductService(f.Db).CreateAsync(new() { CopyFromId = 1, Code = "X", Name = "X" }, 2));
        var route = NewRoute(); route.Steps[0].OperationId = 3;
        await Assert.ThrowsAsync<BusinessException>(() => new RoutingService(f.Db).CreateAsync(route, 1));
        await Assert.ThrowsAsync<BusinessException>(() => new OperationService(f.Db).CreateAsync(new() { CopyFromId = 3, Code = "X", Name = "X" }, 1));
        await Assert.ThrowsAsync<BusinessException>(() => new OperationService(f.Db).CreateAsync(new() { Code = "X", Name = "X", DeptIds = new() { 999 } }, 1));
        await Assert.ThrowsAsync<BusinessException>(() => new WorkOrderService(f.Db).CreateAsync(new() { ProductId = 1, Qty = 7, Ext = new() { [2] = "非法" } }, 99, 1));
        await Assert.ThrowsAsync<BusinessException>(() => new WorkOrderService(f.Db).CopyAsync(1, 99, 2));
        Assert.Equal(1, await f.Db.WorkOrders.CountAsync()); Assert.Equal(1, await f.Db.Routings.CountAsync());
    }

    [Fact]
    public async Task OrderPlanFailure_RollsBackOrderAndFields()
    {
        await using var f = new Fixture(); await f.InitializeAsync();
        await Assert.ThrowsAsync<Exception>(() => new WorkOrderService(f.Db).CreateAsync(new()
        {
            CopyFromId = 1, ProductId = 1, Qty = 13, Ext = new() { [1] = "失败" }
        }, 99, 1));
        f.Db.ChangeTracker.Clear();
        Assert.Equal(1, await f.Db.WorkOrders.CountAsync());
        Assert.Equal(1, await f.Db.WorkOrderOperations.CountAsync());
        Assert.Equal(3, await f.Db.CustomFieldValues.CountAsync());
    }

    [Fact]
    public async Task CopyOperation_PreservesDepartmentAndDefectReferences()
    {
        await using var f = new Fixture(); await f.InitializeAsync();
        f.Db.Departments.Add(new SysDepartment { Id = 1, FactoryId = 1, Code = "D", Name = "加工" });
        f.Db.DefectItems.Add(new BaseDefectItem { Id = 1, FactoryId = 1, Name = "划伤" });
        await f.Db.SaveChangesAsync();
        await new OperationService(f.Db).CreateAsync(new() { CopyFromId = 1, Code = "COPY", Name = "下料", DeptIds = new() { 1 }, DefectIds = new() { 1 } }, 1);
        var copy = await f.Db.Operations.SingleAsync(o => o.Code == "COPY");
        Assert.Equal(1, (await f.Db.OperationDepartments.SingleAsync(x => x.OperationId == copy.Id)).DepartmentId);
        Assert.Equal(1, (await f.Db.OperationDefects.SingleAsync(x => x.OperationId == copy.Id)).DefectId);
        f.Db.DepartmentUsers.Add(new SysDepartmentUser { DepartmentId = 1, UserId = 99 });
        await f.Db.SaveChangesAsync();
        Assert.True(await ReportPermissionHelper.CanReportOperationAsync(f.Db, 99, 2, copy.Id));
        Assert.False(await ReportPermissionHelper.CanReportOperationAsync(f.Db, 100, 2, copy.Id));
    }

    [Theory]
    [InlineData(2, "/api/Product")]
    [InlineData(3, "/api/Routing")]
    [InlineData(2, "/api/Operation")]
    [InlineData(3, "/api/WorkOrder/1/copy")]
    public async Task NonAdminWrites_AreBlocked(byte role, string path)
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("role", role.ToString()) }, "test"));
        ctx.Request.Path = path; ctx.Request.Method = "POST"; ctx.Response.Body = new MemoryStream();
        var called = false;
        await new RoleGuardMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(ctx);
        Assert.False(called); Assert.Equal(403, ctx.Response.StatusCode);
    }
}
