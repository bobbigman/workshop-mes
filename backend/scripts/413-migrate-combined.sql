-- ============================================================
-- 413 迁移脚本（合并版）：步骤① 建新表 + 步骤② 清理旧工价结构
-- 适用：新版本(v1.1.410) 部署后启动报「检测到旧工价结构」时使用
-- 幂等：可重复执行，不影响工资单/工单/报工/基础资料数据
-- ⚠️ 本脚本会删除旧表/旧列，不可逆，执行前务必先完整备份数据库
-- ⚠️ 请确认连接的是目标生产库（本机验证示例：WorkshopMesServer1025）
-- ============================================================
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- ============ 步骤① 建新表 base_operation_price_list（幂等）============
-- 与代码 OperationPriceListSchema.UpgradeSql 保持一致
IF COL_LENGTH('sys_custom_field', 'unit_text') IS NULL
    ALTER TABLE sys_custom_field ADD unit_text NVARCHAR(16) NULL;
IF COL_LENGTH('sys_custom_field', 'description') IS NULL
    ALTER TABLE sys_custom_field ADD description NVARCHAR(256) NULL;
IF COL_LENGTH('sys_custom_field', 'pricing_required') IS NULL
    ALTER TABLE sys_custom_field ADD pricing_required BIT NOT NULL DEFAULT 0;

IF OBJECT_ID('base_operation_price_list', 'U') IS NULL
BEGIN
    CREATE TABLE base_operation_price_list (
        id                BIGINT IDENTITY(1,1) PRIMARY KEY,
        factory_id        BIGINT NOT NULL,
        code              NVARCHAR(64) NOT NULL,
        name              NVARCHAR(128) NOT NULL,
        operation_id      BIGINT NOT NULL,
        pricing_type      TINYINT NOT NULL DEFAULT 1,
        status            TINYINT NOT NULL DEFAULT 0,
        effective_from    DATE NOT NULL,
        effective_to      DATE NULL,
        price_unit        NVARCHAR(32) NOT NULL,
        report_unit_id    BIGINT NOT NULL,
        units_per_price_unit DECIMAL(12,4) NOT NULL,
        version           NVARCHAR(32) NOT NULL,
        details           NVARCHAR(MAX) NOT NULL,
        created_at        DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        updated_at        DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        CONSTRAINT uq_operation_price_list_code UNIQUE(factory_id, code)
    );
END;

IF OBJECT_ID('base_operation_price_list', 'U') IS NOT NULL
   AND COL_LENGTH('base_operation_price_list','pricing_type') IS NULL
    ALTER TABLE base_operation_price_list ADD pricing_type TINYINT NOT NULL DEFAULT 1;

-- ============ 步骤② 新增列 ============
IF COL_LENGTH('base_operation_price_list','pricing_type') IS NULL
    ALTER TABLE base_operation_price_list ADD pricing_type TINYINT NOT NULL CONSTRAINT DF_opl_pricing_type DEFAULT 1;
IF COL_LENGTH('salary_statement_item','price_list_id') IS NULL
    ALTER TABLE salary_statement_item ADD price_list_id BIGINT NULL;

-- ============ 步骤② 删除旧工价结构 ============
-- 删除旧工价来源列（DROP COLUMN 需动态 SQL，编译期校验）
IF COL_LENGTH('salary_statement_item','rule_id') IS NOT NULL
    EXEC(N'ALTER TABLE salary_statement_item DROP COLUMN rule_id;');

-- 删除旧工价表 base_price_rule
IF OBJECT_ID('base_price_rule','U') IS NOT NULL
    DROP TABLE base_price_rule;

-- 删除双模式开关表 base_product_operation_pricing
IF OBJECT_ID('base_product_operation_pricing','U') IS NOT NULL
    DROP TABLE base_product_operation_pricing;

-- 删除旧 280 加工选价列 base_operation.pricing_config
IF COL_LENGTH('base_operation','pricing_config') IS NOT NULL
    EXEC(N'ALTER TABLE base_operation DROP COLUMN pricing_config;');

COMMIT;
