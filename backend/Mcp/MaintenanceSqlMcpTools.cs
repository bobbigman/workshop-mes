using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;
using ModelContextProtocol.Server;

namespace ahu.MicrosoftMes.Mcp;

/// <summary>实例维护权限独立于账套查询权限；演示免绑定不能放行。</summary>
[McpServerToolType]
public sealed class MaintenanceSqlMcpTools(MaintenanceSqlService service, IConfiguration config,
    IHttpContextAccessor http, IMcpRequestContext context)
{
    public static void RequireMaintenance(IConfiguration config, string? presented)
    {
        var key = config["Mcp:Maintenance:Key"];
        if (!config.GetValue("Mcp:Maintenance:Enabled", false) || string.IsNullOrWhiteSpace(key) || key.Length is < 32 or > 1024
            || string.IsNullOrWhiteSpace(presented) || presented.Length > 1024
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(key)), SHA256.HashData(Encoding.UTF8.GetBytes(presented))))
            throw ThrowHelper.BizUser("SQL维护未启用或缺少有效的独立维护权限");
    }

    private string Identity()
    {
        var presented = http.HttpContext?.Request.Headers["X-Maintenance-Key"].ToString();
        RequireMaintenance(config, presented);
        var factory = context.RequireFactoryId(nameof(MaintenanceSqlMcpTools));
        return $"{context.KeyAlias}:{factory}:{MaintenanceSqlService.Hash(presented!)}";
    }

    [McpServerTool(Name = "prepare_maintenance_sql", ReadOnly = true, Destructive = false), Description("准备通用SQL维护（可查询、写入、DDL且可跨账套）。仅系统维护者独立维护权限可用。只返回完整SQL、实际服务器/数据库和5分钟一次性confirmationToken，不执行提交的SQL。核对后再调用execute_maintenance_sql。禁止未经用户授权清理数据。")]
    public Task<string> PrepareMaintenanceSql([Description("完整SQL，最多64000字符；不支持GO。任意写入请自行编写事务和引用保护。不要在SQL中放密码/Token。")] string sql, CancellationToken cancellationToken = default)
        => RunAsync(async () => await service.PrepareAsync(sql, Identity(), cancellationToken));

    [McpServerTool(Name = "execute_maintenance_sql", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true), Description("执行已准备并确认的通用维护SQL，可能跨账套修改/删除数据。只接受prepare_maintenance_sql返回的凭据；一次性使用，不允许替换SQL。失败/超时可能部分提交，须先查询结果，禁止自动重试。返回结果截断不停止SQL执行，recordsAffected=-1表示驱动未提供计数。")]
    public Task<string> ExecuteMaintenanceSql([Description("核对SQL和目标后，使用准备步骤返回的confirmationToken；过期需重新准备。")] string confirmationToken, CancellationToken cancellationToken = default)
        => RunAsync(async () => await service.ExecuteAsync(confirmationToken, Identity(), cancellationToken));

    private static async Task<string> RunAsync(Func<Task<object>> action)
    {
        try { return JsonSerializer.Serialize(new { code = 0, data = await action() }, OperationPricingService.Json); }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { code = 1, msg = LogRedactor.RedactText(ex.Message + (ex.InnerException == null ? "" : "；" + ex.InnerException.Message), 12000) });
        }
    }
}
