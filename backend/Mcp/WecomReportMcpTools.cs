using System.ComponentModel;
using System.Text.Json;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;

namespace ahu.MicrosoftMes.Mcp;

/// <summary>只接受可信平台请求头身份；不接受模型填写身份，不使用演示免绑定。</summary>
[McpServerToolType]
public class WecomReportMcpTools
{
    private static readonly SemaphoreSlim[] Gates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IWecomReportService _reports;
    private readonly IMcpRequestContext _context;
    private readonly IMcpWxBindService _binding;
    private readonly IHttpContextAccessor _http;
    private readonly IConfiguration _config;
    private readonly IMemoryCache _cache;
    private readonly IWecomReportTaskService? _tasks;
    private readonly ILogger<WecomReportMcpTools> _logger;
    private readonly IReportIdentityService? _identity;

    public WecomReportMcpTools(IWecomReportService reports, IMcpRequestContext context,
        IMcpWxBindService binding, IHttpContextAccessor http, IConfiguration config, IMemoryCache cache,
        IWecomReportTaskService? tasks = null, ILogger<WecomReportMcpTools>? logger = null,
        IReportIdentityService? identity = null)
    {
        (_reports, _context, _binding, _http, _config, _cache, _tasks) = (reports, context, binding, http, config, cache, tasks);
        _logger = logger ?? NullLogger<WecomReportMcpTools>.Instance;
        _identity = identity;
    }

    [McpServerTool(Name = "identify_report_worker"), Description("仅用户明确要求在聊天中直接完成报工时使用；默认报工先提供H5免密链接或本人个人登录码扫码入口，不因普通报工请求先让用户网页密码验证或回传验证码。聊天分支未获得可信企微userid且没有有效reportAccessToken时，用机器人已知的当前用户名先跨本实例工厂精确匹配员工；名字未知/重名/未命中请问手机号后6位，再不唯一则问完整手机号。本工具仅定位、不授权、不写报工、不默认入口工厂；完整手机号仍匹配多个工厂时按select_factory实际列表让用户选序号，再携带factorySelectionToken和factoryNumber重调，不混传姓名手机号，不猜厂名或厂ID。成功回显姓名工厂并把authorizationUrl原样发给用户；用户须网页登录本人账号、确认授权后将8位验证码回给机器人。lookupToken保留供verify_report_identity，不让用户在聊天发密码，也不能用get_report_link_by_identity的免密链接替代本次验证。")]
    public Task<string> IdentifyWorker(
        [Description("当前企微用户名或用户提供的准确员工姓名，可空，不猜姓名")] string? displayName = null,
        [Description("姓名不唯一时用户提供的手机号后6位或完整11位号码，可空")] string? phone = null,
        [Description("完整手机号多厂匹配返回的短期选择凭证")] string? factorySelectionToken = null,
        [Description("用户从本次实际工厂列表选择的序号，不是厂ID")] int? factoryNumber = null)
        => RunAsync("identify_report_worker", async () => JsonSerializer.Serialize(ApiResult<object>.Ok(
            await IdentityService().IdentifyAsync(_context.RequireFactoryId(nameof(IdentifyWorker)), displayName, phone, factorySelectionToken, factoryNumber)), JsonOptions));

    [McpServerTool(Name = "verify_report_identity"), Description("首次网页验证后，用户给出8位验证码时调用；lookupToken必须来自本次identify_report_worker，不能猜。仅换取30分钟reportAccessToken，不提交报工。后续候选/预览/确认/接入检查都携带该凭证，不能展示或朗读长凭证；不能索要账号密码。未网页确认、错误/过期/跨厂/重复验证码拒绝，不能靠姓名或手机号跳过。")]
    public Task<string> VerifyIdentity(
        [Description("本次定位返回的lookupToken，非用户姓名或手机号")] string lookupToken,
        [Description("用户从登录验证页获得的8位一次性验证码")] string verificationCode)
        => RunAsync("verify_report_identity", async () => JsonSerializer.Serialize(ApiResult<object>.Ok(
            await IdentityService().VerifyAsync(_context.RequireFactoryId(nameof(VerifyIdentity)), lookupToken, verificationCode)), JsonOptions));

    private IReportIdentityService IdentityService() => _identity ?? throw ThrowHelper.BizUser("网页登录授权服务未配置，请联系管理员");

    [McpServerTool(Name = "check_work_report_readiness"), Description("只读检查企微对话报工是否具备接入条件；报工未启用、缺身份或绑定问题时调用。返回开关、工厂、平台身份、员工核验映射及有效绑定的通过/阻断/未检查状态与下一步，不启用、不绑定、不查任务、不提交报工。code=0仅表示检查成功，ready=false仍不能办理；平台身份头可信性仍须现场核实。按返回具体原因回复，不统称暂时无法回答。")]
    public Task<string> CheckReadiness([Description("已网页验证的短期报工凭证，可空；不是JWT或手机号")] string? reportAccessToken = null)
        => RunAsync("check_work_report_readiness", () => CheckReadinessCoreAsync(reportAccessToken));

    private async Task<string> CheckReadinessCoreAsync(string? reportAccessToken)
    {
        var enabled = _config.GetValue("Mcp:WecomReport:Enabled", false);
        var factory = _context.CurrentFactoryId;
        var session = _http.HttpContext?.Request.Headers["X-Mcp-Session-Id"].ToString().Trim();
        var hasFactory = factory is > 0;
        if (reportAccessToken != null)
        {
            var login = await IdentityService().ResolveAsync(_context.RequireFactoryId(nameof(CheckReadiness)), reportAccessToken);
            return JsonSerializer.Serialize(ApiResult<object>.Ok(new { ready = enabled, authenticationMode = "verified_login",
                checks = new[]
                {
                    new { name = "enabled", status = enabled ? "passed" : "blocked", message = enabled ? "报工已启用" : "报工尚未启用，请管理员核验配置后启用" },
                    new { name = "identity", status = "passed", message = "本次网页登录身份已核验，无需企微userid头" },
                    new { name = "factory", status = "passed", message = "已锁定本人网页验证的目标工厂及当前MCP入口" },
                    new { name = "binding", status = "passed", message = "员工当前有效；本方式不新增或修改企微绑定" }
                }, hint = "使用网页登录短期授权，后续工具继续携带reportAccessToken。" }), JsonOptions);
        }
        var hasIdentity = !string.IsNullOrEmpty(session) && session.Length <= 256;
        var verified = hasFactory && hasIdentity
            ? _config.GetValue<long>($"Mcp:WecomReport:VerifiedUsers:{factory}:{McpWxBindService.HashSession(session!)}", 0) : 0;
        var user = verified > 0 ? await _binding.ResolveBoundUserAsync("wecom", session) : null;
        var matches = user != null && user.Id == verified && user.FactoryId == factory && user.Status == 1 && user.Role is 1 or 2 or 3;
        var ready = enabled && hasFactory && hasIdentity && verified > 0 && matches;
        return JsonSerializer.Serialize(ApiResult<object>.Ok(new
        {
            ready,
            checks = new[]
            {
                new { name = "enabled", status = enabled ? "passed" : "blocked", message = enabled ? "报工已启用" : "报工尚未启用；完成身份接入核验后由管理员启用" },
                new { name = "factory", status = hasFactory ? "passed" : "blocked", message = hasFactory ? "MCP已确定工厂" : "MCP未确定工厂，请管理员检查连接钥匙" },
                new { name = "identity", status = hasIdentity ? "passed" : "blocked", message = hasIdentity ? "收到平台身份头，其真实性仍须现场核实" : "未收到有效的平台用户身份，请管理员检查身份头传递" },
                new { name = "verifiedMapping", status = !hasFactory || !hasIdentity ? "not_checked" : verified > 0 ? "passed" : "blocked", message = !hasFactory || !hasIdentity ? "缺少工厂或身份，暂未检查员工核验映射" : verified > 0 ? "已配置员工核验映射" : "尚未配置当前身份的员工核验映射" },
                new { name = "binding", status = verified <= 0 ? "not_checked" : matches ? "passed" : "blocked", message = verified <= 0 ? "尚未具备核验条件，暂未检查绑定" : matches ? "员工有效绑定与核验映射匹配" : "有效员工绑定缺失、停用或与核验映射不符，请联系管理员" }
            },
            hint = ready ? "程序可检查项已通过；仍须确认平台身份头不可由用户或模型伪造，再进行现场联调。" : "当前不能对话报工，请按阻断项完善配置；可继续使用现有报工表单。"
        }), JsonOptions);
    }

    [McpServerTool(Name = "query_my_report_tasks"), Description("仅聊天直接报工分支使用；普通帮我报工默认引导免密H5或扫描本人个人登录码。聊天中用户要报工但不知道工单号时先调用：优先列本人/本班组尚可报工的派工工序，没有则列本人有权限的未完成工序；每页最多5项。按返回序号原样展示产品、完整单号、工序与工序剩余，不把剩余当个人完成量。支持产品/单号关键词；换一批用下一页，仅hasMore时翻页。用户说第一项50件后，带本次candidateListToken、candidateNumber=1、goodQty=50调用preview_work_report，不猜单号或直接提交。查询失败不能当暂无派工。")]
    public Task<string> QueryTasks(
        [Description("产品名、产品编号或工单号关键词，可空")] string? keyword = null,
        [Description("从1开始；每页5项")] int page = 1,
        [Description("网页登录验证所得短期报工凭证，无可信企微身份头时必传")] string? reportAccessToken = null)
        => RunAsync("query_my_report_tasks", () => QueryTasksCoreAsync(keyword, page, reportAccessToken));

    private async Task<string> QueryTasksCoreAsync(string? keyword, int page, string? reportAccessToken)
    {
        var actor = await RequireActorAsync(reportAccessToken);
        if (_tasks == null) throw ThrowHelper.BizUser("报工候选查询服务未配置，请联系管理员");
        var gate = Gate(actor.Owner);
        await gate.WaitAsync();
        try
        {
            Invalidate(actor);
            InvalidateList(actor.Owner);
            var result = await _tasks.QueryAsync(actor.FactoryId, actor.UserId, actor.Role, keyword, page);
            if (result.Items.Count > 0)
            {
                var token = Guid.NewGuid().ToString("N");
                result.CandidateListToken = token;
                var life = TimeSpan.FromMinutes(result.ExpireMinutes);
                _cache.Set("mcp-report-list:" + token, new CandidateSnapshot(actor.Owner, result.Items.ToArray()), life);
                _cache.Set(actor.Owner + ":list", token, life);
            }
            return JsonSerializer.Serialize(ApiResult<ReportTaskPage>.Ok(result), JsonOptions);
        }
        finally { gate.Release(); }
    }

    [McpServerTool(Name = "preview_work_report"), Description("办理本人报工，只预览不入账。可用准确工单号+工序+goodQty；也可用最新query_my_report_tasks返回的candidateListToken+candidateNumber+goodQty，此时不填单号/工序。缺单号先查候选，缺实际数量或数字有歧义才追问，禁止把工序剩余自动当产量。回显员工/工单/工序/数量，用户明确确认后再调confirm_work_report；不良/多人报工走表单。")]
    public Task<string> Preview(
        [Description("准确工单号，使用候选序号时留空")] string? orderNo = null,
        [Description("工序名称，使用候选序号时留空")] string? operationName = null,
        [Description("用户本次实际完成良品件数，正整数，不得自动取剩余数")] int? goodQty = null,
        [Description("最新候选列表返回的凭证，不展示给用户")] string? candidateListToken = null,
        [Description("用户选择的本页序号，1至5")] int? candidateNumber = null,
        [Description("网页登录验证所得短期报工凭证，无可信企微身份头时必传")] string? reportAccessToken = null)
        => RunAsync("preview_work_report", () => PreviewCoreAsync(orderNo, operationName, goodQty, candidateListToken, candidateNumber, reportAccessToken));

    private async Task<string> PreviewCoreAsync(string? orderNo, string? operationName, int? goodQty,
        string? candidateListToken, int? candidateNumber, string? reportAccessToken)
    {
        var actor = await RequireActorAsync(reportAccessToken);
        var gate = Gate(actor.Owner);
        await gate.WaitAsync();
        try
        {
            Invalidate(actor);
            if (goodQty is null or <= 0) throw ThrowHelper.BizUser("请说明本次实际完成的良品件数（正整数）");
            ReportTaskCandidate? selected = null;
            if (candidateListToken != null || candidateNumber != null)
            {
                if (!string.IsNullOrWhiteSpace(orderNo) || !string.IsNullOrWhiteSpace(operationName))
                    throw ThrowHelper.BizUser("候选序号与手填工单/工序不能混用，请重新选择");
                if (string.IsNullOrEmpty(candidateListToken) || candidateListToken.Length > 64 || candidateNumber is null or < 1 or > 5 ||
                    !_cache.TryGetValue(actor.Owner + ":list", out string? currentList) || currentList != candidateListToken ||
                    !_cache.TryGetValue("mcp-report-list:" + candidateListToken, out CandidateSnapshot? snapshot) || snapshot?.Owner != actor.Owner)
                    throw ThrowHelper.BizUser("候选列表过期、已替换或不属于本人，请重新查询后选择");
                selected = snapshot.Items.FirstOrDefault(x => x.Number == candidateNumber);
                if (selected == null) throw ThrowHelper.BizUser("序号不在当前列表，请按本页序号重新选择");
                orderNo = selected.OrderNo;
                operationName = selected.OperationName;
            }
            var result = await _reports.PreviewAsync(new WecomReportPreviewDto
            {
                UserId = actor.Session, ChanType = "wecom", McpOwner = actor.Owner,
                VerifiedLoginUserId = actor.LoginVerified ? actor.UserId : null,
                OrderNo = orderNo, OperationName = operationName, GoodQty = goodQty.Value,
                ExpectedOrderId = selected?.OrderId, ExpectedOperationId = selected?.OperationId
            }, actor.FactoryId);
            if (result.Code == 0 && result.Data != null)
                _cache.Set(actor.Owner, result.Data.ConfirmToken, TimeSpan.FromMinutes(result.Data.ExpireMinutes));
            return JsonSerializer.Serialize(result, JsonOptions);
        }
        finally { gate.Release(); }
    }

    [McpServerTool(Name = "confirm_work_report"), Description("仅在用户明确确认当前预览后提交本人报工。必须使用本会话最新预览返回的 confirmToken，不向用户展示凭证。用户取消或要求修改但尚未给齐新数据时，cancel=true 立即作废当前预览。只有 code=0 且返回实际报工结果才可说报工成功；未知结果按原凭证重试，不新建报工。")]
    public Task<string> Confirm(
        [Description("最新预览返回的凭证；取消时可空")] string? confirmToken = null,
        [Description("用户取消/修改时为 true，作废预览而不提交")] bool cancel = false,
        [Description("网页登录验证所得短期报工凭证，无可信企微身份头时必传")] string? reportAccessToken = null)
        => RunAsync(cancel ? "cancel_work_report" : "confirm_work_report", () => ConfirmCoreAsync(confirmToken, cancel, reportAccessToken));

    private async Task<string> ConfirmCoreAsync(string? confirmToken, bool cancel, string? reportAccessToken)
    {
        var actor = await RequireActorAsync(reportAccessToken);
        var gate = Gate(actor.Owner);
        await gate.WaitAsync();
        try
        {
            if (cancel)
            {
                Invalidate(actor);
                return JsonSerializer.Serialize(ApiResult<object?>.Ok(new { cancelled = true }), JsonOptions);
            }
            if (!_cache.TryGetValue(actor.Owner, out string? current) || current != confirmToken || string.IsNullOrEmpty(current))
                throw ThrowHelper.BizUser("当前预览已取消、替换或过期，请重新报数预览");
            return JsonSerializer.Serialize(await _reports.ConfirmAsync(new WecomReportConfirmDto
            {
                ConfirmToken = current, McpOwner = actor.Owner, McpUserId = actor.UserId
            }, actor.FactoryId), JsonOptions);
        }
        finally { gate.Release(); }
    }

    // MCP SDK会处理工具异常，普通HTTP异常中间件无法保证这些提示进入工具返回。
    // 在协议边界显式返回失败，服务层仍用ThrowHelper抛错，技术故障保留脱敏堆栈。
    private async Task<string> RunAsync(string tool, Func<Task<string>> action)
    {
        var trace = _http.HttpContext?.TraceIdentifier ?? LogRedactor.CurrentTraceId();
        try { return await action(); }
        catch (OperationCanceledException) when (_http.HttpContext?.RequestAborted.IsCancellationRequested == true)
        {
            _logger.LogWarning("event=mcp_report_cancelled Tool={Tool} TraceId={TraceId}", tool, trace);
            throw;
        }
        catch (BusinessException ex) when (!LogRedactor.LooksTechnical(ex.Message) && ex.InnerException == null)
        {
            var message = LogRedactor.RedactText(ex.Message, 4096);
            _logger.LogWarning("event=mcp_report_business Tool={Tool} TraceId={TraceId} Code={Code} Msg={Msg}", tool, trace, ex.Code, message);
            return JsonSerializer.Serialize(new { code = ex.Code == 0 ? 1 : ex.Code, msg = message, data = new { traceId = trace } }, JsonOptions);
        }
        catch (Exception ex)
        {
            // 不传原异常对象，防止logger另写未脱敏的Message/堆栈；格式化链保留定位信息。
            _logger.LogError("event=mcp_report_error Tool={Tool} TraceId={TraceId} ExceptionChain={Chain}",
                tool, trace, LogRedactor.FormatExceptionChain(ex));
            var message = tool switch
            {
                "confirm_work_report" => "报工确认发生技术故障，提交结果待核实。请保留原确认凭证，联系管理员查账或按原凭证重试，不要新建一笔报工。",
                "cancel_work_report" => "取消报工预览发生技术故障，取消结果待核实。请勿继续确认旧预览，联系管理员核实后再继续。",
                "check_work_report_readiness" => "报工接入检查失败，请联系管理员按追踪号查看具体原因。",
                "query_my_report_tasks" => "查询待报工任务失败，请联系管理员按追踪号查看具体原因，不能将本次失败当作没有派工。",
                _ => "报工预览失败，本次未完成预览，请联系管理员按追踪号查看具体原因。"
            };
            return JsonSerializer.Serialize(new { code = 500, msg = message, data = new { traceId = trace, resultUnknown = tool is "confirm_work_report" or "cancel_work_report" } }, JsonOptions);
        }
    }

    private async Task<Actor> RequireActorAsync(string? reportAccessToken = null)
    {
        if (!_config.GetValue("Mcp:WecomReport:Enabled", false))
            throw ThrowHelper.BizUser("企微对话报工尚未启用，请使用报工表单");
        var factoryId = _context.RequireFactoryId(nameof(RequireActorAsync));
        if (reportAccessToken != null)
        {
            var login = await IdentityService().ResolveAsync(factoryId, reportAccessToken);
            return new Actor(login.Grant.FactoryId, login.User.Id, login.User.Role, "verified-login:" + login.Grant.GrantId,
                $"mcp-report-login:{factoryId}:{login.Grant.FactoryId}:{login.User.Id}:{login.Grant.GrantId}", true);
        }
        var session = _http.HttpContext?.Request.Headers["X-Mcp-Session-Id"].ToString().Trim();
        if (string.IsNullOrEmpty(session) || session.Length > 256)
            throw ThrowHelper.BizUser("缺少可靠的企微用户身份，请先按姓名定位并网页登录验证，或使用报工表单");
        var hash = McpWxBindService.HashSession(session);
        // 管理员须先核验平台身份与 MES 员工关系；仅手机号自绑不授予写入资格。
        var verifiedId = _config.GetValue<long>($"Mcp:WecomReport:VerifiedUsers:{factoryId}:{hash}", 0);
        if (verifiedId <= 0)
            throw ThrowHelper.BizUser("当前企微身份尚未核验，可先网页登录验证，或联系管理员核验员工绑定");
        var user = await _binding.ResolveBoundUserAsync("wecom", session);
        if (user == null || user.Id != verifiedId || user.FactoryId != factoryId || user.Status != 1 || user.Role is not (1 or 2 or 3))
            throw ThrowHelper.BizUser("员工绑定无效或与核验身份不一致，请联系管理员");
        return new Actor(factoryId, user.Id, user.Role, session, $"mcp-report:{factoryId}:{hash}:{user.Id}");
    }

    private void InvalidateList(string owner)
    {
        if (_cache.TryGetValue(owner + ":list", out string? token) && token != null)
            _cache.Remove("mcp-report-list:" + token);
        _cache.Remove(owner + ":list");
    }

    private void Invalidate(Actor actor)
    {
        if (_cache.TryGetValue(actor.Owner, out string? token) && token != null)
            _reports.CancelMcpPreview(token, actor.FactoryId, actor.Owner);
        _cache.Remove(actor.Owner);
    }

    private static SemaphoreSlim Gate(string owner) => Gates[(int)((uint)StringComparer.Ordinal.GetHashCode(owner) % (uint)Gates.Length)];
    private sealed record Actor(long FactoryId, long UserId, byte Role, string Session, string Owner, bool LoginVerified = false);
    private sealed record CandidateSnapshot(string Owner, ReportTaskCandidate[] Items);
}
