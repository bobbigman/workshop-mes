/* ============================================================================
  MesServer0927 补齐缺失的非聚集索引（对齐开发库 WorkshopMes）
  幂等：已存在则跳过。仅影响性能，不影响功能。
  执行：sqlcmd -S localhost -E -d MesServer0927 -C -i 本文件
 ============================================================================ */
SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='ix_price_rule_factory')
  CREATE INDEX ix_price_rule_factory ON base_price_rule(factory_id, effective_from);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_base_routing_step_routing_id_seq')
  CREATE INDEX IX_base_routing_step_routing_id_seq ON base_routing_step(routing_id, seq);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='ix_knowledge_file_ref')
  CREATE INDEX ix_knowledge_file_ref ON base_knowledge_file(ref_type, ref_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_prod_report_user_id_report_time')
  CREATE INDEX IX_prod_report_user_id_report_time ON prod_report(user_id, report_time);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_base_operation_defect_operation_id_defect_id')
  CREATE INDEX IX_base_operation_defect_operation_id_defect_id ON base_operation_defect(operation_id, defect_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_sys_custom_field_value_field_id_target_id')
  CREATE INDEX IX_sys_custom_field_value_field_id_target_id ON sys_custom_field_value(field_id, target_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_prod_work_order_operation_work_seq')
  CREATE INDEX IX_prod_work_order_operation_work_seq ON prod_work_order_operation(work_order_id, seq);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_base_operation_department_operation_id_department_id')
  CREATE INDEX IX_base_operation_department_operation_id_department_id ON base_operation_department(operation_id, department_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_sys_factory_factory_code')
  CREATE INDEX IX_sys_factory_factory_code ON sys_factory(factory_code);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_sys_user_factory_id_account')
  CREATE INDEX IX_sys_user_factory_id_account ON sys_user(factory_id, account);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_prod_work_order_factory_id_order_no')
  CREATE INDEX IX_prod_work_order_factory_id_order_no ON prod_work_order(factory_id, order_no);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_prod_work_order_operation_work_operation')
  CREATE INDEX IX_prod_work_order_operation_work_operation ON prod_work_order_operation(work_order_id, operation_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_sys_department_user_department_id_user_id')
  CREATE INDEX IX_sys_department_user_department_id_user_id ON sys_department_user(department_id, user_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_prod_report_order_id')
  CREATE INDEX IX_prod_report_order_id ON prod_report(order_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='ix_statement_item_statement')
  CREATE INDEX ix_statement_item_statement ON salary_statement_item(statement_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_base_product_factory_id_code')
  CREATE INDEX IX_base_product_factory_id_code ON base_product(factory_id, code);

PRINT 'done';
