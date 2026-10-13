using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WorkOrderController : ControllerBase
{
    private readonly IWorkOrderService _svc;
    public WorkOrderController(IWorkOrderService svc) { _svc = svc; }

    [HttpGet]
    public Task<ApiResult<PageResult<WorkOrderListDto>>> Query([FromQuery] WorkOrderQueryDto q) =>
        _svc.QueryAsync(q, JwtHelper.GetFactoryId(User));

    [HttpGet("status-counts")]
    public Task<ApiResult<WorkOrderStatusCountsDto>> StatusCounts() =>
        _svc.GetStatusCountsAsync(JwtHelper.GetFactoryId(User));

    [HttpGet("{id:long}")]
    public Task<ApiResult<WorkOrderDetailDto>> Get(long id) =>
        _svc.GetAsync(id, JwtHelper.GetFactoryId(User));

    [HttpGet("by-no")]
    public Task<ApiResult<WorkOrderDetailDto>> GetByNo([FromQuery] string orderNo) =>
        _svc.GetByOrderNoAsync(orderNo, JwtHelper.GetFactoryId(User), JwtHelper.GetUserId(User), JwtHelper.GetRole(User));

    [HttpPost]
    public Task<ApiResult<object?>> Create([FromBody] WorkOrderCreateDto dto) =>
        _svc.CreateAsync(dto, JwtHelper.GetUserId(User), JwtHelper.GetFactoryId(User));

    [HttpPut("{id:long}")]
    public Task<ApiResult<object?>> Update(long id, [FromBody] WorkOrderUpdateDto dto) =>
        _svc.UpdateAsync(id, dto, JwtHelper.GetFactoryId(User));

    [HttpPost("{id:long}/transition")]
    public Task<ApiResult<object?>> Transition(long id, [FromBody] TransitionDto dto) =>
        _svc.TransitionAsync(id, dto.Action, JwtHelper.GetFactoryId(User));

    [HttpPost("{id:long}/copy")]
    public Task<ApiResult<object?>> Copy(long id) =>
        _svc.CopyAsync(id, JwtHelper.GetFactoryId(User));

    [HttpDelete("{id:long}")]
    public Task<ApiResult<object?>> Delete(long id) => _svc.DeleteAsync(id);

    [HttpGet("{id:long}/qr")]
    public Task<ApiResult<string>> GetQr(long id) =>
        _svc.GetQrContentAsync(id, JwtHelper.GetFactoryId(User));
}

public class TransitionDto
{
    public string Action { get; set; } = "";
}
