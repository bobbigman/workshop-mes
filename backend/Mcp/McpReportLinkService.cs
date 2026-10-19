using System.Text.Json;
using Microsoft.Extensions.Options;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Mcp;

/// <summary>MCP：已核验平台身份获取本人入口；未关联返回普通H5首页与个人码指引（docs/249）。</summary>
public interface IMcpReportLinkService
{
    Task<string> GetReportLinkByIdentityAsync(string? identity, CancellationToken ct = default);
}

public class McpReportLinkService : IMcpReportLinkService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IMcpWxBindService _binding;
    private readonly IHttpContextAccessor _http;
    private readonly IMcpRequestContext _ctx;
    private readonly IUserService _users;
    private readonly IConfiguration _config;
    private readonly InstanceOptions _instance;

    public McpReportLinkService(
        IMcpWxBindService binding,
        IHttpContextAccessor http,
        IMcpRequestContext ctx,
        IUserService users,
        IConfiguration config,
        IOptions<InstanceOptions> instance)
    {
        _binding = binding;
        _http = http;
        _ctx = ctx;
        _users = users;
        _config = config;
        _instance = instance.Value;
    }

    public async Task<string> GetReportLinkByIdentityAsync(string? identity, CancellationToken ct = default)
    {
        var factoryId = _ctx.RequireFactoryId(nameof(GetReportLinkByIdentityAsync));
        var baseUrl = ResolvePhoneReachableBaseUrl();
        if (string.IsNullOrEmpty(baseUrl))
            return Fail("请管理员配置手机可访问的HTTPS报工地址；已有本人个人码可继续扫码，不需要回传验证码");
        var publicEntryUrl = $"{baseUrl}/#/h5/home";
        string UseOwnEntry(string reason) => JsonSerializer.Serialize(new {
            code = 1, state = "use_existing_login_or_personal_code", publicEntryUrl,
            msg = reason + "。已有H5登录可打开报工首页；也可使用本人原免密链接或扫描管理员提供的本人个人登录码。首次关联请管理员核验，不用在聊天发密码或8位验证码。普通首页不代表已按聊天姓名登录。"
        }, JsonOpts);
        // 249：只读可信请求头和管理员核验映射，模型的identity仅核对本人，不能用于选取账号。
        var session = _http.HttpContext?.Request.Headers["X-Mcp-Session-Id"].ToString().Trim();
        if (string.IsNullOrEmpty(session) || session.Length > 256)
            return UseOwnEntry("当前没有可靠的平台用户身份，不能仅凭姓名或手机号发放个人免密链接");
        var verifiedId = _config.GetValue<long>($"Mcp:WecomReport:VerifiedUsers:{factoryId}:{McpWxBindService.HashSession(session)}", 0);
        if (verifiedId <= 0) return UseOwnEntry("当前平台身份尚未由管理员关联核验");
        var user = await _binding.ResolveBoundUserAsync("wecom", session, ct);
        if (user == null || user.Id != verifiedId || user.FactoryId != factoryId || user.Status != 1 ||
            user.UserType == UserTypeHelper.External || user.Role is not (1 or 2 or 3))
            return UseOwnEntry("当前员工绑定无效或与管理员核验结果不一致");
        var raw = (identity ?? "").Trim();
        if (raw.Length > 0 && raw != user.Name && raw != user.Account && raw != user.Phone)
            return UseOwnEntry("只能获取当前已核验本人的入口，不能按聊天填写的信息替其他员工登录");

        var qr = await _users.GetOrCreateLoginQrAsync(user.Id, factoryId);
        if (qr.Code != 0 || qr.Data == null || string.IsNullOrEmpty(qr.Data.Code))
            return Fail(string.IsNullOrEmpty(qr.Msg) ? "生成报工链接失败，请稍后重试" : qr.Msg);

        var link = $"{baseUrl}/#/login?pq={Uri.EscapeDataString(qr.Data.Code)}";
        // cardText 内嵌完整 URL：企微机器人若只抄文案、改写 link 域名，用户仍能拿到可打开地址
        var cardText = $"{user.Name}师傅，点这里报工：{link}";
        return JsonSerializer.Serialize(new
        {
            code = 0,
            msg = "请把 link 原样发给用户（完整 https），禁止改成 localhost 或其它域名",
            name = user.Name,
            account = user.Account,
            link,
            cardText
        }, JsonOpts);
    }

    private static string Fail(string msg)
        => JsonSerializer.Serialize(new { code = 1, msg }, JsonOpts);

    /// <summary>
    /// 手机/企微外链基址：跟 MCP 公网入口（AuthBaseUrl）；空时才回落 Instance:PublicBaseUrl。
    /// 不回落 AuthLocalBaseUrl（那是本机自测，手机会 CONNECTION_REFUSED）。
    /// </summary>
    private string? ResolvePhoneReachableBaseUrl()
    {
        foreach (var candidate in new[]
                 {
                     (_config["Mcp:AuthBaseUrl"] ?? "").Trim().TrimEnd('/'),
                     (_instance.PublicBaseUrl ?? "").Trim().TrimEnd('/')
                 })
        {
            if (string.IsNullOrEmpty(candidate)) continue;
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                uri.IsLoopback || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment) || IsLoopbackBaseUrl(candidate)) continue;
            return candidate;
        }
        return null;
    }

    private static bool IsLoopbackBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) return false;
        var host = uri.Host;
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
               || host == "127.0.0.1"
               || host == "::1"
               || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    }
}
