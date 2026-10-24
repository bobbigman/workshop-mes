using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

[ApiController]
[Route("api/operation-pricing")]
[Authorize(Roles = "1")]
public class OperationPricingController(AppDbContext db) : ControllerBase
{
    [HttpGet("migration-status")]
    public async Task<ApiResult<bool>> Status() => ApiResult<bool>.Ok(await db.ProductOperationPricings.AnyAsync(m => m.FactoryId == JwtHelper.GetFactoryId(User)));
    [HttpGet("{id:long}")]
    public async Task<ApiResult<ProcessingConfig?>> Get(long id) => ApiResult<ProcessingConfig?>.Ok(await new OperationPricingService(db).GetAsync(id, JwtHelper.GetFactoryId(User)));
    [HttpPut("{id:long}")]
    [RequireLicenseFeature(LicenseFeature.PieceWage)]
    public async Task<ApiResult<object?>> Save(long id, ProcessingConfig config)
    {
        var fid = JwtHelper.GetFactoryId(User);
        if (await db.ProductOperationPricings.AnyAsync(m => m.FactoryId == fid)) throw ThrowHelper.BizUser("本账套已调整为集中价目表，请到工序价目表维护，旧加工选价已停止新增");
        await new OperationPricingService(db).SaveAsync(id, fid, config);
        return ApiResult<object?>.OkMsg();
    }
    public record RetireInput(string ExpectedVersion);
    [HttpPost("{id:long}/retire")]
    [RequireLicenseFeature(LicenseFeature.PieceWage)]
    public async Task<ApiResult<object?>> Retire(long id, RetireInput input)
    {
        var fid = JwtHelper.GetFactoryId(User);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var operation = await db.Operations.SingleOrDefaultAsync(o => o.Id == id && o.FactoryId == fid) ?? throw ThrowHelper.BizUser("工序不存在或不属于本厂");
        if (operation.PricingConfig == null) return ApiResult<object?>.OkMsg();
        if (OperationPricingService.Read<ProcessingConfig>(operation.PricingConfig).Version != input.ExpectedVersion) throw ThrowHelper.BizUser("旧配置已变化，请重新盘点");
        operation.PricingConfig = null; // 不触碰任务/报工快照，老工单继续原金额。
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return ApiResult<object?>.OkMsg();
    }
    [HttpGet("product/{id:long}")]
    public async Task<ApiResult<List<object>>> Product(long id) => ApiResult<List<object>>.Ok(await new OperationPricingService(db).ProductAsync(id, JwtHelper.GetFactoryId(User)));
    [HttpPost("{id:long}/preview")]
    public async Task<ApiResult<ProcessingQuote>> Preview(long id, [FromQuery] long productId, ProcessingChoice choice) =>
        ApiResult<ProcessingQuote>.Ok(await new OperationPricingService(db).PreviewAsync(id, productId, JwtHelper.GetFactoryId(User), choice));
}
