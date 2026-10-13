using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireLicenseFeature(LicenseFeature.PieceWage)]
public class PriceRuleController : ControllerBase
{
    private readonly IPriceRuleService _svc;
    public PriceRuleController(IPriceRuleService svc) { _svc = svc; }

    [HttpGet]
    public Task<ApiResult<PageResult<PriceRuleListDto>>> Query([FromQuery] PriceRuleQueryDto query) =>
        _svc.QueryAsync(query, JwtHelper.GetFactoryId(User));

    [HttpPost]
    public Task<ApiResult<object?>> Create([FromBody] PriceRuleDto dto) =>
        _svc.CreateAsync(dto, JwtHelper.GetFactoryId(User));

    [HttpPut("{id:long}")]
    public Task<ApiResult<object?>> Update(long id, [FromBody] PriceRuleDto dto) =>
        _svc.UpdateAsync(id, dto, JwtHelper.GetFactoryId(User));

    [HttpDelete("{id:long}")]
    public Task<ApiResult<object?>> Delete(long id) =>
        _svc.DeleteAsync(id, JwtHelper.GetFactoryId(User));
}
