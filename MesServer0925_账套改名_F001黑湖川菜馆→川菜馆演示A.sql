/* ============================================================================
   账套改名脚本：F001 黑湖川菜馆 → 川菜馆演示 A
   ----------------------------------------------------------------------------
   目标库 : MesServer0925.dbo.sys_factory
   目的   : 登录页「账套/工厂」下拉里不再出现"黑湖"品牌字样（客户演示用）
   背景   : 登录页账套下拉读 sys_factory.factory_name（GET /api/factories），
            系统没有改名接口，只能直接 UPDATE 该字段。
            本脚本只改 F001，厨具五金厂(CJ001) 不在范围内。
   执行   : 到服务器 SQL Server 上对 MesServer0925 库执行一次即可；
            改完登录页无需重启，刷新即可看到新名字。
   ============================================================================ */

USE [MesServer0925];
GO

-- ① 改名前先看当前账套（应能看到 F001 = 黑湖川菜馆）
SELECT id, factory_code, factory_name FROM dbo.sys_factory ORDER BY id;
GO

-- ② 改名：黑湖川菜馆 → 川菜馆演示 A（按 factory_code 精准定位，避免误改）
UPDATE dbo.sys_factory
SET factory_name = N'川菜馆演示 A'
WHERE factory_code = N'F001';
GO

-- ③ 核对结果（F001 应显示"川菜馆演示 A"；CJ001 仍为"厨具五金厂"）
SELECT id, factory_code, factory_name FROM dbo.sys_factory ORDER BY id;
GO

/* ============================================================================
   回滚（如需恢复原名，单独执行下面一句）：
   UPDATE dbo.sys_factory SET factory_name = N'黑湖川菜馆' WHERE factory_code = N'F001';
   ============================================================================ */
