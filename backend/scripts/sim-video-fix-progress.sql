-- 修进度：把小工单(10074-10081)上的报工清掉，集中到 A0009(10084)；
-- 再给小工单补少量报工，让它们停在不同工序阶段。
SET NOCOUNT ON; SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;
BEGIN TRANSACTION;

-- 1. 删除小工单上的报工及其工资明细
DELETE FROM salary_statement_item WHERE report_id IN (
  SELECT Id FROM prod_report WHERE order_id BETWEEN 10074 AND 10081 AND factory_id=6
);
DELETE FROM prod_report WHERE order_id BETWEEN 10074 AND 10081 AND factory_id=6;

-- 2. 把这些工人的报工全部改挂到 A0009(10084)。
--    由于已经删了小工单报工，工人只剩 A0009 上的报工。确认一下：
--    (不用动，原来 9 分之一在 10084，8/9 被删了。需要把删掉的量补到 10084。)
--    简单做法：给每个工人在 10084 上补 8/9 的日报工。

-- 重新生成：每个工人 23 天，全部挂在 10084
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

-- 3. 给小工单补少量"阶段报工"，让它们停在不同工序
-- 这些是场景摆设，工资可忽略。用系统账号21报。
-- 格式：(order_id, operation_id, qty, report_date)
-- A0001(10074) 超期 1200把：合片~打珠尾做完，品检/包装没动 -> 卡在品检
INSERT INTO prod_report(factory_id,order_id,operation_id,user_id,good_qty,defect_qty,report_time,duration_minutes,review_status,reviewed_by,reviewed_at,product_id,department_id,unit_price,wage_amount,settled_flag,source)
VALUES
(6,10074,28,24,1200,0,'2026-10-05 10:00',480,1,21,'2026-10-05 17:30',10016,23,0.45,540,0,0),
(6,10074,29,26,1200,0,'2026-10-06 10:00',480,1,21,'2026-10-06 17:30',10016,24,0.40,480,0,0),
(6,10074,30,28,1200,0,'2026-10-07 10:00',480,1,21,'2026-10-07 17:30',10016,25,0.30,360,0,0),
(6,10074,31,29,1200,0,'2026-10-08 10:00',480,1,21,'2026-10-08 17:30',10016,26,0.20,240,0,0),
(6,10074,32,30,1200,0,'2026-10-09 10:00',480,1,21,'2026-10-09 17:30',10016,27,0.25,300,0,0),
(6,10074,33,31,1200,0,'2026-10-10 10:00',480,1,21,'2026-10-10 17:30',10016,28,0.30,360,0,0);

-- A0002(10075) 超期 600把：合片~顶带做完，珠尾/品检/包装没动
INSERT INTO prod_report(factory_id,order_id,operation_id,user_id,good_qty,defect_qty,report_time,duration_minutes,review_status,reviewed_by,reviewed_at,product_id,department_id,unit_price,wage_amount,settled_flag,source)
VALUES
(6,10075,28,24,600,0,'2026-10-08 10:00',480,1,21,'2026-10-08 17:30',10016,23,0.45,270,0,0),
(6,10075,29,26,600,0,'2026-10-09 10:00',480,1,21,'2026-10-09 17:30',10016,24,0.40,240,0,0),
(6,10075,30,28,600,0,'2026-10-10 10:00',480,1,21,'2026-10-10 17:30',10016,25,0.30,180,0,0),
(6,10075,31,29,600,0,'2026-10-12 10:00',480,1,21,'2026-10-12 17:30',10016,26,0.20,120,0,0),
(6,10075,32,30,600,0,'2026-10-13 10:00',480,1,21,'2026-10-13 17:30',10016,27,0.25,150,0,0);

-- A0003(10076) 临期 800把：合片~拉边做完70%，小裁刚开始
INSERT INTO prod_report(factory_id,order_id,operation_id,user_id,good_qty,defect_qty,report_time,duration_minutes,review_status,reviewed_by,reviewed_at,product_id,department_id,unit_price,wage_amount,settled_flag,source)
VALUES
(6,10076,28,24,800,0,'2026-10-12 10:00',480,1,21,'2026-10-12 17:30',10016,23,0.45,360,0,0),
(6,10076,29,26,800,0,'2026-10-13 10:00',480,1,21,'2026-10-13 17:30',10016,24,0.40,320,0,0),
(6,10076,30,28,600,0,'2026-10-14 10:00',480,1,21,'2026-10-14 17:30',10016,25,0.30,180,0,0),
(6,10076,31,29,200,0,'2026-10-20 10:00',480,1,21,'2026-10-20 17:30',10016,26,0.20,40,0,0);

-- A0004(10077) 临期 500把：合片做完，缝伞一半
INSERT INTO prod_report(factory_id,order_id,operation_id,user_id,good_qty,defect_qty,report_time,duration_minutes,review_status,reviewed_by,reviewed_at,product_id,department_id,unit_price,wage_amount,settled_flag,source)
VALUES
(6,10077,28,24,500,0,'2026-10-15 10:00',480,1,21,'2026-10-15 17:30',10016,23,0.45,225,0,0),
(6,10077,29,26,250,0,'2026-10-22 10:00',480,1,21,'2026-10-22 17:30',10016,24,0.40,100,0,0);

-- A0005(10078) 临期 300把：合片刚做一半
INSERT INTO prod_report(factory_id,order_id,operation_id,user_id,good_qty,defect_qty,report_time,duration_minutes,review_status,reviewed_by,reviewed_at,product_id,department_id,unit_price,wage_amount,settled_flag,source)
VALUES
(6,10078,28,24,150,0,'2026-10-24 10:00',480,1,21,'2026-10-24 17:30',10016,23,0.45,67.5,0,0);

-- A0006(10079) 正常 2000把：合片刚起步
INSERT INTO prod_report(factory_id,order_id,operation_id,user_id,good_qty,defect_qty,report_time,duration_minutes,review_status,reviewed_by,reviewed_at,product_id,department_id,unit_price,wage_amount,settled_flag,source)
VALUES
(6,10079,28,24,300,0,'2026-10-26 10:00',480,0,NULL,NULL,10016,23,0.45,135,0,0);

-- A0007(10080) 正常 1000把：合片刚起步
INSERT INTO prod_report(factory_id,order_id,operation_id,user_id,good_qty,defect_qty,report_time,duration_minutes,review_status,reviewed_by,reviewed_at,product_id,department_id,unit_price,wage_amount,settled_flag,source)
VALUES
(6,10080,28,24,150,0,'2026-10-27 10:00',480,0,NULL,NULL,10016,23,0.45,67.5,0,0);

-- A0008(10081) 正常 400把：还没开工（不插报工）

-- 4. 重算工资单总额
UPDATE salary_statement
SET total_amount = ISNULL((
  SELECT SUM(amount) FROM salary_statement_item si
  WHERE si.statement_id = salary_statement.id
),0)
WHERE factory_id=6 AND period_value='2026-10';

-- 校验
PRINT '=== 每人月薪 ===';
SELECT u.name, COUNT(*) AS reports, SUM(r.wage_amount) AS wage
FROM prod_report r JOIN sys_user u ON u.Id=r.user_id
WHERE r.factory_id=6 AND r.user_id BETWEEN 24 AND 33
GROUP BY u.name, r.user_id ORDER BY r.user_id;

PRINT '=== 工资单 ===';
SELECT id, user_id, total_amount, status FROM salary_statement
WHERE factory_id=6 AND period_value='2026-10' ORDER BY user_id;

COMMIT TRANSACTION;
PRINT 'DONE';
