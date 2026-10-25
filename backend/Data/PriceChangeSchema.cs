namespace ahu.MicrosoftMes.Data;

public static class PriceChangeSchema
{
    public const string UpgradeSql = """
IF OBJECT_ID(N'dbo.base_operation_price_revision',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.base_operation_price_revision(
 id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
 factory_id BIGINT NOT NULL,price_list_id BIGINT NOT NULL,operation_id BIGINT NOT NULL,product_id BIGINT NULL,
 effective_from DATE NOT NULL,effective_to DATE NULL,superseded BIT NOT NULL DEFAULT 0,
 snapshot_json NVARCHAR(MAX) NOT NULL,fields_json NVARCHAR(MAX) NOT NULL,
 created_by BIGINT NOT NULL,created_at DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
 CONSTRAINT ck_price_revision_dates CHECK(effective_to IS NULL OR effective_to>=effective_from));
 CREATE INDEX ix_price_revision_scope ON dbo.base_operation_price_revision(factory_id,operation_id,effective_from,effective_to,superseded);
 CREATE INDEX ix_price_revision_parent ON dbo.base_operation_price_revision(factory_id,price_list_id);
END;
IF OBJECT_ID(N'dbo.base_operation_price_change',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.base_operation_price_change(
 id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,factory_id BIGINT NOT NULL,price_list_id BIGINT NOT NULL,
 request_id UNIQUEIDENTIFIER NOT NULL,changed_by BIGINT NOT NULL,reason NVARCHAR(500) NOT NULL,
 effective_from DATE NOT NULL,reprice_current_month BIT NOT NULL,
 request_hash NVARCHAR(64) NOT NULL,plan_hash NVARCHAR(64) NOT NULL,
 before_json NVARCHAR(MAX) NOT NULL,after_json NVARCHAR(MAX) NOT NULL,created_at DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
 CONSTRAINT uq_price_change_request UNIQUE(factory_id,request_id));
END;
""";
}
