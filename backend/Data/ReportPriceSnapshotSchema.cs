namespace ahu.MicrosoftMes.Data;

public static class ReportPriceSnapshotSchema
{
    public const string UpgradeSql = """
        IF COL_LENGTH('prod_report', 'price_snapshot') IS NULL
            ALTER TABLE prod_report ADD price_snapshot NVARCHAR(MAX) NULL;
        """;
}
