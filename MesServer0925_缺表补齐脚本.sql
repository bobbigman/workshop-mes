/* ============================================================================
   MesServer0925 结构对齐脚本（对照 WorkshopMes）
   ----------------------------------------------------------------------------
   对比基准 : WorkshopMes（28 张表）  →  目标库 : MesServer0925（27 张表）
   生成时间 : 2026-09-25
   执行位置 : 在 MesServer0925 所在实例执行（本脚本带 USE [MesServer0925]）

   对比结论（已跨库逐表、逐列校验）：
     A) 缺表 1 张     ：login_setting          → 下方脚本补齐
     B) 字段新增/删除 ：无差异（27 张共有表字段完全一致，无需增删任何列）
     C) 多余表        ：无
     D) 同名列类型    ：全部一致（无类型/长度/可空性不一致）

   注：主库 login_setting 现有 2 行演示数据，本脚本只建表、不迁移数据；
       如需把主库该表数据一并带过去，请单独执行 INSERT 迁移。
   ============================================================================ */

USE [MesServer0925];
GO

/* ----------------------------------------------------------------------------
   缺表：login_setting —— 登录页展示设置（登录 banner 图）
   结构完全对齐 WorkshopMes.dbo.login_setting（主键/默认约束/唯一索引同名复刻）
   ---------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.login_setting', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.login_setting
    (
        id          bigint        IDENTITY(1,1) NOT NULL,   -- 主键
        factory_id  bigint        NOT NULL,                -- 工厂 ID（唯一，每厂一条）
        banner_url  nvarchar(510) NULL,                    -- 登录页 banner 图片地址
        updated_at  datetime2     NOT NULL
            CONSTRAINT DF_login_setting_ua DEFAULT (sysdatetime()),  -- 更新时间
        CONSTRAINT PK_login_setting PRIMARY KEY (id)
    );

    -- 每工厂唯一一条登录设置（复刻主库唯一索引）
    CREATE UNIQUE INDEX UQ_login_setting_factory
        ON dbo.login_setting (factory_id);

    PRINT '[OK] login_setting 建表成功';
END
ELSE
BEGIN
    PRINT '[跳过] login_setting 已存在';
END;
GO
