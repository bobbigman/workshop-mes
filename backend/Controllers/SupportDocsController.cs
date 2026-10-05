using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

/// <summary>客服知识目录只读接口（docs/113：PC 常见问题页）。任意登录用户。</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SupportDocsController : ControllerBase
{
    private readonly SupportDocumentService _docs;

    public SupportDocsController(SupportDocumentService docs) { _docs = docs; }

    /// <summary>
    /// 系统能力50问：##=分组、###=问题+正文。
    /// md 缺失/解析失败返回空数组（code=0），不报错不编造。
    /// </summary>
    [HttpGet("faq")]
    public ApiResult<List<FaqGroupDto>> Faq()
    {
        var groups = _docs.GetFaq();
        return ApiResult<List<FaqGroupDto>>.Ok(groups);
    }
}
