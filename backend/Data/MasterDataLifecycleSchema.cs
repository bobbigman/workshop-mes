namespace ahu.MicrosoftMes.Data;

/// <summary>
/// 基础资料生命周期列：status / audit_status / 审核与禁用人时。
/// 历史行 status=1、audit_status=1，避免打断现网引用。
/// </summary>
public static class MasterDataLifecycleSchema
{
    public static string UpgradeTableSql(string table) => $"""
IF OBJECT_ID(N'dbo.{table}', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.{table}', N'status') IS NULL
        ALTER TABLE dbo.{table} ADD status TINYINT NOT NULL
            CONSTRAINT DF_{table}_status DEFAULT 1 WITH VALUES;
    IF COL_LENGTH(N'dbo.{table}', N'audit_status') IS NULL
        ALTER TABLE dbo.{table} ADD audit_status TINYINT NOT NULL
            CONSTRAINT DF_{table}_audit_status DEFAULT 1 WITH VALUES;
    IF COL_LENGTH(N'dbo.{table}', N'audited_by') IS NULL
        ALTER TABLE dbo.{table} ADD audited_by BIGINT NULL;
    IF COL_LENGTH(N'dbo.{table}', N'audited_at') IS NULL
        ALTER TABLE dbo.{table} ADD audited_at DATETIME2 NULL;
    IF COL_LENGTH(N'dbo.{table}', N'forbid_by') IS NULL
        ALTER TABLE dbo.{table} ADD forbid_by BIGINT NULL;
    IF COL_LENGTH(N'dbo.{table}', N'forbid_at') IS NULL
        ALTER TABLE dbo.{table} ADD forbid_at DATETIME2 NULL;
    IF COL_LENGTH(N'dbo.{table}', N'unforbid_by') IS NULL
        ALTER TABLE dbo.{table} ADD unforbid_by BIGINT NULL;
    IF COL_LENGTH(N'dbo.{table}', N'unforbid_at') IS NULL
        ALTER TABLE dbo.{table} ADD unforbid_at DATETIME2 NULL;
END;
""";

    public const string UpgradeSql = """
-- base_operation / base_routing（产品见 ProductForbidSchema）
""";

    public static readonly string OperationAndRoutingSql =
        UpgradeTableSql("base_operation") + "\n" + UpgradeTableSql("base_routing");
}