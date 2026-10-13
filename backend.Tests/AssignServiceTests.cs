using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;
using ahu.MicrosoftMes.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace WorkshopMes.Tests;

[Collection("assign_db")]
public class AssignServiceTests
{
    private readonly AssignDbFixture _fx;
    public AssignServiceTests(AssignDbFixture fx) { _fx = fx; }

    private static (AppDbContext db, AssignService svc) New()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(AssignDbFixture.ConnString)
            .Options;
        var db = new AppDbContext(opts);
        return (db, new AssignService(db));
    }

    [Fact]
    public async Task Assign_Then_MyTasks_Returns_Task_And_Done()
    {
        var (db, svc) = New();
        await svc.AssignAsync(_fx.TaskId, _fx.WorkerId, _fx.FactoryId, _fx.LeaderId);

        var t = await db.WorkOrderOperations.AsNoTracking().FirstAsync(x => x.Id == _fx.TaskId);
        Assert.Equal(_fx.WorkerId, t.AssigneeUserId);

        var mine = await svc.MyTasksAsync(_fx.FactoryId, _fx.WorkerId);
        var row = Assert.Single(mine.Data!);
        Assert.Equal(_fx.OrderNo, row.OrderNo);
        Assert.Equal("切料", row.OperationName);
        Assert.Equal(0, row.DoneQty);

        // 报一条良品后，剩余量应减少
        db.Reports.Add(new ProdReport
        {
            FactoryId = _fx.FactoryId, OrderId = _fx.OrderId, OperationId = _fx.OperationId,
            UserId = _fx.WorkerId, GoodQty = 3, DefectQty = 0, DurationMinutes = 10,
            ReportTime = DateTime.Now, ReviewStatus = 1
        });
        await db.SaveChangesAsync();

        var mine2 = await svc.MyTasksAsync(_fx.FactoryId, _fx.WorkerId);
        var row2 = Assert.Single(mine2.Data!);
        Assert.Equal(3, row2.DoneQty);
        Assert.Equal(_fx.PlanQty - 3, row2.RemainQty);
    }

    [Fact]
    public async Task Assign_Null_Cancels()
    {
        var (db, svc) = New();
        await svc.AssignAsync(_fx.TaskId, _fx.WorkerId, _fx.FactoryId, _fx.LeaderId);
        await svc.AssignAsync(_fx.TaskId, (long?)null, _fx.FactoryId, _fx.LeaderId);

        var t = await db.WorkOrderOperations.AsNoTracking().FirstAsync(x => x.Id == _fx.TaskId);
        Assert.Null(t.AssigneeUserId);

        var mine = await svc.MyTasksAsync(_fx.FactoryId, _fx.WorkerId);
        Assert.Empty(mine.Data!);
    }

    [Fact]
    public async Task Assign_OtherFactory_User_Rejected()
    {
        var (_, svc) = New();
        await Assert.ThrowsAsync<BusinessException>(() =>
            svc.AssignAsync(_fx.TaskId, _fx.OtherFactoryUserId, _fx.FactoryId, _fx.LeaderId));
    }

    [Fact]
    public async Task Assign_CrossFactory_Task_Rejected()
    {
        var (_, svc) = New();
        await Assert.ThrowsAsync<BusinessException>(() =>
            svc.AssignAsync(_fx.OtherFactoryTaskId, _fx.WorkerId, _fx.FactoryId, _fx.LeaderId));
    }

    [Fact]
    public async Task Assign_MissingTask_Rejected()
    {
        var (_, svc) = New();
        await Assert.ThrowsAsync<BusinessException>(() =>
            svc.AssignAsync(999999, _fx.WorkerId, _fx.FactoryId, _fx.LeaderId));
    }

    [Fact]
    public async Task Assign_Department_Then_MyTasks_For_Team_Member()
    {
        var (db, svc) = New();
        await svc.AssignAsync(_fx.TaskId, new AssignDto { DepartmentId = _fx.DeptId }, _fx.FactoryId, _fx.LeaderId);

        var t = await db.WorkOrderOperations.AsNoTracking().FirstAsync(x => x.Id == _fx.TaskId);
        Assert.Equal(_fx.DeptId, t.AssigneeDepartmentId);
        Assert.Null(t.AssigneeUserId);

        var mine = await svc.MyTasksAsync(_fx.FactoryId, _fx.WorkerId);
        var row = Assert.Single(mine.Data!);
        Assert.Equal("team", row.AssignSource);

        // 改派个人 → 班组清空
        await svc.AssignAsync(_fx.TaskId, _fx.WorkerId, _fx.FactoryId, _fx.LeaderId);
        t = await db.WorkOrderOperations.AsNoTracking().FirstAsync(x => x.Id == _fx.TaskId);
        Assert.Null(t.AssigneeDepartmentId);
        Assert.Equal(_fx.WorkerId, t.AssigneeUserId);
    }
}

[CollectionDefinition("assign_db", DisableParallelization = true)]
public class AssignDbCollection : ICollectionFixture<AssignDbFixture> { }

public class AssignDbFixture : IDisposable
{
    public static string ConnString =>
        Environment.GetEnvironmentVariable("MES_TEST_DB")
        ?? "Server=localhost;Database=WorkshopMes_Test;Trusted_Connection=True;TrustServerCertificate=True";

    public long FactoryId;
    public long LeaderId;
    public long WorkerId;
    public long OtherFactoryUserId;
    public long TaskId;
    public long OtherFactoryTaskId;
    public long OrderId;
    public long OperationId;
    public long DeptId;
    public long PlanQty;
    public string OrderNo = "";

    public AssignDbFixture()
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
        var codes = new[] { "TEST-ASG", "TEST-ASG2" };
        var fids = db.Factories.Where(f => codes.Contains(f.FactoryCode)).Select(f => f.Id).ToList();
        if (fids.Count == 0) return;
        var orderIds = db.WorkOrders.Where(o => fids.Contains(o.FactoryId)).Select(o => o.Id).ToList();
        var taskIds = db.WorkOrderOperations.Where(t => orderIds.Contains(t.WorkOrderId)).Select(t => t.Id).ToList();
        if (taskIds.Count > 0)
        {
            db.WorkOrderOperationAssignees.RemoveRange(
                db.WorkOrderOperationAssignees.Where(a => taskIds.Contains(a.WorkOrderOperationId)));
            db.WorkOrderOperations.RemoveRange(db.WorkOrderOperations.Where(t => taskIds.Contains(t.Id)));
        }
        if (orderIds.Count > 0)
        {
            db.Reports.RemoveRange(db.Reports.Where(r => orderIds.Contains(r.OrderId)));
            db.WorkOrders.RemoveRange(db.WorkOrders.Where(o => orderIds.Contains(o.Id)));
        }
        var opIds = db.Operations.Where(o => fids.Contains(o.FactoryId)).Select(o => o.Id).ToList();
        if (opIds.Count > 0)
            db.OperationDepartments.RemoveRange(db.OperationDepartments.Where(x => opIds.Contains(x.OperationId)));
        var deptIds = db.Departments.Where(d => fids.Contains(d.FactoryId)).Select(d => d.Id).ToList();
        if (deptIds.Count > 0)
            db.DepartmentUsers.RemoveRange(db.DepartmentUsers.Where(x => deptIds.Contains(x.DepartmentId)));
        db.Products.RemoveRange(db.Products.Where(p => fids.Contains(p.FactoryId)));
        db.Units.RemoveRange(db.Units.Where(u => fids.Contains(u.FactoryId)));
        db.Operations.RemoveRange(db.Operations.Where(o => fids.Contains(o.FactoryId)));
        db.Departments.RemoveRange(db.Departments.Where(d => fids.Contains(d.FactoryId)));
        db.Users.RemoveRange(db.Users.Where(u => fids.Contains(u.FactoryId)));
        db.Factories.RemoveRange(db.Factories.Where(f => fids.Contains(f.Id)));
        db.SaveChanges();
    }

    private void Seed(AppDbContext db)
    {
        var now = DateTime.Now;
        var f = new SysFactory { FactoryCode = "TEST-ASG", FactoryName = "派工厂", CreatedAt = now };
        var f2 = new SysFactory { FactoryCode = "TEST-ASG2", FactoryName = "对照厂", CreatedAt = now };
        db.Factories.AddRange(f, f2);
        db.SaveChanges();
        FactoryId = f.Id;

        var unit = new BaseUnit { FactoryId = f.Id, Name = "件", CreatedAt = now };
        db.Units.Add(unit);
        db.SaveChanges();

        var leader = new SysUser { FactoryId = f.Id, Account = "asg_leader", Name = "班组长", Role = 3, Password = "x", Status = 1, CreatedAt = now };
        var worker = new SysUser { FactoryId = f.Id, Account = "asg_worker", Name = "工人甲", Role = 2, Password = "x", Status = 1, CreatedAt = now };
        db.Users.AddRange(leader, worker);
        db.SaveChanges();
        LeaderId = leader.Id;
        WorkerId = worker.Id;

        var dept = new SysDepartment { FactoryId = f.Id, Code = "ASG-D1", Name = "切料班", CreatedAt = now };
        db.Departments.Add(dept);
        db.SaveChanges();
        DeptId = dept.Id;
        db.DepartmentUsers.AddRange(
            new SysDepartmentUser { DepartmentId = dept.Id, UserId = leader.Id },
            new SysDepartmentUser { DepartmentId = dept.Id, UserId = worker.Id });
        db.SaveChanges();

        var op = new BaseOperation { FactoryId = f.Id, Code = "ASG-CUT", Name = "切料", CreatedAt = now };
        db.Operations.Add(op);
        db.SaveChanges();
        OperationId = op.Id;
        db.OperationDepartments.Add(new BaseOperationDepartment { OperationId = op.Id, DepartmentId = dept.Id });
        db.SaveChanges();

        var prod = new BaseProduct { FactoryId = f.Id, Code = "ASG-P1", Name = "派工产品", UnitId = unit.Id, CreatedAt = now };
        db.Products.Add(prod);
        db.SaveChanges();

        var order = new ProdWorkOrder
        {
            FactoryId = f.Id, OrderNo = "ASG-001", ProductId = prod.Id, Qty = 20, Status = 1,
            CreatedBy = leader.Id, CreatedAt = now
        };
        db.WorkOrders.Add(order);
        db.SaveChanges();
        OrderId = order.Id;
        OrderNo = order.OrderNo;

        var task = new ProdWorkOrderOperation { WorkOrderId = order.Id, OperationId = op.Id, Seq = 1, PlanQty = 20 };
        db.WorkOrderOperations.Add(task);
        db.SaveChanges();
        TaskId = task.Id;
        PlanQty = task.PlanQty;

        // 对照厂：独立任务 + 用户，用于跨厂校验
        var unit2 = new BaseUnit { FactoryId = f2.Id, Name = "件", CreatedAt = now };
        db.Units.Add(unit2);
        db.SaveChanges();
        var op2 = new BaseOperation { FactoryId = f2.Id, Code = "ASG2-OP", Name = "他厂工序", CreatedAt = now };
        db.Operations.Add(op2);
        db.SaveChanges();
        var user2 = new SysUser { FactoryId = f2.Id, Account = "asg2_user", Name = "他厂人", Role = 2, Password = "x", Status = 1, CreatedAt = now };
        db.Users.Add(user2);
        db.SaveChanges();
        OtherFactoryUserId = user2.Id;

        var prod2 = new BaseProduct { FactoryId = f2.Id, Code = "ASG2-P1", Name = "他厂产品", UnitId = unit2.Id, CreatedAt = now };
        db.Products.Add(prod2);
        db.SaveChanges();
        var order2 = new ProdWorkOrder
        {
            FactoryId = f2.Id, OrderNo = "ASG2-001", ProductId = prod2.Id, Qty = 5, Status = 0,
            CreatedBy = user2.Id, CreatedAt = now
        };
        db.WorkOrders.Add(order2);
        db.SaveChanges();
        var task2 = new ProdWorkOrderOperation { WorkOrderId = order2.Id, OperationId = op2.Id, Seq = 1, PlanQty = 5 };
        db.WorkOrderOperations.Add(task2);
        db.SaveChanges();
        OtherFactoryTaskId = task2.Id;
    }
}
