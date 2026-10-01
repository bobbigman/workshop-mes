using ClosedXML.Excel;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;
using ahu.MicrosoftMes.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace WorkshopMes.Tests;

[Collection("import_db")]
public class ImportServiceTests
{
    private readonly ImportDbFixture _fx;
    public ImportServiceTests(ImportDbFixture fx) { _fx = fx; }

    private (AppDbContext db, ImportService svc) New()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ImportDbFixture.ConnString)
            .Options;
        var db = new AppDbContext(opts);
        return (db, new ImportService(db));
    }

    private static byte[] BuildXlsx(params (int row, string[] cells)[] dataRows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("母版");
        ws.Cell(1, 1).Value = "表头";
        foreach (var (row, cells) in dataRows)
        {
            for (var i = 0; i < cells.Length; i++)
                ws.Cell(row, i + 1).Value = cells[i];
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    [Fact]
    public async Task DownloadTemplate_AllTypes_Ok()
    {
        var (_, svc) = New();
        foreach (var t in new[] { "user", "defect", "operation", "routing", "product" })
        {
            var bytes = await svc.DownloadTemplateAsync(t);
            Assert.True(bytes.Length > 100, t);
            using var ms = new MemoryStream(bytes);
            using var wb = new XLWorkbook(ms);
            var ws = wb.Worksheet(1);
            Assert.False(string.IsNullOrWhiteSpace(ws.Cell(2, 1).GetString()), t);
        }
    }

    [Fact]
    public async Task Import_FiveTypes_Success()
    {
        var (db, svc) = New();
        var tag = Guid.NewGuid().ToString("N")[..8];

        var userBytes = BuildXlsx((2, new[] { $"u_{tag}", "13900000001", "导入用户", "2", "Admin123" }));
        var userRes = await svc.ImportAsync("user", userBytes, _fx.FactoryId);
        Assert.Equal(0, userRes.Data!.FailCount);
        Assert.Equal(1, userRes.Data.SuccessCount);

        var defectBytes = BuildXlsx((2, new[] { $"不良_{tag}" }));
        var defectRes = await svc.ImportAsync("defect", defectBytes, _fx.FactoryId);
        Assert.Equal(1, defectRes.Data!.SuccessCount);

        var opBytes = BuildXlsx((2, new[] { $"OP_{tag}", "导入工序", _fx.DeptCode, $"不良_{tag}" }));
        var opRes = await svc.ImportAsync("operation", opBytes, _fx.FactoryId);
        Assert.Equal(1, opRes.Data!.SuccessCount);
        Assert.True(await db.Operations.AnyAsync(o => o.FactoryId == _fx.FactoryId && o.Code == $"OP_{tag}"));
        Assert.True(await db.OperationDepartments.AnyAsync(od =>
            db.Operations.Any(o => o.Id == od.OperationId && o.Code == $"OP_{tag}")));

        var routingBytes = BuildXlsx((2, new[] { $"R_{tag}", "导入路线", $"OP_{tag}" }));
        var routingRes = await svc.ImportAsync("routing", routingBytes, _fx.FactoryId);
        Assert.Equal(1, routingRes.Data!.SuccessCount);

        var productBytes = BuildXlsx((2, new[] { $"P_{tag}", "导入产品", _fx.UnitName, $"R_{tag}", "", "2.5" }));
        var productRes = await svc.ImportAsync("product", productBytes, _fx.FactoryId);
        Assert.Equal(1, productRes.Data!.SuccessCount);
        Assert.True(await db.Products.AnyAsync(p => p.FactoryId == _fx.FactoryId && p.Code == $"P_{tag}"));
    }

    [Fact]
    public async Task Duplicate_Account_PartialSuccess()
    {
        var (db, svc) = New();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var exist = $"dup_{tag}";
        db.Users.Add(new SysUser
        {
            FactoryId = _fx.FactoryId, Account = exist, Name = "已有", Role = 2,
            Password = "x", Status = 1, CreatedAt = DateTime.Now
        });
        await db.SaveChangesAsync();

        var bytes = BuildXlsx(
            (2, new[] { exist, "13900000002", "重复", "2", "Admin123" }),
            (3, new[] { $"ok_{tag}", "13900000003", "成功", "2", "Admin123" }));
        var res = await svc.ImportAsync("user", bytes, _fx.FactoryId);
        Assert.Equal(1, res.Data!.SuccessCount);
        Assert.Equal(1, res.Data.FailCount);
        Assert.Contains(res.Data.Errors, e => e.StartsWith("第2行：") && e.Contains("已存在"));
        Assert.True(await db.Users.AnyAsync(u => u.FactoryId == _fx.FactoryId && u.Account == $"ok_{tag}"));
    }

    [Fact]
    public async Task Operation_BadDept_NoOrphan()
    {
        var (db, svc) = New();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var code = $"OP_BAD_{tag}";
        var bytes = BuildXlsx((2, new[] { code, "坏部门工序", "NO_SUCH_DEPT", "" }));
        var res = await svc.ImportAsync("operation", bytes, _fx.FactoryId);
        Assert.Equal(0, res.Data!.SuccessCount);
        Assert.Equal(1, res.Data.FailCount);
        Assert.Contains(res.Data.Errors, e => e.Contains("报工部门编码不存在"));
        Assert.False(await db.Operations.AnyAsync(o => o.FactoryId == _fx.FactoryId && o.Code == code));
    }

    [Fact]
    public async Task Routing_BadOp_NoOrphan()
    {
        var (db, svc) = New();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var code = $"R_BAD_{tag}";
        var bytes = BuildXlsx((2, new[] { code, "坏工序路线", "NO_SUCH_OP" }));
        var res = await svc.ImportAsync("routing", bytes, _fx.FactoryId);
        Assert.Equal(0, res.Data!.SuccessCount);
        Assert.Equal(1, res.Data.FailCount);
        Assert.Contains(res.Data.Errors, e => e.Contains("工序编码不存在"));
        Assert.False(await db.Routings.AnyAsync(r => r.FactoryId == _fx.FactoryId && r.Code == code));
    }

    [Fact]
    public async Task Product_BadRouting_NoInsert()
    {
        var (db, svc) = New();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var code = $"P_BAD_{tag}";
        var bytes = BuildXlsx((2, new[] { code, "坏路线产品", _fx.UnitName, "NO_SUCH_R", "", "1" }));
        var res = await svc.ImportAsync("product", bytes, _fx.FactoryId);
        Assert.Equal(0, res.Data!.SuccessCount);
        Assert.Equal(1, res.Data.FailCount);
        Assert.Contains(res.Data.Errors, e => e.Contains("工艺路线编号不存在"));
        Assert.False(await db.Products.AnyAsync(p => p.FactoryId == _fx.FactoryId && p.Code == code));
    }

    [Fact]
    public async Task EmptyFile_Throws()
    {
        var (_, svc) = New();
        var bytes = BuildXlsx(); // 只有表头
        var ex = await Assert.ThrowsAsync<BusinessException>(() => svc.ImportAsync("user", bytes, _fx.FactoryId));
        Assert.Contains("没有可导入的数据行", ex.Message);
    }

    [Fact]
    public async Task BlankRows_ExcelLineNumberPreserved()
    {
        var (_, svc) = New();
        var tag = Guid.NewGuid().ToString("N")[..8];
        // 第2行成功，第3行空（跳过），第4行账号重复失败 → 错误应写第4行
        var exist = $"gap_{tag}";
        var (db, _) = New();
        db.Users.Add(new SysUser
        {
            FactoryId = _fx.FactoryId, Account = exist, Name = "已有", Role = 2,
            Password = "x", Status = 1, CreatedAt = DateTime.Now
        });
        await db.SaveChangesAsync();

        var bytes = BuildXlsx(
            (2, new[] { $"gap_ok_{tag}", "13900000004", "空行测试", "2", "Admin123" }),
            (4, new[] { exist, "13900000005", "应失败", "2", "Admin123" }));
        var res = await svc.ImportAsync("user", bytes, _fx.FactoryId);
        Assert.Equal(1, res.Data!.SuccessCount);
        Assert.Equal(1, res.Data.FailCount);
        Assert.Contains(res.Data.Errors, e => e.StartsWith("第4行："));
    }

    [Fact]
    public async Task EmptyAssociation_Allowed()
    {
        var (db, svc) = New();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var code = $"OP_EMPTY_{tag}";
        var bytes = BuildXlsx((2, new[] { code, "无关联工序", "", "" }));
        var res = await svc.ImportAsync("operation", bytes, _fx.FactoryId);
        Assert.Equal(1, res.Data!.SuccessCount);
        Assert.True(await db.Operations.AnyAsync(o => o.FactoryId == _fx.FactoryId && o.Code == code));
    }
}

[CollectionDefinition("import_db", DisableParallelization = true)]
public class ImportDbCollection : ICollectionFixture<ImportDbFixture> { }

public class ImportDbFixture : IDisposable
{
    public static string ConnString =>
        Environment.GetEnvironmentVariable("MES_TEST_DB")
        ?? "Server=localhost;Database=WorkshopMes_Test;Trusted_Connection=True;TrustServerCertificate=True";

    public long FactoryId;
    public string DeptCode = "IMP-DEPT";
    public string UnitName = "件";

    public ImportDbFixture()
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
        var fids = db.Factories.Where(f => f.FactoryCode == "TEST-IMP").Select(f => f.Id).ToList();
        if (fids.Count == 0) return;

        var opIds = db.Operations.Where(o => fids.Contains(o.FactoryId)).Select(o => o.Id).ToList();
        var routingIds = db.Routings.Where(r => fids.Contains(r.FactoryId)).Select(r => r.Id).ToList();
        if (opIds.Count > 0)
        {
            db.OperationDepartments.RemoveRange(db.OperationDepartments.Where(x => opIds.Contains(x.OperationId)));
            db.OperationDefects.RemoveRange(db.OperationDefects.Where(x => opIds.Contains(x.OperationId)));
        }
        if (routingIds.Count > 0)
            db.RoutingSteps.RemoveRange(db.RoutingSteps.Where(x => routingIds.Contains(x.RoutingId)));
        db.Products.RemoveRange(db.Products.Where(p => fids.Contains(p.FactoryId)));
        db.Routings.RemoveRange(db.Routings.Where(r => fids.Contains(r.FactoryId)));
        db.Operations.RemoveRange(db.Operations.Where(o => fids.Contains(o.FactoryId)));
        db.DefectItems.RemoveRange(db.DefectItems.Where(d => fids.Contains(d.FactoryId)));
        db.Units.RemoveRange(db.Units.Where(u => fids.Contains(u.FactoryId)));
        db.Departments.RemoveRange(db.Departments.Where(d => fids.Contains(d.FactoryId)));
        db.Users.RemoveRange(db.Users.Where(u => fids.Contains(u.FactoryId)));
        db.Factories.RemoveRange(db.Factories.Where(f => fids.Contains(f.Id)));
        db.SaveChanges();
    }

    private void Seed(AppDbContext db)
    {
        var now = DateTime.Now;
        var f = new SysFactory { FactoryCode = "TEST-IMP", FactoryName = "导入测试厂", CreatedAt = now };
        db.Factories.Add(f);
        db.SaveChanges();
        FactoryId = f.Id;

        db.Units.Add(new BaseUnit { FactoryId = f.Id, Name = UnitName, CreatedAt = now });
        db.Departments.Add(new SysDepartment { FactoryId = f.Id, Code = DeptCode, Name = "导入部门", CreatedAt = now });
        db.SaveChanges();
    }
}
