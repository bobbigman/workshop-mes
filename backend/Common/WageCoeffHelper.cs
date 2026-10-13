namespace ahu.MicrosoftMes.Common;

/// <summary>人级工价系数（docs/225）。默认 1，不绑人员类型。</summary>
public static class WageCoeffHelper
{
    public const decimal Default = 1m;

    /// <summary>空当 1。有值须在 0.10～9.99，最多两位小数。</summary>
    public static decimal Normalize(decimal? raw, string where)
    {
        if (raw == null) return Default;
        var v = raw.Value;
        if (v < 0.10m || v > 9.99m)
            throw ThrowHelper.Biz(where, "工价系数须在 0.10～9.99 之间");
        if (v != decimal.Round(v, 2, MidpointRounding.AwayFromZero))
            throw ThrowHelper.Biz(where, "工价系数最多两位小数");
        return v;
    }

    /// <summary>库内值：≤0 当 1（防御缺列或脏数据）。</summary>
    public static decimal Coerce(decimal stored) => stored > 0m ? stored : Default;
}
