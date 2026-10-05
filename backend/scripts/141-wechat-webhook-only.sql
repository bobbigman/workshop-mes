-- 前置：启动时自动执行129、131、141；手动运行须先完成129/131。
-- docs/141：群通道收口；保留历史和旧应用字段，未知结果不得自动重发。
IF COL_LENGTH(N'sys_wechat_send_attempt', N'robot_key_hash') IS NULL
    ALTER TABLE sys_wechat_send_attempt ADD robot_key_hash NVARCHAR(64) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.sys_wechat_send_attempt') AND name=N'ix_wechat_attempt_robot_rate')
    EXEC(N'CREATE INDEX ix_wechat_attempt_robot_rate ON sys_wechat_send_attempt(robot_key_hash, created_at)');
UPDATE sys_wechat_alert_rule SET enabled=0, updated_at=SYSDATETIME()
WHERE target_channel=N'session' AND enabled=1;
UPDATE d SET status=N'unknown', error=N'旧会话发送阶段中断，请先核实；此通道已停止，不可重新排队',
    lease_id=NULL, lease_until=NULL, completed_at=SYSDATETIME()
FROM sys_wechat_alert_delivery d
JOIN sys_wechat_alert_rule r ON r.id=d.rule_id AND r.factory_id=d.factory_id
WHERE r.target_channel=N'session' AND d.status=N'processing' AND d.sending_started=1;
UPDATE d SET status=N'cancelled', error=N'旧会话通道已停止，请重新配置群规则',
    lease_id=NULL, lease_until=NULL, completed_at=SYSDATETIME()
FROM sys_wechat_alert_delivery d
JOIN sys_wechat_alert_rule r ON r.id=d.rule_id AND r.factory_id=d.factory_id
WHERE r.target_channel=N'session' AND (d.status IN (N'pending',N'retry',N'failed',N'partial')
    OR (d.status=N'processing' AND d.sending_started=0));
