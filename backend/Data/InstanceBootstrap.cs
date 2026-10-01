using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Data;

/// <summary>
/// 客户实例首次初始化：仅创建工厂 + 管理员；与演示 SeedData 分离；不重置已有密码。
/// </summary>
public static class InstanceBootstrap
{
    public static async Task EnsureAsync(AppDbContext db, InstanceOptions opt, ILogger logger)
    {
        if (!opt.BootstrapOnStartup)
            return;

        logger.LogWarning(
            "Instance:BootstrapOnStartup=true，正在确保工厂与管理员存在。完成后请改回 false。Instance={Code}",
            opt.Code);

        var factory = await db.Factories.FirstOrDefaultAsync(f => f.FactoryCode == opt.FactoryCode);
        if (factory == null)
        {
            factory = new SysFactory
            {
                FactoryCode = opt.FactoryCode.Trim(),
                FactoryName = string.IsNullOrWhiteSpace(opt.DisplayName) ? opt.FactoryCode : opt.DisplayName.Trim(),
                LicenseTier = "trial",
                CreatedAt = DateTime.Now
            };
            db.Factories.Add(factory);
            await db.SaveChangesAsync();
            logger.LogInformation("已创建工厂 {FactoryCode}", factory.FactoryCode);
        }

        var account = string.IsNullOrWhiteSpace(opt.BootstrapAdminAccount)
            ? "admin"
            : opt.BootstrapAdminAccount.Trim();
        var existing = await db.Users.FirstOrDefaultAsync(u =>
            u.FactoryId == factory.Id && u.Account == account);
        if (existing != null)
        {
            logger.LogInformation("管理员账号 {Account} 已存在，跳过（不重置密码）", account);
            return;
        }

        PasswordHelper.EnsureComplexity(opt.BootstrapAdminPassword, nameof(EnsureAsync));
        db.Users.Add(new SysUser
        {
            FactoryId = factory.Id,
            Account = account,
            Name = string.IsNullOrWhiteSpace(opt.BootstrapAdminName) ? "管理员" : opt.BootstrapAdminName.Trim(),
            Phone = "",
            Role = 1,
            Password = PasswordHelper.Hash(opt.BootstrapAdminPassword),
            Status = 1,
            CreatedAt = DateTime.Now
        });
        await db.SaveChangesAsync();
        logger.LogInformation("已创建管理员账号 {Account}，请通过安全渠道交付并要求修改密码", account);
    }
}
