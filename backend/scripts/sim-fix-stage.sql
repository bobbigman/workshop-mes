SET NOCOUNT ON; SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;
BEGIN TRANSACTION;

-- A0001(10074) 超期 1200：合片~打珠尾做完，品检/包装没动
INSERT INTO prod_report(factory_id,order_id,operation_id,user_id,good_qty,defect_qty,report_time,duration_minutes,review_status,reviewed_by,reviewed_at,product_id,department_id,unit_price,wage_amount,settled_flag,source) VALUES
(6,10074,28,24,1200,0,'2026-10-05 10:00',480,1,21,'2026-10-05 17:30',10016,23,0.45,540,0,0),
(6,10074,29,26,1200,0,'2026-10-06 10:00',480,1,21,'2026-10-06 17:30',10016,24,0.40,480,0,0),
(6,10074,30,28,1200,0,'2026-10-07 10:00',480,1,21,'2026-10-07 17:30',10016,25,0.30,360,0,0),
(6,10074,31,29,1200,0,'2026-10-08 10:00',480,1,21,'2026-10-08 17:30',10016,26,0.20,240,0,0),
(6,10074,32,30,1200,0,'2026-10-09 10:00',480,1,21,'2026-10-09 17:30',10016,27,0.25,300,0,0),
(6,10074,33,31,1200,0,'2026-10-10 10:00',480,1,21,'2026-10-10 17:30',10016,28,0.30,360,0,0);

-- A0002(10075) 超期 600：合片~顶带做完，珠尾/品检/包装没动
INSERT INTO prod_report(factory_id,order_id,operation_id,user_id,good_qty,defect_qty,report_time,duration_minutes,review_status,reviewed_by,reviewed_at,product_id,department_id,unit_price,wage_amount,settled_flag,source) VALUES
(6,10075,28,24,600,0,'2026-10-08 10:00',480,1,21,'2026-10-08 17:30',10016,23,0.45,270,0,0),
(6,10075,29,26,600,0,'2026-10-09 10:00',480,1,21,'2026-10-09 17:30',10016,24,0.40,240,0,0),
(6,10075,30,28,600,0,'2026-10-10 10:00',480,1,21,'2026-10-10 17:30',10016,25,0.30,180,0,0),
(6,10075,31,29,600,0,'2026-10-12 10:00',480,1,21,'2026-10-12 17:30',10016,26,0.20,120,0,0),
(6,10075,32,30,600,0,'2026-10-13 10:00',480,1,21,'2026-10-13 17:30',10016,27,0.25,150,0,0);

-- A0003(10076) 临期 800：合片~拉边做完，小裁刚开始
INSERT INTO prod_report(factory_id,order_id,operation_id,user_id,good_qty,defect_qty,report_time,duration_minutes,review_status,reviewed_by,reviewed_at,product_id,department_id,unit_price,wage_amount,settled_flag,source) VALUES
(6,10076,28,24,800,0,'2026-10-12 10:00',480,1,21,'2026-10-12 17:30',10016,23,0.45,360,0,0),
(6,10076,29,26,800,0,'2026-10-13 10:00',480,1,21,'2026-10-13 17:30',10016,24,0.40,320,0,0),
(6,10076,30,28,600,0,'2026-10-14 10:00',480,1,21,'2026-10-14 17:30',10016,25,0.30,180,0,0),
(6,10076,31,29,200,0,'2026-10-20 10:00',480,1,21,'2026-10-20 17:30',10016,26,0.20,40,0,0);

-- A0004(10077) 临期 500：合片做完，缝伞一半
INSERT INTO prod_report(factory_id,order_id,operation_id,user_id,good_qty,defect_qty,report_time,duration_minutes,review_status,reviewed_by,reviewed_at,product_id,department_id,unit_price,wage_amount,settled_flag,source) VALUES
(6,10077,28,24,500,0,'2026-10-15 10:00',480,1,21,'2026-10-15 17:30',10016,23,0.45,225,0,0),
(6,10077,29,26,250,0,'2026-10-22 10:00',480,1,21,'2026-10-22 17:30',10016,24,0.40,100,0,0);

-- A0005(10078) 临期 300：合片刚做一半
INSERT INTO prod_report(factory_id,order_id,operation_id,user_id,good_qty,defect_qty,report_time,duration_minutes,review_status,reviewed_by,reviewed_at,product_id,department_id,unit_price,wage_amount,settled_flag,source) VALUES
(6,10078,28,24,150,0,'2026-10-24 10:00',480,1,21,'2026-10-24 17:30',10016,23,0.45,67.5,0,0);

-- A0006(10079) 正常 2000：合片刚起步
INSERT INTO prod_report(factory_id,order_id,operation_id,user_id,good_qty,defect_qty,report_time,duration_minutes,review_status,reviewed_by,reviewed_at,product_id,department_id,unit_price,wage_amount,settled_flag,source) VALUES
(6,10079,28,24,300,0,'2026-10-26 10:00',480,0,NULL,NULL,10016,23,0.45,135,0,0);

-- A0007(10080) 正常 1000：合片刚起步
INSERT INTO prod_report(factory_id,order_id,operation_id,user_id,good_qty,defect_qty,report_time,duration_minutes,review_status,reviewed_by,reviewed_at,product_id,department_id,unit_price,wage_amount,settled_flag,source) VALUES
(6,10080,28,24,150,0,'2026-10-27 10:00',480,0,NULL,NULL,10016,23,0.45,67.5,0,0);

-- A0008(10081) 正常 400：不开工

-- 重算工资总额（只从 salary_statement_item 汇总，stage 报工是视觉摆设）
UPDATE s
SET total_amount = ISNULL((SELECT SUM(amount) FROM salary_statement_item si WHERE si.statement_id=s.id),0)
FROM salary_statement s WHERE s.factory_id=6 AND s.period_value='2026-10';

COMMIT TRANSACTION;
PRINT 'stage reports done';
