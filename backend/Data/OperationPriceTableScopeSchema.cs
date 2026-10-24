namespace ahu.MicrosoftMes.Data;

public static class OperationPriceTableScopeSchema
{
    public const string AddColumnsSql = """
        IF COL_LENGTH('base_operation_price_list','product_id') IS NULL ALTER TABLE base_operation_price_list ADD product_id BIGINT NULL;
        IF COL_LENGTH('base_operation_price_list','remark') IS NULL ALTER TABLE base_operation_price_list ADD remark NVARCHAR(1000) NOT NULL DEFAULT N'';
        IF COL_LENGTH('base_operation_price_list','activated_at') IS NULL ALTER TABLE base_operation_price_list ADD activated_at DATETIME2 NULL;
        IF COL_LENGTH('sys_custom_field','operation_id') IS NULL ALTER TABLE sys_custom_field ADD operation_id BIGINT NULL;
        """;
    public const string FinalizeSql = """
        UPDATE base_operation_price_list SET activated_at=SYSDATETIME() WHERE activated_at IS NULL AND status IN(1,2);
        IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('base_operation_price_list') AND name='ix_operation_price_list_scope')
          CREATE INDEX ix_operation_price_list_scope ON base_operation_price_list(factory_id,operation_id,product_id,status,effective_from,effective_to);
        IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('sys_custom_field') AND name='ux_custom_field_scope_key')
          CREATE UNIQUE INDEX ux_custom_field_scope_key ON sys_custom_field(factory_id,target,operation_id,field_key) WHERE field_key IS NOT NULL;
        """;
}
