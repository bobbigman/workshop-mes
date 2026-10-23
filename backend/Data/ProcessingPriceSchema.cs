namespace ahu.MicrosoftMes.Data;

public static class ProcessingPriceSchema
{
    public const string UpgradeSql = """
        IF COL_LENGTH('base_operation', 'pricing_config') IS NULL
            ALTER TABLE base_operation ADD pricing_config NVARCHAR(MAX) NULL;
        IF OBJECT_ID('prod_work_order_operation', 'U') IS NOT NULL AND COL_LENGTH('prod_work_order_operation', 'processing_price') IS NULL
            ALTER TABLE prod_work_order_operation ADD processing_price NVARCHAR(MAX) NULL;
        """;
}
