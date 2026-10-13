using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

/// <summary>知识库文件（作业指导书/图纸）管理 + 预览（docs/66）。</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class KnowledgeFileController : ControllerBase
{
    private readonly IKnowledgeFileService _svc;
    public KnowledgeFileController(IKnowledgeFileService svc) { _svc = svc; }

    /// <summary>PC 管理：某产品/工序已挂文件清单（管理员）。</summary>
    [HttpGet]
    public async Task<ApiResult<List<KnowledgeFileDto>>> List([FromQuery] string refType, [FromQuery] long refId) =>
        ApiResult<List<KnowledgeFileDto>>.Ok(await _svc.ListAsync(refType, refId, JwtHelper.GetFactoryId(User)));

    /// <summary>H5 工人：按工单列出作业指导书/图纸清单。</summary>
    [HttpGet("by-order/{orderId:long}")]
    public async Task<ApiResult<List<KnowledgeFileDto>>> ByOrder(long orderId) =>
        ApiResult<List<KnowledgeFileDto>>.Ok(await _svc.ListByOrderAsync(orderId, JwtHelper.GetFactoryId(User)));

    /// <summary>返回原始文件流（预览/下载）。</summary>
    [HttpGet("{fileId:long}/raw")]
    public async Task<IActionResult> Raw(long fileId)
    {
        var (content, contentType, fileName) = await _svc.GetRawAsync(fileId, JwtHelper.GetFactoryId(User));
        return File(content, contentType, fileName);
    }

    /// <summary>上传文件并登记（管理员）。</summary>
    [HttpPost]
    public async Task<ApiResult<KnowledgeFileDto>> Upload(
        [FromForm] string refType, [FromForm] long refId, [FromForm] string? version, IFormFile file)
    {
        if (file == null || file.Length == 0)
            throw ThrowHelper.Biz(nameof(Upload), "上传文件为空");
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        return ApiResult<KnowledgeFileDto>.Ok(await _svc.UploadAsync(
            refType, refId, version, file.FileName, ms.ToArray(),
            JwtHelper.GetFactoryId(User), JwtHelper.GetUserId(User)));
    }

    /// <summary>删除登记记录（管理员；磁盘文件保留，因可能被多处引用）。</summary>
    [HttpDelete("{fileId:long}")]
    public async Task<ApiResult<object?>> Delete(long fileId)
    {
        await _svc.DeleteAsync(fileId, JwtHelper.GetFactoryId(User));
        return ApiResult<object?>.OkMsg();
    }
}
