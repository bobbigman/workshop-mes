using ahu.MicrosoftMes.Common;

namespace ahu.MicrosoftMes.Services;

public interface ILicenseTierService
{
    Task<LicenseTier> GetTierAsync(long factoryId);
    Task EnsureFeatureAsync(long factoryId, LicenseFeature feature);
    Task<bool> CanUseAsync(long factoryId, LicenseFeature feature);
}

public class LicenseTierService : ILicenseTierService
{
    private readonly ILicenseCloudService _cloud;
    private readonly ILogger<LicenseTierService> _logger;

    public LicenseTierService(ILicenseCloudService cloud, ILogger<LicenseTierService> logger)
    {
        _cloud = cloud;
        _logger = logger;
    }

    public Task<LicenseTier> GetTierAsync(long factoryId) =>
        _cloud.GetEffectiveTierAsync(factoryId);

    public async Task<bool> CanUseAsync(long factoryId, LicenseFeature feature)
    {
        var tier = await GetTierAsync(factoryId);
        return LicenseTierPolicy.CanUse(tier, feature);
    }

    public async Task EnsureFeatureAsync(long factoryId, LicenseFeature feature)
    {
        var tier = await GetTierAsync(factoryId);
        if (LicenseTierPolicy.CanUse(tier, feature))
            return;

        var need = LicenseTierPolicy.MinTier(feature);
        _logger.LogWarning(
            "档位拒绝 FactoryId={FactoryId} Tier={Tier} Feature={Feature} Need={Need}",
            factoryId, LicenseTierPolicy.ToDb(tier), feature, LicenseTierPolicy.ToDb(need));

        throw ThrowHelper.BizUser(
            $"当前为{LicenseTierPolicy.DisplayName(tier)}，{LicenseTierPolicy.FeatureDisplayName(feature)}需{LicenseTierPolicy.DisplayName(need)}及以上");
    }
}
