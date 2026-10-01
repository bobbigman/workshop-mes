using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Data;

/// <summary>数据库上下文。表结构以 docs/02-数据字典.md 为准。</summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<SysFactory> Factories => Set<SysFactory>();
    public DbSet<SysUser> Users => Set<SysUser>();
    public DbSet<SysDepartment> Departments => Set<SysDepartment>();
    public DbSet<SysDepartmentUser> DepartmentUsers => Set<SysDepartmentUser>();

    public DbSet<BaseUnit> Units => Set<BaseUnit>();
    public DbSet<BaseDefectItem> DefectItems => Set<BaseDefectItem>();
    public DbSet<BaseOperation> Operations => Set<BaseOperation>();
    public DbSet<BaseOperationDepartment> OperationDepartments => Set<BaseOperationDepartment>();
    public DbSet<BaseOperationDefect> OperationDefects => Set<BaseOperationDefect>();
    public DbSet<BaseRouting> Routings => Set<BaseRouting>();
    public DbSet<BaseRoutingStep> RoutingSteps => Set<BaseRoutingStep>();
    public DbSet<BaseProduct> Products => Set<BaseProduct>();
    public DbSet<BasePriceRule> PriceRules => Set<BasePriceRule>();
    public DbSet<BaseKnowledgeFile> KnowledgeFiles => Set<BaseKnowledgeFile>();

    public DbSet<ProdWorkOrder> WorkOrders => Set<ProdWorkOrder>();
    public DbSet<ProdWorkOrderOperation> WorkOrderOperations => Set<ProdWorkOrderOperation>();
    public DbSet<ProdReport> Reports => Set<ProdReport>();
    public DbSet<ProdAbnormal> Abnormals => Set<ProdAbnormal>();

    public DbSet<SalaryStatement> SalaryStatements => Set<SalaryStatement>();
    public DbSet<SalaryStatementItem> SalaryStatementItems => Set<SalaryStatementItem>();

    public DbSet<SysCustomField> CustomFields => Set<SysCustomField>();
    public DbSet<SysCustomFieldOption> CustomFieldOptions => Set<SysCustomFieldOption>();
    public DbSet<SysCustomFieldValue> CustomFieldValues => Set<SysCustomFieldValue>();
    public DbSet<SysPrintSetting> PrintSettings => Set<SysPrintSetting>();
    public DbSet<LoginSetting> LoginSettings => Set<LoginSetting>();
    public DbSet<SysWechatAlertSetting> WechatAlertSettings => Set<SysWechatAlertSetting>();
    public DbSet<SysWechatAlertLog> WechatAlertLogs => Set<SysWechatAlertLog>();
    public DbSet<SysMcpAuth> McpAuths => Set<SysMcpAuth>();
    public DbSet<SysMcpKey> McpKeys => Set<SysMcpKey>();
    public DbSet<SysWxBind> WxBinds => Set<SysWxBind>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SysFactory>(e =>
        {
            e.ToTable("sys_factory");
            e.HasIndex(x => x.FactoryCode).IsUnique();
            e.Property(x => x.FactoryCode).HasColumnName("factory_code").HasMaxLength(64);
            e.Property(x => x.FactoryName).HasColumnName("factory_name").HasMaxLength(128);
            e.Property(x => x.LicenseTier).HasColumnName("license_tier").HasMaxLength(16);
            e.Property(x => x.LicenseExpiresAtUtc).HasColumnName("license_expires_at_utc");
            e.Property(x => x.LastLicenseOkUtc).HasColumnName("last_license_ok_utc");
            e.Property(x => x.LastLicenseSeenUtc).HasColumnName("last_license_seen_utc");
            e.Property(x => x.TrialExpiresAtUtc).HasColumnName("trial_expires_at_utc");
            e.Property(x => x.TrialCleanedAtUtc).HasColumnName("trial_cleaned_at_utc");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<SysUser>(e =>
        {
            e.ToTable("sys_user");
            e.HasIndex(x => new { x.FactoryId, x.Account }).IsUnique();
            e.Property(x => x.FactoryId).HasColumnName("factory_id");
            e.Property(x => x.Account).HasColumnName("account").HasMaxLength(64);
            e.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(20);
            e.Property(x => x.WechatId).HasColumnName("wechat_id").HasMaxLength(64);
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(64);
            e.Property(x => x.Role).HasColumnName("role");
            e.Property(x => x.Password).HasColumnName("password").HasMaxLength(128);
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<SysDepartment>(e =>
        {
            e.ToTable("sys_department");
            e.Property(x => x.FactoryId).HasColumnName("factory_id");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(64);
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(128);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<SysDepartmentUser>(e =>
        {
            e.ToTable("sys_department_user");
            e.HasIndex(x => new { x.DepartmentId, x.UserId }).IsUnique();
            e.Property(x => x.DepartmentId).HasColumnName("department_id");
            e.Property(x => x.UserId).HasColumnName("user_id");
        });

        modelBuilder.Entity<BaseUnit>(e =>
        {
            e.ToTable("base_unit");
            e.Property(x => x.FactoryId).HasColumnName("factory_id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(64);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<BaseDefectItem>(e =>
        {
            e.ToTable("base_defect_item");
            e.Property(x => x.FactoryId).HasColumnName("factory_id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(128);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<BaseOperation>(e =>
        {
            e.ToTable("base_operation");
            e.Property(x => x.FactoryId).HasColumnName("factory_id");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(64);
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(128);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<BaseOperationDepartment>(e =>
        {
            e.ToTable("base_operation_department");
            e.HasIndex(x => new { x.OperationId, x.DepartmentId }).IsUnique();
            e.Property(x => x.OperationId).HasColumnName("operation_id");
            e.Property(x => x.DepartmentId).HasColumnName("department_id");
        });

        modelBuilder.Entity<BaseOperationDefect>(e =>
        {
            e.ToTable("base_operation_defect");
            e.HasIndex(x => new { x.OperationId, x.DefectId }).IsUnique();
            e.Property(x => x.OperationId).HasColumnName("operation_id");
            e.Property(x => x.DefectId).HasColumnName("defect_id");
        });

        modelBuilder.Entity<BaseRouting>(e =>
        {
            e.ToTable("base_routing");
            e.Property(x => x.FactoryId).HasColumnName("factory_id");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(64);
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(128);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<BaseRoutingStep>(e =>
        {
            e.ToTable("base_routing_step");
            e.HasIndex(x => new { x.RoutingId, x.Seq }).IsUnique();
            e.Property(x => x.RoutingId).HasColumnName("routing_id");
            e.Property(x => x.OperationId).HasColumnName("operation_id");
            e.Property(x => x.Seq).HasColumnName("seq");
        });

        modelBuilder.Entity<BaseProduct>(e =>
        {
            e.ToTable("base_product");
            e.HasIndex(x => new { x.FactoryId, x.Code }).IsUnique();
            e.Property(x => x.FactoryId).HasColumnName("factory_id");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(64);
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(128);
            e.Property(x => x.UnitId).HasColumnName("unit_id");
            e.Property(x => x.RoutingId).HasColumnName("routing_id");
            e.Property(x => x.Supplier).HasColumnName("supplier").HasMaxLength(128);
            e.Property(x => x.Price).HasColumnName("price").HasPrecision(12, 2);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<ProdWorkOrder>(e =>
        {
            e.ToTable("prod_work_order");
            e.HasIndex(x => new { x.FactoryId, x.OrderNo }).IsUnique();
            e.Property(x => x.FactoryId).HasColumnName("factory_id");
            e.Property(x => x.OrderNo).HasColumnName("order_no").HasMaxLength(64);
            e.Property(x => x.ProductId).HasColumnName("product_id");
            e.Property(x => x.Qty).HasColumnName("qty");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.DueDate).HasColumnName("due_date");
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<ProdWorkOrderOperation>(e =>
        {
            e.ToTable("prod_work_order_operation");
            e.HasIndex(x => new { x.WorkOrderId, x.OperationId }).IsUnique();
            e.HasIndex(x => new { x.WorkOrderId, x.Seq }).IsUnique();
            e.Property(x => x.WorkOrderId).HasColumnName("work_order_id");
            e.Property(x => x.OperationId).HasColumnName("operation_id");
            e.Property(x => x.Seq).HasColumnName("seq");
            e.Property(x => x.PlanQty).HasColumnName("plan_qty");
            e.Property(x => x.AssigneeUserId).HasColumnName("assignee_user_id");
        });

        modelBuilder.Entity<ProdReport>(e =>
        {
            e.ToTable("prod_report");
            e.HasIndex(x => x.OrderId);
            e.HasIndex(x => new { x.UserId, x.ReportTime });
            e.Property(x => x.FactoryId).HasColumnName("factory_id");
            e.Property(x => x.OrderId).HasColumnName("order_id");
            e.Property(x => x.OperationId).HasColumnName("operation_id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.GoodQty).HasColumnName("good_qty");
            e.Property(x => x.DefectQty).HasColumnName("defect_qty");
            e.Property(x => x.DefectId).HasColumnName("defect_id");
            e.Property(x => x.DurationMinutes).HasColumnName("duration_minutes");
            e.Property(x => x.BatchNo).HasColumnName("batch_no").HasMaxLength(32);
            e.Property(x => x.ProductId).HasColumnName("product_id");
            e.Property(x => x.DepartmentId).HasColumnName("department_id");
            e.Property(x => x.UnitPrice).HasColumnName("unit_price").HasPrecision(12, 4);
            e.Property(x => x.WageAmount).HasColumnName("wage_amount").HasPrecision(12, 2);
            e.Property(x => x.SettledFlag).HasColumnName("settled_flag");
            e.Property(x => x.ReviewStatus).HasColumnName("review_status");
            e.Property(x => x.ReviewedBy).HasColumnName("reviewed_by");
            e.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
            e.Property(x => x.RejectReason).HasColumnName("reject_reason").HasMaxLength(256);
            e.Property(x => x.ReportTime).HasColumnName("report_time");
            e.Property(x => x.ClientRequestId).HasColumnName("client_request_id").HasMaxLength(64);
            e.Property(x => x.ShareBatchNo).HasColumnName("share_batch_no").HasMaxLength(32);
            e.Property(x => x.OperatorUserId).HasColumnName("operator_user_id");
            e.HasIndex(x => new { x.FactoryId, x.ReviewStatus, x.ReportTime });
            e.HasIndex(x => new { x.FactoryId, x.ClientRequestId, x.UserId })
                .IsUnique()
                .HasFilter("[client_request_id] IS NOT NULL");
            e.HasIndex(x => x.ShareBatchNo)
                .HasFilter("[share_batch_no] IS NOT NULL");
        });

        modelBuilder.Entity<ProdAbnormal>(e =>
        {
            e.ToTable("prod_abnormal");
            e.HasIndex(x => new { x.FactoryId, x.Status, x.ReportedAt });
            e.Property(x => x.FactoryId).HasColumnName("factory_id");
            e.Property(x => x.AbnormalType).HasColumnName("abnormal_type");
            e.Property(x => x.Description).HasColumnName("description").HasMaxLength(500);
            e.Property(x => x.ImagePath).HasColumnName("image_path").HasMaxLength(512);
            e.Property(x => x.WorkOrderId).HasColumnName("work_order_id");
            e.Property(x => x.ReportedBy).HasColumnName("reported_by");
            e.Property(x => x.ReportedAt).HasColumnName("reported_at");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.HandledBy).HasColumnName("handled_by");
            e.Property(x => x.RecoveredAt).HasColumnName("recovered_at");
            e.Property(x => x.HandleNote).HasColumnName("handle_note").HasMaxLength(256);
        });

        modelBuilder.Entity<BasePriceRule>(e =>
        {
            e.ToTable("base_price_rule");
            e.Property(x => x.FactoryId).HasColumnName("factory_id");
            e.Property(x => x.ProductId).HasColumnName("product_id");
            e.Property(x => x.OperationId).HasColumnName("operation_id");
            e.Property(x => x.DepartmentId).HasColumnName("department_id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.PriceType).HasColumnName("price_type");
            e.Property(x => x.UnitPrice).HasColumnName("unit_price").HasPrecision(12, 4);
            e.Property(x => x.DeductPrice).HasColumnName("deduct_price").HasPrecision(12, 4);
            e.Property(x => x.EffectiveFrom).HasColumnName("effective_from");
            e.Property(x => x.EffectiveTo).HasColumnName("effective_to");
            e.Property(x => x.Priority).HasColumnName("priority");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => new { x.FactoryId, x.EffectiveFrom });
        });

        modelBuilder.Entity<BaseKnowledgeFile>(e =>
        {
            e.ToTable("base_knowledge_file");
            e.Property(x => x.FactoryId).HasColumnName("factory_id");
            e.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(256);
            e.Property(x => x.RelativePath).HasColumnName("relative_path").HasMaxLength(512);
            e.Property(x => x.FileType).HasColumnName("file_type").HasMaxLength(16);
            e.Property(x => x.RefType).HasColumnName("ref_type").HasMaxLength(32);
            e.Property(x => x.RefId).HasColumnName("ref_id");
            e.Property(x => x.Version).HasColumnName("version").HasMaxLength(32);
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => new { x.RefType, x.RefId });
        });

        modelBuilder.Entity<SalaryStatement>(e =>
        {
            e.ToTable("salary_statement");
            e.HasIndex(x => new { x.FactoryId, x.UserId, x.PeriodType, x.PeriodValue }).IsUnique();
            e.Property(x => x.FactoryId).HasColumnName("factory_id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.PeriodType).HasColumnName("period_type");
            e.Property(x => x.PeriodValue).HasColumnName("period_value").HasMaxLength(16);
            e.Property(x => x.TotalAmount).HasColumnName("total_amount").HasPrecision(12, 2);
            e.Property(x => x.BaseSalary).HasColumnName("base_salary").HasPrecision(12, 2);
            e.Property(x => x.MealAllowance).HasColumnName("meal_allowance").HasPrecision(12, 2);
            e.Property(x => x.OtherAllowance).HasColumnName("other_allowance").HasPrecision(12, 2);
            e.Property(x => x.SocialTax).HasColumnName("social_tax").HasPrecision(12, 2);
            e.Property(x => x.OtherDeduction).HasColumnName("other_deduction").HasPrecision(12, 2);
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.ConfirmedAt).HasColumnName("confirmed_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<SalaryStatementItem>(e =>
        {
            e.ToTable("salary_statement_item");
            e.HasIndex(x => x.ReportId).IsUnique();
            e.HasIndex(x => x.StatementId);
            e.Property(x => x.StatementId).HasColumnName("statement_id");
            e.Property(x => x.ReportId).HasColumnName("report_id");
            e.Property(x => x.RuleId).HasColumnName("rule_id");
            e.Property(x => x.CalcQty).HasColumnName("calc_qty");
            e.Property(x => x.UnitPrice).HasColumnName("unit_price").HasPrecision(12, 4);
            e.Property(x => x.Amount).HasColumnName("amount").HasPrecision(12, 2);
            e.Property(x => x.DeductAmount).HasColumnName("deduct_amount").HasPrecision(12, 2);
        });

        modelBuilder.Entity<SysCustomField>(e =>
        {
            e.ToTable("sys_custom_field");
            e.Property(x => x.FactoryId).HasColumnName("factory_id");
            e.Property(x => x.Target).HasColumnName("target").HasMaxLength(32);
            e.Property(x => x.FieldName).HasColumnName("field_name").HasMaxLength(64);
            e.Property(x => x.FieldType).HasColumnName("field_type").HasMaxLength(16);
            e.Property(x => x.FieldKey).HasColumnName("field_key").HasMaxLength(32);
            e.Property(x => x.ShowInOrderList).HasColumnName("show_in_order_list");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<SysCustomFieldOption>(e =>
        {
            e.ToTable("sys_custom_field_option");
            e.Property(x => x.FieldId).HasColumnName("field_id");
            e.Property(x => x.Label).HasColumnName("label").HasMaxLength(64);
            e.Property(x => x.Seq).HasColumnName("seq");
        });

        modelBuilder.Entity<SysCustomFieldValue>(e =>
        {
            e.ToTable("sys_custom_field_value");
            e.HasIndex(x => new { x.FieldId, x.TargetId }).IsUnique();
            e.Property(x => x.FieldId).HasColumnName("field_id");
            e.Property(x => x.TargetId).HasColumnName("target_id");
            e.Property(x => x.Value).HasColumnName("value").HasMaxLength(256);
        });

        modelBuilder.Entity<SysPrintSetting>(e =>
        {
            e.ToTable("sys_print_setting");
            e.HasIndex(x => x.FactoryId).IsUnique();
            e.Property(x => x.FactoryId).HasColumnName("factory_id");
            e.Property(x => x.LabelSize).HasColumnName("label_size").HasMaxLength(32);
            e.Property(x => x.QrMode).HasColumnName("qr_mode").HasMaxLength(32);
            e.Property(x => x.BaseUrl).HasColumnName("base_url").HasMaxLength(256);
            e.Property(x => x.ShowProductCode).HasColumnName("show_product_code");
            e.Property(x => x.ShowProductName).HasColumnName("show_product_name");
            e.Property(x => x.ShowQty).HasColumnName("show_qty");
            e.Property(x => x.ShowOps).HasColumnName("show_ops");
            e.Property(x => x.ShowCustomFieldIds).HasColumnName("show_custom_field_ids").HasMaxLength(512);
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<LoginSetting>(e =>
        {
            e.ToTable("login_setting");
            e.HasIndex(x => x.FactoryId).IsUnique();
            e.Property(x => x.FactoryId).HasColumnName("factory_id");
            e.Property(x => x.BannerUrl).HasColumnName("banner_url").HasMaxLength(255);
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<SysWechatAlertSetting>(e =>
        {
            e.ToTable("sys_wechat_alert_setting");
            e.HasIndex(x => x.FactoryId).IsUnique();
            e.Property(x => x.FactoryId).HasColumnName("factory_id");
            e.Property(x => x.Enabled).HasColumnName("enabled");
            e.Property(x => x.CorpId).HasColumnName("corp_id").HasMaxLength(64);
            e.Property(x => x.Secret).HasColumnName("secret").HasMaxLength(128);
            e.Property(x => x.AgentId).HasColumnName("agent_id").HasMaxLength(32);
            e.Property(x => x.ToUser).HasColumnName("to_user").HasMaxLength(512);
            e.Property(x => x.DailyLimit).HasColumnName("daily_limit");
            e.Property(x => x.NoticeSeen).HasColumnName("notice_seen");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<SysWechatAlertLog>(e =>
        {
            e.ToTable("sys_wechat_alert_log");
            e.HasIndex(x => new { x.FactoryId, x.AlertKey, x.SendDate }).IsUnique();
            e.Property(x => x.FactoryId).HasColumnName("factory_id");
            e.Property(x => x.AlertKey).HasColumnName("alert_key").HasMaxLength(128);
            e.Property(x => x.SendDate).HasColumnName("send_date");
            e.Property(x => x.Content).HasColumnName("content").HasMaxLength(1000);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<SysMcpAuth>(e =>
        {
            e.ToTable("sys_mcp_auth");
            e.HasIndex(x => x.AuthCode).IsUnique();
            e.HasIndex(x => x.AuthToken).IsUnique();
            e.Property(x => x.FactoryId).HasColumnName("factory_id");
            e.Property(x => x.AuthCode).HasColumnName("auth_code").HasMaxLength(32);
            e.Property(x => x.AuthToken).HasColumnName("auth_token").HasMaxLength(64);
            e.Property(x => x.KingdeeUserId).HasColumnName("kingdee_user_id").HasMaxLength(64);
            e.Property(x => x.KingdeeUserName).HasColumnName("kingdee_user_name").HasMaxLength(64);
            e.Property(x => x.IsFinance).HasColumnName("is_finance");
            e.Property(x => x.CodeUsed).HasColumnName("code_used");
            e.Property(x => x.CodeExpire).HasColumnName("code_expire");
            e.Property(x => x.TokenExpire).HasColumnName("token_expire");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<SysMcpKey>(e =>
        {
            e.ToTable("sys_mcp_key");
            e.HasKey(x => x.KeyId);
            e.Property(x => x.KeyId).HasColumnName("key_id");
            e.Property(x => x.FactoryId).HasColumnName("factory_id");
            e.Property(x => x.KeyAlias).HasColumnName("key_alias").HasMaxLength(64);
            e.Property(x => x.ApiKeyHash).HasColumnName("api_key_hash").HasMaxLength(255);
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.ExpireAt).HasColumnName("expire_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.Remark).HasColumnName("remark").HasMaxLength(255);
            e.HasIndex(x => x.FactoryId).HasDatabaseName("ix_mcp_key_factory");
        });

        modelBuilder.Entity<SysWxBind>(e =>
        {
            e.ToTable("sys_wx_bind");
            e.HasKey(x => x.BindId);
            e.Property(x => x.BindId).HasColumnName("bind_id");
            e.Property(x => x.FactoryId).HasColumnName("factory_id");
            e.Property(x => x.ChanType).HasColumnName("chan_type").HasMaxLength(16);
            e.Property(x => x.WxOpenid).HasColumnName("wx_openid").HasMaxLength(128);
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.BoundAt).HasColumnName("bound_at");
            e.Property(x => x.UnboundAt).HasColumnName("unbound_at");
            e.Property(x => x.Remark).HasColumnName("remark").HasMaxLength(255);
            e.HasIndex(x => new { x.FactoryId, x.ChanType, x.WxOpenid })
                .IsUnique()
                .HasFilter("[status]=1")
                .HasDatabaseName("ux_wx_bind_active");
        });
    }
}
