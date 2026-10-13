using ahu.MicrosoftMes.Common;

namespace ahu.MicrosoftMes.Services;

/// <summary>业务时区日期区间（docs/26：半开区间，不复用 DueStateHelper）。</summary>
public static class AiBusinessTime
{
    public static TimeZoneInfo Resolve(string? configured)
    {
        var id = string.IsNullOrWhiteSpace(configured) ? "Asia/Shanghai" : configured.Trim();
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            // Windows 常见映射
            if (string.Equals(id, "Asia/Shanghai", StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, "Asia/Chongqing", StringComparison.OrdinalIgnoreCase))
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time"); }
                catch (TimeZoneNotFoundException)
                {
                    throw ThrowHelper.Biz(nameof(Resolve), $"无法解析业务时区：{id}");
                }
            }
            throw ThrowHelper.Biz(nameof(Resolve), $"无法解析业务时区：{id}");
        }
        catch (InvalidTimeZoneException ex)
        {
            throw ThrowHelper.Biz(nameof(Resolve), $"业务时区无效：{id}（{ex.Message}）");
        }
    }

    public static DateTime TodayStart(TimeZoneInfo tz)
    {
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var startLocal = localNow.Date;
        return DateTime.SpecifyKind(startLocal, DateTimeKind.Unspecified);
    }

    /// <summary>相对词 → [from, toExclusive)。返回业务时区下的 Unspecified 日期时刻，用于和 due_date 比较。</summary>
    public static (DateTime From, DateTime ToExclusive, string Label) ResolveRelativeDueRange(
        string relative, TimeZoneInfo tz)
    {
        var today = TodayStart(tz);
        var key = relative.Trim().ToLowerInvariant();
        return key switch
        {
            "today" or "今天" => (today, today.AddDays(1), today.ToString("yyyy-MM-dd")),
            "tomorrow" or "明天" => (today.AddDays(1), today.AddDays(2), today.AddDays(1).ToString("yyyy-MM-dd")),
            "overdue" or "逾期" or "已逾期" => (DateTime.MinValue.AddDays(1), today, $"早于 {today:yyyy-MM-dd}"),
            _ => throw ThrowHelper.Biz(nameof(ResolveRelativeDueRange),
                $"不支持的相对交期：{relative}。请使用今天/明天/逾期或具体日期")
        };
    }

    public static string FormatDate(DateTime? dt)
        => dt == null ? "" : dt.Value.ToString("yyyy-MM-dd");
}
