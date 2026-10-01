using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProductController : ControllerBase
{
    private readonly IProductService _svc;
    public ProductController(IProductService svc) { _svc = svc; }

    [HttpGet]
    public Task<ApiResult<PageResult<ProductListDto>>> Query([FromQuery] ProductQueryDto q) =>
        _svc.QueryAsync(q, JwtHelper.GetFactoryId(User));

    [HttpGet("{id:long}")]
    public Task<ApiResult<ProductDetailDto>> Get(long id) =>
        _svc.GetAsync(id, JwtHelper.GetFactoryId(User));

    [HttpPost]
    public Task<ApiResult<object?>> Create([FromBody] ProductCreateDto dto) =>
        _svc.CreateAsync(dto, JwtHelper.GetFactoryId(User));

    [HttpPut("{id}")]
    public Task<ApiResult<object?>> Update(long id, [FromBody] ProductCreateDto dto) =>
        _svc.UpdateAsync(id, dto, JwtHelper.GetFactoryId(User));

    [HttpDelete("{id}")]
    public Task<ApiResult<object?>> Delete(long id) => _svc.DeleteAsync(id);
}
