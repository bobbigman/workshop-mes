using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public enum LicenseRuntimeStatus
{
    /// <summary>LicenseCloud:Enabled=false</summary>
    Disabled,
    Valid,
    /// <summary>断网宽限期内仍用合同档</summary>
    Grace,
    /// <summary>过期 / 宽限用尽 / 未激活 / 时钟异常 → 有效档 trial</summary>
    Expired
}

public sealed class LicenseStatusDto
{
    public string LicenseTier { get; set; } = "trial";
    public string EffectiveTier { get; set; } = "trial";
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? LastOkUtc { get; set; }
    public string Status { get; set; } = "disabled";
    public string Message { get; set; } = "";
}

public interface ILicenseCloudService
{
    /// <summary>登录后尝试云校验；失败不阻断登录。</summary>
    Task RefreshAsync(long factoryId, CancellationToken ct = default);

    Task<LicenseTier> GetEffectiveTierAsync(long factoryId, CancellationToken ct = default);

    Task<LicenseStatusDto> GetStatusAsync(long factoryId, CancellationToken ct = default);
}

public class LicenseCloudService : ILicenseCloudService
{
    public const string HttpClientName = "licenseCloud";
    private const int ClockRollbackToleranceMinutes = 5;

    private readonly AppDbContext _db;
    private readonly LicenseCloudOptions _opt;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<LicenseCloudService> _logger;

    public LicenseCloudService(
        AppDbContext db,
        IOptions<LicenseCloudOptions> opt,
        IHttpClientFactory httpFactory,
        ILogger<LicenseCloudService> logger)
    {
        _db = db;
        _opt = opt.Value;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task RefreshAsync(long factoryId, CancellationToken ct = default)
    {
        if (!_opt.Enabled)
            return;

        var factory = await _db.Factories.FirstOrDefaultAsync(f => f.Id == factoryId, ct)
            ?? throw ThrowHelper.BizUser("工厂不存在");

        var localUtc = DateTime.UtcNow;
        factory.LastLicenseSeenUtc = localUtc;

        var baseUrl = (_opt.BaseUrl ?? "").Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl))
        {
            _logger.LogWarning("LicenseCloud 已启用但 BaseUrl 为空 FactoryId={FactoryId}", factoryId);
            await _db.SaveChangesAsync(ct);
            return;
        }

        var url = baseUrl + "/api/license/check";
        var body = new { factoryCode = factory.FactoryCode, requestedAtUtc = localUtc };
        try
        {
            var client = _httpFactory.CreateClient(HttpClientName);
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Content = JsonContent.Create(body);
            if (!string.IsNullOrWhiteSpace(_opt.SharedSecret))
                req.Headers.TryAddWithoutValidation("X-License-Key", _opt.SharedSecret);

            using var resp = await client.SendAsync(req, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "授权云校验 HTTP 失败 FactoryId={FactoryId} Status={Status} Body={Body}",
                    factoryId, (int)resp.StatusCode, Truncate(raw, 500));
                await _db.SaveChangesAsync(ct);
                return;
            }

            var parsed = System.Text.Json.JsonSerializer.Deserialize<LicenseApiEnvelope>(raw,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (parsed == null || parsed.Code != 0 || parsed.Data == null)
            {
                _logger.LogWarning(
                    "授权云校验业务失败 FactoryId={FactoryId} Body={Body}",
                    factoryId, Truncate(raw, 500));
                await _db.SaveChangesAsync(ct);
                return;
            }

            var data = parsed.Data;
            var serverUtc = data.ServerUtc == default ? localUtc : data.ServerUtc.ToUniversalTime();
            factory.LastLicenseOkUtc = serverUtc;
            factory.LastLicenseSeenUtc = localUtc;
            if (data.ExpiresAtUtc.HasValue)
                factory.LicenseExpiresAtUtc = data.ExpiresAtUtc.Value.ToUniversalTime();
            else
                factory.LicenseExpiresAtUtc = null;

            // unknown 不抬档；valid/expired 以云端档为准（合同档）
            if (!string.Equals(data.Status, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                var newTier = LicenseTierPolicy.ToDb(LicenseTierPolicy.Parse(data.LicenseTier));
                factory.LicenseTier = newTier;
                // docs/90：转正式版清空体验到期列，不再进清理队列
                if (newTier is "enterprise" or "flagship")
                    factory.TrialExpiresAtUtc = null;
            }

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "授权云校验成功 FactoryCode={FactoryCode} Status={Status} Tier={Tier} Expires={Expires}",
                factory.FactoryCode, data.Status, factory.LicenseTier, factory.LicenseExpiresAtUtc);
        }
        catch (Exception ex)
        {
            // 断网/超时：保留缓存，仅落 seen；不抛死登录
            _logger.LogWarning(ex, "授权云校验异常 FactoryId={FactoryId} Url={Url}", factoryId, url);
            try { await _db.SaveChangesAsync(ct); }
            catch (Exception saveEx)
            {
                throw ThrowHelper.General(nameof(RefreshAsync), "写入授权校验时间失败", saveEx);
            }
        }
    }

    public async Task<LicenseTier> GetEffectiveTierAsync(long factoryId, CancellationToken ct = default)
    {
        var factory = await _db.Factories.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == factoryId, ct)
            ?? throw ThrowHelper.BizUser("工厂不存在");
        return Resolve(factory).Effective;
    }

    public async Task<LicenseStatusDto> GetStatusAsync(long factoryId, CancellationToken ct = default)
    {
        var factory = await _db.Factories.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == factoryId, ct)
            ?? throw ThrowHelper.BizUser("工厂不存在");
        var r = Resolve(factory);
        return new LicenseStatusDto
        {
            LicenseTier = LicenseTierPolicy.ToDb(LicenseTierPolicy.Parse(factory.LicenseTier)),
            EffectiveTier = LicenseTierPolicy.ToDb(r.Effective),
            ExpiresAtUtc = factory.LicenseExpiresAtUtc,
            LastOkUtc = factory.LastLicenseOkUtc,
            Status = r.Status switch
            {
                LicenseRuntimeStatus.Disabled => "disabled",
                LicenseRuntimeStatus.Valid => "valid",
                LicenseRuntimeStatus.Grace => "grace",
                _ => "expired"
            },
            Message = r.Message
        };
    }

    private (LicenseTier Effective, LicenseRuntimeStatus Status, string Message) Resolve(SysFactory factory)
    {
        var contracted = LicenseTierPolicy.Parse(factory.LicenseTier);
        if (!_opt.Enabled)
            return (contracted, LicenseRuntimeStatus.Disabled, "");

        var localUtc = DateTime.UtcNow;
        var seen = factory.LastLicenseSeenUtc;

        if (seen.HasValue && localUtc < seen.Value.AddMinutes(-ClockRollbackToleranceMinutes))
        {
            return (LicenseTier.Trial, LicenseRuntimeStatus.Expired,
                "系统时间异常，高级功能暂不可用，请联系部署方");
        }

        var effectiveNow = seen.HasValue && seen.Value > localUtc ? seen.Value : localUtc;

        if (factory.LicenseExpiresAtUtc.HasValue &&
            effectiveNow > factory.LicenseExpiresAtUtc.Value)
        {
            return (LicenseTier.Trial, LicenseRuntimeStatus.Expired,
                "授权已过期，高级功能暂不可用，请联系部署方续费");
        }

        if (!factory.LastLicenseOkUtc.HasValue)
        {
            return (LicenseTier.Trial, LicenseRuntimeStatus.Expired,
                "尚未完成授权激活，高级功能暂不可用，请联系部署方");
        }

        var graceDays = _opt.GraceDays <= 0 ? 7 : _opt.GraceDays;
        var graceEnd = factory.LastLicenseOkUtc.Value.AddDays(graceDays);
        if (effectiveNow > graceEnd)
        {
            return (LicenseTier.Trial, LicenseRuntimeStatus.Expired,
                "授权已过期或无法校验，高级功能暂不可用，请联系部署方续费");
        }

        // 距上次成功校验已超过一半宽限，且可能断网 → grace 提示
        var halfGrace = factory.LastLicenseOkUtc.Value.AddDays(graceDays / 2.0);
        if (effectiveNow > halfGrace)
        {
            return (contracted, LicenseRuntimeStatus.Grace,
                "授权校验已超过一段时间，请尽快联网续期；宽限期内高级功能仍可用");
        }

        return (contracted, LicenseRuntimeStatus.Valid, "");
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max];

    private sealed class LicenseApiEnvelope
    {
        public int Code { get; set; }
        public string? Msg { get; set; }
        public LicenseCheckDataDto? Data { get; set; }
    }

    private sealed class LicenseCheckDataDto
    {
        public string? FactoryCode { get; set; }
        public string? LicenseTier { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
        public DateTime ServerUtc { get; set; }
        public string? Status { get; set; }
    }
}
