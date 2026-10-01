namespace ahu.MicrosoftMes.Common;

/// <summary>交期三态常量（对齐黑湖「正常 / 预警 / 延期」三态）。</summary>
public static class DueState
{
    public const string Normal = "normal";    // 正常
    public const string Warning = "warning";  // 预警（临期）
    public const string Overdue = "overdue";  // 延期（超期）
}

/// <summary>交期三态判定（工单列表 / 详情 / 看板共用）。</summary>
public static class DueStateHelper
{
    /// <summary>临期阈值（天）：≤ N 天到期算「预警」。</summary>
    public const int DueWarnDays = 3;

    /// <summary>
    /// 计算交期三态。
    /// 规则（仅未结束工单参与临期/超期）：
    ///   1. status ≥ 2（已结束/已取消）→ Normal
    ///   2. dueDate == null（未设交期）→ Normal
    ///   3. now &gt; dueDate → Overdue（已超期）
    ///   4. dueDate - now ≤ DueWarnDays 天 → Warning（临期，含当天到期）
    ///   5. 其余 → Normal
    /// </summary>
    public static string Calc(byte status, DateTime? dueDate, DateTime now)
    {
        if (status >= 2) return DueState.Normal;
        if (dueDate == null) return DueState.Normal;

        var span = dueDate.Value - now;
        if (span < TimeSpan.Zero) return DueState.Overdue;
        if (span.TotalDays <= DueWarnDays) return DueState.Warning;
        return DueState.Normal;
    }
}
