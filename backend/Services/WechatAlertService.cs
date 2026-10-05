using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public interface IWechatAlertService
{
    Task<ApiResult<WechatAlertSettingDto>> GetSettingAsync(long factoryId);
    Task<ApiResult<object?>> SaveSettingAsync(WechatAlertSettingDto dto, long factoryId);
    Task<ApiResult<object?>> MarkNoticeSeenAsync(long factoryId);
    Task<ApiResult<object?>> TestSendAsync(long factoryId);
    /// <summary>手动：推本厂临期+超期（一天一批一条，key=due-alert:yyyy-MM-dd）。</summary>
    Task<ApiResult<object?>> PushOverdueAsync(long factoryId);
    /// <summary>计划任务：扫本实例所有已开启厂（docs/75）。</summary>
    Task<ApiResult<object?>> PushDueAlertCronAsync();
    /// <summary>统一出口：关闭时静默跳过；开启时调企业微信（docs/1B）。</summary>
    Task SendAsync(long factoryId, string alertKey, string content, string level = "info");
}

public class WechatDuePushResultDto
{
    public long FactoryId { get; set; }
    /// <summary>sent / skipped_disabled / skipped_empty / skipped_dedup</summary>
    public string Status { get; set; } = "";
    public int OrderCount { get; set; }
    public int OverdueCount { get; set; }
    public int WarningCount { get; set; }
    public string Msg { get; set; } = "";
}

public class WechatAlertSettingDto
{
    public bool Enabled { get; set; }
    public string? CorpId { get; set; }
    public string? Secret { get; set; }
    public string? AgentId { get; set; }
    public string? ToUser { get; set; }
    public string? WebhookKey { get; set; }
    public int DailyLimit { get; set; } = 20;
    public bool NoticeSeen { get; set; }
}

public class WechatAlertService : IWechatAlertService
{
    private static readonly ConcurrentDictionary<string, (string Token, DateTime ExpireAt)> TokenCache = new();
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly OutboundHttpErrorLogger _httpErrorLogger;
    private readonly ILicenseTierService _license;
    private readonly WechatMessageSender _sender;

    public WechatAlertService(
        AppDbContext db,
        IHttpClientFactory httpFactory,
        OutboundHttpErrorLogger httpErrorLogger,
        ILicenseTierService license,
        WechatMessageSender sender)
    {
        _db = db;
        _httpFactory = httpFactory;
        _httpErrorLogger = httpErrorLogger;
        _license = license;
        _sender = sender;
    }

    public async Task<ApiResult<WechatAlertSettingDto>> GetSettingAsync(long factoryId)
    {
        var row = await _db.WechatAlertSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.FactoryId == factoryId);
        return ApiResult<WechatAlertSettingDto>.Ok(row == null ? DefaultDto() : ToDto(row));
    }

    public async Task<ApiResult<object?>> SaveSettingAsync(WechatAlertSettingDto dto, long factoryId)
    {
        if (dto == null)
            throw ThrowHelper.Biz(nameof(SaveSettingAsync), "预警设置为空");
        if (dto.DailyLimit < 1 || dto.DailyLimit > 200)
            throw ThrowHelper.Biz(nameof(SaveSettingAsync), "每日推送上限须在 1～200");

        var toUser = NullIfEmpty(WechatMessageSender.NormalizeGroupUsers(dto.ToUser));
        var row = await _db.WechatAlertSettings.FirstOrDefaultAsync(x => x.FactoryId == factoryId);
        if (row == null)
        {
            row = new SysWechatAlertSetting { FactoryId = factoryId };
            _db.WechatAlertSettings.Add(row);
        }
        row.WebhookKey = WechatMessageSender.ResolveWebhookKey(dto.WebhookKey, row.WebhookKey);

        if (dto.Enabled)
        {
            var hasGroup = !string.IsNullOrWhiteSpace(row.WebhookKey);
            var independentKeys = await _db.WechatAlertRules.AsNoTracking()
                .Where(x => x.FactoryId == factoryId && x.Enabled && x.TargetChannel == "group" && x.WebhookKey != null)
                .Select(x => x.WebhookKey).ToListAsync();
            var hasIndependentGroup = independentKeys.Any(x => WechatMessageSender.NormalizeWebhookKey(x) != null);
            if (!hasGroup && !hasIndependentGroup)
                throw ThrowHelper.Biz(nameof(SaveSettingAsync), "开启前请填写默认Webhook，或先启用有独立Webhook的群规则");
        }

        row.Enabled = dto.Enabled;
        // 旧应用字段只保留，不允许本页面保存时覆盖或清空。
        row.ToUser = toUser;
        row.DailyLimit = dto.DailyLimit;
        row.NoticeSeen = dto.NoticeSeen || row.NoticeSeen;
        row.UpdatedAt = DateTime.Now;

        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> MarkNoticeSeenAsync(long factoryId)
    {
        var row = await _db.WechatAlertSettings.FirstOrDefaultAsync(x => x.FactoryId == factoryId);
        if (row == null)
        {
            row = new SysWechatAlertSetting { FactoryId = factoryId, NoticeSeen = true, UpdatedAt = DateTime.Now };
            _db.WechatAlertSettings.Add(row);
        }
        else
        {
            row.NoticeSeen = true;
            row.UpdatedAt = DateTime.Now;
        }
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> TestSendAsync(long factoryId)
    {
        var row = await RequireConfiguredAsync(factoryId, mustEnabled: true);
        var result = await _sender.SendAsync(factoryId, "test", "【微聚】测试推送成功。收到这条说明微信预警配置正确。",
            recipients: row.ToUser, test: true, channel: "group");
        if (result.Outcome != "success")
            throw ThrowHelper.Biz(nameof(TestSendAsync), result.Error ?? "测试推送未成功");
        return new ApiResult<object?> { Code = 0, Msg = "测试推送已发送" };
    }

    public async Task<ApiResult<object?>> PushOverdueAsync(long factoryId)
    {
        var result = await PushDueAlertForFactoryAsync(factoryId);
        return new ApiResult<object?>
        {
            Code = result.Status is "skipped_failed" or "skipped_unknown" ? 1 : 0,
            Msg = result.Msg,
            Data = result
        };
    }

    public async Task<ApiResult<object?>> PushDueAlertCronAsync()
    {
        var factoryIds = await _db.WechatAlertSettings.AsNoTracking()
            .Where(x => x.Enabled)
            .Select(x => x.FactoryId)
            .ToListAsync();

        if (factoryIds.Count == 0)
            return new ApiResult<object?> { Code = 0, Msg = "无已开启推送的工厂，已跳过", Data = new { list = Array.Empty<WechatDuePushResultDto>() } };

        var list = new List<WechatDuePushResultDto>();
        foreach (var fid in factoryIds)
            list.Add(await PushDueAlertForFactoryAsync(fid));

        var sent = list.Count(x => x.Status == "sent");
        var skipped = list.Count(x => x.Status.StartsWith("skipped", StringComparison.Ordinal));
        var failed = list.Count(x => x.Status is "skipped_failed" or "skipped_unknown");
        return new ApiResult<object?>
        {
            Code = failed == 0 ? 0 : 1,
            Msg = $"定时交期预警完成：推送 {sent} 厂，跳过 {skipped} 厂，失败或待核实 {failed} 厂",
            Data = new { list, sent, skipped, failed }
        };
    }

    /// <summary>
    /// 一天一批一条：临期+超期合写；alert_key=due-alert:yyyy-MM-dd；当日批次不重推。
    /// </summary>
    private async Task<WechatDuePushResultDto> PushDueAlertForFactoryAsync(long factoryId)
    {
        if (!await _license.CanUseAsync(factoryId, LicenseFeature.WechatDueAlert))
        {
            return new WechatDuePushResultDto
            {
                FactoryId = factoryId,
                Status = "skipped_tier",
                Msg = "当前授权档不含交期预警，已跳过"
            };
        }

        var setting = await _db.WechatAlertSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.FactoryId == factoryId);
        if (setting == null || !setting.Enabled)
        {
            return new WechatDuePushResultDto
            {
                FactoryId = factoryId,
                Status = "skipped_disabled",
                Msg = "推送未开启，已跳过（看板仍可看临期/超期）"
            };
        }

        if (WechatMessageSender.NormalizeWebhookKey(setting.WebhookKey) == null)
            return new WechatDuePushResultDto { FactoryId = factoryId, Status = "skipped_unconfigured",
                Msg = "交期推送须填写全局默认群机器人Webhook" };

        var now = DateTime.Now;
        var today = DateTime.Today;
        var alertKey = $"due-alert:{today:yyyy-MM-dd}";

        var already = await _db.WechatAlertLogs.AsNoTracking()
            .AnyAsync(x => x.FactoryId == factoryId && x.AlertKey == alertKey && x.SendDate == today);
        if (already)
        {
            return new WechatDuePushResultDto
            {
                FactoryId = factoryId,
                Status = "skipped_dedup",
                Msg = "今日交期预警批次已推过，已跳过（当日批次不重推）"
            };
        }

        var take = Math.Clamp(setting.DailyLimit, 1, 200);
        // 临期上限：交期 ≤ now+DueWarnDays；再内存用 DueStateHelper 分黄/红（与看板同口径）
        var warnUntil = now.AddDays(DueStateHelper.DueWarnDays);
        var candidates = await (
            from o in _db.WorkOrders.AsNoTracking()
            where o.FactoryId == factoryId && o.Status < 2 && o.DueDate != null && o.DueDate <= warnUntil
            join p in _db.Products.AsNoTracking() on o.ProductId equals p.Id
            orderby o.DueDate
            select new
            {
                o.Id,
                o.OrderNo,
                o.ProductId,
                o.Qty,
                o.Status,
                o.DueDate,
                ProductName = p.Name
            }).Take(Math.Max(take * 5, 100)).ToListAsync();

        var dueRows = candidates
            .Select(o =>
            {
                var state = DueStateHelper.Calc(o.Status, o.DueDate, now);
                return new { o, state };
            })
            .Where(x => x.state == DueState.Overdue || x.state == DueState.Warning)
            .OrderBy(x => x.state == DueState.Overdue ? 0 : 1)
            .ThenBy(x => x.o.DueDate)
            .Take(take)
            .ToList();

        if (dueRows.Count == 0)
        {
            return new WechatDuePushResultDto
            {
                FactoryId = factoryId,
                Status = "skipped_empty",
                Msg = "当前没有临期/超期工单，未推送"
            };
        }

        var progress = await WorkOrderProgressQuery.LoadAsync(
            _db,
            factoryId,
            dueRows.Select(x => new WorkOrderProgressQuery.OrderKey
            {
                Id = x.o.Id,
                ProductId = x.o.ProductId,
                Qty = x.o.Qty
            }).ToList(),
            nameof(PushDueAlertForFactoryAsync));

        static string CurrentOpName(WorkOrderProgressQuery.OrderProgressBundle? bundle)
        {
            if (bundle == null || bundle.Ops.Count == 0) return "—";
            var cur = bundle.Ops.FirstOrDefault(op => op.Status != WorkOrderProgressCalculator.OpFull);
            return cur?.OperationName ?? bundle.Ops[^1].OperationName;
        }

        static string DayLabel(string state, DateTime due, DateTime nowLocal)
        {
            var days = (due.Date - nowLocal.Date).Days;
            if (state == DueState.Overdue)
                return $"已超期 {Math.Max(1, -days)} 天";
            if (days <= 0) return "今日到期";
            return $"剩余 {days} 天";
        }

        var lines = dueRows.Select(x =>
        {
            var tag = x.state == DueState.Overdue ? "超期" : "临期";
            progress.TryGetValue(x.o.Id, out var bundle);
            var opName = CurrentOpName(bundle);
            return $"· [{tag}] {x.o.OrderNo} {x.o.ProductName} · {opName} · 交期 {x.o.DueDate:yyyy-MM-dd} · {DayLabel(x.state, x.o.DueDate!.Value, now)}";
        });

        var overdueCount = dueRows.Count(x => x.state == DueState.Overdue);
        var warningCount = dueRows.Count(x => x.state == DueState.Warning);
        var content =
            $"【微聚】交期预警（超期 {overdueCount} / 临期 {warningCount}，共 {dueRows.Count} 张）\n"
            + string.Join("\n", lines);

        var sendResult = await _sender.SendAsync(factoryId, alertKey, content, recipients: setting.ToUser, channel: "group");
        if (sendResult.Outcome != "success")
            return new WechatDuePushResultDto { FactoryId = factoryId, Status = "skipped_" + sendResult.Outcome,
                Msg = sendResult.Error ?? "本次未发送（重复或通道暂不可用）" };
        return new WechatDuePushResultDto
        {
            FactoryId = factoryId,
            Status = "sent",
            OrderCount = dueRows.Count,
            OverdueCount = overdueCount,
            WarningCount = warningCount,
            Msg = $"已推送交期预警（超期 {overdueCount} / 临期 {warningCount}，共 {dueRows.Count} 张）"
        };
    }

    public async Task SendAsync(long factoryId, string alertKey, string content, string level = "info")
    {
        var row = await _db.WechatAlertSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.FactoryId == factoryId);
        if (row == null || !row.Enabled)
            return;
        var result = await _sender.SendAsync(factoryId, alertKey, WechatMessageSender.FitText(content),
            recipients: row.ToUser, channel: "group");
        if (result.Outcome is "paused" or "dedup") return;
        if (result.Outcome != "success")
            throw ThrowHelper.Biz(nameof(SendAsync), result.Error ?? "交期预警发送未成功");
    }

    private async Task<SysWechatAlertSetting> RequireConfiguredAsync(long factoryId, bool mustEnabled)
    {
        var row = await _db.WechatAlertSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(RequireConfiguredAsync), "请先保存企业微信配置");
        if (mustEnabled && !row.Enabled)
            throw ThrowHelper.Biz(nameof(RequireConfiguredAsync), "推送未开启：请先打开启用开关并保存");
        if (WechatMessageSender.NormalizeWebhookKey(row.WebhookKey) == null)
            throw ThrowHelper.Biz(nameof(RequireConfiguredAsync), "测试和交期推送须填写全局默认群机器人Webhook");
        return row;
    }

    private async Task SendWecomTextAsync(SysWechatAlertSetting row, string content)
    {
        if (!int.TryParse(row.AgentId, out var agentId))
            throw ThrowHelper.Biz(nameof(SendWecomTextAsync), "AgentId 须为数字");

        const int maxAttempts = 3;
        Exception? last = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await SendWecomTextOnceAsync(row, content, agentId);
                return;
            }
            catch (BusinessException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                last = ex;
                await Task.Delay(400 * attempt);
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        throw ThrowHelper.Api(
            "https://qyapi.weixin.qq.com/cgi-bin/message/send",
            "(retry exhausted)",
            $"企微推送失败，已重试 {maxAttempts} 次",
            last!);
    }

    private async Task SendWecomTextOnceAsync(SysWechatAlertSetting row, string content, int agentId)
    {
        var token = await GetAccessTokenAsync(row.CorpId!, row.Secret!);
        var url = $"https://qyapi.weixin.qq.com/cgi-bin/message/send?access_token={token}";
        var body = new
        {
            touser = row.ToUser,
            msgtype = "text",
            agentid = agentId,
            text = new { content },
            safe = 0
        };
        var json = JsonSerializer.Serialize(body);
        var client = _httpFactory.CreateClient("wecom");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        HttpResponseMessage resp;
        string respText;
        try
        {
            resp = await client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
            respText = await resp.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            sw.Stop();
            _httpErrorLogger.Log("wecom_send_failed", "POST", url, "application/json", json,
                false, null, null, null, sw.Elapsed.TotalMilliseconds, null, ex);
            throw ThrowHelper.Api(url, json, "请求异常", ex);
        }

        sw.Stop();
        if (!resp.IsSuccessStatusCode)
        {
            _httpErrorLogger.Log("wecom_non_2xx", "POST", url, "application/json", json,
                true, (int)resp.StatusCode, resp.Content.Headers.ContentType?.ToString(), respText,
                sw.Elapsed.TotalMilliseconds, null, null);
            throw ThrowHelper.Api(url, json, respText);
        }

        using var doc = JsonDocument.Parse(respText);
        var errcode = doc.RootElement.TryGetProperty("errcode", out var ec) ? ec.GetInt32() : -1;
        if (errcode != 0)
        {
            var errmsg = doc.RootElement.TryGetProperty("errmsg", out var em) ? em.GetString() : respText;
            _httpErrorLogger.Log("wecom_business_fail", "POST", url, "application/json", json,
                true, (int)resp.StatusCode, resp.Content.Headers.ContentType?.ToString(), respText,
                sw.Elapsed.TotalMilliseconds, errcode.ToString(), null);
            // 业务错误（如 userid 无效）不重试：用 BusinessException
            throw ThrowHelper.Biz(nameof(SendWecomTextOnceAsync), $"企业微信返回失败：errcode={errcode}, errmsg={errmsg}");
        }
    }

    private async Task<string> GetAccessTokenAsync(string corpId, string secret)
    {
        var cacheKey = corpId + "|" + secret;
        if (TokenCache.TryGetValue(cacheKey, out var cached) && cached.ExpireAt > DateTime.Now.AddMinutes(2))
            return cached.Token;

        var url = $"https://qyapi.weixin.qq.com/cgi-bin/gettoken?corpid={Uri.EscapeDataString(corpId)}&corpsecret={Uri.EscapeDataString(secret)}";
        var client = _httpFactory.CreateClient("wecom");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string respText;
        try
        {
            respText = await client.GetStringAsync(url);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _httpErrorLogger.Log("wecom_gettoken_failed", "GET", url, null, "(GET)",
                false, null, null, null, sw.Elapsed.TotalMilliseconds, null, ex);
            throw ThrowHelper.Api(url, "(GET)", "获取 access_token 请求异常", ex);
        }

        sw.Stop();
        using var doc = JsonDocument.Parse(respText);
        var errcode = doc.RootElement.TryGetProperty("errcode", out var ec) ? ec.GetInt32() : -1;
        if (errcode != 0)
        {
            var errmsg = doc.RootElement.TryGetProperty("errmsg", out var em) ? em.GetString() : respText;
            _httpErrorLogger.Log("wecom_gettoken_business_fail", "GET", url, null, "(GET)",
                true, 200, "application/json", respText, sw.Elapsed.TotalMilliseconds, errcode.ToString(), null);
            throw ThrowHelper.Api(url, "(GET)", $"errcode={errcode}, errmsg={errmsg}");
        }

        var token = doc.RootElement.GetProperty("access_token").GetString()
            ?? throw ThrowHelper.Api(url, "(GET)", "返回无 access_token");
        var expires = doc.RootElement.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 7200;
        TokenCache[cacheKey] = (token, DateTime.Now.AddSeconds(expires));
        return token;
    }

    private static WechatAlertSettingDto DefaultDto() => new();

    private static WechatAlertSettingDto ToDto(SysWechatAlertSetting row) => new()
    {
        Enabled = row.Enabled,
        CorpId = row.CorpId,
        // Secret 不回传明文，前端显示留空=不修改
        Secret = string.IsNullOrEmpty(row.Secret) ? null : "********",
        AgentId = row.AgentId,
        ToUser = row.ToUser,
        WebhookKey = string.IsNullOrEmpty(row.WebhookKey) ? null : "********",
        DailyLimit = row.DailyLimit,
        NoticeSeen = row.NoticeSeen
    };

    private static string? NullIfEmpty(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? NormalizeToUser(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var parts = s.Split(new[] { '|', ',', '，', ';', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? null : string.Join("|", parts);
    }
}
