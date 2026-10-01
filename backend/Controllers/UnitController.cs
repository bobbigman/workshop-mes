using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UnitController : ControllerBase
{
    private readonly IUnitService _svc;
    public UnitController(IUnitService svc) { _svc = svc; }

    [HttpGet]
    public Task<ApiResult<List<UnitDto>>> Query() =>
        _svc.QueryAsync(JwtHelper.GetFactoryId(User));

    [HttpPost]
    public Task<ApiResult<object?>> Create([FromBody] UnitDto dto) =>
        _svc.CreateAsync(dto, JwtHelper.GetFactoryId(User));

    [HttpPut("{id}")]
    public Task<ApiResult<object?>> Update(long id, [FromBody] UnitDto dto) =>
        _svc.UpdateAsync(id, dto, JwtHelper.GetFactoryId(User));

    [HttpDelete("{id}")]
    public Task<ApiResult<object?>> Delete(long id) => _svc.DeleteAsync(id);
}
