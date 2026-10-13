using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;
using ahu.MicrosoftMes.Services.Kingdee;

namespace ahu.MicrosoftMes.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireLicenseFeature(LicenseFeature.KingdeeMcp)]
public class QuoteSuggestController : ControllerBase
{
    private readonly IQuoteSuggestService _svc;
    public QuoteSuggestController(IQuoteSuggestService svc) => _svc = svc;

    [HttpGet("products")]
    public Task<ApiResult<PageResult<QuoteSuggestProductOptionDto>>> Products(
        [FromQuery] string? keyword, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        _svc.SearchProductsAsync(keyword, page, pageSize, JwtHelper.GetFactoryId(User));

    [HttpGet("kingdee/material")]
    public Task<ApiResult<KingdeeMaterialDto?>> KingdeeMaterial([FromQuery] string code) =>
        _svc.ProbeMaterialAsync(code);

    [HttpPost("preview")]
    public Task<ApiResult<QuoteSuggestPreviewDto>> Preview([FromBody] QuoteSuggestPreviewRequest req) =>
        _svc.PreviewAsync(req, JwtHelper.GetFactoryId(User));

    [HttpPost("export")]
    public async Task<IActionResult> Export([FromBody] QuoteSuggestPreviewRequest req)
    {
        var (content, fileName) = await _svc.ExportAsync(req, JwtHelper.GetFactoryId(User));
        return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }
}
