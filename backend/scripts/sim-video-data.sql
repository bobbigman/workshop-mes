-- ============================================================
-- 视频截图模拟数据：HY伞业(factory_id=6)
-- 今天 2026-10-28；日薪~250-320，月薪~7000-8000
-- ============================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
BEGIN TRANSACTION;

-- 1. 设交期
UPDATE prod_work_order SET due_date='2026-10-25' WHERE Id=10074;
UPDATE prod_work_order SET due_date='2026-10-26' WHERE Id=10075;
UPDATE prod_work_order SET due_date='2026-10-29' WHERE Id=10076;
UPDATE prod_work_order SET due_date='2026-10-30' WHERE Id=10077;
UPDATE prod_work_order SET due_date='2026-10-31' WHERE Id=10078;
UPDATE prod_work_order SET due_date='2026-11-05' WHERE Id=10079;
UPDATE prod_work_order SET due_date='2026-11-08' WHERE Id=10080;
UPDATE prod_work_order SET due_date='2026-11-10' WHERE Id=10081;
UPDATE prod_work_order SET due_date='2026-11-15' WHERE Id=10084;

-- 2. 清旧数据
DELETE si FROM salary_statement_item si
JOIN salary_statement s ON s.id=si.statement_id
WHERE s.factory_id=6 AND s.period_value='2026-10';
DELETE FROM prod_report WHERE user_id BETWEEN 24 AND 33 AND factory_id=6;

-- 3. 工作日临时表
IF OBJECT_ID('tempdb..#days') IS NOT NULL DROP TABLE #days;
CREATE TABLE #days (d date);
;WITH cte AS (
  SELECT CAST('2026-10-01' AS date) AS d
  UNION ALL
  SELECT DATEADD(day,1,d) FROM cte WHERE d<'2026-10-27'
)
INSERT INTO #days(d)
SELECT d FROM cte WHERE DATEPART(weekday,d) NOT IN (1)
OPTION (MAXRECURSION 100);

-- 4. 工人参数
IF OBJECT_ID('tempdb..#workers') IS NOT NULL DROP TABLE #workers;
CREATE TABLE #workers (
  user_id int, operation_id int, dept_id int, price decimal(10,4),
  pl_id int, base_qty int
);
INSERT INTO #workers VALUES
 (24,28,23,0.45,10,670),
 (25,28,23,0.45,10,670),
 (26,29,24,0.40,11,750),
 (27,29,24,0.40,11,750),
 (28,30,25,0.30,12,1000),
 (29,31,26,0.20,13,1500),
 (30,32,27,0.25,14,1200),
 (31,33,28,0.30,15,1000),
 (32,34,29,0.22,16,1360),
 (33,35,30,0.18,17,1670);

-- 5. 工单池
IF OBJECT_ID('tempdb..#orders') IS NOT NULL DROP TABLE #orders;
CREATE TABLE #orders (oid int, rn int);
INSERT INTO #orders(oid,rn) VALUES
 (10074,1),(10075,2),(10076,3),(10077,4),(10078,5),
 (10079,6),(10080,7),(10081,8),(10084,9);

-- 6. 生成 plan：每天每工人一条报工
IF OBJECT_ID('tempdb..#plan') IS NOT NULL DROP TABLE #plan;
CREATE TABLE #plan (
  d date, user_id int, operation_id int, dept_id int,
  price decimal(10,4), pl_id int, good_qty int,
  report_time datetime2, review_time datetime2, oid int
);

INSERT INTO #plan(d,user_id,operation_id,dept_id,price,pl_id,good_qty,report_time,review_time,oid)
SELECT
  d.d, w.user_id, w.operation_id, w.dept_id, w.price, w.pl_id,
  CAST(w.base_qty * (0.85 + (ABS(CHECKSUM(NEWID())) % 30)/100.0) AS int),
  DATEADD(minute, (ABS(CHECKSUM(NEWID())) % 570), CAST(d.d AS datetime) + '08:00'),
  DATEADD(minute, (ABS(CHECKSUM(NEWID())) % 60), CAST(d.d AS datetime) + '17:30'),
  o.oid
FROM #days d CROSS JOIN #workers w CROSS JOIN #orders o
WHERE ((DATEDIFF(day,'2026-10-01',d.d) + w.user_id) % 9) + 1 = o.rn;

-- 7. 写入 prod_report
IF OBJECT_ID('tempdb..#newrep') IS NOT NULL DROP TABLE #newrep;
CREATE TABLE #newrep (
  rid bigint, user_id int, good_qty int, unit_price decimal(10,4),
  wage decimal(10,2), order_id int
);

INSERT INTO prod_report
  (factory_id, order_id, operation_id, user_id, good_qty, defect_qty,
   report_time, duration_minutes, review_status, reviewed_by, reviewed_at,
   product_id, department_id, unit_price, wage_amount, settled_flag, source)
OUTPUT inserted.Id, inserted.user_id, inserted.good_qty, inserted.unit_price,
       inserted.wage_amount, inserted.order_id
INTO #newrep
SELECT
  6, p.oid, p.operation_id, p.user_id, p.good_qty, 0,
  p.report_time, 480,
  CASE WHEN p.d < '2026-10-26' THEN 1 ELSE 0 END,
  CASE WHEN p.d < '2026-10-26' THEN 21 ELSE NULL END,
  CASE WHEN p.d < '2026-10-26' THEN p.review_time ELSE NULL END,
  10016, p.dept_id, p.price, p.good_qty * p.price,
  0, 0
FROM #plan p;

-- 8. 写工资单明细
INSERT INTO salary_statement_item
  (statement_id, report_id, calc_qty, unit_price, amount, deduct_amount,
   wage_coeff, price_type, duration_minutes, price_list_id)
SELECT
  s.id, nr.rid, nr.good_qty, nr.unit_price, nr.wage, 0,
  1.00, 1, 480, w.pl_id
FROM #newrep nr
JOIN salary_statement s ON s.user_id=nr.user_id AND s.factory_id=6 AND s.period_value='2026-10'
JOIN #workers w ON w.user_id=nr.user_id;

-- 9. 更新工资单总额
UPDATE s
SET total_amount = ISNULL((SELECT SUM(amount) FROM salary_statement_item si WHERE si.statement_id=s.id),0)
FROM salary_statement s
WHERE s.factory_id=6 AND s.period_value='2026-10';

UPDATE salary_statement SET status=1, confirmed_at='2026-10-28 09:00'
WHERE factory_id=6 AND period_value='2026-10' AND user_id IN (24,25,26);

-- 校验
PRINT '=== 每人月薪 ===';
SELECT u.name, p.user_id, COUNT(*) AS reports, SUM(p.good_qty) AS qty, SUM(p.good_qty*p.price) AS wage
FROM #plan p JOIN sys_user u ON u.Id=p.user_id
GROUP BY u.name, p.user_id ORDER BY p.user_id;

PRINT '=== 工资单 ===';
SELECT id, user_id, period_value, total_amount, status FROM salary_statement
WHERE factory_id=6 AND period_value='2026-10' ORDER BY user_id;

PRINT '=== 工单交期 ===';
SELECT Id, order_no, due_date, status FROM prod_work_order
WHERE Id BETWEEN 10074 AND 10084 ORDER BY Id;

COMMIT TRANSACTION;
PRINT 'DONE';
