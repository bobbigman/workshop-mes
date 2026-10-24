-- 413: drop legacy price rule tables and unify operation price list pricing.
-- Target: Server=localhost;Database=WorkshopMes (dev: local / F001). Idempotent.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- add new columns
IF COL_LENGTH('base_operation_price_list','pricing_type') IS NULL
    ALTER TABLE base_operation_price_list ADD pricing_type TINYINT NOT NULL CONSTRAINT DF_opl_pricing_type DEFAULT 1;
IF COL_LENGTH('salary_statement_item','price_list_id') IS NULL
    ALTER TABLE salary_statement_item ADD price_list_id BIGINT NULL;

-- drop old wage source column (dynamic SQL: DROP COLUMN is compile-time validated)
IF COL_LENGTH('salary_statement_item','rule_id') IS NOT NULL
    EXEC(N'ALTER TABLE salary_statement_item DROP COLUMN rule_id;');

-- drop old wage rule table
IF OBJECT_ID('base_price_rule','U') IS NOT NULL
    DROP TABLE base_price_rule;

-- drop dual-mode switch table
IF OBJECT_ID('base_product_operation_pricing','U') IS NOT NULL
    DROP TABLE base_product_operation_pricing;

-- drop old 280 processing-choice column
IF COL_LENGTH('base_operation','pricing_config') IS NOT NULL
    EXEC(N'ALTER TABLE base_operation DROP COLUMN pricing_config;');

COMMIT;
