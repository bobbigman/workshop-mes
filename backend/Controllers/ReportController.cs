using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReportController : ControllerBase
{
    private readonly IReportService _reportService;
    public ReportController(IReportService reportService) { _reportService = reportService; }

    [HttpPost]
    public Task<ApiResult<object?>> Submit([FromBody] ReportDto dto) =>
        _reportService.SubmitAsync(dto, JwtHelper.GetUserId(User));

    [HttpPost("batch")]
    public Task<ApiResult<BatchReportResultDto>> Batch([FromBody] BatchReportDto dto) =>
        _reportService.BatchReportAsync(dto, JwtHelper.GetUserId(User));

    [HttpPut("{id:long}")]
    public Task<ApiResult<object?>> Update(long id, [FromBody] ReportDto dto) =>
        _reportService.UpdateAsync(id, dto, JwtHelper.GetUserId(User));

    [HttpGet]
    public Task<ApiResult<PageResult<ReportListDto>>> Query([FromQuery] ReportQueryDto query) =>
        _reportService.QueryAsync(query, JwtHelper.GetFactoryId(User));

    /// <summary>本人报工只读列表（docs/94）</summary>
    [HttpGet("mine")]
    public Task<ApiResult<PageResult<ReportListDto>>> Mine([FromQuery] ReportMineQueryDto query) =>
        _reportService.QueryMineAsync(query, JwtHelper.GetFactoryId(User), JwtHelper.GetUserId(User));

    /// <summary>本人今日计件汇总（docs/206）：已通过+待复核</summary>
    [HttpGet("mine/today-summary")]
    public Task<ApiResult<ReportTodaySummaryDto>> TodaySummary() =>
        _reportService.TodaySummaryAsync(JwtHelper.GetFactoryId(User), JwtHelper.GetUserId(User));

    /// <summary>报工修改日志（docs/205）</summary>
    [HttpGet("{id:long}/changes")]
    public Task<ApiResult<List<ReportChangeLogDto>>> Changes(long id) =>
        _reportService.GetChangeLogsAsync(id, JwtHelper.GetFactoryId(User), JwtHelper.GetUserId(User));

    /// <summary>工序报权部门内可选人员（多人分摊/代报选人）</summary>
    [HttpGet("candidates/{operationId:long}")]
    public Task<ApiResult<List<ReportCandidateDto>>> Candidates(long operationId) =>
        _reportService.GetCandidatesAsync(operationId, JwtHelper.GetFactoryId(User), JwtHelper.GetUserId(User));

    [HttpGet("defects/{operationId}")]
    public Task<ApiResult<List<DefectItemDto>>> Defects(long operationId) =>
        _reportService.GetDefectsByOperationAsync(operationId);
}
