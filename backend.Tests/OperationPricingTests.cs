using System.Data.Common;
using System.Text.Json;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;
using ahu.MicrosoftMes.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace WorkshopMes.Tests;

public class OperationPricingTests
{
    [Fact]
    public void PricingApi_IsAdminOnly_AndConfigurationWritesRequirePieceWage()
    {
        var controller = typeof(ahu.MicrosoftMes.Controllers.OperationPricingController);
        var auth = (Microsoft.AspNetCore.Authorization.AuthorizeAttribute)Attribute.GetCustomAttribute(controller, typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute))!;
        Assert.Equal("1", auth.Roles);
        Assert.NotNull(Attribute.GetCustomAttribute(controller.GetMethod("Save")!, typeof(RequireLicenseFeatureAttribute)));
    }
    public static ProcessingConfig Config() => new()
    {
        Version = "v1",
        Groups = new() { new() { Id = "silk", Name = "丝印", Options = new()
        {
            new() { Id = "none", Name = "不丝印", Extra = 0 },
            new() { Id = "one", Name = "1片", Extra = .25m },
            new() { Id = "many", Name = "5—8片", Extra = 1.65m }
        } } },
        Bases = new() { new() { Id = "regular", ProductId = 1, Name = "25寸8K", Price = 5, PriceUnit = "打", ReportUnitId = 1,
            UnitsPerPriceUnit = 12, DeductPrice = .6m, Allowed = new() { ["silk"] = new() { "none", "one", "many" } } },
            new() { Id = "package", ProductId = 1, Name = "含1片丝印", Price = 7.7m, PriceUnit = "打", ReportUnitId = 1,
                UnitsPerPriceUnit = 12, Allowed = new() { ["silk"] = new() { "one", "many" } }, Included = new() { "one" } } }
    };
    private static ProcessingChoice Choice(string value = "one", string basis = "regular") => new() { BaseId = basis, Values = new() { ["silk"] = value } };

    [Fact]
    public void AdditivePrice_InclusionIsValueSpecific_AndDoesNotEraseRequirements()
    {
        Assert.Equal(5.25m, OperationPricingService.Calculate(Config(), 1, 1, 1, Choice()).UnitPrice);
        var included = OperationPricingService.Calculate(Config(), 1, 1, 1, Choice("one", "package"));
        Assert.Equal(7.7m, included.UnitPrice); Assert.True(included.Lines[0].Included); Assert.Equal("one", included.Choice.Values["silk"]);
        Assert.Equal(9.35m, OperationPricingService.Calculate(Config(), 1, 1, 1, Choice("many", "package")).UnitPrice);
    }
    [Fact]
    public void MissingPriceSelectionWrongProductUnitOrForbiddenValue_Rejects()
    {
        var c = Config(); c.Groups[0].Options[1].Extra = null;
        Assert.Throws<BusinessException>(() => OperationPricingService.Calculate(c, 1, 1, 1, Choice()));
        Assert.Throws<BusinessException>(() => OperationPricingService.Calculate(Config(), 1, 2, 1, Choice()));
        Assert.Throws<BusinessException>(() => OperationPricingService.Calculate(Config(), 1, 1, 2, Choice()));
        Assert.Throws<BusinessException>(() => OperationPricingService.Calculate(Config(), 1, 1, 1, new() { BaseId = "regular" }));
        Assert.Throws<BusinessException>(() => OperationPricingService.Calculate(Config(), 1, 1, 1, Choice("none", "package")));
    }
    [Fact]
    public void Snapshot_ConvertsBeforeRounding_PreservesOldPriceAndDeduction()
    {
        var c = Config(); var quote = OperationPricingService.Calculate(c, 1, 1, 1, Choice());
        var snapshot = new ReportPriceSnapshot(-10, 1, quote.UnitPrice, quote.DeductPrice) { Processing = quote };
        var report = new ProdReport { GoodQty = 1200, DefectQty = 12, PriceSnapshot = JsonSerializer.Serialize(snapshot) };
        c.Bases[0].Price = 999;
        var result = ReportPriceSnapshot.Calculate(report, 1m);
        Assert.Equal(525m, result.Amount); Assert.Equal(.6m, result.DeductAmount);
        Assert.Equal(420m, ReportPriceSnapshot.Calculate(report, .8m).Amount);
        quote = OperationPricingService.Calculate(Config(), 1, 1, 1, Choice()); quote.UnitsPerPriceUnit = 12;
        quote.UnitPrice = 5; // 5/12 cannot be rounded to four places before multiplying.
        report.GoodQty = 1200; report.PriceSnapshot = JsonSerializer.Serialize(new ReportPriceSnapshot(-10, 1, 5, .6m) { Processing = quote });
        Assert.Equal(500m, ReportPriceSnapshot.Calculate(report, 1).Amount);
    }

    // Service tests use an isolated SQLite DB. SQL Server row-lock syntax is translated only here.
    private sealed class TestLock : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData e, InterceptionResult<int> result, CancellationToken ct = default)
        { command.CommandText = command.CommandText.Replace(" WITH (UPDLOCK, ROWLOCK)", ""); return ValueTask.FromResult(result); }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public AppDbContext Db = null!;
        public async Task Init()
        {
            await connection.OpenAsync();
            Db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).AddInterceptors(new TestLock()).Options);
            await Db.Database.EnsureCreatedAsync();
            Db.Units.Add(new() { Id = 1, FactoryId = 1, Name = "把" });
            Db.Operations.Add(new() { Id = 1, FactoryId = 1, Code = "OP", Name = "合片", PricingConfig = OperationPricingService.Write(Config()) });
            Db.Routings.Add(new() { Id = 1, FactoryId = 1, Code = "R", Name = "路线" });
            Db.RoutingSteps.Add(new() { RoutingId = 1, OperationId = 1, Seq = 1 });
            Db.Products.Add(new() { Id = 1, FactoryId = 1, Code = "P", Name = "伞", UnitId = 1, RoutingId = 1 });
            Db.Users.Add(new() { Id = 1, FactoryId = 1, Account = "admin", Name = "管理员", Role = 1, Status = 1, WageCoeff = 1 });
            await Db.SaveChangesAsync();
        }
        public async Task<ProdWorkOrder> Create()
        {
            var quote = await new OperationPricingService(Db).PreviewAsync(1, 1, 1, Choice());
            quote.Choice.QuoteToken = quote.QuoteToken;
            await new WorkOrderService(Db).CreateAsync(new() { ProductId = 1, Qty = 1200, ProcessingChoices = new() { [1] = quote.Choice } }, 1, 1);
            return await Db.WorkOrders.OrderByDescending(o => o.Id).FirstAsync();
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
    [Fact]
    public async Task Create_LocksQuote_QuantityEditKeepsPrice_ConfigEditDoesNotChangeIt_CopyUsesNewPrice()
    {
        await using var f = new Fixture(); await f.Init(); var order = await f.Create();
        var service = new OperationPricingService(f.Db);
        var config = Config(); config.Bases[0].Price = 6;
        await service.SaveAsync(1, 1, config);
        await new WorkOrderService(f.Db).UpdateAsync(order.Id, new() { Qty = 1300 }, 1);
        Assert.Equal(5.25m, (await service.LockedAsync(order.Id, 1, 1))!.UnitPrice);
        await new WorkOrderService(f.Db).CopyAsync(order.Id, 1, 1);
        var copy = await f.Db.WorkOrders.OrderByDescending(o => o.Id).FirstAsync();
        Assert.Equal(6.25m, (await service.LockedAsync(copy.Id, 1, 1))!.UnitPrice);
        Assert.Equal(1300, (await f.Db.WorkOrderOperations.FirstAsync(t => t.WorkOrderId == order.Id)).PlanQty);
        f.Db.Products.Add(new() { Id = 2, FactoryId = 1, Code = "NO-ROUTE", Name = "无工艺产品", UnitId = 1 });
        await f.Db.SaveChangesAsync();
        await new WorkOrderService(f.Db).UpdateAsync(copy.Id, new() { ProductId = 2, Qty = 80 }, 1);
        Assert.Empty(await f.Db.WorkOrderOperations.Where(t => t.WorkOrderId == copy.Id).ToListAsync());
    }
    [Fact]
    public async Task StaleTamperedOrMissingQuote_RollsBackWholeOrder_AndTenantIsEnforced()
    {
        await using var f = new Fixture(); await f.Init(); var service = new OperationPricingService(f.Db);
        await Assert.ThrowsAsync<BusinessException>(() => service.GetAsync(1, 2));
        var choice = Choice(); choice.QuoteToken = "tampered";
        await Assert.ThrowsAsync<BusinessException>(() => new WorkOrderService(f.Db).CreateAsync(new() { ProductId = 1, Qty = 10, ProcessingChoices = new() { [1] = choice } }, 1, 1));
        f.Db.ChangeTracker.Clear(); Assert.Empty(await f.Db.WorkOrders.ToListAsync());
        await Assert.ThrowsAsync<BusinessException>(() => new WorkOrderService(f.Db).CreateAsync(new() { ProductId = 1, Qty = 10 }, 1, 1));
        f.Db.ChangeTracker.Clear(); Assert.Empty(await f.Db.WorkOrderOperations.ToListAsync());
        var config = Config(); config.Bases[0].ProductId = 2;
        await Assert.ThrowsAsync<BusinessException>(() => service.SaveAsync(1, 1, config));
    }
    [Fact]
    public async Task AnyReport_IncludingRejected_BlocksRepricing_ButAllowsUnchangedLockedChoice()
    {
        await using var f = new Fixture(); await f.Init(); var order = await f.Create();
        f.Db.Reports.Add(new() { FactoryId = 1, OrderId = order.Id, OperationId = 1, UserId = 1, ReviewStatus = 2 }); await f.Db.SaveChangesAsync();
        var service = new OperationPricingService(f.Db); var quote = await service.PreviewAsync(1, 1, 1, Choice("many")); quote.Choice.QuoteToken = quote.QuoteToken;
        await using var tx = await f.Db.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<BusinessException>(() => service.BindAsync(order, new() { [1] = quote.Choice }));
        var old = OperationPricingService.Read<ProcessingQuote>((await f.Db.WorkOrderOperations.SingleAsync()).ProcessingPrice!);
        old.Choice.QuoteToken = old.QuoteToken;
        await service.BindAsync(order, new() { [1] = old.Choice });
    }
    [Fact]
    public async Task ReportPreviewAndBatchReport_UseLockedQuote_NotLegacyHourlyRule()
    {
        await using var f = new Fixture(); await f.Init(); var order = await f.Create();
        f.Db.PriceRules.Add(new() { FactoryId = 1, OperationId = 1, PriceType = 2, UnitPrice = 100, EffectiveFrom = DateTime.Now.AddDays(-1) }); await f.Db.SaveChangesAsync();
        Assert.Equal(1, await new WorkSessionService(f.Db).PriceTypeAsync(order, 1, 1, DateTime.Now));
        var reportService = new ReportService(f.Db, new ReportUpstreamSettingService(f.Db));
        var preview = await reportService.PreviewWageForSubmitAsync(new() { OrderId = order.Id, OperationId = 1, GoodQty = 1200 }, 1, 1);
        Assert.Equal(525m, preview.Data!.WageAmount);
        await reportService.BatchReportAsync(new() { OrderId = order.Id, GoodQty = 1200 }, 1);
        var report = await f.Db.Reports.SingleAsync(); Assert.Equal(525m, report.WageAmount);
        Assert.Equal(525m, ReportPriceSnapshot.Calculate(report, 1).WageAmount);
    }

    [Fact]
    public async Task SelfProxyAndSharedReports_AllCaptureSameLockedPrice()
    {
        await using var f = new Fixture(); await f.Init(); var order = await f.Create();
        f.Db.Users.Add(new() { Id = 2, FactoryId = 1, Account = "second", Name = "第二人", Role = 1, Status = 1, WageCoeff = 1 });
        await f.Db.SaveChangesAsync();
        var reports = new ReportService(f.Db, new ReportUpstreamSettingService(f.Db));
        await reports.SubmitAsync(new() { OrderId = order.Id, OperationId = 1, GoodQty = 120 }, 1);
        await reports.SubmitAsync(new() { OrderId = order.Id, OperationId = 1, Assignees = new()
        { new() { UserId = 1, GoodQty = 12 }, new() { UserId = 2, GoodQty = 24 } } }, 1);
        await reports.SubmitAsync(new() { OrderId = order.Id, OperationId = 1, GoodQty = 24, ShareMode = 1, ParticipantUserIds = new() { 1, 2 } }, 1);
        var saved = await f.Db.Reports.ToListAsync();
        Assert.Equal(5, saved.Count); Assert.Equal(78.75m, saved.Sum(r => r.WageAmount));
        foreach (var report in saved)
        {
            var snapshot = JsonSerializer.Deserialize<ReportPriceSnapshot>(report.PriceSnapshot!);
            Assert.NotNull(snapshot!.Processing); Assert.Equal(5.25m, snapshot.UnitPrice);
            Assert.Equal(report.WageAmount, ReportPriceSnapshot.Calculate(report, 1).WageAmount);
        }
    }

    [Fact]
    public async Task ConfigurationVersion_RejectsLostUpdates_AndActualPriceChangeInvalidatesPreview()
    {
        await using var f = new Fixture(); await f.Init();
        var service = new OperationPricingService(f.Db);
        var preview = await service.PreviewAsync(1, 1, 1, Choice()); preview.Choice.QuoteToken = preview.QuoteToken;
        var changed = Config(); changed.Groups[0].Options[1].Extra = .3m;
        await service.SaveAsync(1, 1, changed);
        await Assert.ThrowsAsync<BusinessException>(() => service.SaveAsync(1, 1, Config()));
        await Assert.ThrowsAsync<BusinessException>(() => new WorkOrderService(f.Db).CreateAsync(new()
        { ProductId = 1, Qty = 10, ProcessingChoices = new() { [1] = preview.Choice } }, 1, 1));
        f.Db.ChangeTracker.Clear(); Assert.Empty(await f.Db.WorkOrders.ToListAsync());
    }
}
