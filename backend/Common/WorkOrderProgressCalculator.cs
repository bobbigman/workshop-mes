namespace ahu.MicrosoftMes.Common;

/// <summary>
/// 工单现场进度统一计算（列表 / 看板 / 执行监控共用）。
/// 进度轨：review_status=0/1（排除退回）；正式报表/工资仍仅 =1，不在此改。
/// </summary>
public static class WorkOrderProgressCalculator
{
    public const string HintNoOps = "无工序计划";
    public const string HintNoValidPlan = "暂无有效计划";
    public const string OpZeroPlan = "无需报工";
    public const string OpNotReported = "未报工";
    public const string OpPartial = "已报未满";
    public const string OpFull = "已报满";

    /// <summary>单道工序输入（已按工单+工序汇总良品/不良）。</summary>
    public sealed class OpQty
    {
        public long OperationId { get; set; }
        public int PlanQty { get; set; }
        public int DoneQty { get; set; }
        public int DefectQty { get; set; }
    }

    public sealed class Result
    {
        /// <summary>有工序时：各工序良品最小值；无工序：0（不再用跨工序总和伪造成品完成数）。</summary>
        public int DoneQty { get; set; }
        public int RemainQty { get; set; }
        /// <summary>正计划工序 done/plan 最小值，限制 0～100；无则 null。</summary>
        public int? ProgressPercent { get; set; }
        /// <summary>百分比为空时的展示说明。</summary>
        public string? ProgressHint { get; set; }
    }

    /// <summary>非法负数计划按业务异常报告，不静默生成正常进度。</summary>
    public static void EnsurePlansLegal(IEnumerable<OpQty> ops, string where)
    {
        foreach (var op in ops)
        {
            if (op.PlanQty < 0)
                throw ThrowHelper.Biz(where, $"工序计划数非法（负数）：operationId={op.OperationId}, planQty={op.PlanQty}");
            if (op.DoneQty < 0 || op.DefectQty < 0)
                throw ThrowHelper.Biz(where, $"报工汇总数量非法：operationId={op.OperationId}");
        }
    }

    public static Result Calc(IReadOnlyList<OpQty> ops, int orderQty, string where = nameof(Calc))
    {
        EnsurePlansLegal(ops, where);

        if (ops.Count == 0)
        {
            return new Result
            {
                DoneQty = 0,
                RemainQty = Math.Max(0, orderQty),
                ProgressPercent = null,
                ProgressHint = HintNoOps
            };
        }

        var doneQty = ops.Min(o => o.DoneQty);
        var remain = Math.Max(0, orderQty - doneQty);
        var positive = ops.Where(o => o.PlanQty > 0).ToList();
        if (positive.Count == 0)
        {
            return new Result
            {
                DoneQty = doneQty,
                RemainQty = remain,
                ProgressPercent = null,
                ProgressHint = HintNoValidPlan
            };
        }

        var ratio = positive.Min(o => (double)o.DoneQty / o.PlanQty);
        var pct = (int)Math.Round(Math.Clamp(ratio * 100.0, 0, 100), MidpointRounding.AwayFromZero);
        return new Result
        {
            DoneQty = doneQty,
            RemainQty = remain,
            ProgressPercent = pct,
            ProgressHint = null
        };
    }

    /// <summary>
    /// 工序展示状态。仅不良也算已报未满；零计划显示无需报工。
    /// </summary>
    public static string OpStatus(int planQty, int doneQty, int defectQty, string where = nameof(OpStatus))
    {
        if (planQty < 0)
            throw ThrowHelper.Biz(where, $"工序计划数非法（负数）：planQty={planQty}");
        if (planQty == 0)
            return OpZeroPlan;
        if (doneQty >= planQty)
            return OpFull;
        if (doneQty > 0 || defectQty > 0)
            return OpPartial;
        return OpNotReported;
    }
}
