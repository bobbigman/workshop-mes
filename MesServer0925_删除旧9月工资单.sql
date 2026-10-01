/* ============================================================================
   删除 CJ001 旧的 2026-09 工资单（用于重新生成工资数据）
   ----------------------------------------------------------------------------
   目标库 : MesServer0925.dbo
   目的   : 清掉 9 月已确认的旧工资单（应发合计 19.6/18.5/18.5），
            以便重新生成包含新报工(每人300-400)的工资单。
   注意   :
     1) 只删 salary_statement_item / salary_statement 两张表 2026-09 的记录；
        不删任何报工(prod_report)，旧报工保留。
     2) factory_id=2 是 CJ001(厨具五金厂)。F001=1 不受影响。
     3) 执行前先跑第 ① 步核对，确认无误再执行 ②③。
   ============================================================================ */

USE [MesServer0925];
GO

-- ① 核对：当前 2026-09 工资单（应能看到 CJ001 的 3 条，应发合计 19.6/18.5/18.5）
SELECT s.id, s.factory_id, u.name AS 姓名, s.period_value, s.total_amount AS 应发合计, s.status
FROM dbo.salary_statement s
JOIN dbo.sys_user u ON u.id = s.user_id
WHERE s.period_type = 1 AND s.period_value = N'2026-09'
ORDER BY s.id;
GO

-- ② 删除工资单明细（先子表）
DELETE si
FROM dbo.salary_statement_item si
JOIN dbo.salary_statement s ON si.statement_id = s.id
WHERE s.factory_id = 2 AND s.period_type = 1 AND s.period_value = N'2026-09';
GO

-- ③ 删除工资单主表（后父表）
DELETE FROM dbo.salary_statement
WHERE factory_id = 2 AND period_type = 1 AND period_value = N'2026-09';
GO

-- ④ 验证：应返回 0 行
SELECT COUNT(*) AS 剩余旧工资单数
FROM dbo.salary_statement
WHERE factory_id = 2 AND period_type = 1 AND period_value = N'2026-09';
GO

/* ============================================================================
   回滚提示：本操作为删除数据，无法撤销。
   如需恢复，只能重新生成工资单（新报工仍可生成，金额会变成 300-400 区间）。
   ============================================================================ */
