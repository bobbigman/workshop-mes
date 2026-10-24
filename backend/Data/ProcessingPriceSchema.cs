namespace ahu.MicrosoftMes.Data;

public static class ProcessingPriceSchema
{
    public const string UpgradeSql = """
        IF OBJECT_ID('prod_work_order_operation', 'U') IS NOT NULL AND COL_LENGTH('prod_work_order_operation', 'processing_price') IS NULL
            ALTER TABLE prod_work_order_operation ADD processing_price NVARCHAR(MAX) NULL;
        """;
}
