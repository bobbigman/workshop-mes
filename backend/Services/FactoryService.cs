using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public interface IFactoryService
{
    Task<ApiResult<FactoryOptionDto>> CreateAsync(FactoryCreateDto dto);
}

public class FactoryOptionDto
{
    public string FactoryCode { get; set; } = "";
    public string FactoryName { get; set; } = "";
}

public class FactoryCreateDto
{
    public string FactoryCode { get; set; } = "";
    public string FactoryName { get; set; } = "";
}

public class FactoryService : IFactoryService
{
    private readonly AppDbContext _db;
    private readonly DeploymentOptions _deployment;
    private readonly TrialCleanupOptions _trialCleanup;

    public FactoryService(
        AppDbContext db,
        IOptions<DeploymentOptions> deployment,
        IOptions<TrialCleanupOptions> trialCleanup)
    {
        _db = db;
        _deployment = deployment.Value;
        _trialCleanup = trialCleanup.Value;
    }

    /// <summary>新建账套（工厂）并灌一套演示数据 + 管理员（docs/52，先 PC 管理动作）。</summary>
    public async Task<ApiResult<FactoryOptionDto>> CreateAsync(FactoryCreateDto dto)
    {
        var code = (dto.FactoryCode ?? "").Trim();
        var name = (dto.FactoryName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(code))
            throw ThrowHelper.BizUser("账套代码不能为空");
        if (string.IsNullOrWhiteSpace(name))
            throw ThrowHelper.BizUser("工厂名称不能为空");
        if (code.Length > 64)
            throw ThrowHelper.BizUser("账套代码过长");
        if (name.Length > 128)
            throw ThrowHelper.BizUser("工厂名称过长");

        if (await _db.Factories.AnyAsync(f => f.FactoryCode == code))
            throw ThrowHelper.BizUser("账套代码已存在，请换一个");

        var factory = await SeedData.SeedFactoryAsync(_db, code, name);

        // docs/90：SaaS 管理端新建 = 体验账套，写到期；私有版不写
        if (!_deployment.Private)
        {
            _trialCleanup.Validate();
            factory.TrialExpiresAtUtc = DateTime.UtcNow.AddDays(_trialCleanup.DurationDays);
            factory.TrialCleanedAtUtc = null;
            await _db.SaveChangesAsync();
        }

        return ApiResult<FactoryOptionDto>.Ok(new FactoryOptionDto
        {
            FactoryCode = factory.FactoryCode,
            FactoryName = factory.FactoryName
        });
    }
}
