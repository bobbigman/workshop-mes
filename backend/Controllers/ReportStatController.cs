using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReportStatController : ControllerBase
{
    private readonly IReportStatService _svc;
    public ReportStatController(IReportStatService svc) { _svc = svc; }

    [HttpGet("production")]
    public Task<ApiResult<ProductionReportDto>> Production([FromQuery] ProductionReportQueryDto q) =>
        _svc.ProductionAsync(q, JwtHelper.GetFactoryId(User));

    /// <summary>色码汇总（docs/102）；按 product.code + color + spec 分组。不动 production。</summary>
    [HttpGet("sku-summary")]
    public Task<ApiResult<SkuSummaryDto>> SkuSummary([FromQuery] SkuSummaryQueryDto q) =>
        _svc.SkuSummaryAsync(q, JwtHelper.GetFactoryId(User));

    /// <summary>不良品报表三表（docs/207）：分布/汇总/明细。口径待复核+已通过，不含退回。</summary>
    [HttpGet("defect")]
    public Task<ApiResult<DefectReportDto>> Defect([FromQuery] DefectReportQueryDto q) =>
        _svc.DefectAsync(q, JwtHelper.GetFactoryId(User));

    [HttpGet("board")]
    public Task<ApiResult<BoardDto>> Board() =>
        _svc.BoardAsync(JwtHelper.GetFactoryId(User));
}
