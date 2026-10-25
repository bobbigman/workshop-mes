using System.Text.Json;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;
using ahu.MicrosoftMes.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace WorkshopMes.Tests;

public class PriceChangeTests
{
    sealed class FailAudit : SaveChangesInterceptor
    {
        public bool Enabled;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken ct=default)
        {
            if(Enabled && data.Context!.ChangeTracker.Entries<OperationPriceChange>().Any(x=>x.State==EntityState.Added))
                throw new InvalidOperationException("injected audit failure");
            return ValueTask.FromResult(result);
        }
    }
    sealed class TestDb(DbContextOptions<AppDbContext> options) : AppDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            if(Database.IsSqlServer())builder.Entity<SysUser>().Property(x=>x.Id).UseIdentityColumn(14);
        }
    }
    sealed class Fixture : IAsyncDisposable
    {
        public SqliteConnection Connection = new("Data Source=:memory:");
        public AppDbContext Db=null!;
        public FailAudit Fault=new();
        public PriceChangeDraftStore Drafts=new(new MemoryCache(new MemoryCacheOptions()));
        public PriceChangeService Service=>new(Db,Drafts);
        public OperationPriceList Table=null!;
        private bool ownsSqlDatabase;
        public static async Task<Fixture> Create(byte type=1,byte status=1)
        {
            var f=new Fixture();await f.Connection.OpenAsync();
            var options=new DbContextOptionsBuilder<AppDbContext>().AddInterceptors(f.Fault);
            if(Environment.GetEnvironmentVariable("PRICE_CHANGE_SQLSERVER")=="1")
                options.UseSqlServer($"Server=localhost;Database=WorkshopMes418Checks_{Guid.NewGuid():N};Integrated Security=true;Encrypt=true;TrustServerCertificate=true;Connect Timeout=8");
            else options.UseSqlite(f.Connection);
            f.Db=new TestDb(options.Options);
            f.ownsSqlDatabase=await f.Db.Database.EnsureCreatedAsync() && f.Db.Database.IsSqlServer();
            try
            {
            if(f.Db.Database.IsSqlServer())
            {
                await f.Db.Database.ExecuteSqlRawAsync("DROP TABLE dbo.base_operation_price_change; DROP TABLE dbo.base_operation_price_revision;");
                await f.Db.Database.ExecuteSqlRawAsync(PriceChangeSchema.UpgradeSql);
                await f.Db.Database.ExecuteSqlRawAsync(PriceChangeSchema.UpgradeSql);
            }
            f.Db.Users.Add(new(){Id=f.Db.Database.IsSqlServer()?0:14,FactoryId=4,Name="admin",Role=1,Status=1,WageCoeff=2});
            f.Table=new(){Id=f.Db.Database.IsSqlServer()?0:1,FactoryId=4,OperationId=42,Code="PRICE",Name="test",PricingType=type,Status=1,ActivatedAt=DateTime.Now,
                EffectiveFrom=PriceTimelineService.Today.AddDays(-20),Version="original",ReportUnitId=18,PriceUnit=type==2?"小时":type==3?"笔":"件",UnitsPerPriceUnit=1,
                Details=OperationPricingService.Write(new AttributePriceDetails{SchemaVersion=2,Rows=new(){new(){RowId="base",Amount=1,DeductPrice=.1m,Confirmed=true,Source="test"}}})};
            f.Db.OperationPriceLists.Add(f.Table);
            await f.Db.SaveChangesAsync();
            await new PriceTimelineService(f.Db).RecordAsync(f.Table,f.Table.EffectiveFrom,14,new());
            var quote=await new PriceTimelineService(f.Db).QuoteAsync(4,10,42,18,new(),PriceTimelineService.Today);
            var r=new ProdReport{Id=f.Db.Database.IsSqlServer()?0:1,FactoryId=4,OrderId=1,OperationId=42,ProductId=10,UserId=14,GoodQty=100,DefectQty=1,DurationMinutes=120,
                ReportTime=PriceTimelineService.Today.AddHours(10),ReviewStatus=1,SettledFlag=status==1,
                PriceSnapshot=OperationPricingService.Write(new ReportPriceSnapshot(1,type,quote.UnitPrice,quote.DeductPrice){Processing=quote,AppliedWageCoeff=.8m})};
            var c=ReportPriceSnapshot.Calculate(r,.8m);r.UnitPrice=c.UnitPrice;r.WageAmount=c.WageAmount;
            f.Db.Reports.Add(r);
            f.Db.SalaryStatements.Add(new(){Id=f.Db.Database.IsSqlServer()?0:1,FactoryId=4,UserId=14,PeriodType=1,PeriodValue=PriceTimelineService.Today.ToString("yyyy-MM"),Status=status,TotalAmount=c.WageAmount,ConfirmedAt=DateTime.Now});
            f.Db.SalaryStatementItems.Add(new(){Id=f.Db.Database.IsSqlServer()?0:1,StatementId=1,ReportId=1,PriceListId=1,PriceType=type,CalcQty=c.CalcQty,DurationMinutes=120,WageCoeff=c.WageCoeff,UnitPrice=c.UnitPrice,Amount=c.Amount,DeductAmount=c.DeductAmount});
            await f.Db.SaveChangesAsync();return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }
        public PriceChangeInput Input(bool retro=true,DateTime? date=null)=>new(){RequestId=Guid.NewGuid(),ExpectedVersion=Table.Version,EffectiveFrom=date??PriceTimelineService.Today,
            RepriceCurrentMonth=retro,Reason="customer correction",Rows=new(){new("base",1.2m,.2m)}};
        public async Task<PriceChangeCommit> Preview(PriceChangeInput input)
        {
            var json=JsonSerializer.SerializeToElement(await Service.PreviewAsync(1,input,4,14),OperationPricingService.Json);
            Assert.Equal(0,json.GetProperty("blockers").GetArrayLength());
            return new(input.RequestId,json.GetProperty("previewToken").GetString()!,json.GetProperty("planHash").GetString()!);
        }
        public async ValueTask DisposeAsync()
        {
            if(ownsSqlDatabase && Db.Database.GetDbConnection().Database.StartsWith("WorkshopMes418Checks_",StringComparison.Ordinal))
                await Db.Database.EnsureDeletedAsync();
            await Db.DisposeAsync();await Connection.DisposeAsync();
        }
    }

    [Theory][InlineData(1)][InlineData(2)][InlineData(3)]
    public async Task Retro_UsesHistoricalCoefficients_RevokesAndAudits_AndRetryIsIdempotent(byte type)
    {
        await using var f=await Fixture.Create(type);var before=(await f.Db.Reports.AsNoTracking().SingleAsync()).WageAmount;
        var commit=await f.Preview(f.Input());
        Assert.Equal(before,(await f.Db.Reports.AsNoTracking().SingleAsync()).WageAmount);
        Assert.Equal("original",(await f.Db.OperationPriceLists.AsNoTracking().SingleAsync()).Version);
        var first=JsonSerializer.SerializeToElement(await f.Service.CommitAsync(1,commit,4,14),OperationPricingService.Json);
        f.Db.ChangeTracker.Clear();
        var report=await f.Db.Reports.SingleAsync();var item=await f.Db.SalaryStatementItems.SingleAsync();var statement=await f.Db.SalaryStatements.SingleAsync();
        Assert.Equal(type==3?1m:.8m,item.WageCoeff);Assert.Equal(100,report.GoodQty);Assert.Equal(120,report.DurationMinutes);Assert.Equal(1,report.ReviewStatus);
        Assert.Equal(type==1?95.8m:type==2?1.72m:1m,report.WageAmount);
        Assert.Equal(report.WageAmount,statement.TotalAmount);Assert.False(report.SettledFlag);Assert.Equal(0,statement.Status);Assert.Null(statement.ConfirmedAt);
        var batch=await f.Db.OperationPriceChanges.SingleAsync();Assert.Contains("original",batch.BeforeJson);Assert.Contains("customer correction",batch.Reason);
        var retry=JsonSerializer.SerializeToElement(await f.Service.CommitAsync(1,commit,4,14),OperationPricingService.Json);
        Assert.Equal(first.GetProperty("batchId").GetInt64(),retry.GetProperty("batchId").GetInt64());Assert.True(retry.GetProperty("idempotent").GetBoolean());
        Assert.Equal(1,await f.Db.OperationPriceChanges.CountAsync());
    }

    [Fact] public async Task FutureAdjustment_LeavesReportsAndTodayPrice_ThenBlocksOverlap()
    {
        await using var f=await Fixture.Create();var input=f.Input(false,PriceTimelineService.Today.AddDays(1));
        await f.Service.CommitAsync(1,await f.Preview(input),4,14);f.Db.ChangeTracker.Clear();
        var timeline=new PriceTimelineService(f.Db);
        Assert.Equal(1m,(await timeline.QuoteAsync(4,10,42,18,new(),PriceTimelineService.Today)).UnitPrice);
        Assert.Equal(1.2m,(await timeline.QuoteAsync(4,10,42,18,new(),PriceTimelineService.Today.AddDays(1))).UnitPrice);
        Assert.Equal(79.9m,(await f.Db.Reports.SingleAsync()).WageAmount);
        f.Table=await f.Db.OperationPriceLists.SingleAsync();
        await Assert.ThrowsAsync<BusinessException>(()=>f.Service.PreviewAsync(1,f.Input(),4,14));
    }

    [Fact] public async Task PreviewScopeChange_RejectsEntireCommit()
    {
        await using var f=await Fixture.Create();var commit=await f.Preview(f.Input());
        (await f.Db.Reports.SingleAsync()).GoodQty++;await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessException>(()=>f.Service.CommitAsync(1,commit,4,14));
        Assert.Equal("original",(await f.Db.OperationPriceLists.SingleAsync()).Version);Assert.Empty(await f.Db.OperationPriceChanges.ToListAsync());
    }

    [Fact] public async Task InjectedMidCommitFailure_RollsBackPriceReportsSalaryAndAudit()
    {
        await using var f=await Fixture.Create();var commit=await f.Preview(f.Input());f.Fault.Enabled=true;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.CommitAsync(1,commit,4,14));f.Db.ChangeTracker.Clear();
        Assert.Equal("original",(await f.Db.OperationPriceLists.SingleAsync()).Version);Assert.Equal(1,await f.Db.OperationPriceRevisions.CountAsync());
        Assert.Equal(79.9m,(await f.Db.Reports.SingleAsync()).WageAmount);Assert.True((await f.Db.Reports.SingleAsync()).SettledFlag);
        Assert.Equal(1,(await f.Db.SalaryStatements.SingleAsync()).Status);Assert.Equal(79.9m,(await f.Db.SalaryStatementItems.SingleAsync()).Amount-.1m);
        Assert.Empty(await f.Db.OperationPriceChanges.ToListAsync());
    }

    [Theory][InlineData(0)][InlineData(1)][InlineData(2)]
    public async Task PaidOrMissingHistoricalBasis_BlocksWithoutWrites(int condition)
    {
        await using var f=await Fixture.Create(status:condition==0?(byte)2:(byte)1);
        var r=await f.Db.Reports.SingleAsync();
        if(condition==1)r.PriceSnapshot=null;
        if(condition==2){var s=OperationPricingService.Read<ReportPriceSnapshot>(r.PriceSnapshot!);r.PriceSnapshot=OperationPricingService.Write(s with{AppliedWageCoeff=null});(await f.Db.SalaryStatementItems.SingleAsync()).WageCoeff=2;}
        await f.Db.SaveChangesAsync();
        var json=JsonSerializer.SerializeToElement(await f.Service.PreviewAsync(1,f.Input(),4,14),OperationPricingService.Json);
        Assert.True(json.GetProperty("blockers").GetArrayLength()>0);Assert.Equal(JsonValueKind.Null,json.GetProperty("previewToken").ValueKind);
        Assert.Equal("original",(await f.Db.OperationPriceLists.SingleAsync()).Version);
    }

    [Fact] public async Task OtherFactoryAndStaleVersionAndNoChange_AreRejected()
    {
        await using var f=await Fixture.Create();
        await Assert.ThrowsAsync<BusinessException>(()=>f.Service.PreviewAsync(1,f.Input(),99,14));
        var input=f.Input();input.ExpectedVersion="stale";await Assert.ThrowsAsync<BusinessException>(()=>f.Service.PreviewAsync(1,input,4,14));
        input=f.Input();input.Rows=new(){new("base",1m,.1m)};await Assert.ThrowsAsync<BusinessException>(()=>f.Service.PreviewAsync(1,input,4,14));
    }

    [Fact] public async Task ConfirmedStatement_ReleasesWholeStatementButOnlyRepricesTargetItems()
    {
        await using var f=await Fixture.Create();
        var other=PriceTimelineService.Clone(await f.Db.Reports.SingleAsync());other.Id=f.Db.Database.IsSqlServer()?0:2;other.OperationId=99;other.WageAmount=7;
        f.Db.Reports.Add(other);f.Db.SalaryStatementItems.Add(new(){Id=f.Db.Database.IsSqlServer()?0:2,StatementId=1,ReportId=2,Amount=7,UnitPrice=7,PriceType=3,WageCoeff=1});
        (await f.Db.SalaryStatements.SingleAsync()).TotalAmount+=7;await f.Db.SaveChangesAsync();
        await f.Service.CommitAsync(1,await f.Preview(f.Input()),4,14);f.Db.ChangeTracker.Clear();
        Assert.False((await f.Db.Reports.SingleAsync(x=>x.Id==2)).SettledFlag);
        Assert.Equal(7m,(await f.Db.SalaryStatementItems.SingleAsync(x=>x.Id==2)).Amount);
        Assert.Equal(102.8m,(await f.Db.SalaryStatements.SingleAsync()).TotalAmount);
        var batch=await f.Db.OperationPriceChanges.SingleAsync();var after=JsonDocument.Parse(batch.AfterJson);
        Assert.Equal(2,after.RootElement.GetProperty("reports").GetArrayLength());
    }

    [Fact] public async Task UnsettledReportWithoutSalary_IsRepricedWithoutGeneratingPayrollOrApprovingReport()
    {
        await using var f=await Fixture.Create(status:0);
        f.Db.SalaryStatementItems.RemoveRange(f.Db.SalaryStatementItems);f.Db.SalaryStatements.RemoveRange(f.Db.SalaryStatements);
        (await f.Db.Reports.SingleAsync()).ReviewStatus=2;await f.Db.SaveChangesAsync();
        await f.Service.CommitAsync(1,await f.Preview(f.Input()),4,14);f.Db.ChangeTracker.Clear();
        var report=await f.Db.Reports.SingleAsync();Assert.Equal(95.8m,report.WageAmount);Assert.Equal(2,report.ReviewStatus);
        Assert.Empty(await f.Db.SalaryStatements.ToListAsync());
    }

    [Fact] public async Task OldTaskBudgetStaysFrozen_ReportingUsesBusinessDatePrice()
    {
        await using var f=await Fixture.Create();
        var quote=await new PriceTimelineService(f.Db).QuoteAsync(4,10,42,18,new(),PriceTimelineService.Today);
        f.Db.WorkOrders.Add(new(){Id=f.Db.Database.IsSqlServer()?0:1,FactoryId=4,ProductId=10,OrderNo="A1"});
        f.Db.WorkOrderOperations.Add(new(){Id=f.Db.Database.IsSqlServer()?0:1,WorkOrderId=1,OperationId=42,ProcessingPrice=OperationPricingService.Write(quote)});
        await f.Db.SaveChangesAsync();
        await f.Service.CommitAsync(1,await f.Preview(f.Input(false,PriceTimelineService.Today.AddDays(1))),4,14);
        var pricing=new OperationPricingService(f.Db);
        Assert.Equal(1m,(await pricing.BudgetAsync(1,42,4)).UnitPrice);
        Assert.Equal(1m,(await pricing.LockedAsync(1,42,4,PriceTimelineService.Today.AddHours(23))).UnitPrice);
        Assert.Equal(1.2m,(await pricing.LockedAsync(1,42,4,PriceTimelineService.Today.AddDays(1).AddMinutes(1))).UnitPrice);
    }

    [Fact] public async Task SameDayPriceHistoryIsRetained_AndNextChangeInvalidatesOldPreview()
    {
        await using var f=await Fixture.Create();
        await f.Service.CommitAsync(1,await f.Preview(f.Input()),4,14);
        f.Table=await f.Db.OperationPriceLists.SingleAsync();var next=f.Input();next.Rows=new(){new("base",1.4m,.1m)};
        await f.Service.CommitAsync(1,await f.Preview(next),4,14);f.Db.ChangeTracker.Clear();
        Assert.Equal(2,await f.Db.OperationPriceChanges.CountAsync());Assert.Equal(3,await f.Db.OperationPriceRevisions.CountAsync());
        Assert.Equal(1.4m,(await new PriceTimelineService(f.Db).QuoteAsync(4,10,42,18,new(),PriceTimelineService.Today)).UnitPrice);
        Assert.Equal(1,await f.Db.OperationPriceRevisions.CountAsync(x=>x.Superseded));
    }

    [Fact] public async Task ConcurrentSameRequest_CommitsOnlyOnce_OnSqlServer()
    {
        if(Environment.GetEnvironmentVariable("PRICE_CHANGE_SQLSERVER")!="1")return;
        await using var f=await Fixture.Create();var commit=await f.Preview(f.Input());
        await using var db2=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(f.Db.Database.GetConnectionString()).Options);
        var second=new PriceChangeService(db2,f.Drafts);
        var results=await Task.WhenAll(f.Service.CommitAsync(1,commit,4,14),second.CommitAsync(1,commit,4,14));
        Assert.Equal(1,await f.Db.OperationPriceChanges.CountAsync());
        var json=results.Select(x=>JsonSerializer.SerializeToElement(x,OperationPricingService.Json)).ToList();
        Assert.Equal(json[0].GetProperty("batchId").GetInt64(),json[1].GetProperty("batchId").GetInt64());
        Assert.Single(json,x=>x.GetProperty("idempotent").GetBoolean());
    }

    [Fact] public async Task Baseline_UsesKnownModificationDate_AndDoesNotInventEarlierPrices()
    {
        await using var f=await Fixture.Create();
        f.Db.OperationPriceRevisions.RemoveRange(f.Db.OperationPriceRevisions);await f.Db.SaveChangesAsync();
        f.Table.UpdatedAt=PriceTimelineService.Today.AddDays(-3);f.Table.ActivatedAt=PriceTimelineService.Today.AddDays(-5);await f.Db.SaveChangesAsync();
        var timeline=new PriceTimelineService(f.Db);await timeline.EnsureBaselineAsync(f.Table);
        Assert.Equal(PriceTimelineService.Today.AddDays(-3),(await f.Db.OperationPriceRevisions.SingleAsync()).EffectiveFrom);
        Assert.Equal(1m,(await timeline.QuoteAsync(4,10,42,18,new(),PriceTimelineService.Today.AddDays(-3))).UnitPrice);
        await Assert.ThrowsAsync<BusinessException>(()=>timeline.QuoteAsync(4,10,42,18,new(),PriceTimelineService.Today.AddDays(-4)));
    }

    [Fact] public async Task OrdinaryCoefficientRefresh_KeepsPriceAndPriorCoefficientEvidence()
    {
        await using var f=await Fixture.Create();var report=await f.Db.Reports.SingleAsync();
        var updated=ReportPriceSnapshot.WithCoefficient(report,2m,199.9m,14,"salary-recalculate");
        var snapshot=OperationPricingService.Read<ReportPriceSnapshot>(updated);
        Assert.Equal(1m,snapshot.UnitPrice);Assert.Equal(2m,snapshot.AppliedWageCoeff);
        var history=Assert.Single(snapshot.CoefficientHistory!);Assert.Equal(.8m,history.BeforeCoeff);Assert.Equal(79.9m,history.BeforeAmount);
        Assert.Equal(14,history.OperatorId);Assert.Equal(199.9m,history.AfterAmount);
    }

    [Fact] public async Task ActualReportSubmit_UsesNewDatePriceAndSupportsClientRetry_OnSqlServer()
    {
        if(Environment.GetEnvironmentVariable("PRICE_CHANGE_SQLSERVER")!="1")return;
        await using var f=await Fixture.Create();
        var quote=await new PriceTimelineService(f.Db).QuoteAsync(4,10,42,18,new(),PriceTimelineService.Today);
        f.Db.WorkOrders.Add(new(){FactoryId=4,ProductId=10,OrderNo="A1",Qty=1000});await f.Db.SaveChangesAsync();
        f.Db.WorkOrderOperations.Add(new(){WorkOrderId=1,OperationId=42,Seq=1,PlanQty=1000,ProcessingPrice=OperationPricingService.Write(quote)});await f.Db.SaveChangesAsync();
        await f.Service.CommitAsync(1,await f.Preview(f.Input(false)),4,14);
        var reports=new ReportService(f.Db,new ReportUpstreamSettingService(f.Db));
        var dto=new ReportDto{OrderId=1,OperationId=42,GoodQty=10,ClientRequestId=Guid.NewGuid().ToString("N")};
        await reports.SubmitAsync(dto,14);await reports.SubmitAsync(dto,14);
        var submitted=await f.Db.Reports.SingleAsync(x=>x.ClientRequestId==dto.ClientRequestId);
        Assert.Equal(24m,submitted.WageAmount);Assert.Equal(1.2m,submitted.UnitPrice);
        Assert.Equal(2m,OperationPricingService.Read<ReportPriceSnapshot>(submitted.PriceSnapshot!).AppliedWageCoeff);
        Assert.Equal(1m,OperationPricingService.Read<ProcessingQuote>((await f.Db.WorkOrderOperations.SingleAsync()).ProcessingPrice!).UnitPrice);
    }

    [Fact] public async Task ExpiredPriceCanBeStoppedWithoutCreatingInvalidDateSegment()
    {
        await using var f=await Fixture.Create();
        f.Table.Status=2;f.Table.EffectiveTo=PriceTimelineService.Today.AddDays(-1);await f.Db.SaveChangesAsync();
        var timeline=new PriceTimelineService(f.Db);await timeline.RecordAsync(f.Table,PriceTimelineService.Today,14,new());
        Assert.Equal(1,await f.Db.OperationPriceRevisions.CountAsync());
        Assert.Equal(PriceTimelineService.Today.AddDays(-1),(await f.Db.OperationPriceRevisions.SingleAsync()).EffectiveTo);
        await Assert.ThrowsAsync<BusinessException>(()=>timeline.QuoteAsync(4,10,42,18,new(),PriceTimelineService.Today));
        Assert.Equal(1m,(await timeline.QuoteAsync(4,10,42,18,new(),PriceTimelineService.Today.AddDays(-1))).UnitPrice);
    }
}
