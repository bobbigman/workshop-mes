using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace WorkshopMes.Tests;

/// <summary>报工权限：用户部门集合 ∩ 工序权限部门集合（docs/160 回归）。</summary>
[Collection("import_db")]
public class ReportPermissionHelperTests
{
    private readonly ImportDbFixture _fx;
    public ReportPermissionHelperTests(ImportDbFixture fx) { _fx = fx; }

    private AppDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ImportDbFixture.ConnString)
            .Options;
        return new AppDbContext(opts);
    }

    [Fact]
    public async Task Intersection_NonEmpty_Allows_Empty_Denies_AdminExempt()
    {
        await using var db = NewDb();
        var now = DateTime.Now;
        var tag = Guid.NewGuid().ToString("N")[..8];

        var sew = new SysDepartment { FactoryId = _fx.FactoryId, Code = $"SEW_{tag}", Name = "车缝", CreatedAt = now };
        var pack = new SysDepartment { FactoryId = _fx.FactoryId, Code = $"PKG_{tag}", Name = "包装", CreatedAt = now };
        var qc = new SysDepartment { FactoryId = _fx.FactoryId, Code = $"QC_{tag}", Name = "质检", CreatedAt = now };
        db.Departments.AddRange(sew, pack, qc);
        await db.SaveChangesAsync();

        var worker = new SysUser
        {
            FactoryId = _fx.FactoryId, Account = $"w_{tag}", Name = "张三", Role = 2,
            Password = "x", Status = 1, CreatedAt = now
        };
        var admin = new SysUser
        {
            FactoryId = _fx.FactoryId, Account = $"a_{tag}", Name = "管理员", Role = 1,
            Password = "x", Status = 1, CreatedAt = now
        };
        db.Users.AddRange(worker, admin);
        await db.SaveChangesAsync();

        db.DepartmentUsers.AddRange(
            new SysDepartmentUser { DepartmentId = sew.Id, UserId = worker.Id },
            new SysDepartmentUser { DepartmentId = pack.Id, UserId = worker.Id });
        await db.SaveChangesAsync();

        var opA = new BaseOperation { FactoryId = _fx.FactoryId, Code = $"OPA_{tag}", Name = "工序A", CreatedAt = now };
        var opB = new BaseOperation { FactoryId = _fx.FactoryId, Code = $"OPB_{tag}", Name = "工序B", CreatedAt = now };
        db.Operations.AddRange(opA, opB);
        await db.SaveChangesAsync();

        db.OperationDepartments.AddRange(
            new BaseOperationDepartment { OperationId = opA.Id, DepartmentId = sew.Id },
            new BaseOperationDepartment { OperationId = opB.Id, DepartmentId = qc.Id });
        await db.SaveChangesAsync();

        Assert.True(await ReportPermissionHelper.CanReportOperationAsync(db, worker.Id, 2, opA.Id));
        Assert.False(await ReportPermissionHelper.CanReportOperationAsync(db, worker.Id, 2, opB.Id));
        Assert.True(await ReportPermissionHelper.CanReportOperationAsync(db, admin.Id, 1, opB.Id));
    }
}
