using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public record WechatSendResult(string Outcome, string? Error = null, string? FailedUsers = null);

/// <summary>所有企微入口共用 SQL Server 会话锁和持久额度；网络调用不持有业务事务。</summary>
public class WechatMessageSender(AppDbContext db, IHttpClientFactory http, ILicenseTierService license,
    ILogger<WechatMessageSender> logger, SqlCommandErrorInterceptor? sqlErrors = null)
{
    public const string GroupHttpClientName = "wecom-webhook";

    public static string FitText(string text)
    {
        if (text.Length <= 1000 && Encoding.UTF8.GetByteCount(text) <= 2000) return text;
        var result = new StringBuilder(); var bytes = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > 1997 || result.Length + rune.Utf16SequenceLength > 999) break;
            result.Append(rune.ToString()); bytes += rune.Utf8SequenceLength;
        }
        return result.Append('…').ToString();
    }

    public static string NormalizeUsers(string? value)
    {
        var users = (value ?? "").Split(['|', ',', '，', ';', '；', ' ', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (users.Length == 0 || users.Any(x => x.StartsWith('@')))
            throw ThrowHelper.BizUser("请填写企微成员 UserId，禁止 @all");
        var result = string.Join('|', users);
        if (result.Length > 512 || users.Length > 1000)
            throw ThrowHelper.BizUser("企微接收人过长（最多 512 字符）");
        return result;
    }

    public static string NormalizeGroupUsers(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var users = value.Split(['|', ',', '，', ';', '；', ' ', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (users.Any(x => x.StartsWith('@') && x != "@all"))
            throw ThrowHelper.BizUser("群内成员请填写企微 UserId，或 @all 代表全体");
        if (users.Contains("@all")) return "@all";
        return NormalizeUsers(value);
    }

    public static string? ResolveWebhookKey(string? value, string? existing) =>
        value?.Trim() == "********" ? NormalizeWebhookKey(existing) : NormalizeWebhookKey(value);

    public static string RobotKeyHash(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    public static string? NormalizeWebhookKey(string? value)
    {
        var raw = (value ?? "").Trim();
        if (raw.Length == 0 || raw == "********") return null;
        if (raw.Contains("://", StringComparison.Ordinal) || raw.Contains("key=", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                !uri.Host.Equals("qyapi.weixin.qq.com", StringComparison.OrdinalIgnoreCase) ||
                uri.AbsolutePath != "/cgi-bin/webhook/send" || !uri.IsDefaultPort ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
                throw ThrowHelper.BizUser("请粘贴企业微信官方 HTTPS 群机器人 Webhook 链接");
            raw = "";
            var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in query)
            {
                var idx = part.IndexOf('=');
                if (idx <= 0) continue;
                if (!Uri.UnescapeDataString(part[..idx]).Equals("key", StringComparison.OrdinalIgnoreCase)) continue;
                raw = Uri.UnescapeDataString(part[(idx + 1)..]).Trim();
                break;
            }
        }
        if (raw.Length is 0 or > 200 || raw.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw ThrowHelper.BizUser("群机器人 Webhook 无效：请粘贴完整链接或其中的 key");
        return raw;
    }

    public static WechatSendResult ParseWebhookResponse(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        if (!root.TryGetProperty("errcode", out var ec) || !ec.TryGetInt32(out var code))
            return new("unknown", "群机器人响应缺少有效 errcode");
        if (code == 0) return new("success");
        return new("failed", $"Webhook不可用，请检查机器人配置（errcode={code}）：" +
            (root.TryGetProperty("errmsg", out var msg) ? msg.GetString() : "无错误说明"));
    }

    public static WechatSendResult ParseResponse(string raw, string recipients)
    {
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        if (!root.TryGetProperty("errcode", out var ec) || !ec.TryGetInt32(out var code))
            return new("unknown", "企微响应缺少有效 errcode");
        if (code != 0) return new("failed", $"企微拒绝消息：errcode={code}，" +
            (root.TryGetProperty("errmsg", out var msg) ? msg.GetString() : "无错误说明"));
        var invalid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in new[] { "invaliduser", "unlicenseduser" })
            if (root.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String)
                foreach (var user in (field.GetString() ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries))
                    invalid.Add(user);
        var users = recipients.Split('|');
        if (invalid.Any(x => !users.Contains(x, StringComparer.OrdinalIgnoreCase)))
            return new("unknown", "企微返回了本次请求外的无效接收人，请核实");
        var failed = users.Where(invalid.Contains).ToArray();
        return failed.Length == 0 ? new("success") : new(
            failed.Length == users.Length ? "failed" : "partial",
            "部分接收人无效或不在应用可见范围内", string.Join('|', failed));
    }

    public async Task<WechatSendResult> SendAsync(long factoryId, string alertKey, string content,
        string? recipients = null, long? deliveryId = null, Guid? leaseId = null, bool test = false,
        string? channel = null)
    {
        if (!string.IsNullOrWhiteSpace(channel) && channel != "group")
            return new("cancelled", "旧会话通道已停止，请重新配置群规则");
        await using var conn = new SqlConnection(db.Database.GetConnectionString());
        await conn.OpenAsync();
        var resource = $"mes:wecom:{factoryId}";
        const string lockSql = "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=0; SELECT @r;";
        await using var command = new SqlCommand(lockSql, conn);
        command.Parameters.AddWithValue("@resource", resource);
        int acquired;
        try { acquired = Convert.ToInt32(await command.ExecuteScalarAsync()); }
        catch (Exception ex) { throw ThrowHelper.Sql(lockSql, ex); }
        if (acquired < 0) return new("busy", "本厂正在发送，下一轮再处理");
        string? robotResource = null;
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(conn);
            if (sqlErrors != null) options.AddInterceptors(sqlErrors);
            await using var sendingDb = new AppDbContext(options.Options);
            if (!await license.CanUseAsync(factoryId, LicenseFeature.WechatDueAlert)) return new("paused", "授权暂不可用");
            var setting = await sendingDb.WechatAlertSettings.AsNoTracking().FirstOrDefaultAsync(x => x.FactoryId == factoryId);
            if (setting == null || !setting.Enabled) return new("paused", "推送通道已关闭");
            SysWechatAlertDelivery? delivery = null;
            if (deliveryId.HasValue)
            {
                delivery = await sendingDb.WechatAlertDeliveries.FirstOrDefaultAsync(x => x.Id == deliveryId && x.FactoryId == factoryId);
                if (delivery == null || delivery.LeaseId != leaseId || delivery.Status != "processing" || delivery.LeaseUntil <= DateTime.Now)
                    return new("busy", "领取已失效");
                if (delivery.AutoAttempts >= 3) return new("failed", "本轮自动尝试已达 3 次，请管理员检查后重试");
                if (!await sendingDb.WechatAlertRules.AnyAsync(x => x.Id == delivery.RuleId && x.FactoryId == factoryId && x.Enabled))
                    return new("cancelled", "规则已停用");
            }
            var today = DateTime.Today;
            var requestKey = test ? $"test:{Guid.NewGuid():N}" : delivery != null
                ? $"delivery:{delivery.Id}:{delivery.Attempts + 1}" : $"daily:{today:yyyy-MM-dd}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(alertKey)))}";
            if (delivery == null && !test)
            {
                // 成功日志兼容旧版本；unknown/partial 账本也阻止旧入口盲目重发。
                if (await sendingDb.WechatAlertLogs.AnyAsync(x => x.FactoryId == factoryId && x.AlertKey == alertKey && x.SendDate == today))
                    return new("dedup");
                var uncertain = await sendingDb.WechatSendAttempts.AsNoTracking().Where(x => x.FactoryId == factoryId &&
                    x.AlertKey == alertKey && x.SendDate == today && (x.Outcome == "unknown" || x.Outcome == "partial"))
                    .OrderByDescending(x => x.Id).FirstOrDefaultAsync();
                if (uncertain != null) return new(uncertain.Outcome, uncertain.Error ?? "上次发送结果待核实，禁止直接重发", uncertain.FailedUsers);
                var prior = await sendingDb.WechatSendAttempts.AsNoTracking().FirstOrDefaultAsync(x => x.FactoryId == factoryId && x.RequestKey == requestKey);
                if (prior != null && prior.Outcome is "success" or "unknown" or "partial")
                    return new(prior.Outcome == "success" ? "dedup" : prior.Outcome, prior.Error, prior.FailedUsers);
                // 明确失败允许旧手动入口再次尝试，每次另记额度账本。
                if (prior != null) requestKey += $":{Guid.NewGuid():N}";
            }
            if (!test && await sendingDb.WechatSendAttempts.CountAsync(x => x.FactoryId == factoryId && x.SendDate == today && x.Charged) >= setting.DailyLimit)
                return new("limited", "今日推送额度已用完");
            var webhook = setting.WebhookKey;
            var resolvedChannel = channel;
            if (delivery != null)
            {
                var rule = await sendingDb.WechatAlertRules.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == delivery.RuleId && x.FactoryId == factoryId);
                if (rule?.TargetChannel != "group")
                    return new("cancelled", "旧会话通道已停止，请重新配置群规则");
                resolvedChannel = rule.TargetChannel;
                if (!string.IsNullOrWhiteSpace(rule.WebhookKey)) webhook = rule.WebhookKey;
            }
            resolvedChannel = string.IsNullOrWhiteSpace(resolvedChannel) ? "group" : resolvedChannel;
            var group = resolvedChannel == "group";
            int agent = 0;
            string users;
            string? webhookKey = null;
            try
            {
                if (group)
                {
                    webhookKey = NormalizeWebhookKey(webhook);
                    if (webhookKey == null) return new("failed", "未配置群机器人 Webhook");
                    users = NormalizeGroupUsers(delivery == null ? recipients ?? setting.ToUser : recipients);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(setting.CorpId) || string.IsNullOrWhiteSpace(setting.Secret) ||
                        !int.TryParse(setting.AgentId, out agent) || agent <= 0)
                        return new("failed", "企微配置不完整，须填写有效 CorpID、Secret、AgentId");
                    users = NormalizeUsers(recipients ?? setting.ToUser);
                }
            }
            catch (BusinessException ex) { return new("failed", ex.Message); }
            // 同key跨工厂串行；哈希仅用于内部限流，不保存或回传机器人密钥。
            var robotHash = RobotKeyHash(webhookKey!);
            var candidateResource = $"mes:wecom:robot:{robotHash}";
            await using (var robotLock = new SqlCommand(lockSql, conn))
            {
                robotLock.Parameters.AddWithValue("@resource", candidateResource);
                int robotAcquired;
                try { robotAcquired = Convert.ToInt32(await robotLock.ExecuteScalarAsync()); }
                catch (Exception ex) { throw ThrowHelper.Sql(lockSql, ex); }
                if (robotAcquired < 0) return new("busy", "此机器人正在发送，下一轮再处理");
                robotResource = candidateResource;
            }
            var since = DateTime.Now.AddMinutes(-1);
            if (await sendingDb.WechatSendAttempts.CountAsync(x => x.RobotKeyHash == robotHash && x.CreatedAt > since) >= 20)
                return new("rate_limited", "此机器人每分钟最多20条，请稍后再试");
            var attempt = new SysWechatSendAttempt {
                FactoryId = factoryId, RequestKey = requestKey, AlertKey = alertKey, DeliveryId = deliveryId,
                SendDate = today, ToUser = string.IsNullOrEmpty(users) ? "(group)" : users, Outcome = "retry", CreatedAt = DateTime.Now
            };
            sendingDb.WechatSendAttempts.Add(attempt);
            await sendingDb.SaveChangesAsync();
            var client = http.CreateClient(group ? GroupHttpClientName : "wecom");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            string? token = null;
            if (!group)
            {
                var tokenUrl = $"https://qyapi.weixin.qq.com/cgi-bin/gettoken?corpid={Uri.EscapeDataString(setting.CorpId!)}&corpsecret={Uri.EscapeDataString(setting.Secret!)}";
                try
                {
                    var rawToken = await client.GetStringAsync(tokenUrl, timeout.Token);
                    using var tokenDoc = JsonDocument.Parse(rawToken);
                    if (!tokenDoc.RootElement.TryGetProperty("errcode", out var ec) || ec.GetInt32() != 0 ||
                        !tokenDoc.RootElement.TryGetProperty("access_token", out var tk) || string.IsNullOrWhiteSpace(tk.GetString()))
                    {
                        var error = tokenDoc.RootElement.TryGetProperty("errmsg", out var em) ? em.GetString() : "无有效 token";
                        return await FinishAsync(sendingDb, attempt, new("failed", ThrowHelper.Api(tokenUrl, "(GET)", error ?? "无有效 token").Message));
                    }
                    token = tk.GetString()!;
                }
                catch (Exception ex)
                {
                    logger.LogWarning("企微获取凭证失败 Factory={Factory} Delivery={Delivery} Type={Type}", factoryId, deliveryId, ex.GetType().Name);
                    return await FinishAsync(sendingDb, attempt, new("retry", ThrowHelper.Api(tokenUrl, "(GET)", "获取凭证失败，消息尚未发送：" + ex.GetType().Name).Message));
                }
            }
            await using (var stageTx = await sendingDb.Database.BeginTransactionAsync())
            {
                if (delivery != null)
                {
                    var changed = await sendingDb.WechatAlertDeliveries.Where(x => x.Id == delivery.Id &&
                        x.LeaseId == leaseId && x.Status == "processing" && x.LeaseUntil > DateTime.Now && !x.SendingStarted)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.SendingStarted, true));
                    if (changed == 0) { await stageTx.RollbackAsync(); return new("busy", "租约已失效，消息尚未发送"); }
                }
                // 外发前再检查总开关与规则；关闭后不再开始新的HTTP请求。
                if (!await sendingDb.WechatAlertSettings.AnyAsync(x => x.FactoryId == factoryId && x.Enabled) ||
                    !await license.CanUseAsync(factoryId, LicenseFeature.WechatDueAlert))
                {
                    await stageTx.RollbackAsync();
                    return new("paused", "推送已关闭或授权不可用，消息尚未发送");
                }
                if (delivery != null && !await sendingDb.WechatAlertRules.AnyAsync(x => x.Id == delivery.RuleId &&
                    x.FactoryId == factoryId && x.Enabled && x.TargetChannel == "group"))
                {
                    await stageTx.RollbackAsync();
                    return new("cancelled", "规则已停用或旧会话通道已停止，消息尚未发送");
                }
                if (delivery != null) { delivery.Attempts++; delivery.AutoAttempts++; }
                attempt.Outcome = "unknown";
                attempt.RobotKeyHash = robotHash;
                attempt.CreatedAt = DateTime.Now;
                attempt.Charged = !test;
                await sendingDb.SaveChangesAsync();
                await stageTx.CommitAsync();
            }
            var url = group
                ? $"https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key={webhookKey}"
                : $"https://qyapi.weixin.qq.com/cgi-bin/message/send?access_token={token}";
            var body = group
                ? JsonSerializer.Serialize(new { msgtype = "text", text = new { content = FitText(content), mentioned_list = string.IsNullOrEmpty(users) ? Array.Empty<string>() : users.Split('|') } })
                : JsonSerializer.Serialize(new { touser = users, msgtype = "text", agentid = agent, text = new { content = FitText(content) }, safe = 0 });
            WechatSendResult result;
            try
            {
                using var response = await client.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"), timeout.Token);
                var raw = await response.Content.ReadAsStringAsync(timeout.Token);
                result = !response.IsSuccessStatusCode ? new("unknown", "企微 HTTP 响应不能确认是否接受消息")
                    : group ? ParseWebhookResponse(raw) : ParseResponse(raw, users);
                if (result.Outcome != "success")
                {
                    var trace = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
                    var context = ThrowHelper.Api(url, body, result.Error + "；响应：" + raw).Message;
                    logger.LogWarning("企微发送失败 TraceId={TraceId} Factory={Factory} Delivery={Delivery} Context={Context}",
                        trace, factoryId, deliveryId, RedactWebhookContext(context, webhookKey));
                    result = result with { Error = $"{(result.Outcome == "unknown" ? "发送结果未知，请先核实" : "Webhook不可用，请检查机器人配置")}；追踪号：{trace}" };
                }
            }
            catch (Exception ex)
            {
                var trace = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
                var context = ThrowHelper.Api(url, body, "消息请求已开始，发送结果未知：" + ex.GetType().Name + "；" + ex.Message).Message;
                logger.LogWarning("企微网络异常 TraceId={TraceId} Factory={Factory} Delivery={Delivery} Context={Context}",
                    trace, factoryId, deliveryId, RedactWebhookContext(context, webhookKey));
                result = new("unknown", $"网络异常，发送结果未知，请先核实；追踪号：{trace}");
            }
            return await FinishAsync(sendingDb, attempt, result, delivery, content);
        }
        finally
        {
            const string unlockSql = "EXEC sys.sp_releaseapplock @Resource=@resource, @LockOwner='Session';";
            foreach (var heldResource in new[] { robotResource, resource }.Where(x => x != null))
            {
                await using var unlock = new SqlCommand(unlockSql, conn);
                unlock.Parameters.AddWithValue("@resource", heldResource!);
                try { await unlock.ExecuteNonQueryAsync(); }
                catch (Exception ex) { logger.LogError(ex, "释放企微锁失败 Factory={Factory}; SQL={Sql}", factoryId, unlockSql); SqlConnection.ClearPool(conn); }
            }
        }
    }

    public static string RedactWebhookContext(string context, string? key) =>
        LogRedactor.RedactText(string.IsNullOrEmpty(key) ? context : context.Replace(key, "[REDACTED]", StringComparison.Ordinal), 8000);

    private static async Task<WechatSendResult> FinishAsync(AppDbContext context, SysWechatSendAttempt attempt,
        WechatSendResult result, SysWechatAlertDelivery? delivery = null, string? content = null)
    {
        attempt.Outcome = result.Outcome;
        attempt.Error = result.Error == null ? null : LogRedactor.RedactText(result.Error, 3900);
        attempt.FailedUsers = result.FailedUsers;
        if (result.Outcome is "failed" or "retry") attempt.Charged = false;
        await using var tx = await context.Database.BeginTransactionAsync();
        // 消息响应与投递终态同事务保存，避免部分成功后进程退出丢失失败成员。
        if (delivery != null)
        {
            var expectedLease = delivery.LeaseId;
            var failedUsers = result.Outcome == "success" ? null : result.FailedUsers ?? delivery.FailedUsers;
            context.Entry(delivery).State = EntityState.Detached;
            var changed = await context.WechatAlertDeliveries.Where(x => x.Id == delivery.Id &&
                x.LeaseId == expectedLease && x.Status == "processing" && x.LeaseUntil > DateTime.Now)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, result.Outcome).SetProperty(x => x.CompletedAt, DateTime.Now)
                    .SetProperty(x => x.LeaseId, (Guid?)null).SetProperty(x => x.LeaseUntil, (DateTime?)null)
                    .SetProperty(x => x.Error, attempt.Error).SetProperty(x => x.FailedUsers, failedUsers));
            if (changed == 0)
            {
                await context.SaveChangesAsync(); await tx.CommitAsync();
                return new("unknown", "企微响应已记录但租约失效，请核实发送记录");
            }
        }
        if (result.Outcome == "success" && !attempt.RequestKey.StartsWith("test:"))
        {
            context.WechatAlertLogs.Add(new SysWechatAlertLog { FactoryId = attempt.FactoryId, AlertKey = attempt.AlertKey,
                SendDate = attempt.SendDate, Content = FitText(content ?? ""), CreatedAt = DateTime.Now });
        }
        await context.SaveChangesAsync();
        await tx.CommitAsync();
        return result with { Error = attempt.Error };
    }
}
