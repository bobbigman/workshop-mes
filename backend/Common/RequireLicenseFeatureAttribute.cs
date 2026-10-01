using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Common;

/// <summary>控制器/动作按工厂档位拦截（叠加在 JWT role 之上）。</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireLicenseFeatureAttribute : TypeFilterAttribute
{
    public RequireLicenseFeatureAttribute(LicenseFeature feature)
        : base(typeof(LicenseFeatureFilter))
    {
        Arguments = new object[] { feature };
    }
}

public sealed class LicenseFeatureFilter : IAsyncActionFilter
{
    private readonly LicenseFeature _feature;
    private readonly ILicenseTierService _license;

    public LicenseFeatureFilter(LicenseFeature feature, ILicenseTierService license)
    {
        _feature = feature;
        _license = license;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var factoryId = JwtHelper.GetFactoryId(context.HttpContext.User);
        await _license.EnsureFeatureAsync(factoryId, _feature);
        await next();
    }
}
