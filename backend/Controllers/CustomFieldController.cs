using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CustomFieldController : ControllerBase
{
    private readonly ICustomFieldService _svc;
    public CustomFieldController(ICustomFieldService svc) { _svc = svc; }

    [HttpGet]
    public Task<ApiResult<List<CustomFieldDto>>> Query([FromQuery] string target) =>
        _svc.QueryByTargetAsync(target, JwtHelper.GetFactoryId(User));

    [HttpPost]
    public Task<ApiResult<object?>> Create([FromBody] CustomFieldCreateDto dto) =>
        _svc.CreateAsync(dto, JwtHelper.GetFactoryId(User));

    [HttpPut("{id}")]
    public Task<ApiResult<object?>> Update(long id, [FromBody] CustomFieldCreateDto dto) =>
        _svc.UpdateAsync(id, dto, JwtHelper.GetFactoryId(User));

    [HttpDelete("{id}")]
    [Authorize(Roles = "1")]
    public Task<ApiResult<object?>> Delete(long id) =>
        _svc.DeleteAsync(id, JwtHelper.GetFactoryId(User));
}
