namespace ahu.MicrosoftMes.Common;

/// <summary>人员类型口径（docs/161）：0内部 / 1临时 / 2外部。</summary>
public static class UserTypeHelper
{
    public const byte Internal = 0;
    public const byte Temp = 1;
    public const byte External = 2;

    public const string ExternalLoginDeniedMsg = "外部供应商账号不开放登录，由管理员代报工";

    public static string Label(byte userType) => userType switch
    {
        Temp => "临时用工",
        External => "外部供应商",
        _ => "内部员工"
    };

    /// <summary>JWT 签发前拦截：外部供应商不可登录（含账号密码 / 选厂 / 个人码）。</summary>
    public static void EnsureCanLogin(byte userType)
    {
        if (userType == External)
            throw ThrowHelper.BizUser(ExternalLoginDeniedMsg);
    }

    public static void EnsureValid(byte userType, string where)
    {
        if (userType is not (Internal or Temp or External))
            throw ThrowHelper.Biz(where, "人员类型无效：须为 0内部员工 / 1临时用工 / 2外部供应商");
    }

    /// <summary>导入列解析：空→内部；内部/临时/外部及全称/数字；无法识别整行报错。</summary>
    public static byte ParseImport(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Internal;

        var s = raw.Trim();
        if (s is "0" or "内部" or "内部员工")
            return Internal;
        if (s is "1" or "临时" or "临时用工")
            return Temp;
        if (s is "2" or "外部" or "外部供应商")
            return External;

        throw ThrowHelper.Biz(nameof(ParseImport), $"人员类型无法识别：{s}（请填 内部/临时/外部）");
    }
}
