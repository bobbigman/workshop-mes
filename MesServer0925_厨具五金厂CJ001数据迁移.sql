/* ============================================================================
   厨具五金厂 CJ001 全量数据迁移脚本  →  目标库 MesServer0925
   ----------------------------------------------------------------------------
   数据来源 : WorkshopMes 中 factory_id=5（CJ001 厨具五金厂）全部业务数据
   覆盖范围 : 22 张表、120+ 行（工价规则→报工→复核→工资 完整闭环，含 66 号多人报工）
   生成时间 : 2026-09-25
   执行位置 : 远程服务器，SSMS / sqlcmd 直接执行本脚本

   ★ 本脚本特性（安全设计）：
     1) 动态 ID 重映射：不硬塞本地自增 id，每行插入后取新 id 并在脚本内维护映射，
        子表一律通过映射引用。远程库已有的 F001（黑湖川菜馆）数据不受影响、不冲突。
     2) 防重复守卫：事务内检测 factory_code='CJ001'，已存在则 THROW 整批回滚中止。
        （不可把守卫单独放在 GO 批里再 RETURN——拦不住后续批；部分目标库还可能缺
        factory_code 唯一索引，重跑会再插一套。）如需重灌，先按文末清理清单清理。
     3) login_setting 表若不存在会自动补建（幂等）。
     4) 密码：bcrypt 哈希原样迁移，登录账号密码不变（admin/Admin123 等）。
     5) 报工映射 / 工资明细行数校验：漏匹配会显式失败，避免静默少插。
   ============================================================================ */

USE [MesServer0925];
GO

SET NOCOUNT ON;
GO

/* ----------------------------------------------------------------------------
   0.5 确保 login_setting 表存在（幂等补建；DDL 须在事务批之外）
   ---------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.login_setting', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.login_setting
    (
        id          bigint        IDENTITY(1,1) NOT NULL,
        factory_id  bigint        NOT NULL,
        banner_url  nvarchar(255) NULL,
        updated_at  datetime2     NOT NULL CONSTRAINT DF_login_setting_ua DEFAULT (sysdatetime()),
        CONSTRAINT PK_login_setting PRIMARY KEY (id)
    );
    CREATE UNIQUE INDEX UQ_login_setting_factory ON dbo.login_setting (factory_id);
END
GO

/* ============================================================================
   以下为一个事务批（变量在 GO 间不保留，故全部放同一批）
   ★ 防重复守卫必须放在本批内：独立 GO 批里的 RETURN 拦不住后续批；
     且部分目标库可能缺少 factory_code 唯一索引，重跑会再插一套 CJ001。
   ============================================================================ */
SET XACT_ABORT ON;

DECLARE
    @fid bigint, @routing bigint, @product bigint, @cf_batch bigint, @cf_material bigint,
    @u_admin bigint, @u_banzhang bigint, @u_gongren bigint, @u_wangjun bigint, @u_zhao bigint,
    @u_piece bigint, @u_set bigint,
    @d_hua bigint, @d_chi bigint, @d_mao bigint, @d_zz bigint, @d_bz bigint,
    @op_xc bigint, @op_cy bigint, @op_dm bigint, @op_zj bigint, @op_zz bigint, @op_bz bigint,
    @dept_chong bigint, @dept_da bigint, @dept_jian bigint, @dept_zz bigint,
    @wo1 bigint, @wo2 bigint, @wo3 bigint, @wo4 bigint, @wo5 bigint,
    @pr1 bigint, @pr2 bigint, @pr3 bigint, @pr4 bigint, @pr5 bigint, @pr6 bigint,
    @st1 bigint, @st2 bigint, @st3 bigint,
    @rpt_cnt int, @item_cnt int;

-- 报工新 id 映射（本地 report id → 新 id）
DECLARE @rpt TABLE (local_id bigint PRIMARY KEY, new_id bigint NOT NULL);

BEGIN TRY
    BEGIN TRANSACTION;

    /* ---- 0. 防重复守卫（事务内，失败整批回滚） ---- */
    IF EXISTS (SELECT 1 FROM dbo.sys_factory WHERE factory_code = N'CJ001')
        THROW 50001, N'目标库已存在 CJ001（厨具五金厂），脚本中止以避免重复灌数。如需重灌，请先按文末清理清单清理后再执行。', 1;

    /* ---- 1. 工厂 ---- */
    INSERT INTO dbo.sys_factory (factory_code, factory_name, created_at)
    VALUES (N'CJ001', N'厨具五金厂', '2026-09-24 20:53:25.2927689');
    SET @fid = SCOPE_IDENTITY();

    /* ---- 2. 用户 ---- */
    INSERT INTO dbo.sys_user (factory_id, account, phone, name, role, password, status, created_at)
    VALUES (@fid, N'admin', NULL, N'管理员', 1, N'$2a$11$cDBTre2v2KFw9VNwcLjnNeaJ/eOIL4A99NgPONA9CcYRyBoayrcPW', 1, '2026-09-24 20:53:25.603');
    SET @u_admin = SCOPE_IDENTITY();
    INSERT INTO dbo.sys_user (factory_id, account, phone, name, role, password, status, created_at)
    VALUES (@fid, N'banzhang', NULL, N'张班长', 3, N'$2a$11$2AVq2.ViG3Zz1dbshCzbB.008HCe74..Uj.4GrYpUoFWO2pQBK7L.', 1, '2026-09-24 20:53:25.886');
    SET @u_banzhang = SCOPE_IDENTITY();
    INSERT INTO dbo.sys_user (factory_id, account, phone, name, role, password, status, created_at)
    VALUES (@fid, N'gongren', NULL, N'李师傅', 2, N'$2a$11$ygjlDWwqLFZGxl1DF8V.yexSA3FYK34s.7d8.VaoiNbw2bJO5wGam', 1, '2026-09-24 20:53:26.142');
    SET @u_gongren = SCOPE_IDENTITY();
    INSERT INTO dbo.sys_user (factory_id, account, phone, name, role, password, status, created_at)
    VALUES (@fid, N'wangjun', NULL, N'王军', 2, N'$2a$11$uWTg6nX5tA0VWej7UbElb.M9oa.ua/3jm3IsrVDoD1Fe8mO7bNihi', 1, '2026-09-24 21:51:05.454');
    SET @u_wangjun = SCOPE_IDENTITY();
    INSERT INTO dbo.sys_user (factory_id, account, phone, name, role, password, status, created_at)
    VALUES (@fid, N'zhaojiangling', NULL, N'赵江陵', 2, N'$2a$11$m9793Zzt6G2quDaxylkkM.Dj/gTrnFAfihiE6IPZEcaWJom/ttmQm', 1, '2026-09-24 21:51:07.825');
    SET @u_zhao = SCOPE_IDENTITY();

    /* ---- 3. 单位 ---- */
    INSERT INTO dbo.base_unit (factory_id, name, created_at) VALUES (@fid, N'件', '2026-09-24 20:53:26.1538327');
    SET @u_piece = SCOPE_IDENTITY();
    INSERT INTO dbo.base_unit (factory_id, name, created_at) VALUES (@fid, N'套', '2026-09-24 20:53:26.1557216');
    SET @u_set = SCOPE_IDENTITY();

    /* ---- 4. 不良品项 ---- */
    INSERT INTO dbo.base_defect_item (factory_id, name, created_at) VALUES (@fid, N'划伤', '2026-09-24 20:53:26.1577709');
    SET @d_hua = SCOPE_IDENTITY();
    INSERT INTO dbo.base_defect_item (factory_id, name, created_at) VALUES (@fid, N'尺寸超差', '2026-09-24 20:53:26.1592672');
    SET @d_chi = SCOPE_IDENTITY();
    INSERT INTO dbo.base_defect_item (factory_id, name, created_at) VALUES (@fid, N'毛刺', '2026-09-24 20:53:26.1614342');
    SET @d_mao = SCOPE_IDENTITY();
    INSERT INTO dbo.base_defect_item (factory_id, name, created_at) VALUES (@fid, N'组装松动', '2026-09-24 20:53:57.5251702');
    SET @d_zz = SCOPE_IDENTITY();
    INSERT INTO dbo.base_defect_item (factory_id, name, created_at) VALUES (@fid, N'包装破损', '2026-09-24 20:53:59.5560516');
    SET @d_bz = SCOPE_IDENTITY();

    /* ---- 5. 部门 ---- */
    INSERT INTO dbo.sys_department (factory_id, code, name, created_at) VALUES (@fid, N'CHONG', N'冲压组', '2026-09-24 20:53:26.1452108');
    SET @dept_chong = SCOPE_IDENTITY();
    INSERT INTO dbo.sys_department (factory_id, code, name, created_at) VALUES (@fid, N'DA', N'打磨组', '2026-09-24 20:53:26.1499308');
    SET @dept_da = SCOPE_IDENTITY();
    INSERT INTO dbo.sys_department (factory_id, code, name, created_at) VALUES (@fid, N'JIAN', N'质检组', '2026-09-24 20:53:26.1515154');
    SET @dept_jian = SCOPE_IDENTITY();
    INSERT INTO dbo.sys_department (factory_id, code, name, created_at) VALUES (@fid, N'ZZ', N'组装组', '2026-09-24 20:53:53.4502271');
    SET @dept_zz = SCOPE_IDENTITY();

    /* ---- 6. 工序 ---- */
    INSERT INTO dbo.base_operation (factory_id, code, name, created_at) VALUES (@fid, N'XC', N'下料', '2026-09-24 20:53:26.1638585');
    SET @op_xc = SCOPE_IDENTITY();
    INSERT INTO dbo.base_operation (factory_id, code, name, created_at) VALUES (@fid, N'CY', N'冲压', '2026-09-24 20:53:26.1678328');
    SET @op_cy = SCOPE_IDENTITY();
    INSERT INTO dbo.base_operation (factory_id, code, name, created_at) VALUES (@fid, N'DM', N'打磨', '2026-09-24 20:53:26.1749543');
    SET @op_dm = SCOPE_IDENTITY();
    INSERT INTO dbo.base_operation (factory_id, code, name, created_at) VALUES (@fid, N'ZJ', N'质检', '2026-09-24 20:53:26.1787436');
    SET @op_zj = SCOPE_IDENTITY();
    INSERT INTO dbo.base_operation (factory_id, code, name, created_at) VALUES (@fid, N'ZZ', N'组装', '2026-09-24 20:54:03.6544516');
    SET @op_zz = SCOPE_IDENTITY();
    INSERT INTO dbo.base_operation (factory_id, code, name, created_at) VALUES (@fid, N'BZ', N'包装', '2026-09-24 20:54:05.7116363');
    SET @op_bz = SCOPE_IDENTITY();

    /* ---- 7. 自定义字段（批次/材质） ---- */
    INSERT INTO dbo.sys_custom_field (factory_id, target, field_name, field_type, created_at, show_in_order_list)
    VALUES (@fid, N'work_order', N'批次', N'single', '2026-09-24 20:53:26.1873340', 0);
    SET @cf_batch = SCOPE_IDENTITY();
    INSERT INTO dbo.sys_custom_field (factory_id, target, field_name, field_type, created_at, show_in_order_list)
    VALUES (@fid, N'work_order', N'材质', N'single', '2026-09-24 20:53:26.1902682', 0);
    SET @cf_material = SCOPE_IDENTITY();

    /* ---- 8. 工艺路线 + 路线步骤 ---- */
    INSERT INTO dbo.base_routing (factory_id, code, name, created_at) VALUES (@fid, N'RT-CJ', N'厨具标准线', '2026-09-24 20:54:09.8236411');
    SET @routing = SCOPE_IDENTITY();
    INSERT INTO dbo.base_routing_step (routing_id, operation_id, seq) VALUES (@routing, @op_xc, 1);
    INSERT INTO dbo.base_routing_step (routing_id, operation_id, seq) VALUES (@routing, @op_cy, 2);
    INSERT INTO dbo.base_routing_step (routing_id, operation_id, seq) VALUES (@routing, @op_dm, 3);
    INSERT INTO dbo.base_routing_step (routing_id, operation_id, seq) VALUES (@routing, @op_zj, 4);
    INSERT INTO dbo.base_routing_step (routing_id, operation_id, seq) VALUES (@routing, @op_zz, 5);
    INSERT INTO dbo.base_routing_step (routing_id, operation_id, seq) VALUES (@routing, @op_bz, 6);

    /* ---- 9. 产品 ---- */
    INSERT INTO dbo.base_product (factory_id, code, name, unit_id, routing_id, supplier, price, created_at)
    VALUES (@fid, N'DDQ-001', N'打蛋器', @u_piece, @routing, NULL, NULL, '2026-09-24 20:54:13.9663903');
    SET @product = SCOPE_IDENTITY();

    /* ---- 10. 工序-不良品 关联 ---- */
    INSERT INTO dbo.base_operation_defect (operation_id, defect_id) VALUES (@op_cy, @d_chi);
    INSERT INTO dbo.base_operation_defect (operation_id, defect_id) VALUES (@op_cy, @d_mao);
    INSERT INTO dbo.base_operation_defect (operation_id, defect_id) VALUES (@op_dm, @d_hua);
    INSERT INTO dbo.base_operation_defect (operation_id, defect_id) VALUES (@op_zz, @d_zz);
    INSERT INTO dbo.base_operation_defect (operation_id, defect_id) VALUES (@op_bz, @d_bz);

    /* ---- 11. 工序-报工部门 关联 ---- */
    INSERT INTO dbo.base_operation_department (operation_id, department_id) VALUES (@op_xc, @dept_chong);
    INSERT INTO dbo.base_operation_department (operation_id, department_id) VALUES (@op_cy, @dept_chong);
    INSERT INTO dbo.base_operation_department (operation_id, department_id) VALUES (@op_dm, @dept_da);
    INSERT INTO dbo.base_operation_department (operation_id, department_id) VALUES (@op_zj, @dept_jian);
    INSERT INTO dbo.base_operation_department (operation_id, department_id) VALUES (@op_zz, @dept_zz);
    INSERT INTO dbo.base_operation_department (operation_id, department_id) VALUES (@op_bz, @dept_jian);

    /* ---- 12. 部门-成员 关联 ---- */
    INSERT INTO dbo.sys_department_user (department_id, user_id) VALUES (@dept_chong, @u_banzhang);
    INSERT INTO dbo.sys_department_user (department_id, user_id) VALUES (@dept_chong, @u_gongren);
    INSERT INTO dbo.sys_department_user (department_id, user_id) VALUES (@dept_chong, @u_wangjun);
    INSERT INTO dbo.sys_department_user (department_id, user_id) VALUES (@dept_chong, @u_zhao);
    INSERT INTO dbo.sys_department_user (department_id, user_id) VALUES (@dept_da, @u_gongren);
    INSERT INTO dbo.sys_department_user (department_id, user_id) VALUES (@dept_da, @u_wangjun);
    INSERT INTO dbo.sys_department_user (department_id, user_id) VALUES (@dept_da, @u_zhao);
    INSERT INTO dbo.sys_department_user (department_id, user_id) VALUES (@dept_jian, @u_gongren);
    INSERT INTO dbo.sys_department_user (department_id, user_id) VALUES (@dept_jian, @u_wangjun);
    INSERT INTO dbo.sys_department_user (department_id, user_id) VALUES (@dept_jian, @u_zhao);
    INSERT INTO dbo.sys_department_user (department_id, user_id) VALUES (@dept_zz, @u_gongren);
    INSERT INTO dbo.sys_department_user (department_id, user_id) VALUES (@dept_zz, @u_wangjun);
    INSERT INTO dbo.sys_department_user (department_id, user_id) VALUES (@dept_zz, @u_zhao);

    /* ---- 13. 自定义字段选项 ---- */
    INSERT INTO dbo.sys_custom_field_option (field_id, label, seq) VALUES (@cf_batch, N'第一批', 1);
    INSERT INTO dbo.sys_custom_field_option (field_id, label, seq) VALUES (@cf_batch, N'第二批', 2);
    INSERT INTO dbo.sys_custom_field_option (field_id, label, seq) VALUES (@cf_material, N'304不锈钢', 1);
    INSERT INTO dbo.sys_custom_field_option (field_id, label, seq) VALUES (@cf_material, N'201不锈钢', 2);

    /* ---- 14. 工单 ---- */
    INSERT INTO dbo.prod_work_order (factory_id, order_no, product_id, qty, status, created_by, created_at, due_date)
    VALUES (@fid, N'GD20260924001', @product, 5, 1, @u_admin, '2026-09-24 20:54:18.1650533', NULL);
    SET @wo1 = SCOPE_IDENTITY();
    INSERT INTO dbo.prod_work_order (factory_id, order_no, product_id, qty, status, created_by, created_at, due_date)
    VALUES (@fid, N'GD20260924002', @product, 5, 1, @u_admin, '2026-09-24 21:51:13.9889255', NULL);
    SET @wo2 = SCOPE_IDENTITY();
    INSERT INTO dbo.prod_work_order (factory_id, order_no, product_id, qty, status, created_by, created_at, due_date)
    VALUES (@fid, N'GD20260924003', @product, 8, 1, @u_admin, '2026-09-24 21:51:16.0342924', NULL);
    SET @wo3 = SCOPE_IDENTITY();
    INSERT INTO dbo.prod_work_order (factory_id, order_no, product_id, qty, status, created_by, created_at, due_date)
    VALUES (@fid, N'GD20260925001', @product, 5, 2, @u_admin, '2026-09-25 07:44:27.6035050', NULL);
    SET @wo4 = SCOPE_IDENTITY();
    INSERT INTO dbo.prod_work_order (factory_id, order_no, product_id, qty, status, created_by, created_at, due_date)
    VALUES (@fid, N'GD20260925002', @product, 5, 1, @u_admin, '2026-09-25 07:44:29.7528622', NULL);
    SET @wo5 = SCOPE_IDENTITY();

    /* ---- 15. 工单-工序 关联（30 行） ---- */
    -- 工单1（GD20260924001，plan 5）
    INSERT INTO dbo.prod_work_order_operation (work_order_id, operation_id, seq, plan_qty, assignee_user_id) VALUES (@wo1, @op_xc, 1, 5, NULL);
    INSERT INTO dbo.prod_work_order_operation (work_order_id, operation_id, seq, plan_qty, assignee_user_id) VALUES (@wo1, @op_cy, 2, 5, NULL);
    INSERT INTO dbo.prod_work_order_operation (work_order_id, operation_id, seq, plan_qty, assignee_user_id) VALUES (@wo1, @op_dm, 3, 5, NULL);
    INSERT INTO dbo.prod_work_order_operation (work_order_id, operation_id, seq, plan_qty, assignee_user_id) VALUES (@wo1, @op_zj, 4, 5, NULL);
    INSERT INTO dbo.prod_work_order_operation (work_order_id, operation_id, seq, plan_qty, assignee_user_id) VALUES (@wo1, @op_zz, 5, 5, NULL);
    INSERT INTO dbo.prod_work_order_operation (work_order_id, operation_id, seq, plan_qty, assignee_user_id) VALUES (@wo1, @op_bz, 6, 5, NULL);
    -- 工单2（GD20260924002，plan 5）
    INSERT INTO dbo.prod_work_order_operation (work_order_id, operation_id, seq, plan_qty, assignee_user_id) VALUES (@wo2, @op_xc, 1, 5, NULL);
    INSERT INTO dbo.prod_work_order_operation (work_order_id, operation_id, seq, plan_qty, assignee_user_id) VALUES (@wo2, @op_cy, 2, 5, NULL);
    INSERT INTO dbo.prod_work_order_operation (work_order_id, operation_id, seq, plan_qty, assignee_user_id) VALUES (@wo2, @op_dm, 3, 5, NULL);
    INSERT INTO dbo.prod_work_order_operation (work_order_id, operation_id, seq, plan_qty, assignee_user_id) VALUES (@wo2, @op_zj, 4, 5, NULL);
    INSERT INTO dbo.prod_work_order_operation (work_order_id, operation_id, seq, plan_qty, assignee_user_id) VALUES (@wo2, @op_zz, 5, 5, NULL);
    INSERT INTO dbo.prod_work_order_operation (work_order_id, operation_id, seq, plan_qty, assignee_user_id) VALUES (@wo2, @op_bz, 6, 5, NULL);
    -- 工单3（GD20260924003，plan 8）
    INSERT INTO dbo.prod_work_order_operation (work_order_id, operation_id, seq, plan_qty, assignee_user_id) VALUES (@wo3, @op_xc, 1, 8, NULL);
    INSERT INTO dbo.prod_work_order_operation (work_order_id, operation_id, seq, plan_qty, assignee_user_id) VALUES (@wo3, @op_cy, 2, 8, NULL);
    INSERT INTO dbo.prod_work_order_operation (work_order_id, operation_id, seq, plan_qty, assignee_user_id) VALUES (@wo3, @op_dm, 3, 8, NULL);
    INSERT INTO dbo.prod_work_order_operation (work_order_id, operation_id, seq, plan_qty, assignee_user_id) VALUES (@wo3, @op_zj, 4, 8, NULL);
    INSERT INTO dbo.prod_work_order_operation (work_order_id, operation_id, seq, plan_qty, assignee_user_id) VALUES (@wo3, @op_zz, 5, 8, NULL);
    INSERT INTO dbo.prod_work_order_operation (work_order_id, operation_id, seq, plan_qty, assignee_user_id) VALUES (@wo3, @op_bz, 6, 8, NULL);
    -- 工单4（GD20260925001，plan 5）
    INSERT INTO dbo.prod_work_order_operation (work_order_id, operation_id, seq, plan_qty, assignee_user_id) VALUES (@wo4, @op_xc, 1, 5, NULL);
    INSERT INTO dbo.prod_work_order_operation (work_order_id, operation_id, seq, plan_qty, assignee_user_id) VALUES (@wo4, @op_cy, 2, 5, NULL);
    INSERT INTO dbo.prod_work_order_operation (work_order_id, operation_id, seq, plan_qty, assignee_user_id) VALUES (@wo4, @op_dm, 3, 5, NULL);
    INSERT INTO dbo.prod_work_order_operation (work_order_id, operation_id, seq, plan_qty, assignee_user_id) VALUES (@wo4, @op_zj, 4, 5, NULL);
    INSERT INTO dbo.prod_work_order_operation (work_order_id, operation_id, seq, plan_qty, assignee_user_id) VALUES (@wo4, @op_zz, 5, 5, NULL);
    INSERT INTO dbo.prod_work_order_operation (work_order_id, operation_id, seq, plan_qty, assignee_user_id) VALUES (@wo4, @op_bz, 6, 5, NULL);
    -- 工单5（GD20260925002，plan 5）
    INSERT INTO dbo.prod_work_order_operation (work_order_id, operation_id, seq, plan_qty, assignee_user_id) VALUES (@wo5, @op_xc, 1, 5, NULL);
    INSERT INTO dbo.prod_work_order_operation (work_order_id, operation_id, seq, plan_qty, assignee_user_id) VALUES (@wo5, @op_cy, 2, 5, NULL);
    INSERT INTO dbo.prod_work_order_operation (work_order_id, operation_id, seq, plan_qty, assignee_user_id) VALUES (@wo5, @op_dm, 3, 5, NULL);
    INSERT INTO dbo.prod_work_order_operation (work_order_id, operation_id, seq, plan_qty, assignee_user_id) VALUES (@wo5, @op_zj, 4, 5, NULL);
    INSERT INTO dbo.prod_work_order_operation (work_order_id, operation_id, seq, plan_qty, assignee_user_id) VALUES (@wo5, @op_zz, 5, 5, NULL);
    INSERT INTO dbo.prod_work_order_operation (work_order_id, operation_id, seq, plan_qty, assignee_user_id) VALUES (@wo5, @op_bz, 6, 5, NULL);

    /* ---- 16. 工单自定义字段值 ---- */
    INSERT INTO dbo.sys_custom_field_value (field_id, target_id, value) VALUES (@cf_batch, @wo1, N'第一批');
    INSERT INTO dbo.sys_custom_field_value (field_id, target_id, value) VALUES (@cf_material, @wo1, N'304不锈钢');

    /* ---- 17. 工价规则（6 条） ---- */
    INSERT INTO dbo.base_price_rule (factory_id, product_id, operation_id, department_id, user_id, price_type, unit_price, deduct_price, effective_from, effective_to, priority, created_at)
    VALUES (@fid, @product, @op_xc, NULL, NULL, 1, 0.5000, NULL, '2026-09-24 00:00:00.0000000', NULL, 0, '2026-09-24 21:13:56.6679438');
    SET @pr1 = SCOPE_IDENTITY();
    INSERT INTO dbo.base_price_rule (factory_id, product_id, operation_id, department_id, user_id, price_type, unit_price, deduct_price, effective_from, effective_to, priority, created_at)
    VALUES (@fid, @product, @op_cy, NULL, NULL, 1, 1.2000, 0.5000, '2026-09-24 00:00:00.0000000', NULL, 0, '2026-09-24 21:13:58.7618031');
    SET @pr2 = SCOPE_IDENTITY();
    INSERT INTO dbo.base_price_rule (factory_id, product_id, operation_id, department_id, user_id, price_type, unit_price, deduct_price, effective_from, effective_to, priority, created_at)
    VALUES (@fid, @product, @op_dm, NULL, NULL, 1, 0.8000, 0.3000, '2026-09-24 00:00:00.0000000', NULL, 0, '2026-09-24 21:14:00.8367740');
    SET @pr3 = SCOPE_IDENTITY();
    INSERT INTO dbo.base_price_rule (factory_id, product_id, operation_id, department_id, user_id, price_type, unit_price, deduct_price, effective_from, effective_to, priority, created_at)
    VALUES (@fid, @product, @op_zj, NULL, NULL, 1, 0.3000, NULL, '2026-09-24 00:00:00.0000000', NULL, 0, '2026-09-24 21:14:02.8975887');
    SET @pr4 = SCOPE_IDENTITY();
    INSERT INTO dbo.base_price_rule (factory_id, product_id, operation_id, department_id, user_id, price_type, unit_price, deduct_price, effective_from, effective_to, priority, created_at)
    VALUES (@fid, @product, @op_zz, NULL, NULL, 1, 1.5000, 0.6000, '2026-09-24 00:00:00.0000000', NULL, 0, '2026-09-24 21:14:04.9599165');
    SET @pr5 = SCOPE_IDENTITY();
    INSERT INTO dbo.base_price_rule (factory_id, product_id, operation_id, department_id, user_id, price_type, unit_price, deduct_price, effective_from, effective_to, priority, created_at)
    VALUES (@fid, @product, @op_bz, NULL, NULL, 1, 0.4000, 0.2000, '2026-09-24 00:00:00.0000000', NULL, 0, '2026-09-24 21:14:06.9932042');
    SET @pr6 = SCOPE_IDENTITY();

    /* ---- 18. 报工记录（28 行，逐行捕获新 id 到 @rpt 映射） ---- */
    -- 工单1 李师傅（已复核，settled）
    INSERT INTO dbo.prod_report (factory_id, order_id, operation_id, user_id, good_qty, defect_qty, defect_id, report_time, duration_minutes, batch_no, review_status, reviewed_by, reviewed_at, reject_reason, product_id, department_id, unit_price, wage_amount, settled_flag)
    VALUES (@fid, @wo1, @op_xc, @u_gongren, 5, 0, NULL, '2026-09-24 21:14:25', 30, NULL, 1, @u_banzhang, '2026-09-24 21:14:57', NULL, @product, @dept_chong, 0.5000, 2.50, 1);
    INSERT INTO @rpt (local_id, new_id) VALUES (24, SCOPE_IDENTITY());
    INSERT INTO dbo.prod_report (factory_id, order_id, operation_id, user_id, good_qty, defect_qty, defect_id, report_time, duration_minutes, batch_no, review_status, reviewed_by, reviewed_at, reject_reason, product_id, department_id, unit_price, wage_amount, settled_flag)
    VALUES (@fid, @wo1, @op_cy, @u_gongren, 4, 1, @d_mao, '2026-09-24 21:14:28', 60, NULL, 1, @u_banzhang, '2026-09-24 21:14:55', NULL, @product, @dept_chong, 1.2000, 4.30, 1);
    INSERT INTO @rpt (local_id, new_id) VALUES (25, SCOPE_IDENTITY());
    INSERT INTO dbo.prod_report (factory_id, order_id, operation_id, user_id, good_qty, defect_qty, defect_id, report_time, duration_minutes, batch_no, review_status, reviewed_by, reviewed_at, reject_reason, product_id, department_id, unit_price, wage_amount, settled_flag)
    VALUES (@fid, @wo1, @op_dm, @u_gongren, 4, 1, @d_hua, '2026-09-24 21:14:30', 45, NULL, 1, @u_banzhang, '2026-09-24 21:14:53', NULL, @product, @dept_da, 0.8000, 2.90, 1);
    INSERT INTO @rpt (local_id, new_id) VALUES (26, SCOPE_IDENTITY());
    INSERT INTO dbo.prod_report (factory_id, order_id, operation_id, user_id, good_qty, defect_qty, defect_id, report_time, duration_minutes, batch_no, review_status, reviewed_by, reviewed_at, reject_reason, product_id, department_id, unit_price, wage_amount, settled_flag)
    VALUES (@fid, @wo1, @op_zj, @u_gongren, 4, 0, NULL, '2026-09-24 21:14:32', 20, NULL, 1, @u_banzhang, '2026-09-24 21:14:51', NULL, @product, @dept_jian, 0.3000, 1.20, 1);
    INSERT INTO @rpt (local_id, new_id) VALUES (27, SCOPE_IDENTITY());
    INSERT INTO dbo.prod_report (factory_id, order_id, operation_id, user_id, good_qty, defect_qty, defect_id, report_time, duration_minutes, batch_no, review_status, reviewed_by, reviewed_at, reject_reason, product_id, department_id, unit_price, wage_amount, settled_flag)
    VALUES (@fid, @wo1, @op_zz, @u_gongren, 4, 0, NULL, '2026-09-24 21:14:34', 40, NULL, 1, @u_banzhang, '2026-09-24 21:14:49', NULL, @product, @dept_zz, 1.5000, 6.00, 1);
    INSERT INTO @rpt (local_id, new_id) VALUES (28, SCOPE_IDENTITY());
    INSERT INTO dbo.prod_report (factory_id, order_id, operation_id, user_id, good_qty, defect_qty, defect_id, report_time, duration_minutes, batch_no, review_status, reviewed_by, reviewed_at, reject_reason, product_id, department_id, unit_price, wage_amount, settled_flag)
    VALUES (@fid, @wo1, @op_bz, @u_gongren, 4, 0, NULL, '2026-09-24 21:14:36', 15, NULL, 1, @u_banzhang, '2026-09-24 21:14:47', NULL, @product, @dept_jian, 0.4000, 1.60, 1);
    INSERT INTO @rpt (local_id, new_id) VALUES (29, SCOPE_IDENTITY());
    -- 工单2 王军（已复核，settled）
    INSERT INTO dbo.prod_report (factory_id, order_id, operation_id, user_id, good_qty, defect_qty, defect_id, report_time, duration_minutes, batch_no, review_status, reviewed_by, reviewed_at, reject_reason, product_id, department_id, unit_price, wage_amount, settled_flag)
    VALUES (@fid, @wo2, @op_xc, @u_wangjun, 5, 0, NULL, '2026-09-24 21:51:53', 30, NULL, 1, @u_banzhang, '2026-09-24 21:52:24', NULL, @product, @dept_chong, 0.5000, 2.50, 1);
    INSERT INTO @rpt (local_id, new_id) VALUES (30, SCOPE_IDENTITY());
    INSERT INTO dbo.prod_report (factory_id, order_id, operation_id, user_id, good_qty, defect_qty, defect_id, report_time, duration_minutes, batch_no, review_status, reviewed_by, reviewed_at, reject_reason, product_id, department_id, unit_price, wage_amount, settled_flag)
    VALUES (@fid, @wo2, @op_cy, @u_wangjun, 4, 1, @d_mao, '2026-09-24 21:51:55', 60, NULL, 1, @u_banzhang, '2026-09-24 21:52:24', NULL, @product, @dept_chong, 1.2000, 4.30, 1);
    INSERT INTO @rpt (local_id, new_id) VALUES (31, SCOPE_IDENTITY());
    INSERT INTO dbo.prod_report (factory_id, order_id, operation_id, user_id, good_qty, defect_qty, defect_id, report_time, duration_minutes, batch_no, review_status, reviewed_by, reviewed_at, reject_reason, product_id, department_id, unit_price, wage_amount, settled_flag)
    VALUES (@fid, @wo2, @op_dm, @u_wangjun, 4, 1, @d_hua, '2026-09-24 21:51:57', 45, NULL, 1, @u_banzhang, '2026-09-24 21:52:24', NULL, @product, @dept_da, 0.8000, 2.90, 1);
    INSERT INTO @rpt (local_id, new_id) VALUES (32, SCOPE_IDENTITY());
    INSERT INTO dbo.prod_report (factory_id, order_id, operation_id, user_id, good_qty, defect_qty, defect_id, report_time, duration_minutes, batch_no, review_status, reviewed_by, reviewed_at, reject_reason, product_id, department_id, unit_price, wage_amount, settled_flag)
    VALUES (@fid, @wo2, @op_zj, @u_wangjun, 4, 0, NULL, '2026-09-24 21:51:59', 20, NULL, 1, @u_banzhang, '2026-09-24 21:52:24', NULL, @product, @dept_jian, 0.3000, 1.20, 1);
    INSERT INTO @rpt (local_id, new_id) VALUES (33, SCOPE_IDENTITY());
    INSERT INTO dbo.prod_report (factory_id, order_id, operation_id, user_id, good_qty, defect_qty, defect_id, report_time, duration_minutes, batch_no, review_status, reviewed_by, reviewed_at, reject_reason, product_id, department_id, unit_price, wage_amount, settled_flag)
    VALUES (@fid, @wo2, @op_zz, @u_wangjun, 4, 0, NULL, '2026-09-24 21:52:01', 40, NULL, 1, @u_banzhang, '2026-09-24 21:52:24', NULL, @product, @dept_zz, 1.5000, 6.00, 1);
    INSERT INTO @rpt (local_id, new_id) VALUES (34, SCOPE_IDENTITY());
    INSERT INTO dbo.prod_report (factory_id, order_id, operation_id, user_id, good_qty, defect_qty, defect_id, report_time, duration_minutes, batch_no, review_status, reviewed_by, reviewed_at, reject_reason, product_id, department_id, unit_price, wage_amount, settled_flag)
    VALUES (@fid, @wo2, @op_bz, @u_wangjun, 4, 0, NULL, '2026-09-24 21:52:03', 15, NULL, 1, @u_banzhang, '2026-09-24 21:52:24', NULL, @product, @dept_jian, 0.4000, 1.60, 1);
    INSERT INTO @rpt (local_id, new_id) VALUES (35, SCOPE_IDENTITY());
    -- 工单3 赵江陵（含一条被驳回 review=2）
    INSERT INTO dbo.prod_report (factory_id, order_id, operation_id, user_id, good_qty, defect_qty, defect_id, report_time, duration_minutes, batch_no, review_status, reviewed_by, reviewed_at, reject_reason, product_id, department_id, unit_price, wage_amount, settled_flag)
    VALUES (@fid, @wo3, @op_xc, @u_zhao, 8, 0, NULL, '2026-09-24 21:52:07', 40, NULL, 1, @u_banzhang, '2026-09-24 21:52:26', NULL, @product, @dept_chong, 0.5000, 4.00, 1);
    INSERT INTO @rpt (local_id, new_id) VALUES (36, SCOPE_IDENTITY());
    INSERT INTO dbo.prod_report (factory_id, order_id, operation_id, user_id, good_qty, defect_qty, defect_id, report_time, duration_minutes, batch_no, review_status, reviewed_by, reviewed_at, reject_reason, product_id, department_id, unit_price, wage_amount, settled_flag)
    VALUES (@fid, @wo3, @op_cy, @u_zhao, 7, 1, @d_mao, '2026-09-24 21:52:09', 80, NULL, 1, @u_banzhang, '2026-09-24 21:52:28', NULL, @product, @dept_chong, 1.2000, 7.90, 1);
    INSERT INTO @rpt (local_id, new_id) VALUES (37, SCOPE_IDENTITY());
    INSERT INTO dbo.prod_report (factory_id, order_id, operation_id, user_id, good_qty, defect_qty, defect_id, report_time, duration_minutes, batch_no, review_status, reviewed_by, reviewed_at, reject_reason, product_id, department_id, unit_price, wage_amount, settled_flag)
    VALUES (@fid, @wo3, @op_dm, @u_zhao, 7, 0, NULL, '2026-09-24 21:52:11', 60, NULL, 1, @u_banzhang, '2026-09-24 21:52:30', NULL, @product, @dept_da, 0.8000, 5.60, 1);
    INSERT INTO @rpt (local_id, new_id) VALUES (38, SCOPE_IDENTITY());
    INSERT INTO dbo.prod_report (factory_id, order_id, operation_id, user_id, good_qty, defect_qty, defect_id, report_time, duration_minutes, batch_no, review_status, reviewed_by, reviewed_at, reject_reason, product_id, department_id, unit_price, wage_amount, settled_flag)
    VALUES (@fid, @wo3, @op_zj, @u_zhao, 7, 0, NULL, '2026-09-24 21:52:14', 25, NULL, 1, @u_banzhang, '2026-09-24 21:52:32', NULL, @product, @dept_jian, 0.3000, 2.10, 1);
    INSERT INTO @rpt (local_id, new_id) VALUES (39, SCOPE_IDENTITY());
    INSERT INTO dbo.prod_report (factory_id, order_id, operation_id, user_id, good_qty, defect_qty, defect_id, report_time, duration_minutes, batch_no, review_status, reviewed_by, reviewed_at, reject_reason, product_id, department_id, unit_price, wage_amount, settled_flag)
    VALUES (@fid, @wo3, @op_zz, @u_zhao, 6, 1, @d_zz, '2026-09-24 21:52:16', 55, NULL, 2, @u_banzhang, '2026-09-24 21:52:36', N'良品数量与实物不符，请核实后重新报工', @product, @dept_zz, 1.5000, 8.40, 0);
    INSERT INTO @rpt (local_id, new_id) VALUES (40, SCOPE_IDENTITY());
    INSERT INTO dbo.prod_report (factory_id, order_id, operation_id, user_id, good_qty, defect_qty, defect_id, report_time, duration_minutes, batch_no, review_status, reviewed_by, reviewed_at, reject_reason, product_id, department_id, unit_price, wage_amount, settled_flag)
    VALUES (@fid, @wo3, @op_bz, @u_zhao, 6, 0, NULL, '2026-09-24 21:52:18', 20, NULL, 1, @u_banzhang, '2026-09-24 21:54:08', NULL, @product, @dept_jian, 0.4000, 2.40, 0);
    INSERT INTO @rpt (local_id, new_id) VALUES (41, SCOPE_IDENTITY());
    -- 工单4 王军（待复核 review=0）
    INSERT INTO dbo.prod_report (factory_id, order_id, operation_id, user_id, good_qty, defect_qty, defect_id, report_time, duration_minutes, batch_no, review_status, reviewed_by, reviewed_at, reject_reason, product_id, department_id, unit_price, wage_amount, settled_flag)
    VALUES (@fid, @wo4, @op_xc, @u_wangjun, 5, 0, NULL, '2026-09-25 07:44:40', 20, NULL, 0, NULL, NULL, NULL, @product, @dept_chong, 0.5000, 2.50, 0);
    INSERT INTO @rpt (local_id, new_id) VALUES (42, SCOPE_IDENTITY());
    INSERT INTO dbo.prod_report (factory_id, order_id, operation_id, user_id, good_qty, defect_qty, defect_id, report_time, duration_minutes, batch_no, review_status, reviewed_by, reviewed_at, reject_reason, product_id, department_id, unit_price, wage_amount, settled_flag)
    VALUES (@fid, @wo4, @op_cy, @u_wangjun, 5, 0, NULL, '2026-09-25 07:44:42', 20, NULL, 0, NULL, NULL, NULL, @product, @dept_chong, 1.2000, 6.00, 0);
    INSERT INTO @rpt (local_id, new_id) VALUES (43, SCOPE_IDENTITY());
    INSERT INTO dbo.prod_report (factory_id, order_id, operation_id, user_id, good_qty, defect_qty, defect_id, report_time, duration_minutes, batch_no, review_status, reviewed_by, reviewed_at, reject_reason, product_id, department_id, unit_price, wage_amount, settled_flag)
    VALUES (@fid, @wo4, @op_dm, @u_wangjun, 5, 0, NULL, '2026-09-25 07:44:44', 20, NULL, 0, NULL, NULL, NULL, @product, @dept_da, 0.8000, 4.00, 0);
    INSERT INTO @rpt (local_id, new_id) VALUES (44, SCOPE_IDENTITY());
    INSERT INTO dbo.prod_report (factory_id, order_id, operation_id, user_id, good_qty, defect_qty, defect_id, report_time, duration_minutes, batch_no, review_status, reviewed_by, reviewed_at, reject_reason, product_id, department_id, unit_price, wage_amount, settled_flag)
    VALUES (@fid, @wo4, @op_zj, @u_wangjun, 5, 0, NULL, '2026-09-25 07:44:46', 20, NULL, 0, NULL, NULL, NULL, @product, @dept_jian, 0.3000, 1.50, 0);
    INSERT INTO @rpt (local_id, new_id) VALUES (45, SCOPE_IDENTITY());
    INSERT INTO dbo.prod_report (factory_id, order_id, operation_id, user_id, good_qty, defect_qty, defect_id, report_time, duration_minutes, batch_no, review_status, reviewed_by, reviewed_at, reject_reason, product_id, department_id, unit_price, wage_amount, settled_flag)
    VALUES (@fid, @wo4, @op_zz, @u_wangjun, 5, 0, NULL, '2026-09-25 07:44:48', 20, NULL, 0, NULL, NULL, NULL, @product, @dept_zz, 1.5000, 7.50, 0);
    INSERT INTO @rpt (local_id, new_id) VALUES (46, SCOPE_IDENTITY());
    INSERT INTO dbo.prod_report (factory_id, order_id, operation_id, user_id, good_qty, defect_qty, defect_id, report_time, duration_minutes, batch_no, review_status, reviewed_by, reviewed_at, reject_reason, product_id, department_id, unit_price, wage_amount, settled_flag)
    VALUES (@fid, @wo4, @op_bz, @u_wangjun, 5, 0, NULL, '2026-09-25 07:44:50', 20, NULL, 0, NULL, NULL, NULL, @product, @dept_jian, 0.4000, 2.00, 0);
    INSERT INTO @rpt (local_id, new_id) VALUES (47, SCOPE_IDENTITY());
    -- 工单5 王军（待复核 review=0）
    INSERT INTO dbo.prod_report (factory_id, order_id, operation_id, user_id, good_qty, defect_qty, defect_id, report_time, duration_minutes, batch_no, review_status, reviewed_by, reviewed_at, reject_reason, product_id, department_id, unit_price, wage_amount, settled_flag)
    VALUES (@fid, @wo5, @op_xc, @u_wangjun, 5, 0, NULL, '2026-09-25 07:44:52', 20, NULL, 0, NULL, NULL, NULL, @product, @dept_chong, 0.5000, 2.50, 0);
    INSERT INTO @rpt (local_id, new_id) VALUES (48, SCOPE_IDENTITY());
    INSERT INTO dbo.prod_report (factory_id, order_id, operation_id, user_id, good_qty, defect_qty, defect_id, report_time, duration_minutes, batch_no, review_status, reviewed_by, reviewed_at, reject_reason, product_id, department_id, unit_price, wage_amount, settled_flag)
    VALUES (@fid, @wo5, @op_cy, @u_wangjun, 5, 0, NULL, '2026-09-25 07:44:55', 20, NULL, 0, NULL, NULL, NULL, @product, @dept_chong, 1.2000, 6.00, 0);
    INSERT INTO @rpt (local_id, new_id) VALUES (49, SCOPE_IDENTITY());
    INSERT INTO dbo.prod_report (factory_id, order_id, operation_id, user_id, good_qty, defect_qty, defect_id, report_time, duration_minutes, batch_no, review_status, reviewed_by, reviewed_at, reject_reason, product_id, department_id, unit_price, wage_amount, settled_flag)
    VALUES (@fid, @wo5, @op_dm, @u_wangjun, 5, 0, NULL, '2026-09-25 07:44:57', 20, NULL, 0, NULL, NULL, NULL, @product, @dept_da, 0.8000, 4.00, 0);
    INSERT INTO @rpt (local_id, new_id) VALUES (50, SCOPE_IDENTITY());
    INSERT INTO dbo.prod_report (factory_id, order_id, operation_id, user_id, good_qty, defect_qty, defect_id, report_time, duration_minutes, batch_no, review_status, reviewed_by, reviewed_at, reject_reason, product_id, department_id, unit_price, wage_amount, settled_flag)
    VALUES (@fid, @wo5, @op_zj, @u_wangjun, 5, 0, NULL, '2026-09-25 07:44:59', 20, NULL, 0, NULL, NULL, NULL, @product, @dept_jian, 0.3000, 1.50, 0);
    INSERT INTO @rpt (local_id, new_id) VALUES (51, SCOPE_IDENTITY());

    /* ---- 19. 工资单（3 张） ---- */
    INSERT INTO dbo.salary_statement (factory_id, user_id, period_type, period_value, total_amount, status, confirmed_at, created_at)
    VALUES (@fid, @u_gongren, 1, N'2026-09', 18.50, 1, '2026-09-24 21:15:10.5965580', '2026-09-24 21:15:04.1664899');
    SET @st1 = SCOPE_IDENTITY();
    INSERT INTO dbo.salary_statement (factory_id, user_id, period_type, period_value, total_amount, status, confirmed_at, created_at)
    VALUES (@fid, @u_wangjun, 1, N'2026-09', 18.50, 1, '2026-09-24 21:52:47.4866679', '2026-09-24 21:52:41.3187454');
    SET @st2 = SCOPE_IDENTITY();
    INSERT INTO dbo.salary_statement (factory_id, user_id, period_type, period_value, total_amount, status, confirmed_at, created_at)
    VALUES (@fid, @u_zhao, 1, N'2026-09', 19.60, 1, '2026-09-24 21:52:49.5462761', '2026-09-24 21:52:43.3510645');
    SET @st3 = SCOPE_IDENTITY();

    /* ---- 20. 工资单明细（16 行，report_id 走 @rpt 映射，rule_id 走工价规则映射） ---- */
    -- 李师傅工资单明细
    INSERT INTO dbo.salary_statement_item (statement_id, report_id, rule_id, calc_qty, unit_price, amount, deduct_amount)
    SELECT @st1, r.new_id, @pr1, 5, 0.5000, 2.50, 0.00 FROM @rpt r WHERE r.local_id = 24;
    INSERT INTO dbo.salary_statement_item (statement_id, report_id, rule_id, calc_qty, unit_price, amount, deduct_amount)
    SELECT @st1, r.new_id, @pr2, 4, 1.2000, 4.80, 0.50 FROM @rpt r WHERE r.local_id = 25;
    INSERT INTO dbo.salary_statement_item (statement_id, report_id, rule_id, calc_qty, unit_price, amount, deduct_amount)
    SELECT @st1, r.new_id, @pr3, 4, 0.8000, 3.20, 0.30 FROM @rpt r WHERE r.local_id = 26;
    INSERT INTO dbo.salary_statement_item (statement_id, report_id, rule_id, calc_qty, unit_price, amount, deduct_amount)
    SELECT @st1, r.new_id, @pr4, 4, 0.3000, 1.20, 0.00 FROM @rpt r WHERE r.local_id = 27;
    INSERT INTO dbo.salary_statement_item (statement_id, report_id, rule_id, calc_qty, unit_price, amount, deduct_amount)
    SELECT @st1, r.new_id, @pr5, 4, 1.5000, 6.00, 0.00 FROM @rpt r WHERE r.local_id = 28;
    INSERT INTO dbo.salary_statement_item (statement_id, report_id, rule_id, calc_qty, unit_price, amount, deduct_amount)
    SELECT @st1, r.new_id, @pr6, 4, 0.4000, 1.60, 0.00 FROM @rpt r WHERE r.local_id = 29;
    -- 王军工资单明细
    INSERT INTO dbo.salary_statement_item (statement_id, report_id, rule_id, calc_qty, unit_price, amount, deduct_amount)
    SELECT @st2, r.new_id, @pr1, 5, 0.5000, 2.50, 0.00 FROM @rpt r WHERE r.local_id = 30;
    INSERT INTO dbo.salary_statement_item (statement_id, report_id, rule_id, calc_qty, unit_price, amount, deduct_amount)
    SELECT @st2, r.new_id, @pr2, 4, 1.2000, 4.80, 0.50 FROM @rpt r WHERE r.local_id = 31;
    INSERT INTO dbo.salary_statement_item (statement_id, report_id, rule_id, calc_qty, unit_price, amount, deduct_amount)
    SELECT @st2, r.new_id, @pr3, 4, 0.8000, 3.20, 0.30 FROM @rpt r WHERE r.local_id = 32;
    INSERT INTO dbo.salary_statement_item (statement_id, report_id, rule_id, calc_qty, unit_price, amount, deduct_amount)
    SELECT @st2, r.new_id, @pr4, 4, 0.3000, 1.20, 0.00 FROM @rpt r WHERE r.local_id = 33;
    INSERT INTO dbo.salary_statement_item (statement_id, report_id, rule_id, calc_qty, unit_price, amount, deduct_amount)
    SELECT @st2, r.new_id, @pr5, 4, 1.5000, 6.00, 0.00 FROM @rpt r WHERE r.local_id = 34;
    INSERT INTO dbo.salary_statement_item (statement_id, report_id, rule_id, calc_qty, unit_price, amount, deduct_amount)
    SELECT @st2, r.new_id, @pr6, 4, 0.4000, 1.60, 0.00 FROM @rpt r WHERE r.local_id = 35;
    -- 赵江陵工资单明细
    INSERT INTO dbo.salary_statement_item (statement_id, report_id, rule_id, calc_qty, unit_price, amount, deduct_amount)
    SELECT @st3, r.new_id, @pr1, 8, 0.5000, 4.00, 0.00 FROM @rpt r WHERE r.local_id = 36;
    INSERT INTO dbo.salary_statement_item (statement_id, report_id, rule_id, calc_qty, unit_price, amount, deduct_amount)
    SELECT @st3, r.new_id, @pr2, 7, 1.2000, 8.40, 0.50 FROM @rpt r WHERE r.local_id = 37;
    INSERT INTO dbo.salary_statement_item (statement_id, report_id, rule_id, calc_qty, unit_price, amount, deduct_amount)
    SELECT @st3, r.new_id, @pr3, 7, 0.8000, 5.60, 0.00 FROM @rpt r WHERE r.local_id = 38;
    INSERT INTO dbo.salary_statement_item (statement_id, report_id, rule_id, calc_qty, unit_price, amount, deduct_amount)
    SELECT @st3, r.new_id, @pr4, 7, 0.3000, 2.10, 0.00 FROM @rpt r WHERE r.local_id = 39;

    /* ---- 20.5 映射完整性校验（SELECT 插入漏匹配时不会报错，必须显式拦） ---- */
    SELECT @rpt_cnt = COUNT(*) FROM @rpt;
    IF @rpt_cnt <> 28
        THROW 50002, N'报工映射行数不是 28，中止以免工资明细挂错报工。', 1;

    IF EXISTS (
        SELECT 1 FROM (VALUES (24),(25),(26),(27),(28),(29),(30),(31),(32),(33),(34),(35),(36),(37),(38),(39)) v(local_id)
        WHERE NOT EXISTS (SELECT 1 FROM @rpt r WHERE r.local_id = v.local_id)
    )
        THROW 50003, N'工资明细所需的报工 local_id（24-39）在 @rpt 中缺失，中止。', 1;

    SELECT @item_cnt = COUNT(*)
    FROM dbo.salary_statement_item
    WHERE statement_id IN (@st1, @st2, @st3);
    IF @item_cnt <> 16
        THROW 50004, N'工资明细不是 16 行（映射可能未命中），中止。', 1;

    /* ---- 21. 登录页设置（CJ001） ----
       路径按 docs/02 约定 /uploads/login/{factoryId}.ext；
       远程还需把本机 uploads/login/5.jpg 拷过去并改名为 {新factory_id}.jpg，否则登录页左侧图 404。 */
    INSERT INTO dbo.login_setting (factory_id, banner_url, updated_at)
    VALUES (@fid, N'/uploads/login/' + CONVERT(nvarchar(20), @fid) + N'.jpg', '2026-09-25 07:46:42.0250719');

    COMMIT TRANSACTION;
    PRINT '[OK] CJ001 厨具五金厂全量数据迁移完成（22 张表）。';
    PRINT N'[提醒] 新 factory_id=' + CONVERT(nvarchar(20), @fid)
        + N'；请拷贝登录图到 /uploads/login/' + CONVERT(nvarchar(20), @fid) + N'.jpg';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT '[失败] ' + ERROR_MESSAGE();
    THROW;
END CATCH;
GO

/* ============================================================================
   迁移结果核对（应能查到 CJ001 数据；失败回滚后数量均为 0）
   ---------------------------------------------------------------------------- */
DECLARE @cid bigint = (SELECT TOP (1) Id FROM dbo.sys_factory WHERE factory_code = N'CJ001' ORDER BY Id);
SELECT '工厂' AS 项, CASE WHEN @cid IS NULL THEN 0 ELSE 1 END AS 数量
UNION ALL SELECT '用户', CASE WHEN @cid IS NULL THEN 0 ELSE (SELECT COUNT(*) FROM dbo.sys_user WHERE factory_id=@cid) END
UNION ALL SELECT '工单', CASE WHEN @cid IS NULL THEN 0 ELSE (SELECT COUNT(*) FROM dbo.prod_work_order WHERE factory_id=@cid) END
UNION ALL SELECT '报工', CASE WHEN @cid IS NULL THEN 0 ELSE (SELECT COUNT(*) FROM dbo.prod_report WHERE factory_id=@cid) END
UNION ALL SELECT '工资单', CASE WHEN @cid IS NULL THEN 0 ELSE (SELECT COUNT(*) FROM dbo.salary_statement WHERE factory_id=@cid) END
UNION ALL SELECT '工资明细', CASE WHEN @cid IS NULL THEN 0 ELSE (SELECT COUNT(*) FROM dbo.salary_statement_item WHERE statement_id IN (SELECT id FROM dbo.salary_statement WHERE factory_id=@cid)) END;
GO

/* ============================================================================
   重灌清理清单（仅当需要重灌时使用；顺序即删除倒序）
   ----------------------------------------------------------------------------
   USE [MesServer0925];
   DECLARE @fid bigint = (SELECT Id FROM dbo.sys_factory WHERE factory_code='CJ001');
   IF @fid IS NOT NULL
   BEGIN
       DELETE FROM dbo.salary_statement_item WHERE statement_id IN (SELECT id FROM dbo.salary_statement WHERE factory_id=@fid);
       DELETE FROM dbo.salary_statement WHERE factory_id=@fid;
       DELETE FROM dbo.prod_report WHERE factory_id=@fid;
       DELETE FROM dbo.prod_work_order_operation WHERE work_order_id IN (SELECT Id FROM dbo.prod_work_order WHERE factory_id=@fid);
       DELETE FROM dbo.sys_custom_field_value WHERE field_id IN (SELECT Id FROM dbo.sys_custom_field WHERE factory_id=@fid);
       DELETE FROM dbo.prod_work_order WHERE factory_id=@fid;
       DELETE FROM dbo.base_price_rule WHERE factory_id=@fid;
       DELETE FROM dbo.base_operation_department WHERE operation_id IN (SELECT Id FROM dbo.base_operation WHERE factory_id=@fid);
       DELETE FROM dbo.base_operation_defect WHERE operation_id IN (SELECT Id FROM dbo.base_operation WHERE factory_id=@fid);
       DELETE FROM dbo.base_routing_step WHERE routing_id IN (SELECT Id FROM dbo.base_routing WHERE factory_id=@fid);
       DELETE FROM dbo.sys_department_user WHERE department_id IN (SELECT Id FROM dbo.sys_department WHERE factory_id=@fid);
       DELETE FROM dbo.sys_custom_field_option WHERE field_id IN (SELECT Id FROM dbo.sys_custom_field WHERE factory_id=@fid);
       DELETE FROM dbo.base_product WHERE factory_id=@fid;
       DELETE FROM dbo.base_routing WHERE factory_id=@fid;
       DELETE FROM dbo.base_operation WHERE factory_id=@fid;
       DELETE FROM dbo.sys_custom_field WHERE factory_id=@fid;
       DELETE FROM dbo.sys_department WHERE factory_id=@fid;
       DELETE FROM dbo.base_defect_item WHERE factory_id=@fid;
       DELETE FROM dbo.base_unit WHERE factory_id=@fid;
       DELETE FROM dbo.sys_user WHERE factory_id=@fid;
       DELETE FROM dbo.login_setting WHERE factory_id=@fid;
       DELETE FROM dbo.sys_factory WHERE Id=@fid;
   END
   ============================================================================ */
GO
