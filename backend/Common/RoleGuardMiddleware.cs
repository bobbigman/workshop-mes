using Microsoft.AspNetCore.Http;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Common;

/// <summary>
/// 角色网关（docs/22）：
///   role=2 生产人员：可查询工单/报工/报表/看板；报工提交仍走部门权限。不能改基础数据。
///   role=3 班组长：工人白名单 + 复核 API + 报工列表 + 生产报表/看板只读
///   role=1 管理员：不拦截
    ///   登录相关与实例信息永不拦截（浏览器里可能还带着旧 token，如工人再扫个人免密码）。
    /// </summary>
public class RoleGuardMiddleware
{
    private readonly RequestDelegate _next;

    public RoleGuardMiddleware(RequestDelegate next) { _next = next; }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        var method = context.Request.Method;
        if (IsLoginOrInstance(path, method))
        {
            await _next(context);
            return;
        }

        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var role = JwtHelper.GetRole(context.User);
            if (role == 2 || role == 3)
            {

                var allowed = role == 2
                    ? IsAllowedForWorker(path, method)
                    : IsAllowedForLeader(path, method);

                if (!allowed)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    var msg = "该功能仅管理员可用，如需使用请联系管理员";
                    await context.Response.WriteAsJsonAsync(new { code = 403, msg });
                    return;
                }
            }
        }

        await _next(context);
    }

    private static bool IsAllowedForWorker(string path, string method)
    {
        if (WorkerHelpPolicy.IsAllowedEndpoint(path, method)) return true;
        if (method == "POST" && (path.Equals("/api/QueryIdentity/inspect", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/QueryIdentity/approve", StringComparison.OrdinalIgnoreCase))) return true;
        if ((method == "POST" && path.Equals("/api/work-session/start", StringComparison.OrdinalIgnoreCase))
            || (method == "GET" && path.Equals("/api/work-session/current", StringComparison.OrdinalIgnoreCase))) return true;
        if (method == "POST" && (path.Equals("/api/ReportIdentity/inspect", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/ReportIdentity/approve", StringComparison.OrdinalIgnoreCase)))
            return true; // 仅本人JWT确认短期报工授权；服务再次校验目标员工与工厂。
        if (path.Equals("/api/Report", StringComparison.OrdinalIgnoreCase) && method == "POST")
            return true;
        if (method == "POST" && path.StartsWith("/api/Report/", StringComparison.OrdinalIgnoreCase)
            && path.EndsWith("/withdraw", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(path["/api/Report/".Length..^"/withdraw".Length], out _)) return true;
        // 工人异常上报（docs/28）
        if (path.Equals("/api/Abnormal", StringComparison.OrdinalIgnoreCase) && method == "POST")
            return true;
        if (path.Equals("/api/Abnormal", StringComparison.OrdinalIgnoreCase) && method == "GET")
            return true;
        if (path.Equals("/api/Abnormal/handlers", StringComparison.OrdinalIgnoreCase) && method == "GET")
            return true;
        // 工人改自己的待复核/退回报工（docs/22）
        if (method.Equals("PUT", StringComparison.OrdinalIgnoreCase)
            && path.StartsWith("/api/Report/", StringComparison.OrdinalIgnoreCase))
        {
            var rest = path["/api/Report/".Length..];
            if (long.TryParse(rest, out _)) return true;
        }
        if (path.StartsWith("/api/Report/defects", StringComparison.OrdinalIgnoreCase) && method == "GET")
            return true;
        // 本人报工 / 工序可选人（docs/94）；今日计件汇总（docs/206）
        if (path.Equals("/api/Report/mine", StringComparison.OrdinalIgnoreCase) && method == "GET")
            return true;
        if (path.Equals("/api/Report/mine/today-summary", StringComparison.OrdinalIgnoreCase) && method == "GET")
            return true;
        // 报工修改日志（docs/205）
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && path.StartsWith("/api/Report/", StringComparison.OrdinalIgnoreCase)
            && path.EndsWith("/changes", StringComparison.OrdinalIgnoreCase))
            return true;
        // docs/241：仅放行本人工资单两个只读接口，管理接口保持原权限。
        const string myStatements = "/api/Salary/my-statements";
        if (method == "GET" && path.Equals(myStatements, StringComparison.OrdinalIgnoreCase)) return true;
        if (method == "GET" && path.StartsWith(myStatements + "/", StringComparison.OrdinalIgnoreCase)
            && path.EndsWith("/items", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(path[(myStatements.Length + 1)..^"/items".Length], out _)) return true;
        // 本人工资预估（docs/103）；受 PieceWage 控制器特性约束
        if (path.Equals("/api/Salary/mine", StringComparison.OrdinalIgnoreCase) && method == "GET")
            return true;
        if (path.StartsWith("/api/Report/candidates/", StringComparison.OrdinalIgnoreCase) && method == "GET")
            return true;
        // 个人菜单收藏（docs/240）：只改自己的星标，不改基础数据
        if (path.Equals("/api/userfavorite", StringComparison.OrdinalIgnoreCase) && method == "GET")
            return true;
        if (path.Equals("/api/userfavorite/toggle", StringComparison.OrdinalIgnoreCase) && method == "POST")
            return true;
        // 报工列表：没有报工权限也可以查
        if (path.Equals("/api/Report", StringComparison.OrdinalIgnoreCase) && method == "GET")
            return true;

        // 我的任务（docs/29）：工人/班组长查看派给自己的工序任务
        if (path.Equals("/api/Assign/my-tasks", StringComparison.OrdinalIgnoreCase) && method == "GET")
            return true;

        if (!method.Equals("GET", StringComparison.OrdinalIgnoreCase))
            return false;

        // 作业指导书/图纸（docs/66）：工人可读本厂知识库文件清单与原文
        if (path.StartsWith("/api/KnowledgeFile/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/KnowledgeFile", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.StartsWith("/api/ReportStat", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.StartsWith("/api/CustomField", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.Equals("/api/WorkOrder", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.Equals("/api/WorkOrder/status-counts", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.Equals("/api/WorkOrder/by-no", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.StartsWith("/api/WorkOrder/", StringComparison.OrdinalIgnoreCase))
        {
            var rest = path["/api/WorkOrder/".Length..];
            if (long.TryParse(rest, out _)) return true;
        }

        // 执行监控只读（docs/19）
        if (path.Equals("/api/ExecutionMonitor", StringComparison.OrdinalIgnoreCase))
            return true;

        // docs/89：授权状态只读
        if (path.Equals("/api/license/status", StringComparison.OrdinalIgnoreCase) && method == "GET")
            return true;

        // 工人手机视角只读（docs/200）；PUT 仍仅管理员
        if (path.Equals("/api/WorkerView", StringComparison.OrdinalIgnoreCase) && method == "GET")
            return true;

        // PC 常见问题（docs/113）：任意登录用户只读
        if (path.Equals("/api/SupportDocs/faq", StringComparison.OrdinalIgnoreCase) && method == "GET")
            return true;

        return false;
    }

    private static bool IsLoginOrInstance(string path, string method)
    {
        if (path.Equals("/api/instance-info", StringComparison.OrdinalIgnoreCase) && method.Equals("GET", StringComparison.OrdinalIgnoreCase))
            return true;
        // 登录页配图（docs/69）：[AllowAnonymous]；工人带着旧 token 再进登录页仍须可读
        if (path.Equals("/api/LoginSetting", StringComparison.OrdinalIgnoreCase) && method.Equals("GET", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            return false;
        // 账号密码 / 手机 / 个人免密（docs/150）/ 选厂 / 清缓存：均可能带着旧工人 JWT 再调
        return path.Equals("/api/Auth/login", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/Auth/login-phone", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/Auth/login-by-qr", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/Auth/select-factory", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/Auth/clear-browser-cache", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowedForLeader(string path, string method)
    {
        if (IsAllowedForWorker(path, method))
            return true;

        // 报工列表
        if (path.Equals("/api/Report", StringComparison.OrdinalIgnoreCase) && method == "GET")
            return true;

        // 复核
        if (path.StartsWith("/api/Review", StringComparison.OrdinalIgnoreCase))
            return true;

        // 异常处理回填（docs/28）
        if (method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            && path.StartsWith("/api/Abnormal/", StringComparison.OrdinalIgnoreCase)
            && path.EndsWith("/resolve", StringComparison.OrdinalIgnoreCase))
            return true;

        // 派工（docs/29/201/214）：班组长派个人或班组；GET workers / departments
        if (path.Equals("/api/Assign/workers", StringComparison.OrdinalIgnoreCase) && method == "GET")
            return true;
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && path.StartsWith("/api/Assign/departments/", StringComparison.OrdinalIgnoreCase))
            return true;
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && path.StartsWith("/api/Assign/suggest/", StringComparison.OrdinalIgnoreCase))
        {
            var rest = path["/api/Assign/suggest/".Length..];
            if (long.TryParse(rest, out _)) return true;
        }
        if (method.Equals("PUT", StringComparison.OrdinalIgnoreCase)
            && path.StartsWith("/api/Assign/", StringComparison.OrdinalIgnoreCase))
        {
            var rest = path["/api/Assign/".Length..];
            if (long.TryParse(rest, out _)) return true;
        }

        // 生产报表 / 看板
        if (path.StartsWith("/api/ReportStat", StringComparison.OrdinalIgnoreCase) && method == "GET")
            return true;

        return false;
    }
}
