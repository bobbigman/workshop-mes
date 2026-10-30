using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public class WechatRuleDto
{
    public string Name { get; set; } = "";
    public string EventType { get; set; } = "report_created";
    public string ConditionType { get; set; } = "always";
    public decimal? Threshold { get; set; }
    public string TargetChannel { get; set; } = "group";
    public string? WebhookKey { get; set; }
    public string? ToUser { get; set; }
    public string Template { get; set; } = "";
    public bool Enabled { get; set; }
}

public class WechatDeliveryActionDto
{
    public string? Note { get; set; }
    public bool Sent { get; set; }
}

public class WechatEventService(AppDbContext db, ILicenseTierService license, WechatMessageSender sender,
    ILogger<WechatEventService> logger)
{
    public static string? StatusEvent(byte before, byte after) =>
        before == 0 && after == 1 ? "order_started" : before is 0 or 1 && after == 2 ? "order_completed" : null;

    public static bool Matches(WechatRuleDto rule, int good, int defect) => rule.ConditionType == "always" ||
        (rule.ConditionType == "defect_rate" && (long)good + defect > 0 &&
         (decimal)defect * 100m / ((long)good + defect) > rule.Threshold);

    public static string AbnormalKind(byte type) => type switch
    {
        1 => "abnormal_device",
        2 => "abnormal_material",
        3 => "abnormal_quality",
        _ => ""
    };

    public static bool MatchesAbnormal(string conditionType, byte type) =>
        conditionType == "always" || conditionType == AbnormalKind(type);

    public static void ValidateRule(WechatRuleDto rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Name) || rule.Name.Trim().Length > 64)
            throw ThrowHelper.BizUser("规则名称长度须在1到64个字符之间，请修改后重新保存。");
        if (rule.EventType is not ("report_created" or "order_started" or "order_completed" or "abnormal_reported" or "operation_assigned"))
            throw ThrowHelper.BizUser("选择了未知的消息事件类型，请从支持的事件列表中选择正确的事件。");
        if (rule.TargetChannel is not ("session" or "group"))
            throw ThrowHelper.BizUser("接收通道须选择会话到人或群消息其中一种，请修正后重新保存。");
        if (rule.ConditionType == "always")
        {
            if (rule.Threshold != null) throw ThrowHelper.BizUser("全部提醒类型的规则不需要填写阈值，请清空阈值后重新保存。");
        }
        else if (rule.EventType == "abnormal_reported")
        {
            if (rule.ConditionType is not ("abnormal_device" or "abnormal_material" or "abnormal_quality") || rule.Threshold != null)
                throw ThrowHelper.BizUser("异常上报事件请选择设备故障、物料短缺或质量异常中的一种，且不要填写阈值，请修正后重新保存。");
        }
        else if (rule.EventType == "operation_assigned")
        {
            if (rule.ConditionType != "always" || rule.Threshold != null)
                throw ThrowHelper.BizUser("派工事件只支持全部提醒模式，不支持设置阈值条件，请移除阈值后重新保存。");
        }
        else if (rule.ConditionType != "defect_rate" || rule.EventType != "report_created" ||
                 rule.Threshold == null || rule.Threshold < 0 || rule.Threshold > 100 || decimal.Round(rule.Threshold.Value, 2) != rule.Threshold)
            throw ThrowHelper.BizUser("不良率预警数值请填0到100之间、最多两位小数，且只用于报工事件。");
        if (string.IsNullOrWhiteSpace(rule.Template) || rule.Template.Length > 1000)
            throw ThrowHelper.BizUser("消息模板内容长度须在1到1000个字符之间，请修改后重新保存。");
        var allowed = new HashSet<string> { "工单号", "产品编号", "产品名称", "事件时间" };
        if (rule.EventType == "report_created") allowed.UnionWith(["工序", "良品数", "不良数", "不良率"]);
        if (rule.EventType == "abnormal_reported") allowed.UnionWith(["异常类型", "问题描述", "上报人"]);
        if (rule.EventType == "operation_assigned") allowed.UnionWith(["工序", "派工对象"]);
        foreach (Match match in Regex.Matches(rule.Template, @"\{([^{}]+)\}"))
            if (!allowed.Contains(match.Groups[1].Value)) throw ThrowHelper.BizUser($"消息模板中使用了当前事件不支持的变量 {match.Value}，请参照可用变量列表修正模板内容。");
        var rest = Regex.Replace(rule.Template, @"\{([^{}]+)\}", "");
        if (rest.Contains('{') || rest.Contains('}')) throw ThrowHelper.BizUser("消息模板中的变量括号不成对，请检查模板内容，确保每个变量括号完整配对。");
        if (!string.IsNullOrWhiteSpace(rule.ToUser))
        {
            if (rule.TargetChannel == "group") WechatMessageSender.NormalizeGroupUsers(rule.ToUser);
            else WechatMessageSender.NormalizeUsers(rule.ToUser);
        }
        if (!string.IsNullOrWhiteSpace(rule.WebhookKey) && rule.WebhookKey.Trim() != "********")
            WechatMessageSender.NormalizeWebhookKey(rule.WebhookKey);
    }

    public async Task<List<SysWechatAlertRule>> RulesAsync(long factoryId)
    {
        var list = await db.WechatAlertRules.AsNoTracking()
            .Where(x => x.FactoryId == factoryId).OrderByDescending(x => x.Id).ToListAsync();
        foreach (var row in list)
            row.WebhookKey = string.IsNullOrEmpty(row.WebhookKey) ? null : "********";
        return list;
    }

    public async Task SaveRuleAsync(long? id, WechatRuleDto dto, long factoryId)
    {
        if (string.IsNullOrWhiteSpace(dto.TargetChannel)) dto.TargetChannel = "group";
        var existing = id == null ? null : await db.WechatAlertRules.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.FactoryId == factoryId)
            ?? (id == null ? null : throw ThrowHelper.BizUser("该规则不存在或当前用户无权限修改，请刷新列表确认规则状态及自身权限。"));
        if (dto.TargetChannel != "group" || existing is { TargetChannel: "session" })
            throw ThrowHelper.BizUser("旧的会话机器人规则已停用，请删除旧规则并重新创建新的群机器人规则。");
        ValidateRule(dto);
        if (dto.Enabled)
        {
            var setting = await db.WechatAlertSettings.AsNoTracking().FirstOrDefaultAsync(x => x.FactoryId == factoryId);
            if (setting == null)
                throw ThrowHelper.BizUser("请先在「微信预警设置」中保存基础设置，然后再添加群消息规则；如某条规则有独立Webhook，可先保存该规则再开启总开关。");
            if (dto.TargetChannel == "group")
            {
                var key = WechatMessageSender.ResolveWebhookKey(dto.WebhookKey, existing?.WebhookKey)
                    ?? WechatMessageSender.NormalizeWebhookKey(setting.WebhookKey);
                if (key == null) throw ThrowHelper.BizUser("这条群消息规则没有填Webhook，全厂也没设默认Webhook。请在本规则中填写Webhook，或到「微信预警设置」中设置全局默认Webhook。");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(setting.CorpId) || string.IsNullOrWhiteSpace(setting.Secret) ||
                    !int.TryParse(setting.AgentId, out var agent) || agent <= 0)
                    throw ThrowHelper.BizUser("选择会话到人模式前，需要先在企业微信设置中填写有效的 CorpID、Secret 和 AgentId 并保存。");
                WechatMessageSender.NormalizeUsers(string.IsNullOrWhiteSpace(dto.ToUser) ? setting.ToUser : dto.ToUser);
            }
        }
        await using var tx = await db.Database.BeginTransactionAsync();
        var row = id == null ? new SysWechatAlertRule { FactoryId = factoryId } :
            await db.WechatAlertRules.FirstOrDefaultAsync(x => x.Id == id && x.FactoryId == factoryId)
            ?? throw ThrowHelper.BizUser("规则不存在或无权限");
        if (id == null) db.WechatAlertRules.Add(row);
        row.Name = dto.Name.Trim(); row.EventType = dto.EventType; row.ConditionType = dto.ConditionType;
        row.Threshold = dto.Threshold; row.TargetChannel = dto.TargetChannel;
        row.ToUser = NullIfEmpty(WechatMessageSender.NormalizeGroupUsers(dto.ToUser));
        row.WebhookKey = WechatMessageSender.ResolveWebhookKey(dto.WebhookKey, existing?.WebhookKey);
        row.Template = dto.Template; row.Enabled = dto.Enabled; row.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync();
        if (!row.Enabled) await db.WechatAlertDeliveries.Where(x => x.FactoryId == factoryId && x.RuleId == row.Id &&
            (x.Status == "pending" || x.Status == "retry" || x.Status == "failed" || x.Status == "partial"))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "cancelled").SetProperty(x => x.CompletedAt, DateTime.Now));
        await tx.CommitAsync();
    }

    /// <summary>服务事务内使用；既加锁又重载，避免预先读取的旧状态生成重复状态事件。</summary>
    public static async Task LockOrderAsync(AppDbContext context, ProdWorkOrder order)
    {
        if (context.Database.CurrentTransaction == null) throw ThrowHelper.General(nameof(LockOrderAsync), "工单锁须在业务事务内取得");
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM prod_work_order WITH (UPDLOCK, ROWLOCK) WHERE id={order.Id}");
        await context.Entry(order).ReloadAsync();
        if (context.Entry(order).State == EntityState.Detached) throw ThrowHelper.BizUser("关联的工单已被删除，无法锁定；请确认工单是否存在，或重新选择有效工单。");
    }

    public async Task EnqueueAsync(ProdWorkOrder order, byte before, IReadOnlyCollection<ProdReport>? reports = null)
    {
        if (db.Database.CurrentTransaction == null) throw ThrowHelper.General(nameof(EnqueueAsync), "待发消息须与业务写入同事务");
        var setting = await db.WechatAlertSettings.AsNoTracking().FirstOrDefaultAsync(x => x.FactoryId == order.FactoryId);
        if (setting == null || !setting.Enabled) return;
        var rules = await db.WechatAlertRules.AsNoTracking().Where(x => x.FactoryId == order.FactoryId && x.Enabled && x.TargetChannel == "group").ToListAsync();
        if (rules.Count == 0 || !await license.CanUseAsync(order.FactoryId, LicenseFeature.WechatDueAlert)) return;
        var product = await db.Products.AsNoTracking().FirstAsync(x => x.Id == order.ProductId && x.FactoryId == order.FactoryId);
        var eventTime = DateTime.Now;
        var common = new Dictionary<string, string> {
            ["工单号"] = order.OrderNo, ["产品编号"] = product.Code, ["产品名称"] = product.Name,
            ["事件时间"] = eventTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
        };
        var stateEvent = StatusEvent(before, order.Status);
        if (stateEvent != null) await AddAsync(stateEvent, Guid.NewGuid().ToString("N"), null, common);
        if (reports != null)
        {
            var opIds = reports.Select(x => x.OperationId).Distinct().ToArray();
            var operations = await db.Operations.AsNoTracking().Where(x => opIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name);
            foreach (var report in reports)
            {
                var values = new Dictionary<string, string>(common) {
                    ["工序"] = operations.GetValueOrDefault(report.OperationId, "—"),
                    ["良品数"] = report.GoodQty.ToString(CultureInfo.InvariantCulture),
                    ["不良数"] = report.DefectQty.ToString(CultureInfo.InvariantCulture),
                    ["不良率"] = ((long)report.GoodQty + report.DefectQty == 0 ? 0m :
                        report.DefectQty * 100m / ((long)report.GoodQty + report.DefectQty)).ToString("F2", CultureInfo.InvariantCulture) + "%"
                };
                await AddAsync("report_created", report.Id.ToString(CultureInfo.InvariantCulture), report, values);
            }
        }
        await db.SaveChangesAsync();

        async Task AddAsync(string eventType, string eventId, ProdReport? report, Dictionary<string, string> values)
        {
            foreach (var rule in rules.Where(x => x.EventType == eventType))
            {
                if (report != null && !Matches(new WechatRuleDto { ConditionType = rule.ConditionType, Threshold = rule.Threshold }, report.GoodQty, report.DefectQty)) continue;
                if (await db.WechatAlertDeliveries.AnyAsync(x => x.FactoryId == order.FactoryId && x.RuleId == rule.Id && x.EventType == eventType && x.EventId == eventId)) continue;
                if (!TryRecipients(rule, setting, out var users)) continue;
                var content = Regex.Replace(rule.Template, @"\{([^{}]+)\}", m => values[m.Groups[1].Value]);
                db.WechatAlertDeliveries.Add(new SysWechatAlertDelivery {
                    FactoryId = order.FactoryId, RuleId = rule.Id, RuleName = rule.Name, EventType = eventType, EventId = eventId,
                    OccurredAt = eventTime, OrderId = order.Id, ReportId = report?.Id, ToUser = users,
                    Content = WechatMessageSender.FitText(content), Status = "pending", NextAttemptAt = eventTime
                });
            }
        }
    }

    /// <summary>派工成功待发（docs/214）；须与派工业务同事务；取消派工不调用。</summary>
    public async Task EnqueueAssignAsync(ProdWorkOrder order, ProdWorkOrderOperation task, string operationName, string targetLabel)
    {
        if (db.Database.CurrentTransaction == null) throw ThrowHelper.General(nameof(EnqueueAssignAsync), "待发消息须与业务写入同事务");
        var setting = await db.WechatAlertSettings.AsNoTracking().FirstOrDefaultAsync(x => x.FactoryId == order.FactoryId);
        if (setting == null || !setting.Enabled) return;
        var rules = await db.WechatAlertRules.AsNoTracking()
            .Where(x => x.FactoryId == order.FactoryId && x.Enabled && x.TargetChannel == "group" && x.EventType == "operation_assigned")
            .ToListAsync();
        if (rules.Count == 0 || !await license.CanUseAsync(order.FactoryId, LicenseFeature.WechatDueAlert)) return;
        var product = await db.Products.AsNoTracking().FirstAsync(x => x.Id == order.ProductId && x.FactoryId == order.FactoryId);
        var eventTime = DateTime.Now;
        var values = new Dictionary<string, string>
        {
            ["工单号"] = order.OrderNo,
            ["产品编号"] = product.Code,
            ["产品名称"] = product.Name,
            ["事件时间"] = eventTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ["工序"] = operationName,
            ["派工对象"] = targetLabel
        };
        // 每次派工/改派单独事件，避免同工序改派被幂等吞掉
        var eventId = $"{task.Id}:{Guid.NewGuid():N}";
        // docs/226：只 @ 被派工人的企微账号，不用规则上的固定名单；没有可 @ 的人则只发群
        var mention = await ResolveAssignMentionsAsync(order.FactoryId, task.Id);
        foreach (var rule in rules)
        {
            if (await db.WechatAlertDeliveries.AnyAsync(x => x.FactoryId == order.FactoryId && x.RuleId == rule.Id && x.EventType == "operation_assigned" && x.EventId == eventId))
                continue;
            if (!TryRecipients(rule, setting, out var users)) continue;
            users = string.IsNullOrEmpty(mention) ? "(group)" : mention;
            var content = Regex.Replace(rule.Template, @"\{([^{}]+)\}", m => values[m.Groups[1].Value]);
            db.WechatAlertDeliveries.Add(new SysWechatAlertDelivery
            {
                FactoryId = order.FactoryId, RuleId = rule.Id, RuleName = rule.Name, EventType = "operation_assigned", EventId = eventId,
                OccurredAt = eventTime, OrderId = order.Id, ToUser = users,
                Content = WechatMessageSender.FitText(content), Status = "pending", NextAttemptAt = eventTime
            });
        }
        await db.SaveChangesAsync();
    }

    public async Task EnqueueAbnormalAsync(ProdAbnormal abnormal, SysUser reporter)
    {
        if (db.Database.CurrentTransaction == null) throw ThrowHelper.General(nameof(EnqueueAbnormalAsync), "待发消息须与业务写入同事务");
        var setting = await db.WechatAlertSettings.AsNoTracking().FirstOrDefaultAsync(x => x.FactoryId == abnormal.FactoryId);
        if (setting == null || !setting.Enabled) return;
        var rules = await db.WechatAlertRules.AsNoTracking()
            .Where(x => x.FactoryId == abnormal.FactoryId && x.Enabled && x.TargetChannel == "group" && x.EventType == "abnormal_reported").ToListAsync();
        if (rules.Count == 0 || !await license.CanUseAsync(abnormal.FactoryId, LicenseFeature.WechatDueAlert)) return;
        var eventTime = DateTime.Now;
        var values = new Dictionary<string, string> {
            ["工单号"] = "—", ["产品编号"] = "—", ["产品名称"] = "—",
            ["事件时间"] = eventTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ["异常类型"] = AbnormalService.TypeLabel(abnormal.AbnormalType),
            ["问题描述"] = abnormal.Description,
            ["上报人"] = reporter.Name
        };
        long orderId = 0;
        if (abnormal.WorkOrderId is > 0)
        {
            var order = await db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == abnormal.WorkOrderId && x.FactoryId == abnormal.FactoryId);
            if (order != null)
            {
                orderId = order.Id;
                values["工单号"] = order.OrderNo;
                var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == order.ProductId && x.FactoryId == abnormal.FactoryId);
                if (product != null) { values["产品编号"] = product.Code; values["产品名称"] = product.Name; }
            }
        }
        var eventId = abnormal.Id.ToString(CultureInfo.InvariantCulture);
        // docs/228：指定了处理人则覆盖规则固定名单；没有有效企微号则只发群不 @
        string? mentionOverride = null;
        if (abnormal.AssignHandlerUserId is > 0)
        {
            var mention = await ResolveUserMentionAsync(abnormal.FactoryId, abnormal.AssignHandlerUserId.Value);
            mentionOverride = string.IsNullOrEmpty(mention) ? "(group)" : mention;
        }
        foreach (var rule in rules)
        {
            if (!MatchesAbnormal(rule.ConditionType, abnormal.AbnormalType)) continue;
            if (await db.WechatAlertDeliveries.AnyAsync(x => x.FactoryId == abnormal.FactoryId && x.RuleId == rule.Id && x.EventType == "abnormal_reported" && x.EventId == eventId)) continue;
            if (!TryRecipients(rule, setting, out var users)) continue;
            if (mentionOverride != null) users = mentionOverride;
            var content = Regex.Replace(rule.Template, @"\{([^{}]+)\}", m => values[m.Groups[1].Value]);
            db.WechatAlertDeliveries.Add(new SysWechatAlertDelivery {
                FactoryId = abnormal.FactoryId, RuleId = rule.Id, RuleName = rule.Name, EventType = "abnormal_reported", EventId = eventId,
                OccurredAt = eventTime, OrderId = orderId, ToUser = users,
                Content = WechatMessageSender.FitText(content), Status = "pending", NextAttemptAt = eventTime
            });
        }
        await db.SaveChangesAsync();
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>被派工人的企微账号，去重。无人、未填或格式无效则返回空（只发群不 @）。</summary>
    private async Task<string> ResolveAssignMentionsAsync(long factoryId, long taskId)
    {
        var userIds = await db.WorkOrderOperationAssignees.AsNoTracking()
            .Where(a => a.WorkOrderOperationId == taskId)
            .Select(a => a.UserId)
            .ToListAsync();
        if (userIds.Count == 0) return "";
        var raw = await db.Users.AsNoTracking()
            .Where(u => u.FactoryId == factoryId && userIds.Contains(u.Id) && u.WecomUserId != null && u.WecomUserId != "")
            .Select(u => u.WecomUserId!)
            .ToListAsync();
        var ids = raw.Select(x => x.Trim()).Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ids.Count == 0) return "";
        try
        {
            return WechatMessageSender.NormalizeGroupUsers(string.Join('|', ids));
        }
        catch (BusinessException ex)
        {
            logger.LogWarning(ex, "派工@人跳过无效企微UserId Factory={Factory} Task={Task}", factoryId, taskId);
            return "";
        }
    }

    /// <summary>单个用户的企微账号。未启用、未填或格式无效则返回空（只发群不 @）。</summary>
    private async Task<string> ResolveUserMentionAsync(long factoryId, long userId)
    {
        var raw = await db.Users.AsNoTracking()
            .Where(u => u.FactoryId == factoryId && u.Id == userId && u.Status == 1 && u.WecomUserId != null && u.WecomUserId != "")
            .Select(u => u.WecomUserId)
            .FirstOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(raw)) return "";
        try
        {
            return WechatMessageSender.NormalizeGroupUsers(raw.Trim());
        }
        catch (BusinessException ex)
        {
            logger.LogWarning(ex, "异常@人跳过无效企微UserId Factory={Factory} User={User}", factoryId, userId);
            return "";
        }
    }

    public static bool TryRecipients(SysWechatAlertRule rule, SysWechatAlertSetting setting, out string users)
    {
        users = "";
        if (rule.TargetChannel == "group")
        {
            var key = WechatMessageSender.NormalizeWebhookKey(rule.WebhookKey)
                ?? WechatMessageSender.NormalizeWebhookKey(setting.WebhookKey);
            if (key == null) return false;
            users = string.IsNullOrWhiteSpace(rule.ToUser) ? "(group)" : WechatMessageSender.NormalizeGroupUsers(rule.ToUser);
            return true;
        }
        return false;
    }

    public async Task<PageResult<SysWechatAlertDelivery>> DeliveriesAsync(long factoryId, int page, int pageSize)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var q = db.WechatAlertDeliveries.AsNoTracking().Where(x => x.FactoryId == factoryId);
        return new() { Total = await q.CountAsync(), List = await q.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync() };
    }

    public Task<List<SysWechatSendAttempt>> AttemptsAsync(long factoryId, long id) => db.WechatSendAttempts.AsNoTracking()
        .Where(x => x.FactoryId == factoryId && x.DeliveryId == id).OrderByDescending(x => x.Id).ToListAsync();

    public async Task HandleAsync(long factoryId, long id, long userId, WechatDeliveryActionDto dto, bool verify)
    {
        if (verify && (string.IsNullOrWhiteSpace(dto.Note) || dto.Note.Trim().Length > 256))
            throw ThrowHelper.BizUser("核实说明长度须在1到256个字符之间，请修改后重新提交。");
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM sys_wechat_alert_delivery WITH (UPDLOCK, ROWLOCK) WHERE id={id} AND factory_id={factoryId}");
        var row = await db.WechatAlertDeliveries.FirstOrDefaultAsync(x => x.Id == id && x.FactoryId == factoryId)
            ?? throw ThrowHelper.BizUser("该发送记录不存在或当前用户无权限操作，请刷新列表确认记录状态及自身权限。");
        await db.Entry(row).ReloadAsync();
        if (verify ? row.Status != "unknown" : row.Status is not ("failed" or "partial"))
            throw ThrowHelper.BizUser("该记录状态已被其他操作变更，请刷新页面获取最新状态；未知结果的记录须先完成核实再操作。");
        if (verify && dto.Sent) { row.Status = "confirmed"; row.CompletedAt = DateTime.Now; }
        else
        {
            if (!await license.CanUseAsync(factoryId, LicenseFeature.WechatDueAlert) ||
                !await db.WechatAlertSettings.AnyAsync(x => x.FactoryId == factoryId && x.Enabled))
                throw ThrowHelper.BizUser("微信预警功能尚未开启或未正式授权，请先在预警设置中开启并完成授权后再重试。");
            if (!await db.WechatAlertRules.AnyAsync(x => x.Id == row.RuleId && x.FactoryId == factoryId && x.TargetChannel == "group"))
                throw ThrowHelper.BizUser("旧的会话推送方式已停止服务，无法重试，请重新设置群机器人规则以使用新的推送方式。");
            if (!await db.WechatAlertRules.AnyAsync(x => x.Id == row.RuleId && x.FactoryId == factoryId && x.Enabled))
                throw ThrowHelper.BizUser("该规则当前处于停用状态，请先启用该规则后再重试操作。");
            row.Status = "pending"; row.AutoAttempts = 0; row.NextAttemptAt = DateTime.Now; row.CompletedAt = null;
        }
        row.SendingStarted = false; row.LeaseId = null; row.LeaseUntil = null;
        row.HandledBy = userId; row.HandledAt = DateTime.Now; row.HandlingNote = verify ? dto.Note!.Trim() : "手动重试明确失败接收人";
        db.WechatSendAttempts.Add(new SysWechatSendAttempt {
            FactoryId = factoryId, DeliveryId = id, RequestKey = $"handle:{id}:{Guid.NewGuid():N}",
            AlertKey = $"event:{row.RuleId}:{row.EventType}:{row.EventId}", SendDate = DateTime.Today,
            ToUser = row.FailedUsers ?? row.ToUser, Outcome = verify && dto.Sent ? "confirmed" : "requeued",
            CreatedAt = DateTime.Now, HandledBy = userId, HandlingNote = row.HandlingNote
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task<ApiResult<object?>> DispatchAsync()
    {
        var now = DateTime.Now;
        var recoveredUnknown = await db.WechatAlertDeliveries.Where(x => x.Status == "processing" && x.LeaseUntil < now && x.SendingStarted)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "unknown").SetProperty(x => x.Error, "发送阶段中断或结果未落库，请核实")
                .SetProperty(x => x.LeaseId, (Guid?)null).SetProperty(x => x.LeaseUntil, (DateTime?)null));
        await db.WechatAlertDeliveries.Where(x => x.Status == "processing" && x.LeaseUntil < now && !x.SendingStarted)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "retry").SetProperty(x => x.LeaseId, (Guid?)null).SetProperty(x => x.LeaseUntil, (DateTime?)null));
        var cancelled = await db.WechatAlertDeliveries.Where(x => (x.Status == "pending" || x.Status == "retry") &&
            !db.WechatAlertRules.Any(r => r.Id == x.RuleId && r.FactoryId == x.FactoryId && r.Enabled && r.TargetChannel == "group"))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "cancelled").SetProperty(x => x.Error, "规则已停用或旧会话通道已停止，请重新配置群规则")
                .SetProperty(x => x.CompletedAt, DateTime.Now));
        var ids = await db.WechatAlertDeliveries.AsNoTracking().Where(x => (x.Status == "pending" || x.Status == "retry") && x.NextAttemptAt <= now &&
            db.WechatAlertSettings.Any(c => c.FactoryId == x.FactoryId && c.Enabled))
            .OrderBy(x => x.NextAttemptAt).ThenBy(x => x.Id).Select(x => x.Id).Take(50).ToListAsync();
        var disabled = await db.WechatAlertDeliveries.CountAsync(x => (x.Status == "pending" || x.Status == "retry") && x.NextAttemptAt <= now &&
            !db.WechatAlertSettings.Any(c => c.FactoryId == x.FactoryId && c.Enabled));
        var counts = new Dictionary<string, int> { ["unknown"] = recoveredUnknown, ["cancelled"] = cancelled };
        foreach (var id in ids)
        {
            var lease = Guid.NewGuid(); var start = DateTime.Now;
            var claimed = await db.WechatAlertDeliveries.Where(x => x.Id == id && (x.Status == "pending" || x.Status == "retry") && x.NextAttemptAt <= start)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "processing").SetProperty(x => x.LeaseId, (Guid?)lease)
                    .SetProperty(x => x.LeaseUntil, start.AddMinutes(2)).SetProperty(x => x.SendingStarted, false));
            if (claimed == 0) continue;
            var row = await db.WechatAlertDeliveries.AsNoTracking().FirstAsync(x => x.Id == id);
            WechatSendResult result;
            try
            {
                var key = $"event:{row.RuleId}:{row.EventType}:{row.EventId}";
                var rule = await db.WechatAlertRules.AsNoTracking().FirstOrDefaultAsync(x => x.Id == row.RuleId && x.FactoryId == row.FactoryId);
                result = await sender.SendAsync(row.FactoryId, key, row.Content,
                    string.IsNullOrWhiteSpace(row.FailedUsers) ? (row.ToUser == "(group)" ? "" : row.ToUser) : row.FailedUsers,
                    row.Id, lease, channel: rule?.TargetChannel);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "企微事件投递失败 Factory={Factory} Rule={Rule} Event={Event} Delivery={Delivery}", row.FactoryId, row.RuleId, row.EventId, id);
                // 不能假设企微未收到。读取持久发送阶段决定是否还可重试。
                var stage = await db.WechatAlertDeliveries.AsNoTracking().FirstAsync(x => x.Id == id);
                result = new(stage.SendingStarted ? "unknown" : "failed", LogRedactor.RedactText(
                    $"投递失败 factory={row.FactoryId} rule={row.RuleId} event={row.EventId}：{ex.Message}", 3900));
            }
            counts[result.Outcome] = counts.GetValueOrDefault(result.Outcome) + 1;
            // Sender 完全成功已将成功状态+成功日志一同提交，旧租约不再持有，禁止再写。
            if (result.Outcome == "success") continue;
            var current = await db.WechatAlertDeliveries.AsNoTracking().FirstAsync(x => x.Id == id);
            var status = result.Outcome switch { "busy" or "paused" or "limited" or "rate_limited" => "pending",
                "retry" => current.AutoAttempts < 3 ? "retry" : "failed", _ => result.Outcome };
            var next = result.Outcome == "limited" ? DateTime.Today.AddDays(1) : DateTime.Now.AddMinutes(result.Outcome == "rate_limited" || current.AutoAttempts <= 1 ? 1 : 5);
            var terminal = status is "failed" or "partial" or "unknown" or "cancelled";
            await db.WechatAlertDeliveries.Where(x => x.Id == id && x.LeaseId == lease && x.Status == "processing" && x.LeaseUntil > DateTime.Now)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status).SetProperty(x => x.NextAttemptAt, next)
                    .SetProperty(x => x.Error, result.Error).SetProperty(x => x.FailedUsers, result.FailedUsers ?? current.FailedUsers)
                    .SetProperty(x => x.CompletedAt, terminal ? DateTime.Now : (DateTime?)null)
                    .SetProperty(x => x.LeaseId, (Guid?)null).SetProperty(x => x.LeaseUntil, (DateTime?)null));
        }
        var failed = counts.GetValueOrDefault("failed") + counts.GetValueOrDefault("partial") + counts.GetValueOrDefault("unknown");
        return new() { Code = failed == 0 ? 0 : 1, Msg = failed == 0 ? "事件投递完成" : "存在失败或待核实消息，请查看发送记录",
            Data = new { counts, processed = counts.Values.Sum(), sent = counts.GetValueOrDefault("success"), failed,
                retry = counts.GetValueOrDefault("retry"), skipped = disabled + counts.GetValueOrDefault("busy") + counts.GetValueOrDefault("paused") + counts.GetValueOrDefault("limited") + counts.GetValueOrDefault("rate_limited") } };
    }
}
