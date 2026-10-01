using ahu.MicrosoftMes.Common;
using Xunit;

namespace WorkshopMes.Tests;

public class WorkOrderProgressCalculatorTests
{
    private static WorkOrderProgressCalculator.OpQty Op(long id, int plan, int done, int defect = 0) =>
        new() { OperationId = id, PlanQty = plan, DoneQty = done, DefectQty = defect };

    [Fact]
    public void Three_Ops_Same_Plan_Min_Done_Is_Progress()
    {
        // 100 件，三道计划均 100，良品 100/80/20 → 完成 20，工序达成 20%
        var ops = new[] { Op(1, 100, 100), Op(2, 100, 80), Op(3, 100, 20) };
        var r = WorkOrderProgressCalculator.Calc(ops, 100);
        Assert.Equal(20, r.DoneQty);
        Assert.Equal(80, r.RemainQty);
        Assert.Equal(20, r.ProgressPercent);
        Assert.Null(r.ProgressHint);
    }

    [Fact]
    public void Different_Plan_Uses_Min_Ratio_Not_Order_Qty()
    {
        // 计划 100/80，良品 100/80 → 工序达成 100%，完成数仍为 min(100,80)=80
        var ops = new[] { Op(1, 100, 100), Op(2, 80, 80) };
        var r = WorkOrderProgressCalculator.Calc(ops, 100);
        Assert.Equal(80, r.DoneQty);
        Assert.Equal(100, r.ProgressPercent);
    }

    [Fact]
    public void Review_Pending_Counts_In_Progress_Track()
    {
        // 待复核 30 + 通过 20 = 现场 50；退回不在入参（调用方已排除）
        var ops = new[] { Op(1, 100, 50) };
        var r = WorkOrderProgressCalculator.Calc(ops, 100);
        Assert.Equal(50, r.DoneQty);
        Assert.Equal(50, r.ProgressPercent);
    }

    [Fact]
    public void Defect_Only_Is_Partial_Not_Unreported()
    {
        Assert.Equal(WorkOrderProgressCalculator.OpPartial,
            WorkOrderProgressCalculator.OpStatus(100, 0, 5));
        Assert.Equal(WorkOrderProgressCalculator.OpNotReported,
            WorkOrderProgressCalculator.OpStatus(100, 0, 0));
        Assert.Equal(WorkOrderProgressCalculator.OpFull,
            WorkOrderProgressCalculator.OpStatus(100, 100, 0));
    }

    [Fact]
    public void Zero_Plan_Excluded_From_Percent()
    {
        var ops = new[] { Op(1, 0, 0), Op(2, 100, 40) };
        var r = WorkOrderProgressCalculator.Calc(ops, 100);
        Assert.Equal(0, r.DoneQty); // min(0,40)=0
        Assert.Equal(40, r.ProgressPercent);
        Assert.Equal(WorkOrderProgressCalculator.OpZeroPlan,
            WorkOrderProgressCalculator.OpStatus(0, 0, 0));
    }

    [Fact]
    public void All_Zero_Plan_Hint_No_Valid_Plan()
    {
        var ops = new[] { Op(1, 0, 0), Op(2, 0, 0) };
        var r = WorkOrderProgressCalculator.Calc(ops, 100);
        Assert.Null(r.ProgressPercent);
        Assert.Equal(WorkOrderProgressCalculator.HintNoValidPlan, r.ProgressHint);
    }

    [Fact]
    public void No_Ops_Hint_No_Plan()
    {
        var r = WorkOrderProgressCalculator.Calc(Array.Empty<WorkOrderProgressCalculator.OpQty>(), 100);
        Assert.Equal(0, r.DoneQty);
        Assert.Equal(100, r.RemainQty);
        Assert.Null(r.ProgressPercent);
        Assert.Equal(WorkOrderProgressCalculator.HintNoOps, r.ProgressHint);
    }

    [Fact]
    public void Negative_Plan_Throws()
    {
        var ops = new[] { Op(1, -1, 0) };
        Assert.Throws<BusinessException>(() => WorkOrderProgressCalculator.Calc(ops, 100));
    }

    [Fact]
    public void Finished_But_Not_Full_Keeps_Real_Percent()
    {
        // 手动结束不改百分比
        var ops = new[] { Op(1, 100, 30), Op(2, 100, 30) };
        var r = WorkOrderProgressCalculator.Calc(ops, 100);
        Assert.Equal(30, r.ProgressPercent);
        Assert.Equal(30, r.DoneQty);
    }
}
