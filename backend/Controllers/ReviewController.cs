using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireLicenseFeature(LicenseFeature.ReportReview)]
public class ReviewController : ControllerBase
{
    private readonly IReviewService _reviewService;
    public ReviewController(IReviewService reviewService) { _reviewService = reviewService; }

    [HttpGet("pending")]
    public Task<ApiResult<PageResult<ReviewListDto>>> Pending([FromQuery] ReviewQueryDto query) =>
        _reviewService.PendingAsync(query, JwtHelper.GetFactoryId(User), JwtHelper.GetUserId(User));

    [HttpGet("order-options")]
    public Task<ApiResult<List<ReviewOrderOptionDto>>> OrderOptions() =>
        _reviewService.OrderOptionsAsync(JwtHelper.GetFactoryId(User), JwtHelper.GetUserId(User));

    [HttpPost("{id:long}/approve")]
    public Task<ApiResult<object?>> Approve(long id) =>
        _reviewService.ApproveAsync(id, JwtHelper.GetFactoryId(User), JwtHelper.GetUserId(User));

    [HttpPost("{id:long}/reject")]
    public Task<ApiResult<object?>> Reject(long id, [FromBody] RejectDto dto) =>
        _reviewService.RejectAsync(id, dto, JwtHelper.GetFactoryId(User), JwtHelper.GetUserId(User));

    [HttpPost("batch-approve")]
    public Task<ApiResult<object?>> BatchApprove([FromBody] BatchApproveDto dto) =>
        _reviewService.BatchApproveAsync(dto, JwtHelper.GetFactoryId(User), JwtHelper.GetUserId(User));
}
