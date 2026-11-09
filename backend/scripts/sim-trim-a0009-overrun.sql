-- 方案 A：只修 A0009(10084) 超额灌库数据
-- 每道工序按 report_time 保留累计良品 ≤ plan_qty；边界条按剩余量改小；多余报工及工资明细删除并重算工资单。
-- 不改 A0001～A0008。超报比例仍为 0 时，详情页不会穿帮。
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ARITHABORT ON;

DECLARE @OrderId BIGINT = 10084;
DECLARE @FactoryId BIGINT = 6;

BEGIN TRANSACTION;

IF OBJECT_ID('tempdb..#ranked') IS NOT NULL DROP TABLE #ranked;
SELECT
  r.Id AS report_id,
  r.operation_id,
  r.good_qty,
  r.unit_price,
  r.wage_amount,
  t.plan_qty,
  SUM(r.good_qty) OVER (
    PARTITION BY r.operation_id
    ORDER BY r.report_time, r.Id
    ROWS UNBOUNDED PRECEDING
  ) AS running_good
INTO #ranked
FROM prod_report r
JOIN prod_work_order_operation t
  ON t.work_order_id = r.order_id AND t.operation_id = r.operation_id
WHERE r.order_id = @OrderId
  AND r.factory_id = @FactoryId
  AND r.review_status <> 2;

IF OBJECT_ID('tempdb..#keep') IS NOT NULL DROP TABLE #keep;
SELECT
  report_id,
  operation_id,
  plan_qty,
  good_qty AS old_good,
  CASE
    WHEN running_good <= plan_qty THEN good_qty
    WHEN running_good - good_qty < plan_qty THEN plan_qty - (running_good - good_qty)
    ELSE 0
  END AS new_good
INTO #keep
FROM #ranked;

-- 边界条：改数量与计件工资（单价不变）
UPDATE r
SET
  good_qty = k.new_good,
  wage_amount = CAST(ROUND(k.new_good * r.unit_price, 2) AS DECIMAL(12,2))
FROM prod_report r
JOIN #keep k ON k.report_id = r.Id
WHERE k.new_good > 0 AND k.new_good <> k.old_good;

UPDATE si
SET
  calc_qty = k.new_good,
  amount = CAST(ROUND(k.new_good * si.unit_price * ISNULL(si.wage_coeff, 1), 2) AS DECIMAL(12,2))
FROM salary_statement_item si
JOIN #keep k ON k.report_id = si.report_id
WHERE k.new_good > 0 AND k.new_good <> k.old_good;

-- 待删报工
IF OBJECT_ID('tempdb..#del') IS NOT NULL DROP TABLE #del;
SELECT report_id INTO #del FROM #keep WHERE new_good <= 0;

DELETE FROM prod_report_change_log WHERE report_id IN (SELECT report_id FROM #del);
DELETE FROM salary_statement_item WHERE report_id IN (SELECT report_id FROM #del);
DELETE FROM prod_report WHERE Id IN (SELECT report_id FROM #del);

-- 重算本厂 2026-10 工资单（演示期）受影响总额
UPDATE s
SET total_amount = ISNULL((
  SELECT SUM(si.amount - ISNULL(si.deduct_amount, 0))
  FROM salary_statement_item si
  WHERE si.statement_id = s.id
), 0)
FROM salary_statement s
WHERE s.factory_id = @FactoryId
  AND s.period_value = '2026-10'
  AND s.user_id BETWEEN 24 AND 33;

-- 校验：各工序不得超计划
PRINT '=== A0009 after trim ===';
SELECT
  t.seq,
  t.operation_id,
  op.name AS op_name,
  t.plan_qty,
  ISNULL(SUM(CASE WHEN r.review_status <> 2 THEN r.good_qty END), 0) AS done,
  COUNT(CASE WHEN r.review_status <> 2 THEN 1 END) AS rpt_cnt
FROM prod_work_order_operation t
LEFT JOIN prod_report r
  ON r.order_id = t.work_order_id AND r.operation_id = t.operation_id
LEFT JOIN base_operation op ON op.Id = t.operation_id
WHERE t.work_order_id = @OrderId
GROUP BY t.seq, t.operation_id, op.name, t.plan_qty
ORDER BY t.seq;

PRINT '=== deleted reports ===';
SELECT COUNT(*) AS deleted_cnt FROM #del;

PRINT '=== salary 2026-10 ===';
SELECT s.user_id, u.name, s.total_amount, s.status
FROM salary_statement s
LEFT JOIN sys_user u ON u.Id = s.user_id
WHERE s.factory_id = @FactoryId AND s.period_value = '2026-10' AND s.user_id BETWEEN 24 AND 33
ORDER BY s.user_id;

IF EXISTS (
  SELECT 1
  FROM prod_work_order_operation t
  CROSS APPLY (
    SELECT ISNULL(SUM(CASE WHEN r.review_status <> 2 THEN r.good_qty END), 0) AS done
    FROM prod_report r
    WHERE r.order_id = t.work_order_id AND r.operation_id = t.operation_id
  ) x
  WHERE t.work_order_id = @OrderId AND x.done > t.plan_qty
)
BEGIN
  ROLLBACK TRANSACTION;
  THROW 50001, 'trim failed: still over plan', 1;
END

COMMIT TRANSACTION;
PRINT 'DONE';
