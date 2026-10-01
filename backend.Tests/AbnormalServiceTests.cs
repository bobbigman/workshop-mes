using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;
using ahu.MicrosoftMes.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace WorkshopMes.Tests;

[Collection("abnormal_db")]
public class AbnormalServiceTests
{
    private readonly AbnormalDbFixture _fx;
    public AbnormalServiceTests(AbnormalDbFixture fx) { _fx = fx; }

    private static (AppDbContext db, AbnormalService svc, ReportStatService board) New()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(AbnormalDbFixture.ConnString)
            .Options;
        var db = new AppDbContext(opts);
        var env = new FakeWebHostEnvironment();
        return (db, new AbnormalService(db, env), new ReportStatService(db));
    }

    [Fact]
    public async Task Create_Open_Shows_On_Board_Resolve_Clears()
    {
        var (db, svc, board) = New();
        await svc.CreateAsync(new AbnormalCreateForm
        {
            Type = 1,
            Description = "压机异响停机"
        }, _fx.WorkerId, _fx.FactoryId);

        var b1 = await board.BoardAsync(_fx.FactoryId);
        Assert.True(b1.Data!.OpenAbnormalCount >= 1);
        Assert.Contains(b1.Data.AbnormalTicker, t => t.Description.Contains("压机"));

        var list = await svc.QueryAsync(new AbnormalQueryDto { Status = 0, Page = 1, PageSize = 50 }, _fx.FactoryId);
        var row = Assert.Single(list.Data!.List.Where(x => x.Description == "压机异响停机"));

        await svc.ResolveAsync(row.Id, new AbnormalResolveDto { HandleNote = "已换轴承" }, _fx.AdminId, _fx.FactoryId);

        var b2 = await board.BoardAsync(_fx.FactoryId);
        Assert.DoesNotContain(b2.Data!.AbnormalTicker, t => t.Id == row.Id);
        var again = await db.Abnormals.AsNoTracking().FirstAsync(a => a.Id == row.Id);
        Assert.Equal(1, again.Status);
        Assert.Equal(_fx.AdminId, again.HandledBy);
    }

    [Fact]
    public async Task Reject_Invalid_Type_And_Empty_Description()
    {
        var (_, svc, _) = New();
        await Assert.ThrowsAsync<BusinessException>(() => svc.CreateAsync(new AbnormalCreateForm
        {
            Type = 9,
            Description = "x"
        }, _fx.WorkerId, _fx.FactoryId));

        await Assert.ThrowsAsync<BusinessException>(() => svc.CreateAsync(new AbnormalCreateForm
        {
            Type = 2,
            Description = "  "
        }, _fx.WorkerId, _fx.FactoryId));
    }

    [Fact]
    public async Task CrossFactory_WorkOrder_Rejected()
    {
        var (_, svc, _) = New();
        await Assert.ThrowsAsync<BusinessException>(() => svc.CreateAsync(new AbnormalCreateForm
        {
            Type = 3,
            Description = "来料不良",
            WorkOrderId = _fx.OtherFactoryOrderId
        }, _fx.WorkerId, _fx.FactoryId));
    }
}

[CollectionDefinition("abnormal_db", DisableParallelization = true)]
public class AbnormalDbCollection : ICollectionFixture<AbnormalDbFixture> { }

public class AbnormalDbFixture : IDisposable
{
    public static string ConnString =>
        Environment.GetEnvironmentVariable("MES_TEST_DB")
        ?? "Server=localhost;Database=WorkshopMes_Test;Trusted_Connection=True;TrustServerCertificate=True";

    public long FactoryId;
    public long WorkerId;
    public long AdminId;
    public long OtherFactoryOrderId;

    public AbnormalDbFixture()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnString).Options;
        using var db = new AppDbContext(opts);
        db.Database.EnsureCreated();
        DbCompat.EnsureAsync(db).GetAwaiter().GetResult();
        Reset(db);
        Seed(db);
    }

    public void Dispose() { }

    private static void Reset(AppDbContext db)
    {
        var codes = new[] { "TEST-ABN", "TEST-ABN2" };
        var fids = db.Factories.Where(f => codes.Contains(f.FactoryCode)).Select(f => f.Id).ToList();
        if (fids.Count == 0) return;
        var orderIds = db.WorkOrders.Where(o => fids.Contains(o.FactoryId)).Select(o => o.Id).ToList();
        db.Abnormals.RemoveRange(db.Abnormals.Where(a => fids.Contains(a.FactoryId)));
        if (orderIds.Count > 0)
            db.WorkOrders.RemoveRange(db.WorkOrders.Where(o => orderIds.Contains(o.Id)));
        db.Products.RemoveRange(db.Products.Where(p => fids.Contains(p.FactoryId)));
        db.Units.RemoveRange(db.Units.Where(u => fids.Contains(u.FactoryId)));
        db.Users.RemoveRange(db.Users.Where(u => fids.Contains(u.FactoryId)));
        db.Factories.RemoveRange(db.Factories.Where(f => fids.Contains(f.Id)));
        db.SaveChanges();
    }

    private void Seed(AppDbContext db)
    {
        var now = DateTime.Now;
        var f = new SysFactory { FactoryCode = "TEST-ABN", FactoryName = "异常厂", CreatedAt = now };
        var f2 = new SysFactory { FactoryCode = "TEST-ABN2", FactoryName = "对照厂", CreatedAt = now };
        db.Factories.AddRange(f, f2);
        db.SaveChanges();
        FactoryId = f.Id;

        var unit = new BaseUnit { FactoryId = f.Id, Name = "件", CreatedAt = now };
        db.Units.Add(unit);
        db.SaveChanges();

        var admin = new SysUser { FactoryId = f.Id, Account = "abn_admin", Name = "管理员", Role = 1, Password = "x", Status = 1, CreatedAt = now };
        var worker = new SysUser { FactoryId = f.Id, Account = "abn_worker", Name = "工人", Role = 2, Password = "x", Status = 1, CreatedAt = now };
        db.Users.AddRange(admin, worker);
        db.SaveChanges();
        AdminId = admin.Id;
        WorkerId = worker.Id;

        var prod = new BaseProduct { FactoryId = f.Id, Code = "ABN-P1", Name = "产品", UnitId = unit.Id, CreatedAt = now };
        db.Products.Add(prod);
        db.SaveChanges();

        var unit2 = new BaseUnit { FactoryId = f2.Id, Name = "件", CreatedAt = now };
        db.Units.Add(unit2);
        db.SaveChanges();
        var prod2 = new BaseProduct { FactoryId = f2.Id, Code = "ABN-P2", Name = "对照产品", UnitId = unit2.Id, CreatedAt = now };
        var user2 = new SysUser { FactoryId = f2.Id, Account = "abn2", Name = "B", Role = 1, Password = "x", Status = 1, CreatedAt = now };
        db.Products.Add(prod2);
        db.Users.Add(user2);
        db.SaveChanges();
        var o2 = new ProdWorkOrder
        {
            FactoryId = f2.Id, OrderNo = "ABN2-001", ProductId = prod2.Id, Qty = 1, Status = 0,
            CreatedBy = user2.Id, CreatedAt = now
        };
        db.WorkOrders.Add(o2);
        db.SaveChanges();
        OtherFactoryOrderId = o2.Id;
    }
}

internal sealed class FakeWebHostEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "WorkshopMes.Tests";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "mes-abn-www");
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; } = Path.GetTempPath();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
