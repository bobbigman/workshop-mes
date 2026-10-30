using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ahu.MicrosoftMes.Services;

/// <summary>敏感入口每次读取当前身份，不沿用JWT签发时的角色/归属。</summary>
public static class CurrentAccountAccess
{
    public static async Task<SysUser> RequireAsync(AppDbContext db, long userId, long factoryId,
        bool adminOnly = true, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId
            && u.FactoryId == factoryId && u.Status == 1, ct);
        if (user == null || user.UserType == UserTypeHelper.External || user.Role is not (1 or 2 or 3)
            || !await db.Factories.AsNoTracking().AnyAsync(f => f.Id == factoryId && f.TrialCleanedAtUtc == null, ct))
            throw ThrowHelper.BizCode("账号已失效或不属于当前工厂，请重新登录。", 401);
        if (adminOnly && user.Role != 1)
            throw ThrowHelper.BizCode("该功能仅当前有效管理员可用。", 403);
        return user;
    }
}

public sealed class CurrentAdminFilter(AppDbContext db) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        try
        {
            await CurrentAccountAccess.RequireAsync(db, JwtHelper.GetUserId(http.User),
                JwtHelper.GetFactoryId(http.User), ct: http.RequestAborted);
        }
        catch (BusinessException ex) when (ex.Code is 401 or 403)
        {
            context.Result = new ObjectResult(new { code = ex.Code, msg = ex.Message }) { StatusCode = ex.Code };
            return;
        }
        var result = await next();
        if (result.Exception == null && !http.RequestAborted.IsCancellationRequested)
        {
            try
            {
                await CurrentAccountAccess.RequireAsync(db, JwtHelper.GetUserId(http.User),
                    JwtHelper.GetFactoryId(http.User), ct: http.RequestAborted);
            }
            catch (BusinessException ex) when (ex.Code is 401 or 403)
            {
                result.Result = new ObjectResult(new { code = ex.Code, msg = ex.Message }) { StatusCode = ex.Code };
            }
        }
    }
}
