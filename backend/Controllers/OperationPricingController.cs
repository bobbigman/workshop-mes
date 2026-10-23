using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

[ApiController]
[Route("api/operation-pricing")]
[Authorize(Roles = "1")]
public class OperationPricingController(AppDbContext db) : ControllerBase
{
    [HttpGet("{id:long}")]
    public async Task<ApiResult<ProcessingConfig?>> Get(long id) => ApiResult<ProcessingConfig?>.Ok(await new OperationPricingService(db).GetAsync(id, JwtHelper.GetFactoryId(User)));
    [HttpPut("{id:long}")]
    [RequireLicenseFeature(LicenseFeature.PieceWage)]
    public async Task<ApiResult<object?>> Save(long id, ProcessingConfig config)
    {
        await new OperationPricingService(db).SaveAsync(id, JwtHelper.GetFactoryId(User), config);
        return ApiResult<object?>.OkMsg();
    }
    [HttpGet("product/{id:long}")]
    public async Task<ApiResult<List<object>>> Product(long id) => ApiResult<List<object>>.Ok(await new OperationPricingService(db).ProductAsync(id, JwtHelper.GetFactoryId(User)));
    [HttpPost("{id:long}/preview")]
    public async Task<ApiResult<ProcessingQuote>> Preview(long id, [FromQuery] long productId, ProcessingChoice choice) =>
        ApiResult<ProcessingQuote>.Ok(await new OperationPricingService(db).PreviewAsync(id, productId, JwtHelper.GetFactoryId(User), choice));
}
