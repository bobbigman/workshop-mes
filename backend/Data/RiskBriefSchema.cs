namespace ahu.MicrosoftMes.Data;

/// <summary>252：与数据字典一致的幂等增量，启动兼容与EF迁移共用。</summary>
public static class RiskBriefSchema
{
    public const string UpgradeSql = """
IF OBJECT_ID(N'dbo.base_product', N'U') IS NOT NULL
    AND COL_LENGTH(N'dbo.base_product', N'is_risk_focus') IS NULL
BEGIN
    ALTER TABLE dbo.base_product ADD is_risk_focus BIT NOT NULL
        CONSTRAINT DF_base_product_risk_focus DEFAULT 0 WITH VALUES;
END;
""";
}
