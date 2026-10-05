namespace ahu.MicrosoftMes.Data;

public static class WechatEventSchema
{
    public const string UpgradeSql = """
-- docs/129：幂等增量升级；不删除或改写现有业务数据。
IF OBJECT_ID(N'dbo.sys_wechat_alert_rule', N'U') IS NULL
BEGIN
CREATE TABLE sys_wechat_alert_rule (
  id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
  factory_id BIGINT NOT NULL,
  name NVARCHAR(64) NOT NULL,
  event_type NVARCHAR(32) NOT NULL,
  condition_type NVARCHAR(24) NOT NULL DEFAULT 'always',
  threshold DECIMAL(5,2) NULL,
  to_user NVARCHAR(512) NULL,
  template NVARCHAR(1000) NOT NULL,
  enabled BIT NOT NULL DEFAULT 0,
  updated_at DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
  CONSTRAINT ck_wechat_rule_event CHECK (event_type IN ('report_created','order_started','order_completed')),
  CONSTRAINT ck_wechat_rule_condition CHECK ((condition_type = 'always' AND threshold IS NULL) OR (event_type = 'report_created' AND condition_type = 'defect_rate' AND threshold BETWEEN 0 AND 100 AND threshold IS NOT NULL))
);
CREATE INDEX ix_wechat_rule_factory ON sys_wechat_alert_rule(factory_id, enabled);
END;
IF OBJECT_ID(N'dbo.sys_wechat_alert_delivery', N'U') IS NULL
BEGIN
CREATE TABLE sys_wechat_alert_delivery (
  id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
  factory_id BIGINT NOT NULL,
  rule_id BIGINT NOT NULL,
  rule_name NVARCHAR(64) NOT NULL,
  event_type NVARCHAR(32) NOT NULL,
  event_id NVARCHAR(40) NOT NULL,
  occurred_at DATETIME2 NOT NULL,
  order_id BIGINT NOT NULL,
  report_id BIGINT NULL,
  to_user NVARCHAR(512) NOT NULL,
  content NVARCHAR(1000) NOT NULL,
  status NVARCHAR(24) NOT NULL DEFAULT 'pending',
  attempts INT NOT NULL DEFAULT 0 CONSTRAINT ck_wechat_delivery_attempts CHECK (attempts >= 0),
  auto_attempts INT NOT NULL DEFAULT 0 CONSTRAINT ck_wechat_delivery_auto_attempts CHECK (auto_attempts >= 0),
  next_attempt_at DATETIME2 NOT NULL,
  lease_id UNIQUEIDENTIFIER NULL,
  lease_until DATETIME2 NULL,
  sending_started BIT NOT NULL DEFAULT 0,
  failed_users NVARCHAR(512) NULL,
  error NVARCHAR(4000) NULL,
  completed_at DATETIME2 NULL,
  handled_by BIGINT NULL,
  handled_at DATETIME2 NULL,
  handling_note NVARCHAR(256) NULL,
  CONSTRAINT uq_wechat_delivery_event UNIQUE (factory_id, rule_id, event_type, event_id),
  CONSTRAINT ck_wechat_delivery_status CHECK (status IN ('pending','processing','success','retry','failed','unknown','partial','cancelled','confirmed'))
);
CREATE INDEX ix_wechat_delivery_due ON sys_wechat_alert_delivery(status, next_attempt_at, lease_until);
CREATE INDEX ix_wechat_delivery_factory ON sys_wechat_alert_delivery(factory_id, occurred_at);
END;
IF OBJECT_ID(N'dbo.sys_wechat_send_attempt', N'U') IS NULL
BEGIN
CREATE TABLE sys_wechat_send_attempt (
  id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
  factory_id BIGINT NOT NULL,
  request_key NVARCHAR(160) NOT NULL,
  alert_key NVARCHAR(128) NOT NULL,
  delivery_id BIGINT NULL,
  send_date DATE NOT NULL,
  to_user NVARCHAR(512) NOT NULL,
  outcome NVARCHAR(24) NOT NULL,
  charged BIT NOT NULL DEFAULT 0,
  failed_users NVARCHAR(512) NULL,
  error NVARCHAR(4000) NULL,
  handled_by BIGINT NULL,
  handling_note NVARCHAR(256) NULL,
  created_at DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
  CONSTRAINT uq_wechat_attempt_request UNIQUE (factory_id, request_key)
);
CREATE INDEX ix_wechat_attempt_limit ON sys_wechat_send_attempt(factory_id, send_date, charged);
END;
-- 将升级前成功日志计入统一额度；可重复执行，唯一 request_key 防重复。
INSERT INTO sys_wechat_send_attempt(factory_id, request_key, alert_key, send_date, to_user, outcome, charged, created_at)
SELECT l.factory_id, CONCAT('legacy-log:',l.id), l.alert_key, l.send_date, '', 'success', 1, l.created_at
FROM sys_wechat_alert_log l
WHERE NOT EXISTS (SELECT 1 FROM sys_wechat_send_attempt a WHERE a.factory_id=l.factory_id AND a.alert_key=l.alert_key AND a.send_date=l.send_date AND a.charged=1);

""";

    /// <summary>docs/131：群机器人 Webhook 通道；幂等，不改写存量规则数据。</summary>
    public const string GroupUpgradeSql = """
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
""";
    /// <summary>docs/141：先执行129/131升级，再幂等收口旧通道与机器人限流。</summary>
    public const string WebhookOnlyUpgradeSql = """
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
""";

}
