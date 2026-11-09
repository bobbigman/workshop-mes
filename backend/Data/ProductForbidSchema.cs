namespace ahu.MicrosoftMes.Data;

/// <summary>
/// 产品禁用/审核：status、audit_status 及人时痕迹。
/// 历史行：status=1、audit_status=1，避免打断现网建单。
/// </summary>
public static class ProductForbidSchema
{
    public const string UpgradeSql = """
IF OBJECT_ID(N'dbo.base_product', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.base_product', N'status') IS NULL
        ALTER TABLE dbo.base_product ADD status TINYINT NOT NULL
            CONSTRAINT DF_base_product_status DEFAULT 1 WITH VALUES;

    IF COL_LENGTH(N'dbo.base_product', N'audit_status') IS NULL
        ALTER TABLE dbo.base_product ADD audit_status TINYINT NOT NULL
            CONSTRAINT DF_base_product_audit_status DEFAULT 1 WITH VALUES;

    IF COL_LENGTH(N'dbo.base_product', N'audited_by') IS NULL
        ALTER TABLE dbo.base_product ADD audited_by BIGINT NULL;
    IF COL_LENGTH(N'dbo.base_product', N'audited_at') IS NULL
        ALTER TABLE dbo.base_product ADD audited_at DATETIME2 NULL;
    IF COL_LENGTH(N'dbo.base_product', N'forbid_by') IS NULL
        ALTER TABLE dbo.base_product ADD forbid_by BIGINT NULL;
    IF COL_LENGTH(N'dbo.base_product', N'forbid_at') IS NULL
        ALTER TABLE dbo.base_product ADD forbid_at DATETIME2 NULL;
    IF COL_LENGTH(N'dbo.base_product', N'unforbid_by') IS NULL
        ALTER TABLE dbo.base_product ADD unforbid_by BIGINT NULL;
    IF COL_LENGTH(N'dbo.base_product', N'unforbid_at') IS NULL
        ALTER TABLE dbo.base_product ADD unforbid_at DATETIME2 NULL;
END;
""";
}
