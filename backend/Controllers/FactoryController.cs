using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

/// <summary>账套（工厂）接口：登录选厂列表 + 管理端新建账套。禁止返回连接串/密钥。</summary>
[ApiController]
[Route("api/factories")]
public class FactoryController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IFactoryService _factoryService;
    public FactoryController(AppDbContext db, IFactoryService factoryService)
    {
        _db = db;
        _factoryService = factoryService;
    }

    /// <summary>登录页选厂用的可用账套列表；仅 code + name，绝不返回连接串/密钥。</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ApiResult<List<FactoryOptionDto>>> List()
    {
        var list = await _db.Factories
            .OrderBy(f => f.Id)
            .Select(f => new FactoryOptionDto { FactoryCode = f.FactoryCode, FactoryName = f.FactoryName })
            .ToListAsync();
        return ApiResult<List<FactoryOptionDto>>.Ok(list);
    }

    /// <summary>管理端（管理员）新建账套并灌一套演示数据（docs/52）。</summary>
    [HttpPost]
    [Authorize]
    public Task<ApiResult<FactoryOptionDto>> Create([FromBody] FactoryCreateDto dto) =>
        _factoryService.CreateAsync(dto);
}
