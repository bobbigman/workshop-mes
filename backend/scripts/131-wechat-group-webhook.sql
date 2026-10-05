-- docs/131：群机器人 Webhook；幂等。存量规则 target_channel 默认 session。不覆盖业务数据。
-- 启动时 DbCompat 会执行同一段；DBA 可先跑本脚本再启动。

IF COL_LENGTH(N'sys_wechat_alert_setting', N'webhook_key') IS NULL
    ALTER TABLE sys_wechat_alert_setting ADD webhook_key NVARCHAR(200) NULL;
IF OBJECT_ID(N'dbo.sys_wechat_alert_rule', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'sys_wechat_alert_rule', N'target_channel') IS NULL
        ALTER TABLE sys_wechat_alert_rule ADD target_channel NVARCHAR(16) NOT NULL CONSTRAINT DF_wechat_rule_channel DEFAULT 'session';
    IF COL_LENGTH(N'sys_wechat_alert_rule', N'webhook_key') IS NULL
        ALTER TABLE sys_wechat_alert_rule ADD webhook_key NVARCHAR(200) NULL;
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_wechat_rule_event')
        ALTER TABLE sys_wechat_alert_rule DROP CONSTRAINT ck_wechat_rule_event;
    ALTER TABLE sys_wechat_alert_rule ADD CONSTRAINT ck_wechat_rule_event
      CHECK (event_type IN ('report_created','order_started','order_completed','abnormal_reported'));
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_wechat_rule_condition')
        ALTER TABLE sys_wechat_alert_rule DROP CONSTRAINT ck_wechat_rule_condition;
    ALTER TABLE sys_wechat_alert_rule ADD CONSTRAINT ck_wechat_rule_condition CHECK (
      (condition_type = 'always' AND threshold IS NULL)
      OR (event_type = 'report_created' AND condition_type = 'defect_rate' AND threshold BETWEEN 0 AND 100 AND threshold IS NOT NULL)
      OR (event_type = 'abnormal_reported' AND condition_type IN ('abnormal_device','abnormal_material','abnormal_quality') AND threshold IS NULL));
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_wechat_rule_channel')
        ALTER TABLE sys_wechat_alert_rule DROP CONSTRAINT ck_wechat_rule_channel;
    EXEC(N'ALTER TABLE sys_wechat_alert_rule ADD CONSTRAINT ck_wechat_rule_channel CHECK (target_channel IN (''session'',''group''))');
END
