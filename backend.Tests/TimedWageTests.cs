using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;
using ahu.MicrosoftMes.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WorkshopMes.Tests;

/// <summary>docs/240：独立 SQL Server 测试库，不连接业务账套。</summary>
public class TimedWageTests : IAsyncLifetime
{
    private AppDbContext _db = null!;
    private long _factory, _worker, _other, _admin, _op, _order, _second;
    private string Connection => new SqlConnectionStringBuilder(ImportDbFixture.ConnString)
        { InitialCatalog = "WorkshopMes_Timer240_Test", ConnectTimeout = 8 }.ConnectionString;
    private AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Connection).Options);
    private ReportService Reports(AppDbContext db) => new(db, new ReportUpstreamSettingService(db));

    public async Task InitializeAsync()
    {
        _db = NewDb();
        await _db.Database.EnsureCreatedAsync();
        await DbCompat.EnsureAsync(_db);
        var now = DateTime.Now;
        var tag = Guid.NewGuid().ToString("N");
        var factory = new SysFactory { FactoryCode = tag, FactoryName = "计时隔离测试", CreatedAt = now };
        _db.Factories.Add(factory); await _db.SaveChangesAsync(); _factory = factory.Id;
        var admin = new SysUser { FactoryId = _factory, Account = "admin" + tag, Name = "管理员", Role = 1, Password = "test", CreatedAt = now };
        var worker = new SysUser { FactoryId = _factory, Account = "worker" + tag, Name = "张三", Role = 2, Password = "test", WageCoeff = .8m, CreatedAt = now };
        var other = new SysUser { FactoryId = _factory, Account = "other" + tag, Name = "李四", Role = 2, Password = "test", CreatedAt = now };
        var dept = new SysDepartment { FactoryId = _factory, Code = "dept", Name = "辅助", CreatedAt = now };
        var op = new BaseOperation { FactoryId = _factory, Code = "op", Name = "计时工序", CreatedAt = now };
        var product = new BaseProduct { FactoryId = _factory, Code = "product", Name = "辅助产品", CreatedAt = now };
        _db.AddRange(admin, worker, other, dept, op, product); await _db.SaveChangesAsync();
        _admin = admin.Id; _worker = worker.Id; _other = other.Id; _op = op.Id;
        _db.AddRange(new SysDepartmentUser { UserId = _worker, DepartmentId = dept.Id },
            new SysDepartmentUser { UserId = _other, DepartmentId = dept.Id },
            new BaseOperationDepartment { OperationId = _op, DepartmentId = dept.Id });
        var a = new ProdWorkOrder { FactoryId = _factory, OrderNo = "A" + tag, ProductId = product.Id, Qty = 1000, CreatedAt = now };
        var b = new ProdWorkOrder { FactoryId = _factory, OrderNo = "B" + tag, ProductId = product.Id, Qty = 1000, CreatedAt = now };
        _db.AddRange(a, b); await _db.SaveChangesAsync(); _order = a.Id; _second = b.Id;
        _db.AddRange(new ProdWorkOrderOperation { WorkOrderId = _order, OperationId = _op, Seq = 1, PlanQty = 1000 },
            new ProdWorkOrderOperation { WorkOrderId = _second, OperationId = _op, Seq = 1, PlanQty = 1000 },
            new BasePriceRule { FactoryId = _factory, ProductId = product.Id, OperationId = _op, PriceType = 2, UnitPrice = 30, EffectiveFrom = now.AddYears(-1) });
        await _db.SaveChangesAsync(); _db.ChangeTracker.Clear();
    }
    public Task DisposeAsync() => _db.DisposeAsync().AsTask();
    private ReportDto Input(long? session = null, int? minutes = null) => new() { OrderId = _order, OperationId = _op,
        GoodQty = 0, SessionId = session, DurationMinutes = minutes, ClientRequestId = Guid.NewGuid().ToString("N") };

    [Fact]
    public async Task Start_IsIdempotent_AndSwitchLeavesPending()
    {
        var service = new WorkSessionService(_db);
        var a = await service.StartAsync(_order, _op, _worker);
        Assert.Equal(a.Id, (await service.StartAsync(_order, _op, _worker)).Id);
        await service.StartAsync(_second, _op, _worker);
        Assert.Equal(1, await _db.WorkSessions.CountAsync(x => x.UserId == _worker && x.Status == 0));
        Assert.Equal(2, (await _db.WorkSessions.AsNoTracking().FirstAsync(x => x.Id == a.Id)).Status);
    }

    [Fact]
    public async Task Hourly_NoSession_RejectsEvenManualMinutes()
    {
        var error = await Assert.ThrowsAsync<BusinessException>(() => Reports(_db).SubmitAsync(Input(minutes: 30), _worker));
        Assert.Contains("请先开工", error.Message);
        Assert.False(await _db.Reports.AnyAsync(x => x.FactoryId == _factory));
    }

    [Fact]
    public async Task AutomaticTime_ClosesSession_AndRetryDoesNotPayTwice()
    {
        var session = await new WorkSessionService(_db).StartAsync(_order, _op, _worker);
        await _db.WorkSessions.Where(x => x.Id == session.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.StartTime, DateTime.Now.AddSeconds(-30)));
        var input = Input(session.Id);
        await Reports(_db).SubmitAsync(input, _worker);
        await Reports(_db).SubmitAsync(input, _worker);
        var report = await _db.Reports.SingleAsync(x => x.FactoryId == _factory);
        Assert.Equal(1, report.DurationMinutes);
        Assert.Equal(.40m, report.WageAmount);
        var solid = await _db.WorkSessions.AsNoTracking().SingleAsync(x => x.Id == session.Id);
        Assert.Equal(1, solid.Status); Assert.Equal(report.Id, solid.ReportId); Assert.NotNull(solid.EndTime);
    }

    [Theory]
    [InlineData(30, true)]
    [InlineData(2000, false)]
    public async Task ManualMinutes_Validated(int minutes, bool success)
    {
        var session = await new WorkSessionService(_db).StartAsync(_order, _op, _worker);
        if (success)
        {
            await Reports(_db).SubmitAsync(Input(session.Id, minutes), _worker);
            Assert.Equal(minutes, (await _db.Reports.SingleAsync(x => x.FactoryId == _factory)).DurationMinutes);
        }
        else await Assert.ThrowsAsync<BusinessException>(() => Reports(_db).SubmitAsync(Input(session.Id, minutes), _worker));
    }

    [Fact]
    public async Task AdminBackfill_UsesActualEnd_AndCanRetry()
    {
        var end = DateTime.Now.AddDays(-35).Date.AddHours(12);
        var input = Input(); input.Assignees = new() { new() { UserId = _worker, StartTime = end.AddMinutes(-150), EndTime = end } };
        await Reports(_db).SubmitAsync(input, _admin);
        await Reports(_db).SubmitAsync(input, _admin);
        var report = await _db.Reports.SingleAsync(x => x.FactoryId == _factory);
        Assert.Equal(end, report.ReportTime); Assert.Equal(150, report.DurationMinutes); Assert.Equal(60m, report.WageAmount);
        var session = await _db.WorkSessions.SingleAsync(x => x.ReportId == report.Id);
        Assert.Equal(1, session.Status); Assert.Equal(1, session.Source); Assert.Equal(_admin, session.OperatorUserId);
    }

    [Fact]
    public async Task MixedGroup_UsesEachPersonsTime_AndAdminFillsMissingInSameRequest()
    {
        await new WorkSessionService(_db).StartAsync(_order, _op, _worker);
        var end = DateTime.Now.AddMinutes(-1);
        var input = Input(); input.Participants = new() {
            new() { UserId = _admin, StartTime = end.AddMinutes(-30), EndTime = end },
            new() { UserId = _worker, DurationMinutes = 60 },
            new() { UserId = _other, StartTime = end.AddMinutes(-150), EndTime = end } };
        await Reports(_db).SubmitAsync(input, _admin);
        var rows = await _db.Reports.Where(x => x.FactoryId == _factory).ToListAsync();
        Assert.Equal(3, rows.Count); Assert.Equal(150, rows.Single(x => x.UserId == _other).DurationMinutes);
        Assert.Equal(60, rows.Single(x => x.UserId == _worker).DurationMinutes);
    }

    [Fact]
    public async Task MissingGroupTime_RollsBackWholeRequest()
    {
        await new WorkSessionService(_db).StartAsync(_order, _op, _worker);
        var input = Input(); input.Participants = new() { new() { UserId = _worker }, new() { UserId = _other } };
        await Assert.ThrowsAsync<BusinessException>(() => Reports(_db).SubmitAsync(input, _worker));
        Assert.False(await _db.Reports.AnyAsync(x => x.FactoryId == _factory));
        Assert.Equal(0, (await _db.WorkSessions.AsNoTracking().SingleAsync(x => x.UserId == _worker)).Status);
    }

    [Theory]
    [InlineData(0)] [InlineData(30)]
    public async Task Piecework_PreservesExplicitTime(int minutes)
    {
        await _db.PriceRules.Where(x => x.FactoryId == _factory).ExecuteUpdateAsync(s => s.SetProperty(x => x.PriceType, (byte)1));
        await Reports(_db).SubmitAsync(Input(minutes: minutes), _worker);
        Assert.Equal(minutes, (await _db.Reports.SingleAsync(x => x.FactoryId == _factory)).DurationMinutes);
    }

    [Fact]
    public async Task UnmatchedRule_DoesNotRequireSession()
    {
        await _db.PriceRules.Where(x => x.FactoryId == _factory).ExecuteDeleteAsync();
        await Reports(_db).SubmitAsync(Input(), _worker);
        var report = await _db.Reports.SingleAsync(x => x.FactoryId == _factory);
        Assert.Equal(0, report.DurationMinutes); Assert.Null(report.WageAmount);
    }

    [Fact]
    public async Task ConcurrentReports_SameSession_OnlyOnePersists()
    {
        var session = await new WorkSessionService(_db).StartAsync(_order, _op, _worker);
        async Task<bool> Submit()
        {
            await using var db = NewDb();
            try { await Reports(db).SubmitAsync(Input(session.Id, 30), _worker); return true; }
            catch (BusinessException) { return false; }
        }
        var result = await Task.WhenAll(Submit(), Submit());
        Assert.Single(result, x => x);
        Assert.Equal(1, await _db.Reports.CountAsync(x => x.FactoryId == _factory));
    }

    [Fact]
    public async Task ConcurrentStart_HasSingleRunningRecord()
    {
        async Task<long> Start()
        {
            await using var db = NewDb();
            return (await new WorkSessionService(db).StartAsync(_order, _op, _worker)).Id;
        }
        var ids = await Task.WhenAll(Start(), Start());
        Assert.Equal(ids[0], ids[1]);
    }

    [Fact]
    public async Task SalarySnapshot_RecalcAndCsv_UseRecordedMinutes()
    {
        var input = Input(); var end = DateTime.Now.AddMinutes(-1);
        input.Assignees = new() { new() { UserId = _worker, StartTime = end.AddHours(-2), EndTime = end } };
        await Reports(_db).SubmitAsync(input, _admin);
        var salary = new SalaryService(_db, NullLogger<SalaryService>.Instance);
        var period = end.ToString("yyyy-MM");
        await salary.GenerateAsync(new SalaryGenerateDto { PeriodType = 1, PeriodValue = period, UserId = _worker }, _factory);
        var item = await _db.SalaryStatementItems.SingleAsync(x => x.ReportId == _db.Reports.Where(r => r.FactoryId == _factory).Select(r => r.Id).First());
        Assert.Equal(2, item.PriceType); Assert.Equal(120, item.DurationMinutes); Assert.Equal(48m, item.Amount);
        var csv = await salary.ExportItemsCsvAsync(new SalaryItemsQueryDto { PeriodType = 1, PeriodValue = period }, _factory);
        Assert.Contains("计时,120,", System.Text.Encoding.UTF8.GetString(csv.Content));
        await _db.Reports.Where(x => x.FactoryId == _factory).ExecuteUpdateAsync(s => s.SetProperty(x => x.DurationMinutes, 180));
        _db.ChangeTracker.Clear();
        await salary.RecalcDraftAsync(item.StatementId, _factory, _admin);
        var refreshed = await _db.SalaryStatementItems.AsNoTracking().SingleAsync(x => x.Id == item.Id);
        Assert.Equal(180, refreshed.DurationMinutes); Assert.Equal(72m, refreshed.Amount);
        await salary.ConfirmAsync(item.StatementId, _factory);
        var reportId = await _db.Reports.Where(x => x.FactoryId == _factory).Select(x => x.Id).SingleAsync();
        await Assert.ThrowsAsync<BusinessException>(() => Reports(_db).UpdateAsync(reportId, Input(minutes: 60), _admin));
    }

    [Fact]
    public async Task PendingReport_UsesOriginalSession_AndRetryIsIdempotent()
    {
        var clock = new WorkSessionService(_db);
        var a = await clock.StartAsync(_order, _op, _worker);
        await _db.WorkSessions.Where(x => x.Id == a.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.StartTime, DateTime.Now.AddHours(-1)));
        await clock.StartAsync(_second, _op, _worker);
        var input = Input();
        await clock.ReportPendingAsync(a.Id, input, _admin, Reports(_db));
        await clock.ReportPendingAsync(a.Id, input, _admin, Reports(_db));
        Assert.Equal(1, await _db.Reports.CountAsync(x => x.FactoryId == _factory));
        Assert.Equal(1, (await _db.WorkSessions.AsNoTracking().SingleAsync(x => x.Id == a.Id)).Status);
        Assert.Equal(2, await _db.WorkSessions.CountAsync(x => x.UserId == _worker));
    }

    [Fact]
    public async Task BatchHourly_ZeroQuantity_CreatesEndedSession()
    {
        var end = DateTime.Now.AddMinutes(-2);
        await Reports(_db).BatchReportAsync(new BatchReportDto { OrderId = _order, GoodQty = 0,
            StartTime = end.AddMinutes(-150), EndTime = end, BatchNo = Guid.NewGuid().ToString("N"),
            Allocations = new() { new() { OperationId = _op, Users = new() { new() { UserId = _worker, GoodQty = 0 } } } }
        }, _admin);
        var report = await _db.Reports.SingleAsync(x => x.FactoryId == _factory);
        Assert.Equal(150, report.DurationMinutes); Assert.Equal(end, report.ReportTime);
        Assert.Equal(1, (await _db.WorkSessions.SingleAsync(x => x.ReportId == report.Id)).Status);
    }

    [Fact]
    public async Task EditDuration_SynchronizesSession_AndKeepsActualTimestamps()
    {
        var a = await new WorkSessionService(_db).StartAsync(_order, _op, _worker);
        await Reports(_db).SubmitAsync(Input(a.Id, 30), _worker);
        var report = await _db.Reports.SingleAsync(x => x.FactoryId == _factory);
        var before = await _db.WorkSessions.AsNoTracking().SingleAsync(x => x.Id == a.Id);
        await Reports(_db).UpdateAsync(report.Id, Input(minutes: 60), _worker);
        var after = await _db.WorkSessions.AsNoTracking().SingleAsync(x => x.Id == a.Id);
        Assert.Equal(60, after.DurationMinutes); Assert.Equal(before.StartTime, after.StartTime); Assert.Equal(before.EndTime, after.EndTime);
        Assert.Equal(1, await _db.ReportChangeLogs.CountAsync(x => x.ReportId == report.Id));
    }

    [Fact]
    public async Task Withdraw_CancelsSession_AndAllowsFreshStart()
    {
        var clock = new WorkSessionService(_db);
        var a = await clock.StartAsync(_order, _op, _worker);
        await Reports(_db).SubmitAsync(Input(a.Id, 30), _worker);
        var report = await _db.Reports.SingleAsync(x => x.FactoryId == _factory);
        await Reports(_db).WithdrawAsync(report.Id, _worker);
        Assert.Equal(3, (await _db.WorkSessions.AsNoTracking().SingleAsync(x => x.Id == a.Id)).Status);
        Assert.NotEqual(a.Id, (await clock.StartAsync(_order, _op, _worker)).Id);
    }

    [Fact]
    public async Task SessionFromAnotherOrder_IsRejected()
    {
        var a = await new WorkSessionService(_db).StartAsync(_second, _op, _worker);
        await Assert.ThrowsAsync<BusinessException>(() => Reports(_db).SubmitAsync(Input(a.Id, 30), _worker));
        Assert.False(await _db.Reports.AnyAsync(x => x.FactoryId == _factory));
    }

    [Fact]
    public async Task MissingDepartmentPermission_CannotStart()
    {
        await _db.DepartmentUsers.Where(x => x.UserId == _worker).ExecuteDeleteAsync();
        await Assert.ThrowsAsync<BusinessException>(() => new WorkSessionService(_db).StartAsync(_order, _op, _worker));
    }

    [Fact]
    public async Task WorkerCannotBackfillEvenWithValidTimes()
    {
        var input = Input(); input.StartTime = DateTime.Now.AddHours(-1); input.EndTime = DateTime.Now.AddMinutes(-1);
        await Assert.ThrowsAsync<BusinessException>(() => Reports(_db).SubmitAsync(input, _worker));
    }

    [Fact]
    public async Task SessionReferences_BlockUserAndOperationDeletion_OrderDeletionCleansSessions()
    {
        await new WorkSessionService(_db).StartAsync(_order, _op, _worker);
        Assert.NotNull(await DeleteGuard.CheckAsync(_db, "User", _worker));
        Assert.NotNull(await DeleteGuard.CheckAsync(_db, "Operation", _op));
        await new WorkOrderService(_db).DeleteAsync(_order);
        Assert.False(await _db.WorkSessions.AnyAsync(x => x.WorkOrderId == _order));
    }

    [Theory]
    [InlineData(0, -1)] [InlineData(2, 3)] [InlineData(-1500, -1)]
    public void InvalidTimeRange_IsRejected(int start, int end)
    {
        var now = DateTime.Now;
        Assert.Throws<BusinessException>(() => WorkSessionService.CalculateMinutes(now.AddMinutes(start), now.AddMinutes(end), now));
    }
}
