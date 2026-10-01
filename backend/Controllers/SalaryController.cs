using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireLicenseFeature(LicenseFeature.PieceWage)]
public class SalaryController : ControllerBase
{
    private readonly ISalaryService _svc;
    public SalaryController(ISalaryService svc) { _svc = svc; }

    [HttpPost("generate")]
    public Task<ApiResult<object?>> Generate([FromBody] SalaryGenerateDto dto) =>
        _svc.GenerateAsync(dto, JwtHelper.GetFactoryId(User));

    /// <summary>工人本人工资预估（docs/103）；RoleGuard 放行 role=2/3。</summary>
    [HttpGet("mine")]
    public Task<ApiResult<MyWageDto>> Mine([FromQuery] MyWageQueryDto q) =>
        _svc.MineAsync(q, JwtHelper.GetFactoryId(User), JwtHelper.GetUserId(User));

    /// <summary>工资按色码汇总（docs/103）；仅管理侧。</summary>
    [HttpGet("sku-summary")]
    public Task<ApiResult<SalarySkuSummaryDto>> SkuSummary([FromQuery] SalarySkuSummaryQueryDto q) =>
        _svc.SkuSummaryAsync(q, JwtHelper.GetFactoryId(User));

    [HttpGet("sku-summary/export")]
    public async Task<IActionResult> ExportSkuSummary([FromQuery] SalarySkuSummaryQueryDto q)
    {
        var (content, fileName) = await _svc.ExportSkuSummaryCsvAsync(q, JwtHelper.GetFactoryId(User));
        return File(content, "text/csv; charset=utf-8", fileName);
    }

    [HttpGet]
    public Task<ApiResult<PageResult<SalaryListDto>>> Query([FromQuery] SalaryQueryDto query) =>
        _svc.QueryAsync(query, JwtHelper.GetFactoryId(User));

    [HttpGet("{id:long}")]
    public Task<ApiResult<SalaryDetailDto>> Get(long id) =>
        _svc.GetAsync(id, JwtHelper.GetFactoryId(User));

    [HttpPut("{id:long}/components")]
    public Task<ApiResult<object?>> UpdateComponents(long id, [FromBody] SalaryComponentsDto dto) =>
        _svc.UpdateComponentsAsync(id, dto, JwtHelper.GetFactoryId(User), JwtHelper.GetUserId(User));

    [HttpPost("{id:long}/confirm")]
    public Task<ApiResult<object?>> Confirm(long id) =>
        _svc.ConfirmAsync(id, JwtHelper.GetFactoryId(User));

    /// <summary>草稿按当前工价原地重算计件（docs/133）；保留手工项。</summary>
    [HttpPost("{id:long}/recalc")]
    public Task<ApiResult<object?>> Recalc(long id) =>
        _svc.RecalcDraftAsync(id, JwtHelper.GetFactoryId(User), JwtHelper.GetUserId(User));

    /// <summary>撤回已确认工资单（仅管理员；RoleGuard 亦拦 role=2/3）。</summary>
    [HttpPost("{id:long}/revoke")]
    [Authorize(Roles = "1")]
    public Task<ApiResult<object?>> Revoke(long id) =>
        _svc.RevokeAsync(id, JwtHelper.GetFactoryId(User), JwtHelper.GetUserId(User));

    [HttpDelete("{id:long}")]
    public Task<ApiResult<object?>> Delete(long id) =>
        _svc.DeleteDraftAsync(id, JwtHelper.GetFactoryId(User));

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] byte periodType, [FromQuery] string periodValue)
    {
        var (content, fileName) = await _svc.ExportCsvAsync(periodType, periodValue, JwtHelper.GetFactoryId(User));
        return File(content, "text/csv; charset=utf-8", fileName);
    }
}
