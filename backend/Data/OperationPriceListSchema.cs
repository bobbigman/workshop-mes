namespace ahu.MicrosoftMes.Data;

public static class OperationPriceListSchema
{
    public const string UpgradeSql = """
        IF COL_LENGTH('sys_custom_field', 'unit_text') IS NULL ALTER TABLE sys_custom_field ADD unit_text NVARCHAR(16) NULL;
        IF COL_LENGTH('sys_custom_field', 'description') IS NULL ALTER TABLE sys_custom_field ADD description NVARCHAR(256) NULL;
        IF COL_LENGTH('sys_custom_field', 'pricing_required') IS NULL ALTER TABLE sys_custom_field ADD pricing_required BIT NOT NULL DEFAULT 0;
        IF OBJECT_ID('base_operation_price_list', 'U') IS NULL
        BEGIN
            CREATE TABLE base_operation_price_list (
                id BIGINT IDENTITY(1,1) PRIMARY KEY,
                factory_id BIGINT NOT NULL,
                code NVARCHAR(64) NOT NULL,
                name NVARCHAR(128) NOT NULL,
                operation_id BIGINT NOT NULL,
                pricing_type TINYINT NOT NULL DEFAULT 1,
                status TINYINT NOT NULL DEFAULT 0,
                effective_from DATE NOT NULL,
                effective_to DATE NULL,
                price_unit NVARCHAR(32) NOT NULL,
                report_unit_id BIGINT NOT NULL,
                units_per_price_unit DECIMAL(12,4) NOT NULL,
                version NVARCHAR(32) NOT NULL,
                details NVARCHAR(MAX) NOT NULL,
                created_at DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
                updated_at DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
                CONSTRAINT uq_operation_price_list_code UNIQUE(factory_id,code)
            );
        END;
        IF OBJECT_ID('base_operation_price_list', 'U') IS NOT NULL AND COL_LENGTH('base_operation_price_list','pricing_type') IS NULL
            ALTER TABLE base_operation_price_list ADD pricing_type TINYINT NOT NULL DEFAULT 1;
        """;
}
