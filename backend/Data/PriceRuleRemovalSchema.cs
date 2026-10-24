namespace ahu.MicrosoftMes.Data;

/// <summary>
/// 413：彻底删除旧工价表与双模式，统一工序价目表取价。显式增量迁移（不在 DbCompat 启动路径自动 DROP）。
/// SQL 全部用 OBJECT_ID/COL_LENGTH 核对，可重复执行。只处理旧对象，不触碰工资单/工资明细/工单/报工。
/// </summary>
public static class PriceRuleRemovalSchema
{
    /// <summary>新增列 + 删除旧对象（幂等）。执行前应已由运维/开发确认目标库为本地开发库。</summary>
    public const string UpgradeSql = """
        -- 新来源列：salary_statement_item.price_list_id（可空；新结算必须有值）
        IF COL_LENGTH('salary_statement_item','price_list_id') IS NULL
            ALTER TABLE salary_statement_item ADD price_list_id BIGINT NULL;

        -- 删除旧工价来源列 salary_statement_item.rule_id
        IF COL_LENGTH('salary_statement_item','rule_id') IS NOT NULL
            ALTER TABLE salary_statement_item DROP COLUMN rule_id;

        -- 删除旧工价表 base_price_rule（含索引；先核实无外键/视图依赖）
        IF OBJECT_ID('base_price_rule','U') IS NOT NULL
            DROP TABLE base_price_rule;

        -- 删除双模式开关表 base_product_operation_pricing
        IF OBJECT_ID('base_product_operation_pricing','U') IS NOT NULL
            DROP TABLE base_product_operation_pricing;

        -- 删除旧 280 加工选价列 base_operation.pricing_config
        IF COL_LENGTH('base_operation','pricing_config') IS NOT NULL
            ALTER TABLE base_operation DROP COLUMN pricing_config;
        """;

    /// <summary>启动检查：任一旧结构仍存在即返回 true，表示尚未完成 413 清理。</summary>
    public const string HasLegacySql = """
        SELECT CASE WHEN
            OBJECT_ID('base_price_rule','U') IS NOT NULL
            OR OBJECT_ID('base_product_operation_pricing','U') IS NOT NULL
            OR COL_LENGTH('base_operation','pricing_config') IS NOT NULL
            OR COL_LENGTH('salary_statement_item','rule_id') IS NOT NULL
            THEN 1 ELSE 0 END;
        """;
}
