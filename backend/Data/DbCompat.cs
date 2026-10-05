using Microsoft.EntityFrameworkCore;

namespace ahu.MicrosoftMes.Data;

/// <summary>
/// 兼容建表：项目用 EnsureCreated（库存在时不建表），新增表需幂等补齐。
/// 只对「后加的表」执行 IF NOT EXISTS 建表，不动已有表结构与数据。
/// </summary>
public static class DbCompat
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        // 工单工序任务表（P0 新增）
        await db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'prod_work_order_operation')
BEGIN
    CREATE TABLE prod_work_order_operation (
        id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_prod_work_order_operation PRIMARY KEY,
        work_order_id BIGINT NOT NULL,
        operation_id  BIGINT NOT NULL,
        seq           INT NOT NULL DEFAULT 1,
        plan_qty      INT NOT NULL DEFAULT 0
    );
    CREATE UNIQUE INDEX IX_prod_work_order_operation_work_operation ON prod_work_order_operation (work_order_id, operation_id);
    CREATE UNIQUE INDEX IX_prod_work_order_operation_work_seq ON prod_work_order_operation (work_order_id, seq);
END");

        // 班组长派工执行人（docs/29）：工单工序加一列，空=未派工
        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('prod_work_order_operation', 'assignee_user_id') IS NULL
BEGIN
    ALTER TABLE prod_work_order_operation ADD assignee_user_id BIGINT NULL;
END");

        // 多人派工明细（docs/202）：一道工序可派多人；从旧单值列迁移
        await db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'prod_work_order_operation_assignee')
BEGIN
  CREATE TABLE prod_work_order_operation_assignee (
    id                      BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_prod_wop_assignee PRIMARY KEY,
    work_order_operation_id BIGINT NOT NULL,
    user_id                 BIGINT NOT NULL,
    CONSTRAINT UQ_prod_wop_assignee UNIQUE (work_order_operation_id, user_id)
  );
END
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'prod_work_order_operation_assignee')
BEGIN
  INSERT INTO prod_work_order_operation_assignee (work_order_operation_id, user_id)
  SELECT t.id, t.assignee_user_id
  FROM prod_work_order_operation t
  WHERE t.assignee_user_id IS NOT NULL
    AND NOT EXISTS (
      SELECT 1 FROM prod_work_order_operation_assignee a
      WHERE a.work_order_operation_id = t.id AND a.user_id = t.assignee_user_id
    );
END");

        // 报工时长（分钟）
        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('prod_report', 'duration_minutes') IS NULL
BEGIN
    ALTER TABLE prod_report ADD duration_minutes INT NOT NULL CONSTRAINT DF_prod_report_duration DEFAULT 0;
END");

        // 报工批量补报批次号（batch_no，可空；依据 docs/17）
        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('prod_report', 'batch_no') IS NULL
BEGIN
    ALTER TABLE prod_report ADD batch_no NVARCHAR(32) NULL;
END");

        // 工单计划交期（due_date，可空）
        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('prod_work_order', 'due_date') IS NULL
BEGIN
    ALTER TABLE prod_work_order ADD due_date DATETIME2 NULL;
END");

        // 工单打印标签设置（工厂级）
        await db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'sys_print_setting')
BEGIN
    CREATE TABLE sys_print_setting (
        id                BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_sys_print_setting PRIMARY KEY,
        factory_id        BIGINT NOT NULL,
        label_size        NVARCHAR(32) NOT NULL CONSTRAINT DF_sys_print_setting_size DEFAULT N'Label80x60',
        qr_mode           NVARCHAR(32) NOT NULL CONSTRAINT DF_sys_print_setting_qr DEFAULT N'OrderNo',
        base_url          NVARCHAR(256) NULL,
        show_product_code BIT NOT NULL CONSTRAINT DF_sys_print_setting_pc DEFAULT 1,
        show_product_name BIT NOT NULL CONSTRAINT DF_sys_print_setting_pn DEFAULT 1,
        show_qty          BIT NOT NULL CONSTRAINT DF_sys_print_setting_qty DEFAULT 1,
        show_ops          BIT NOT NULL CONSTRAINT DF_sys_print_setting_ops DEFAULT 1,
        show_custom_field_ids NVARCHAR(512) NULL,
        updated_at        DATETIME2 NOT NULL CONSTRAINT DF_sys_print_setting_ua DEFAULT SYSDATETIME(),
        CONSTRAINT UQ_sys_print_setting_factory UNIQUE (factory_id)
    );
END");

        await db.Database.ExecuteSqlRawAsync(@"
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'sys_print_setting')
AND NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'sys_print_setting') AND name = N'show_custom_field_ids'
)
BEGIN
    ALTER TABLE sys_print_setting ADD show_custom_field_ids NVARCHAR(512) NULL;
END");

        // 企业微信预警设置（默认关闭，docs/1B）
        await db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'sys_wechat_alert_setting')
BEGIN
    CREATE TABLE sys_wechat_alert_setting (
        id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_sys_wechat_alert_setting PRIMARY KEY,
        factory_id    BIGINT NOT NULL,
        enabled       BIT NOT NULL CONSTRAINT DF_sys_wechat_alert_enabled DEFAULT 0,
        corp_id       NVARCHAR(64) NULL,
        secret        NVARCHAR(128) NULL,
        agent_id      NVARCHAR(32) NULL,
        to_user       NVARCHAR(512) NULL,
        daily_limit   INT NOT NULL CONSTRAINT DF_sys_wechat_alert_limit DEFAULT 20,
        notice_seen   BIT NOT NULL CONSTRAINT DF_sys_wechat_alert_notice DEFAULT 0,
        updated_at    DATETIME2 NOT NULL CONSTRAINT DF_sys_wechat_alert_ua DEFAULT SYSDATETIME(),
        CONSTRAINT UQ_sys_wechat_alert_setting_factory UNIQUE (factory_id)
    );
END");

        await db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'sys_wechat_alert_log')
BEGIN
    CREATE TABLE sys_wechat_alert_log (
        id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_sys_wechat_alert_log PRIMARY KEY,
        factory_id    BIGINT NOT NULL,
        alert_key     NVARCHAR(128) NOT NULL,
        send_date     DATE NOT NULL,
        content       NVARCHAR(1000) NULL,
        created_at    DATETIME2 NOT NULL CONSTRAINT DF_sys_wechat_alert_log_ua DEFAULT SYSDATETIME(),
        CONSTRAINT UQ_sys_wechat_alert_log UNIQUE (factory_id, alert_key, send_date)
    );
END");

        // docs/129：紧跟企微设置/日志后建事件表+发送账本，避免后续升级段失败时测试推送缺表 500
        await db.Database.ExecuteSqlRawAsync(WechatEventSchema.UpgradeSql);
        await db.Database.ExecuteSqlRawAsync(WechatPushplusRollbackSql);
        await db.Database.ExecuteSqlRawAsync(WechatEventSchema.GroupUpgradeSql);
        await db.Database.ExecuteSqlRawAsync(WechatEventSchema.WebhookOnlyUpgradeSql);

        // 报工复核双轨（docs/22）：历史行 DEFAULT 1=已通过；新插入必须由应用显式赋 0/1
        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('prod_report', 'review_status') IS NULL
BEGIN
    ALTER TABLE prod_report ADD review_status TINYINT NOT NULL
        CONSTRAINT DF_prod_report_review DEFAULT 1;
END");
        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('prod_report', 'reviewed_by') IS NULL
BEGIN
    ALTER TABLE prod_report ADD reviewed_by BIGINT NULL;
END");
        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('prod_report', 'reviewed_at') IS NULL
BEGIN
    ALTER TABLE prod_report ADD reviewed_at DATETIME2 NULL;
END");
        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('prod_report', 'reject_reason') IS NULL
BEGIN
    ALTER TABLE prod_report ADD reject_reason NVARCHAR(256) NULL;
END");
        await db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_report_review' AND object_id = OBJECT_ID('prod_report'))
BEGIN
    CREATE INDEX ix_report_review ON prod_report(factory_id, review_status, report_time);
END");

        // 工资快照列（docs/21）
        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('prod_report', 'product_id') IS NULL
  ALTER TABLE prod_report ADD product_id BIGINT NULL;");
        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('prod_report', 'department_id') IS NULL
  ALTER TABLE prod_report ADD department_id BIGINT NULL;");
        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('prod_report', 'unit_price') IS NULL
  ALTER TABLE prod_report ADD unit_price DECIMAL(12,4) NULL;");
        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('prod_report', 'wage_amount') IS NULL
  ALTER TABLE prod_report ADD wage_amount DECIMAL(12,2) NULL;");
        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('prod_report', 'settled_flag') IS NULL
BEGIN
  ALTER TABLE prod_report ADD settled_flag BIT NOT NULL CONSTRAINT DF_prod_report_settled DEFAULT 0;
END");

        // docs/94：报工去重 / 分摊批次 / 操作人
        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('prod_report', 'client_request_id') IS NULL
  ALTER TABLE prod_report ADD client_request_id NVARCHAR(64) NULL;");
        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('prod_report', 'share_batch_no') IS NULL
  ALTER TABLE prod_report ADD share_batch_no NVARCHAR(32) NULL;");
        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('prod_report', 'operator_user_id') IS NULL
  ALTER TABLE prod_report ADD operator_user_id BIGINT NULL;");
        await db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'uk_report_client_req' AND object_id = OBJECT_ID('prod_report'))
BEGIN
  CREATE UNIQUE INDEX uk_report_client_req
    ON prod_report(factory_id, client_request_id, user_id)
    WHERE client_request_id IS NOT NULL;
END");
        await db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_report_share_batch' AND object_id = OBJECT_ID('prod_report'))
BEGIN
  CREATE INDEX ix_report_share_batch ON prod_report(share_batch_no)
    WHERE share_batch_no IS NOT NULL;
END");

        await db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'base_price_rule')
BEGIN
  CREATE TABLE base_price_rule (
    id             BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_base_price_rule PRIMARY KEY,
    factory_id     BIGINT NOT NULL,
    product_id     BIGINT NULL,
    operation_id   BIGINT NULL,
    department_id  BIGINT NULL,
    user_id        BIGINT NULL,
    price_type     TINYINT NOT NULL,
    unit_price     DECIMAL(12,4) NOT NULL,
    deduct_price   DECIMAL(12,4) NULL,
    effective_from DATETIME2 NOT NULL,
    effective_to   DATETIME2 NULL,
    priority       INT NOT NULL CONSTRAINT DF_base_price_rule_pri DEFAULT 0,
    created_at     DATETIME2 NOT NULL CONSTRAINT DF_base_price_rule_ca DEFAULT SYSDATETIME()
  );
  CREATE INDEX ix_price_rule_factory ON base_price_rule(factory_id, effective_from);
END");

        await db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'salary_statement')
BEGIN
  CREATE TABLE salary_statement (
    id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_salary_statement PRIMARY KEY,
    factory_id    BIGINT NOT NULL,
    user_id       BIGINT NOT NULL,
    period_type   TINYINT NOT NULL,
    period_value  NVARCHAR(16) NOT NULL,
    total_amount  DECIMAL(12,2) NOT NULL CONSTRAINT DF_salary_statement_amt DEFAULT 0,
    status        TINYINT NOT NULL CONSTRAINT DF_salary_statement_st DEFAULT 0,
    confirmed_at  DATETIME2 NULL,
    created_at    DATETIME2 NOT NULL CONSTRAINT DF_salary_statement_ca DEFAULT SYSDATETIME(),
    CONSTRAINT uk_statement_period UNIQUE (factory_id, user_id, period_type, period_value)
  );
END");

        // docs/136：5 手工列——历史全 0 则 DROP；有非 0 则保留列（历史遗留停用，实体已不映射）
        // 不回溯：旧 revoke 删单留下的历史缺口不补；已删单不恢复。
        // 整段必须动态 SQL：列已删/从未建时，静态 WHERE base_salary 会在批编译期报 207，阻断后续 DbCompat（含企微账本表）。
        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('salary_statement', 'base_salary') IS NOT NULL
   AND COL_LENGTH('salary_statement', 'meal_allowance') IS NOT NULL
   AND COL_LENGTH('salary_statement', 'other_allowance') IS NOT NULL
   AND COL_LENGTH('salary_statement', 'social_tax') IS NOT NULL
   AND COL_LENGTH('salary_statement', 'other_deduction') IS NOT NULL
BEGIN
  DECLARE @hasNonZero int = 1;
  DECLARE @chk nvarchar(max) = N'
    SELECT @cnt = CASE WHEN EXISTS (
      SELECT 1 FROM salary_statement
      WHERE base_salary <> 0 OR meal_allowance <> 0 OR other_allowance <> 0
         OR social_tax <> 0 OR other_deduction <> 0
    ) THEN 1 ELSE 0 END';
  EXEC sp_executesql @chk, N'@cnt int OUTPUT', @cnt = @hasNonZero OUTPUT;
  IF @hasNonZero = 0
  BEGIN
    DECLARE @df sysname, @sql nvarchar(512);
    DECLARE @cols TABLE(name sysname);
    INSERT INTO @cols(name) VALUES
      (N'base_salary'),(N'meal_allowance'),(N'other_allowance'),(N'social_tax'),(N'other_deduction');
    DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT name FROM @cols;
    DECLARE @col sysname;
    OPEN c;
    FETCH NEXT FROM c INTO @col;
    WHILE @@FETCH_STATUS = 0
    BEGIN
      SELECT @df = dc.name
      FROM sys.default_constraints dc
      INNER JOIN sys.columns col ON col.default_object_id = dc.object_id
      WHERE dc.parent_object_id = OBJECT_ID(N'salary_statement') AND col.name = @col;
      IF @df IS NOT NULL
      BEGIN
        SET @sql = N'ALTER TABLE salary_statement DROP CONSTRAINT [' + @df + N']';
        EXEC(@sql);
      END
      SET @sql = N'ALTER TABLE salary_statement DROP COLUMN [' + @col + N']';
      EXEC(@sql);
      FETCH NEXT FROM c INTO @col;
    END
    CLOSE c; DEALLOCATE c;
  END
END");

        await db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'salary_statement_item')
BEGIN
  CREATE TABLE salary_statement_item (
    id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_salary_statement_item PRIMARY KEY,
    statement_id  BIGINT NOT NULL,
    report_id     BIGINT NOT NULL,
    rule_id       BIGINT NULL,
    calc_qty      INT NOT NULL CONSTRAINT DF_salary_item_qty DEFAULT 0,
    unit_price    DECIMAL(12,4) NOT NULL,
    amount        DECIMAL(12,2) NOT NULL CONSTRAINT DF_salary_item_amt DEFAULT 0,
    deduct_amount DECIMAL(12,2) NULL,
    CONSTRAINT uk_statement_item_report UNIQUE (report_id)
  );
  CREATE INDEX ix_statement_item_statement ON salary_statement_item(statement_id);
END");

        // MCP 授权（豆包方舟演示，docs/25）
        await db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'sys_mcp_auth')
BEGIN
  CREATE TABLE sys_mcp_auth (
    id                BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_sys_mcp_auth PRIMARY KEY,
    factory_id        BIGINT NOT NULL,
    auth_code         NVARCHAR(32) NOT NULL,
    auth_token        NVARCHAR(64) NOT NULL,
    kingdee_user_id   NVARCHAR(64) NULL,
    kingdee_user_name NVARCHAR(64) NULL,
    is_finance        BIT NOT NULL CONSTRAINT DF_sys_mcp_auth_fin DEFAULT 0,
    code_used         BIT NOT NULL CONSTRAINT DF_sys_mcp_auth_used DEFAULT 0,
    code_expire       DATETIME2 NOT NULL,
    token_expire      DATETIME2 NOT NULL,
    created_at        DATETIME2 NOT NULL CONSTRAINT DF_sys_mcp_auth_ca DEFAULT SYSDATETIME(),
    CONSTRAINT UQ_sys_mcp_auth_code UNIQUE (auth_code),
    CONSTRAINT UQ_sys_mcp_auth_token UNIQUE (auth_token)
  );
END");

        // 制造知识库文件（docs/48）：挂在产品/工序，只存相对路径，不存文件内容
        await db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'base_knowledge_file')
BEGIN
  CREATE TABLE base_knowledge_file (
    id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_base_knowledge_file PRIMARY KEY,
    factory_id    BIGINT NOT NULL,
    file_name     NVARCHAR(256) NOT NULL,
    relative_path NVARCHAR(512) NOT NULL,
    file_type     NVARCHAR(16) NOT NULL,
    ref_type      NVARCHAR(32) NOT NULL,
    ref_id        BIGINT NOT NULL,
    version       NVARCHAR(32) NULL,
    created_by    BIGINT NULL,
    created_at    DATETIME2 NOT NULL CONSTRAINT DF_base_knowledge_file_ca DEFAULT SYSDATETIME()
  );
  CREATE INDEX ix_knowledge_file_ref ON base_knowledge_file(ref_type, ref_id);
END");

        // 工单自定义字段「在列表显示」（docs/18 补充）
        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('sys_custom_field', 'show_in_order_list') IS NULL
BEGIN
  ALTER TABLE sys_custom_field ADD show_in_order_list BIT NOT NULL
    CONSTRAINT DF_sys_custom_field_show_list DEFAULT 0;
END");

        // 自定义字段业务编码（docs/102 色码汇总：color / spec）
        // 加列与建索引必须分批：同批内 CREATE INDEX 会在编译期找不到尚未提交的新列
        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('sys_custom_field', 'field_key') IS NULL
BEGIN
  ALTER TABLE sys_custom_field ADD field_key NVARCHAR(32) NULL;
END");

        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('sys_custom_field', 'field_key') IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'uk_custom_field_key' AND object_id = OBJECT_ID(N'sys_custom_field'))
BEGIN
  CREATE UNIQUE INDEX uk_custom_field_key ON sys_custom_field(factory_id, target, field_key)
    WHERE field_key IS NOT NULL;
END");

        // 工人异常上报（docs/28）
        await db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'prod_abnormal')
BEGIN
  CREATE TABLE prod_abnormal (
    id             BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_prod_abnormal PRIMARY KEY,
    factory_id     BIGINT NOT NULL,
    abnormal_type  TINYINT NOT NULL,
    description    NVARCHAR(500) NOT NULL,
    image_path     NVARCHAR(512) NULL,
    work_order_id  BIGINT NULL,
    reported_by    BIGINT NOT NULL,
    reported_at    DATETIME2 NOT NULL CONSTRAINT DF_prod_abnormal_ra DEFAULT SYSDATETIME(),
    status         TINYINT NOT NULL CONSTRAINT DF_prod_abnormal_st DEFAULT 0,
    handled_by     BIGINT NULL,
    recovered_at   DATETIME2 NULL,
    handle_note    NVARCHAR(256) NULL
  );
  CREATE INDEX ix_abnormal_factory_status ON prod_abnormal(factory_id, status, reported_at DESC);
END");

        // 登录页设置（docs/69）：每工厂一条，左侧大图自配
        await db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'login_setting')
BEGIN
  CREATE TABLE login_setting (
    id         BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_login_setting PRIMARY KEY,
    factory_id BIGINT NOT NULL,
    banner_url NVARCHAR(255) NULL,
    updated_at DATETIME2 NOT NULL CONSTRAINT DF_login_setting_ua DEFAULT SYSDATETIME(),
    CONSTRAINT UQ_login_setting_factory UNIQUE (factory_id)
  );
END");

        // 工人手机视角（docs/200）：每工厂一条；默认全车间可见
        await db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'sys_worker_view_setting')
BEGIN
  CREATE TABLE sys_worker_view_setting (
    id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_sys_worker_view_setting PRIMARY KEY,
    factory_id       BIGINT NOT NULL,
    worker_view_mode TINYINT NOT NULL CONSTRAINT DF_sys_worker_view_mode DEFAULT 1,
    updated_at       DATETIME2 NOT NULL CONSTRAINT DF_sys_worker_view_ua DEFAULT SYSDATETIME(),
    CONSTRAINT UQ_sys_worker_view_setting_factory UNIQUE (factory_id)
  );
END");

        // 用户微信号（docs/80）：可空，仅记录；不做登录/推送
        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('sys_user', 'wechat_id') IS NULL
BEGIN
    ALTER TABLE sys_user ADD wechat_id NVARCHAR(64) NULL;
END");

        // 工厂授权档（docs/88）：trial / enterprise / flagship；存量默认入门款
        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('sys_factory', 'license_tier') IS NULL
BEGIN
    ALTER TABLE sys_factory ADD license_tier NVARCHAR(16) NOT NULL
        CONSTRAINT DF_sys_factory_license_tier DEFAULT 'trial';
END");

        // 轻量云授权缓存（docs/89）
        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('sys_factory', 'license_expires_at_utc') IS NULL
BEGIN
    ALTER TABLE sys_factory ADD license_expires_at_utc DATETIME2 NULL;
END
IF COL_LENGTH('sys_factory', 'last_license_ok_utc') IS NULL
BEGIN
    ALTER TABLE sys_factory ADD last_license_ok_utc DATETIME2 NULL;
END
IF COL_LENGTH('sys_factory', 'last_license_seen_utc') IS NULL
BEGIN
    ALTER TABLE sys_factory ADD last_license_seen_utc DATETIME2 NULL;
END");

        // 体验账套到期清理（docs/90）：只加列，不对历史行回填到期时间
        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('sys_factory', 'trial_expires_at_utc') IS NULL
BEGIN
    ALTER TABLE sys_factory ADD trial_expires_at_utc DATETIME2 NULL;
END
IF COL_LENGTH('sys_factory', 'trial_cleaned_at_utc') IS NULL
BEGIN
    ALTER TABLE sys_factory ADD trial_cleaned_at_utc DATETIME2 NULL;
END");

        // MCP 每厂一把 Key（docs/96）
        await db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'sys_mcp_key')
BEGIN
    CREATE TABLE sys_mcp_key (
        key_id       BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_sys_mcp_key PRIMARY KEY,
        factory_id   BIGINT NOT NULL,
        key_alias    NVARCHAR(64) NOT NULL,
        api_key_hash NVARCHAR(255) NOT NULL,
        status       TINYINT NOT NULL CONSTRAINT DF_sys_mcp_key_status DEFAULT 1,
        expire_at    DATETIME2 NULL,
        created_at   DATETIME2 NOT NULL CONSTRAINT DF_sys_mcp_key_ca DEFAULT SYSDATETIME(),
        remark       NVARCHAR(255) NULL
    );
    CREATE INDEX ix_mcp_key_factory ON sys_mcp_key(factory_id);
END");

        // MCP 会话身份绑定（docs/97，无短信）
        await db.Database.ExecuteSqlRawAsync(@"
SET QUOTED_IDENTIFIER ON;
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'sys_wx_bind')
BEGIN
    CREATE TABLE sys_wx_bind (
        bind_id    BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_sys_wx_bind PRIMARY KEY,
        factory_id BIGINT NOT NULL,
        chan_type  NVARCHAR(16) NOT NULL,
        wx_openid  NVARCHAR(128) NOT NULL,
        user_id    BIGINT NOT NULL,
        status     TINYINT NOT NULL CONSTRAINT DF_sys_wx_bind_status DEFAULT 1,
        bound_at   DATETIME2 NOT NULL CONSTRAINT DF_sys_wx_bind_ba DEFAULT SYSDATETIME(),
        unbound_at DATETIME2 NULL,
        remark     NVARCHAR(255) NULL
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ux_wx_bind_active' AND object_id = OBJECT_ID('sys_wx_bind'))
BEGIN
    CREATE UNIQUE INDEX ux_wx_bind_active
        ON sys_wx_bind(factory_id, chan_type, wx_openid)
        WHERE status=1;
END");

        // 报工修改日志（docs/205）
        await db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'prod_report_change_log')
BEGIN
  CREATE TABLE prod_report_change_log (
    id                   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_prod_report_change_log PRIMARY KEY,
    factory_id           BIGINT NOT NULL,
    report_id            BIGINT NOT NULL,
    changed_by           BIGINT NOT NULL,
    old_good_qty         INT NOT NULL,
    old_defect_qty       INT NOT NULL,
    old_defect_id        BIGINT NULL,
    old_duration_minutes INT NOT NULL,
    new_good_qty         INT NOT NULL,
    new_defect_qty       INT NOT NULL,
    new_defect_id        BIGINT NULL,
    new_duration_minutes INT NOT NULL,
    source               TINYINT NOT NULL,
    changed_at           DATETIME2 NOT NULL CONSTRAINT DF_prod_report_change_log_ca DEFAULT SYSDATETIME()
  );
  CREATE INDEX ix_report_change_log_report ON prod_report_change_log(report_id, changed_at DESC);
  CREATE INDEX ix_report_change_log_factory ON prod_report_change_log(factory_id, changed_at DESC);
END");
    }

    /// <summary>docs/133 回滚：去掉 Pushplus 升表字段与规则扩展列；列不存在则跳过。</summary>
    const string WechatPushplusRollbackSql = """
DECLARE @drop NVARCHAR(MAX) = N'';
SELECT @drop = @drop + N'ALTER TABLE sys_wechat_alert_setting DROP CONSTRAINT ' + QUOTENAME(dc.name) + N';'
FROM sys.default_constraints dc
JOIN sys.columns c ON c.default_object_id = dc.object_id
WHERE dc.parent_object_id = OBJECT_ID(N'dbo.sys_wechat_alert_setting')
  AND c.name IN (N'channel', N'pushplus_token', N'pushplus_tokens', N'due_warn_days', N'overdue_repeat_hours');
IF LEN(@drop) > 0 EXEC sp_executesql @drop;
IF COL_LENGTH(N'sys_wechat_alert_setting', N'channel') IS NOT NULL
    ALTER TABLE sys_wechat_alert_setting DROP COLUMN channel;
IF COL_LENGTH(N'sys_wechat_alert_setting', N'pushplus_token') IS NOT NULL
    ALTER TABLE sys_wechat_alert_setting DROP COLUMN pushplus_token;
IF COL_LENGTH(N'sys_wechat_alert_setting', N'pushplus_tokens') IS NOT NULL
    ALTER TABLE sys_wechat_alert_setting DROP COLUMN pushplus_tokens;
IF COL_LENGTH(N'sys_wechat_alert_setting', N'due_warn_days') IS NOT NULL
    ALTER TABLE sys_wechat_alert_setting DROP COLUMN due_warn_days;
IF COL_LENGTH(N'sys_wechat_alert_setting', N'overdue_repeat_hours') IS NOT NULL
    ALTER TABLE sys_wechat_alert_setting DROP COLUMN overdue_repeat_hours;
IF OBJECT_ID(N'dbo.sys_wechat_alert_rule', N'U') IS NOT NULL
BEGIN
    DELETE FROM sys_wechat_alert_rule WHERE event_type = N'due_alert';
    IF COL_LENGTH(N'sys_wechat_alert_rule', N'severity') IS NOT NULL
        ALTER TABLE sys_wechat_alert_rule DROP COLUMN severity;
    IF COL_LENGTH(N'sys_wechat_alert_rule', N'quiet_begin') IS NOT NULL
        ALTER TABLE sys_wechat_alert_rule DROP COLUMN quiet_begin;
    IF COL_LENGTH(N'sys_wechat_alert_rule', N'quiet_end') IS NOT NULL
        ALTER TABLE sys_wechat_alert_rule DROP COLUMN quiet_end;
    IF COL_LENGTH(N'sys_wechat_alert_rule', N'dedup_key_type') IS NOT NULL
        ALTER TABLE sys_wechat_alert_rule DROP COLUMN dedup_key_type;
    IF COL_LENGTH(N'sys_wechat_alert_rule', N'dedup_limit') IS NOT NULL
        ALTER TABLE sys_wechat_alert_rule DROP COLUMN dedup_limit;
END
""";
}
