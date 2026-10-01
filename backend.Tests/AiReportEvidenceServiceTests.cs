using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;
using ahu.MicrosoftMes.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace WorkshopMes.Tests;

/// <summary>共享同一测试库种子，串行执行（避免并行 Reset/Seed 竞争）。</summary>
[CollectionDefinition("ai_db", DisableParallelization = true)]
public class AiDbCollectionDefinition : ICollectionFixture<AiEvidenceDbFixture> { }

/// <summary>
/// AiReportEvidenceService 集成测试：真实 SQL Server 测试库 WorkshopMes_Test（非模拟）。
/// 覆盖 docs/51 的 D01—D07 及参数校验。数据在独立工厂 TEST-EVID / TEST-EVID2 内，不触碰生产库。
/// 环境要求：本机 SQL Server（Trusted_Connection），连接串可用环境变量 MES_TEST_DB 覆盖。
/// </summary>
[Collection("ai_db")]
public class AiReportEvidenceServiceTests
{
    private readonly AiEvidenceDbFixture _fx;
    public AiReportEvidenceServiceTests(AiEvidenceDbFixture fx) { _fx = fx; }

    private static (AppDbContext db, AiReportEvidenceService svc) NewService()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(AiEvidenceDbFixture.ConnString)
            .Options;
        var db = new AppDbContext(opts);
        var workOrders = new WorkOrderService(db);
        var ai = Options.Create(new AiOptions { BusinessTimeZone = "Asia/Shanghai" });
        var svc = new AiReportEvidenceService(db, workOrders, ai, NullLogger<AiReportEvidenceService>.Instance);
        return (db, svc);
    }

    private static Task<AiReportEvidenceDto> Query(AiReportEvidenceService svc, long factoryId, long orderId,
        long? op = null, DateTime? from = null, DateTime? to = null, int page = 1, int pageSize = 50)
        => svc.GetEvidenceAsync(orderId, null, op, from, to, page, pageSize, factoryId, CancellationToken.None);

    // ---- D01：两工序有效良品 100/80 → 完成数 80（不能得 180） ----
    [Fact]
    public async Task D01_TwoOps_CompletionIsMin()
    {
        var (_, svc) = NewService();
        var r = await Query(svc, _fx.FactoryId, _fx.W1);
        Assert.Equal(80, r.Totals.DoneQty);
        Assert.Equal(2, r.Totals.Tasks.Count);
        Assert.Equal(100, r.Totals.Tasks.First(t => t.Seq == 1).DoneQty);
        Assert.Equal(80, r.Totals.Tasks.First(t => t.Seq == 2).DoneQty);
        Assert.Equal(100, r.Totals.Qty);
    }

    // ---- D02：第二工序 60 已通过＋20 待复核＋10 已退回 → 进度 80、通过口径 60、退回 10 排除 ----
    [Fact]
    public async Task D02_SecondOp_ProgressVsReportVsReject()
    {
        var (_, svc) = NewService();
        var r = await Query(svc, _fx.FactoryId, _fx.W2, op: _fx.Op2);
        var op2 = Assert.Single(r.Summary);
        Assert.Equal(60, op2.ApprovedGood);
        Assert.Equal(20, op2.PendingGood);
        Assert.Equal(10, op2.RejectedGood);
        Assert.Equal(80, op2.ProgressGood);   // 60 + 20
        Assert.Equal(60, op2.ReportGood);     // 仅已通过
        // 整单完成数 = min(工序1 100, 工序2 进度 80) = 80
        Assert.Equal(80, r.Totals.DoneQty);
    }

    // ---- D03：两工序，仅第一工序有报工 → 第二工序 0，完成数 0 ----
    [Fact]
    public async Task D03_SecondOpNoReport_CompletionZero()
    {
        var (_, svc) = NewService();
        var r = await Query(svc, _fx.FactoryId, _fx.W3);
        Assert.Equal(0, r.Totals.DoneQty);
        var op2 = r.Totals.Tasks.First(t => t.Seq == 2);
        Assert.Equal(0, op2.DoneQty);
    }

    // ---- D04：无工序任务工单、无报工工单、取消工单 ----
    [Fact]
    public async Task D04_NoTaskNoReportCancelled_Consistent()
    {
        var (_, svc) = NewService();
        // 无工序任务（产品无路线）→ 无任务行，完成数 0
        var w4 = await Query(svc, _fx.FactoryId, _fx.W4);
        Assert.Empty(w4.Totals.Tasks);
        Assert.Equal(0, w4.Totals.DoneQty);

        // 无报工工单（有任务行但 0 报工）→ 完成数 0
        var w8 = await Query(svc, _fx.FactoryId, _fx.W8);
        Assert.Equal(2, w8.Totals.Tasks.Count);
        Assert.Equal(0, w8.Totals.DoneQty);

        // 取消工单：状态保持 3，不伪造完成
        var w7 = await Query(svc, _fx.FactoryId, _fx.W7);
        Assert.Equal(3, w7.Order.Status);
    }

    // ---- D05：>50 条分页稳定不重漏；汇总/total 覆盖全量；批次仅按标记展示 ----
    [Fact]
    public async Task D05_PaginationStable_Over50()
    {
        var (_, svc) = NewService();

        var page1 = await Query(svc, _fx.FactoryId, _fx.W5, page: 1, pageSize: 20);
        var page2 = await Query(svc, _fx.FactoryId, _fx.W5, page: 2, pageSize: 20);
        var page3 = await Query(svc, _fx.FactoryId, _fx.W5, page: 3, pageSize: 20);

        Assert.Equal(55, page1.Total);
        var ids = page1.List.Concat(page2.List).Concat(page3.List).Select(x => x.Id).ToList();
        Assert.Equal(55, ids.Count);
        Assert.Equal(55, ids.Distinct().Count()); // 不重不漏

        // 汇总覆盖全量（不受分页影响）
        var s = Assert.Single(page1.Summary);
        Assert.Equal(55, s.ApprovedGood);
        Assert.Equal(3, s.ApprovedDefect);
        Assert.Equal(55, page1.Total);

        // 批次仅按现有标记展示：2 条带 BATCH-A，其余为空
        var all = page1.List.Concat(page2.List).Concat(page3.List).ToList();
        Assert.Equal(2, all.Count(x => x.BatchNo == "BATCH-A"));
        Assert.Equal(53, all.Count(x => x.BatchNo == null));
        Assert.Equal(3, all.Count(x => x.DefectName != null));
    }

    // ---- D06：起点包含、终点排除、跨日记录 ----
    [Fact]
    public async Task D06_DateHalfOpenInterval()
    {
        var (_, svc) = NewService();
        var from = new DateTime(2026, 9, 1, 8, 0, 0);
        var to = new DateTime(2026, 9, 1, 12, 0, 0);
        var r = await Query(svc, _fx.FactoryId, _fx.W6, from: from, to: to);
        // 命中：08:00(含起点)、09:00；排除：07:59、12:00(恰终点)、13:00
        Assert.Equal(2, r.Total);
        Assert.All(r.List, x => Assert.True(x.ReportTime >= from && x.ReportTime < to));

        // 跨日：20:00 ~ 次日 02:00 半开区间
        var from2 = new DateTime(2026, 9, 1, 20, 0, 0);
        var to2 = new DateTime(2026, 9, 2, 2, 0, 0);
        var r2 = await Query(svc, _fx.FactoryId, _fx.W6, from: from2, to: to2);
        // 命中：23:00、次日 01:59；排除：次日 02:00(恰终点)
        Assert.Equal(2, r2.Total);
        Assert.All(r2.List, x => Assert.True(x.ReportTime >= from2 && x.ReportTime < to2));
    }

    // ---- D07：工厂隔离 + 同次证据内部一致 ----
    [Fact]
    public async Task D07_FactoryIsolation_AndConsistency()
    {
        var (_, svc) = NewService();
        // 工厂 A 查不到工厂 B 的工单
        await Assert.ThrowsAsync<BusinessException>(() => Query(svc, _fx.FactoryId, _fx.Factory2OrderId));

        // 同次证据内部一致：整单完成数、筛选内汇总、明细总数同源
        var r = await Query(svc, _fx.FactoryId, _fx.W2);
        var summaryGoodSum = r.Summary.Sum(x => x.PendingGood + x.ApprovedGood + x.RejectedGood);
        var detailGoodSum = r.List.Sum(x => x.GoodQty);
        Assert.Equal(detailGoodSum, summaryGoodSum); // 汇总与明细同一快照
        Assert.Equal(4, r.Total); // W2: op1 1 条 + op2 3 条
    }

    // ---- 参数校验 ----
    [Fact]
    public async Task Validation_RejectsBadInput()
    {
        var (_, svc) = NewService();
        await Assert.ThrowsAsync<BusinessException>(() => svc.GetEvidenceAsync(null, null, null, null, null, 1, 20, _fx.FactoryId, CancellationToken.None));
        await Assert.ThrowsAsync<BusinessException>(() => Query(svc, _fx.FactoryId, _fx.W1, op: 999999)); // 非法工序
        await Assert.ThrowsAsync<BusinessException>(() => Query(svc, _fx.FactoryId, _fx.W1, from: new DateTime(2026, 1, 2), to: new DateTime(2026, 1, 1))); // 起止倒置
        await Assert.ThrowsAsync<BusinessException>(() => Query(svc, _fx.FactoryId, _fx.W1, page: 0));
        await Assert.ThrowsAsync<BusinessException>(() => Query(svc, _fx.FactoryId, _fx.W1, pageSize: 51));
    }
}

public class AiEvidenceDbFixture : IDisposable
{
    public const string ConnString =
        "Server=localhost;Database=WorkshopMes_Test;Trusted_Connection=True;TrustServerCertificate=True";

    public long FactoryId;
    public long Factory2Id;
    public long Op1, Op2, W1, W2, W3, W4, W5, W6, W7, W8, Factory2OrderId;

    public AiEvidenceDbFixture()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnString).Options;
        using var db = new AppDbContext(opts);
        db.Database.EnsureCreated();
        Reset(db);
        Seed(db);
    }

    private static void Reset(AppDbContext db)
    {
        var codes = new[] { "TEST-EVID", "TEST-EVID2" };
        var fids = db.Factories.Where(f => codes.Contains(f.FactoryCode)).Select(f => f.Id).ToList();
        if (fids.Count == 0) return;
        var orderIds = db.WorkOrders.Where(o => fids.Contains(o.FactoryId)).Select(o => o.Id).ToList();
        var routingIds = db.Routings.Where(r => fids.Contains(r.FactoryId)).Select(r => r.Id).ToList();
        var opIds = db.Operations.Where(o => fids.Contains(o.FactoryId)).Select(o => o.Id).ToList();

        db.Reports.RemoveRange(db.Reports.Where(x => fids.Contains(x.FactoryId)));
        db.WorkOrderOperations.RemoveRange(db.WorkOrderOperations.Where(t => orderIds.Contains(t.WorkOrderId)));
        db.WorkOrders.RemoveRange(db.WorkOrders.Where(o => fids.Contains(o.FactoryId)));
        db.Products.RemoveRange(db.Products.Where(p => fids.Contains(p.FactoryId)));
        db.RoutingSteps.RemoveRange(db.RoutingSteps.Where(s => routingIds.Contains(s.RoutingId)));
        db.Routings.RemoveRange(db.Routings.Where(r => fids.Contains(r.FactoryId)));
        db.OperationDefects.RemoveRange(db.OperationDefects.Where(d => opIds.Contains(d.OperationId)));
        db.OperationDepartments.RemoveRange(db.OperationDepartments.Where(d => opIds.Contains(d.OperationId)));
        db.Operations.RemoveRange(db.Operations.Where(o => fids.Contains(o.FactoryId)));
        db.DefectItems.RemoveRange(db.DefectItems.Where(d => fids.Contains(d.FactoryId)));
        db.Units.RemoveRange(db.Units.Where(u => fids.Contains(u.FactoryId)));
        db.Users.RemoveRange(db.Users.Where(u => fids.Contains(u.FactoryId)));
        db.Factories.RemoveRange(db.Factories.Where(f => fids.Contains(f.Id)));
        db.SaveChanges();
    }

    private void Seed(AppDbContext db)
    {
        var now = DateTime.Now;

        var f = new SysFactory { FactoryCode = "TEST-EVID", FactoryName = "测试证据厂", CreatedAt = now };
        var f2 = new SysFactory { FactoryCode = "TEST-EVID2", FactoryName = "隔离对照厂", CreatedAt = now };
        db.Factories.AddRange(f, f2);
        db.SaveChanges();
        FactoryId = f.Id;
        Factory2Id = f2.Id;

        var unit = new BaseUnit { FactoryId = FactoryId, Name = "只", CreatedAt = now };
        db.Units.Add(unit);
        var dSticky = new BaseDefectItem { FactoryId = FactoryId, Name = "粘锅", CreatedAt = now };
        db.DefectItems.Add(dSticky);

        var op1 = new BaseOperation { FactoryId = FactoryId, Code = "TCUT", Name = "切菜", CreatedAt = now };
        var op2 = new BaseOperation { FactoryId = FactoryId, Code = "TCOOK", Name = "炒制", CreatedAt = now };
        db.Operations.AddRange(op1, op2);
        db.SaveChanges();
        Op1 = op1.Id; Op2 = op2.Id;

        var rt = new BaseRouting { FactoryId = FactoryId, Code = "TRT", Name = "测试路线", CreatedAt = now };
        db.Routings.Add(rt);
        db.SaveChanges();
        db.RoutingSteps.AddRange(
            new BaseRoutingStep { RoutingId = rt.Id, OperationId = op1.Id, Seq = 1 },
            new BaseRoutingStep { RoutingId = rt.Id, OperationId = op2.Id, Seq = 2 });

        var prodRouted = new BaseProduct { FactoryId = FactoryId, Code = "TP-001", Name = "测试产品", UnitId = unit.Id, RoutingId = rt.Id, CreatedAt = now };
        var prodNoRoute = new BaseProduct { FactoryId = FactoryId, Code = "TP-002", Name = "无路线产品", CreatedAt = now };
        db.Products.AddRange(prodRouted, prodNoRoute);

        var admin = new SysUser { FactoryId = FactoryId, Account = "tadmin", Name = "测试管理员", Role = 1, Password = "x", Status = 1, CreatedAt = now };
        var worker = new SysUser { FactoryId = FactoryId, Account = "tworker", Name = "测试工人", Role = 2, Password = "x", Status = 1, CreatedAt = now };
        db.Users.AddRange(admin, worker);
        db.SaveChanges();

        // 工厂 B：一套独立数据（隔离对照）
        var unit2 = new BaseUnit { FactoryId = Factory2Id, Name = "只", CreatedAt = now };
        var op2b = new BaseOperation { FactoryId = Factory2Id, Code = "BOP", Name = "B工序", CreatedAt = now };
        var rt2 = new BaseRouting { FactoryId = Factory2Id, Code = "BRT", Name = "B路线", CreatedAt = now };
        db.Units.Add(unit2);
        db.Operations.Add(op2b);
        db.Routings.Add(rt2);
        db.SaveChanges();
        db.RoutingSteps.Add(new BaseRoutingStep { RoutingId = rt2.Id, OperationId = op2b.Id, Seq = 1 });
        var prod2b = new BaseProduct { FactoryId = Factory2Id, Code = "TP2-001", Name = "B产品", UnitId = unit2.Id, RoutingId = rt2.Id, CreatedAt = now };
        var user2b = new SysUser { FactoryId = Factory2Id, Account = "badmin", Name = "B管理员", Role = 1, Password = "x", Status = 1, CreatedAt = now };
        db.Products.Add(prod2b);
        db.Users.Add(user2b);
        db.SaveChanges();

        var w2b = new ProdWorkOrder { FactoryId = Factory2Id, OrderNo = "EV2-001", ProductId = prod2b.Id, Qty = 10, Status = 1, CreatedBy = user2b.Id, CreatedAt = now };
        db.WorkOrders.Add(w2b);
        db.SaveChanges();
        db.Reports.Add(new ProdReport { FactoryId = Factory2Id, OrderId = w2b.Id, OperationId = op2b.Id, UserId = user2b.Id, GoodQty = 5, ReviewStatus = 1, ReportTime = now });
        db.SaveChanges();
        Factory2OrderId = w2b.Id;

        // W1（D01）：两工序 100/80
        W1 = MakeOrder(db, "EV001", prodRouted.Id, 100, admin.Id, status: 1);
        AddTask(db, W1, op1.Id, 1, 100);
        AddTask(db, W1, op2.Id, 2, 100);
        AddReport(db, W1, op1.Id, worker.Id, 100, 0, null, 1, "2026-08-01 10:00");
        AddReport(db, W1, op2.Id, worker.Id, 80, 0, null, 1, "2026-08-01 11:00");

        // W2（D02）：工序1 100；工序2 60已通过+20待复核+10已退回
        W2 = MakeOrder(db, "EV002", prodRouted.Id, 100, admin.Id, status: 1);
        AddTask(db, W2, op1.Id, 1, 100);
        AddTask(db, W2, op2.Id, 2, 100);
        AddReport(db, W2, op1.Id, worker.Id, 100, 0, null, 1, "2026-08-02 10:00");
        AddReport(db, W2, op2.Id, worker.Id, 60, 0, null, 1, "2026-08-02 11:00");
        AddReport(db, W2, op2.Id, worker.Id, 20, 0, null, 0, "2026-08-02 11:30");
        AddReport(db, W2, op2.Id, worker.Id, 10, 0, null, 2, "2026-08-02 12:00", rejectReason: "不合格");

        // W3（D03）：仅工序1 有报工
        W3 = MakeOrder(db, "EV003", prodRouted.Id, 100, admin.Id, status: 1);
        AddTask(db, W3, op1.Id, 1, 100);
        AddTask(db, W3, op2.Id, 2, 100);
        AddReport(db, W3, op1.Id, worker.Id, 100, 0, null, 1, "2026-08-03 10:00");

        // W4（D04）：无路线产品、无任务、无报工
        W4 = MakeOrder(db, "EV004", prodNoRoute.Id, 50, admin.Id, status: 0);

        // W5（D05）：55 条报工，3 条含不良，2 条补报批次
        W5 = MakeOrder(db, "EV005", prodRouted.Id, 200, admin.Id, status: 1);
        AddTask(db, W5, op1.Id, 1, 200);
        for (var i = 0; i < 55; i++)
        {
            var hasDefect = i < 3;
            var batch = i >= 53 ? "BATCH-A" : null;
            AddReport(db, W5, op1.Id, worker.Id, 1, hasDefect ? 1 : 0, hasDefect ? dSticky.Id : null, 1,
                $"2026-08-04 08:{i % 60:00}", batchNo: batch);
        }

        // W6（D06）：边界时间记录
        W6 = MakeOrder(db, "EV006", prodRouted.Id, 100, admin.Id, status: 1);
        AddTask(db, W6, op1.Id, 1, 100);
        AddReport(db, W6, op1.Id, worker.Id, 1, 0, null, 1, "2026-09-01 07:59"); // 起点前
        AddReport(db, W6, op1.Id, worker.Id, 1, 0, null, 1, "2026-09-01 08:00"); // 恰起点
        AddReport(db, W6, op1.Id, worker.Id, 1, 0, null, 1, "2026-09-01 09:00");
        AddReport(db, W6, op1.Id, worker.Id, 1, 0, null, 1, "2026-09-01 12:00"); // 恰终点（排除）
        AddReport(db, W6, op1.Id, worker.Id, 1, 0, null, 1, "2026-09-01 13:00"); // 终点后
        AddReport(db, W6, op1.Id, worker.Id, 1, 0, null, 1, "2026-09-01 23:00"); // 跨日
        AddReport(db, W6, op1.Id, worker.Id, 1, 0, null, 1, "2026-09-02 01:59"); // 跨日
        AddReport(db, W6, op1.Id, worker.Id, 1, 0, null, 1, "2026-09-02 02:00"); // 跨日恰终点（排除）

        // W7（D04）：取消工单
        W7 = MakeOrder(db, "EV007", prodRouted.Id, 100, admin.Id, status: 3);
        AddTask(db, W7, op1.Id, 1, 100);
        AddTask(db, W7, op2.Id, 2, 100);
        AddReport(db, W7, op1.Id, worker.Id, 30, 0, null, 1, "2026-08-05 10:00");

        // W8（D04）：有任务行但无报工
        W8 = MakeOrder(db, "EV008", prodRouted.Id, 100, admin.Id, status: 0);
        AddTask(db, W8, op1.Id, 1, 100);
        AddTask(db, W8, op2.Id, 2, 100);

        db.SaveChanges();
    }

    private long MakeOrder(AppDbContext db, string no, long productId, int qty, long createdBy, byte status)
    {
        var o = new ProdWorkOrder { FactoryId = FactoryId, OrderNo = no, ProductId = productId, Qty = qty, Status = status, CreatedBy = createdBy, CreatedAt = DateTime.Now };
        db.WorkOrders.Add(o);
        db.SaveChanges();
        return o.Id;
    }

    private static void AddTask(AppDbContext db, long orderId, long opId, int seq, int planQty)
        => db.WorkOrderOperations.Add(new ProdWorkOrderOperation { WorkOrderId = orderId, OperationId = opId, Seq = seq, PlanQty = planQty });

    private void AddReport(AppDbContext db, long orderId, long opId, long userId, int good, int defect,
        long? defectId, byte reviewStatus, string reportTime, string? rejectReason = null, string? batchNo = null)
    {
        db.Reports.Add(new ProdReport
        {
            FactoryId = FactoryId,
            OrderId = orderId,
            OperationId = opId,
            UserId = userId,
            GoodQty = good,
            DefectQty = defect,
            DefectId = defectId,
            ReviewStatus = reviewStatus,
            RejectReason = rejectReason,
            BatchNo = batchNo,
            ReportTime = DateTime.Parse(reportTime)
        });
    }

    public void Dispose() { }
}
