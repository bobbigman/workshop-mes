using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

/// <summary>
/// 排产优先级评分（docs/36 · ①档）：PC 评分 + 导出 xlsx。
/// 交期分自动算；客户/金额/换型/齐套四维手填或中性兜底，金蝶对接后再换真查询。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ScheduleController : ControllerBase
{
    private readonly IScheduleScoreService _svc;
    public ScheduleController(IScheduleScoreService svc) { _svc = svc; }

    /// <summary>提交请求：手填四维分 + 权重（都可不传，用中性分/默认权重）。</summary>
    public class ScheduleRequest
    {
        public Dictionary<string, ScheduleDimScores>? ManualScores { get; set; }
        public ScheduleWeights? Weights { get; set; }
    }

    /// <summary>排产评分列表（未结束工单，按加权总分降序）。GET 简易入口。</summary>
    [HttpGet]
    public async Task<ApiResult<List<ScheduleRowDto>>> Query([FromQuery] string? weights)
    {
        var rows = await _svc.ScoreAsync(JwtHelper.GetFactoryId(User), null, ParseWeights(weights));
        return ApiResult<List<ScheduleRowDto>>.Ok(rows);
    }

    /// <summary>排产评分列表（POST，支持手填四维分 + 权重）。</summary>
    [HttpPost("query")]
    public async Task<ApiResult<List<ScheduleRowDto>>> QueryPost([FromBody] ScheduleRequest req)
    {
        var rows = await _svc.ScoreAsync(JwtHelper.GetFactoryId(User), req?.ManualScores, req?.Weights);
        return ApiResult<List<ScheduleRowDto>>.Ok(rows);
    }

    /// <summary>导出排产评分表（GET 简易入口，xlsx，对齐「排产示例.xlsx」）。</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? weights)
    {
        var w = ParseWeights(weights) ?? new ScheduleWeights();
        var rows = await _svc.ScoreAsync(JwtHelper.GetFactoryId(User), null, w);
        var bytes = _svc.ExportXlsx(rows, w);
        var fileName = $"排产优先级_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    /// <summary>导出排产评分表（POST，支持手填四维分 + 权重）。</summary>
    [HttpPost("export")]
    public async Task<IActionResult> ExportPost([FromBody] ScheduleRequest req)
    {
        var w = req?.Weights ?? new ScheduleWeights();
        var rows = await _svc.ScoreAsync(JwtHelper.GetFactoryId(User), req?.ManualScores, w);
        var bytes = _svc.ExportXlsx(rows, w);
        var fileName = $"排产优先级_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    private static ScheduleWeights? ParseWeights(string? weights)
    {
        if (string.IsNullOrWhiteSpace(weights)) return null;
        try
        {
            return JsonSerializer.Deserialize<ScheduleWeights>(weights,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception)
        {
            throw ThrowHelper.BizUser("排产权重配置格式不正确");
        }
    }
}
