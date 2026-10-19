using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

[ApiController, Authorize, Route("api/work-session")]
public class WorkSessionController(WorkSessionService service, IReportService reports) : ControllerBase
{
    public sealed class StartDto
    {
        public long WorkOrderId { get; set; }
        public long OperationId { get; set; }
    }

    [HttpPost("start")]
    public async Task<ApiResult<object>> Start(StartDto dto) =>
        ApiResult<object>.Ok(await service.StartAsync(dto.WorkOrderId, dto.OperationId, JwtHelper.GetUserId(User)));

    [HttpGet("current")]
    public async Task<ApiResult<object>> Current(long workOrderId, long operationId) =>
        ApiResult<object>.Ok(await service.CurrentAsync(JwtHelper.GetUserId(User), workOrderId, operationId));

    [HttpGet("pending")]
    public async Task<ApiResult<object>> Pending() =>
        ApiResult<object>.Ok(await service.PendingAsync(JwtHelper.GetUserId(User)));

    [HttpPost("{id:long}/report")]
    public Task<ApiResult<object?>> Report(long id, ReportDto dto) =>
        service.ReportPendingAsync(id, dto, JwtHelper.GetUserId(User), reports);
}
