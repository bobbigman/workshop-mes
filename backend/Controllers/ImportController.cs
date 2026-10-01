using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ImportController : ControllerBase
{
    private readonly IImportService _svc;
    public ImportController(IImportService svc) { _svc = svc; }

    [HttpGet("template")]
    public async Task<IActionResult> DownloadTemplate([FromQuery] string entityType)
    {
        var bytes = await _svc.DownloadTemplateAsync(entityType);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{entityType}_template.xlsx");
    }

    [HttpPost("upload")]
    public async Task<ApiResult<ImportResultDto>> Upload([FromQuery] string entityType, IFormFile file)
    {
        if (file == null || file.Length == 0)
            throw ThrowHelper.Biz(nameof(Upload), "上传文件为空");
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        return await _svc.ImportAsync(entityType, ms.ToArray(), JwtHelper.GetFactoryId(User));
    }
}
