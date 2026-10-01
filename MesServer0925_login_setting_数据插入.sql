/* ============================================================================
   login_setting 数据复制脚本：WorkshopMes → MesServer0925
   ----------------------------------------------------------------------------
   数据来源 : WorkshopMes.dbo.login_setting（当前 2 行）
   目标库   : MesServer0925.dbo.login_setting
   生成时间 : 2026-09-25

   前置条件：
     1) MesServer0925 中 login_setting 表必须已存在
        （若未建表，请先执行「MesServer0925_缺表补齐脚本.sql」）
     2) 本脚本保留源库的 id 主键与原 updated_at 时间，保持与主库完全一致。
     3) 只执行一次：若重复执行会因主键冲突报错；
        目标表若已有数据，请先 TRUNCATE TABLE dbo.login_setting 后再执行。
   ============================================================================ */

USE [MesServer0925];
GO

-- 允许显式插入自增主键 id（保持与源库一致）
SET IDENTITY_INSERT dbo.login_setting ON;
GO

INSERT INTO dbo.login_setting (id, factory_id, banner_url, updated_at)
SELECT 2, 5, N'/uploads/login/5.jpg', '2026-09-25 07:46:42.0250719'
UNION ALL
SELECT 3, 1, N'/uploads/login/1.jpg', '2026-09-25 07:23:27.9391976';
GO

SET IDENTITY_INSERT dbo.login_setting OFF;
GO

-- 核对结果（应返回 2 行）
SELECT id, factory_id, banner_url, CONVERT(varchar(33), updated_at, 121) AS updated_at
FROM dbo.login_setting ORDER BY id;
GO
