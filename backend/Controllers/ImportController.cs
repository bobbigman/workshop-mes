using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ImportController : ControllerBase
{
    private readonly IImportService _svc;
    private readonly AppDbContext _db;
    private readonly ILicenseTierService _license;
    public ImportController(IImportService svc, AppDbContext db, ILicenseTierService license) { _svc = svc; _db = db; _license = license; }

    [HttpGet("template")]
    public async Task<IActionResult> DownloadTemplate([FromQuery] string entityType)
    {
        var fid = JwtHelper.GetFactoryId(User);
        if (entityType == "operationPriceList") await _license.EnsureFeatureAsync(fid, LicenseFeature.PieceWage);
        var bytes = entityType is "product" or "operationPriceList" ? await new AttributeImportService(_db).TemplateAsync(entityType, fid) : await _svc.DownloadTemplateAsync(entityType);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{entityType}_template.xlsx");
    }

    [HttpPost("upload")]
    [Authorize(Roles = "1")]
    public async Task<ApiResult<ImportResultDto>> Upload([FromQuery] string entityType, IFormFile file, [FromQuery] bool preview = false)
    {
        if (file == null || file.Length == 0)
            throw ThrowHelper.Biz(nameof(Upload), "上传的文件是空的。请确认选了正确的文件再上传，不要传空白文件。");
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var fid = JwtHelper.GetFactoryId(User);
        if (entityType is "operationPriceList" or "priceRule") await _license.EnsureFeatureAsync(fid, LicenseFeature.PieceWage);
        if (entityType is "product" or "operationPriceList") return await new AttributeImportService(_db).ImportAsync(entityType, ms.ToArray(), fid, preview);
        if (preview) throw ThrowHelper.BizUser("当前这种导入类型不支持先检查后导入。请直接执行导入，系统会在导入时自动核对数据。");
        return await _svc.ImportAsync(entityType, ms.ToArray(), fid);
    }
}
