SET NOCOUNT ON; SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;
BEGIN TRANSACTION;

IF OBJECT_ID('tempdb..#days2') IS NOT NULL DROP TABLE #days2;
CREATE TABLE #days2 (d date);
;WITH cte AS (
  SELECT CAST('2026-10-01' AS date) AS d
  UNION ALL SELECT DATEADD(day,1,d) FROM cte WHERE d<'2026-10-27'
)
INSERT INTO #days2(d) SELECT d FROM cte WHERE DATEPART(weekday,d) NOT IN (1)
OPTION (MAXRECURSION 100);

IF OBJECT_ID('tempdb..#workers2') IS NOT NULL DROP TABLE #workers2;
CREATE TABLE #workers2 (user_id int, operation_id int, dept_id int, price decimal(10,4), pl_id int, base_qty int);
INSERT INTO #workers2 VALUES
 (24,28,23,0.45,10,670),(25,28,23,0.45,10,670),
 (26,29,24,0.40,11,750),(27,29,24,0.40,11,750),
 (28,30,25,0.30,12,1000),(29,31,26,0.20,13,1500),
 (30,32,27,0.25,14,1200),(31,33,28,0.30,15,1000),
 (32,34,29,0.22,16,1360),(33,35,30,0.18,17,1670);

IF OBJECT_ID('tempdb..#newrep2') IS NOT NULL DROP TABLE #newrep2;
CREATE TABLE #newrep2 (rid bigint, user_id int, good_qty int, unit_price decimal(10,4), wage decimal(10,2));

INSERT INTO prod_report
  (factory_id, order_id, operation_id, user_id, good_qty, defect_qty,
   report_time, duration_minutes, review_status, reviewed_by, reviewed_at,
   product_id, department_id, unit_price, wage_amount, settled_flag, source)
OUTPUT inserted.Id, inserted.user_id, inserted.good_qty, inserted.unit_price, inserted.wage_amount
INTO #newrep2
SELECT
  6, 10084, w.operation_id, w.user_id,
  CAST(w.base_qty * (0.85 + (ABS(CHECKSUM(NEWID())) % 30)/100.0) AS int),
  0,
  DATEADD(minute, (ABS(CHECKSUM(NEWID())) % 570), CAST(d.d AS datetime) + '08:00'),
  480,
  CASE WHEN d.d < '2026-10-26' THEN 1 ELSE 0 END,
  CASE WHEN d.d < '2026-10-26' THEN 21 ELSE NULL END,
  CASE WHEN d.d < '2026-10-26' THEN DATEADD(minute,(ABS(CHECKSUM(NEWID()))%60),CAST(d.d AS datetime)+'17:30') ELSE NULL END,
  10016, w.dept_id, w.price,
  CAST(w.base_qty * (0.85 + (ABS(CHECKSUM(NEWID())) % 30)/100.0) AS int) * w.price,
  0, 0
FROM #days2 d CROSS JOIN #workers2 w;

INSERT INTO salary_statement_item
  (statement_id, report_id, calc_qty, unit_price, amount, deduct_amount,
   wage_coeff, price_type, duration_minutes, price_list_id)
SELECT s.id, nr.rid, nr.good_qty, nr.unit_price, nr.wage, 0, 1.00, 1, 480, w.pl_id
FROM #newrep2 nr
JOIN salary_statement s ON s.user_id=nr.user_id AND s.factory_id=6 AND s.period_value='2026-10'
JOIN #workers2 w ON w.user_id=nr.user_id;

COMMIT TRANSACTION;
PRINT 'main bulk done';
