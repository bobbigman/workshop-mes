using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

[ApiController]
[Route("api/operation-price-list")]
[Authorize(Roles = "1")]
[RequireLicenseFeature(LicenseFeature.PieceWage)]
public class OperationPriceListController(AppDbContext db, PriceChangeDraftStore drafts) : ControllerBase
{
    [HttpGet]
    public async Task<ApiResult<object>> Query(string? keyword, long? operationId, byte? status, DateTime? date, int page = 1, int pageSize = 20)
    {
        var fid = JwtHelper.GetFactoryId(User);
        var q = db.OperationPriceLists.AsNoTracking().Where(t => t.FactoryId == fid);
        if (!string.IsNullOrWhiteSpace(keyword)) q = q.Where(t => t.Code.Contains(keyword) || t.Name.Contains(keyword));
        if (operationId.HasValue) q = q.Where(t => t.OperationId == operationId);
        if (status.HasValue) q = q.Where(t => t.Status == status);
        if (date.HasValue) q = q.Where(t => db.OperationPriceRevisions.Any(r => r.FactoryId == fid && r.PriceListId == t.Id && !r.Superseded && r.EffectiveFrom <= date.Value.Date && (r.EffectiveTo == null || r.EffectiveTo >= date.Value.Date))
            || (!db.OperationPriceRevisions.Any(r => r.FactoryId == fid && r.PriceListId == t.Id) && t.EffectiveFrom <= date.Value.Date && (t.EffectiveTo == null || t.EffectiveTo >= date.Value.Date)));
        var total = await q.CountAsync();
        var tables = await q.OrderBy(t => t.Code).ThenBy(t => t.Id).Skip((Math.Max(1, page)-1)*Math.Clamp(pageSize,1,200)).Take(Math.Clamp(pageSize,1,200)).ToListAsync();
        return ApiResult<object>.Ok(new { Total = total, List = tables.Select(OperationPriceListService.View) });
    }
    [HttpGet("{id:long}")]
    public async Task<ApiResult<object>> Get(long id) => ApiResult<object>.Ok(OperationPriceListService.View(await new OperationPriceListService(db).GetAsync(id, JwtHelper.GetFactoryId(User))));
    [HttpGet("product/{productId:long}")]
    public async Task<ApiResult<List<object>>> Product(long productId) => ApiResult<List<object>>.Ok(await new OperationPricingService(db).ProductAsync(productId, JwtHelper.GetFactoryId(User)));
    [HttpPost]
    public async Task<ApiResult<long>> Create(PriceListInput input) => ApiResult<long>.Ok(await new OperationPriceListService(db).SaveAsync(null, input, JwtHelper.GetFactoryId(User)));
    [HttpPut("{id:long}")]
    public async Task<ApiResult<long>> Update(long id, PriceListInput input) => ApiResult<long>.Ok(await new OperationPriceListService(db).SaveAsync(id, input, JwtHelper.GetFactoryId(User)));
    public record StatusInput(byte Status, string ExpectedVersion);
    [HttpPost("{id:long}/status")]
    public async Task<ApiResult<object?>> Status(long id, StatusInput input)
    {
        await new OperationPriceListService(db).SetStatusAsync(id, input.Status, input.ExpectedVersion, JwtHelper.GetFactoryId(User));
        return ApiResult<object?>.OkMsg();
    }
    [HttpDelete("{id:long}")]
    public async Task<ApiResult<object?>> Delete(long id, string expectedVersion)
    {
        await new OperationPriceListService(db).DeleteAsync(id, expectedVersion, JwtHelper.GetFactoryId(User));
        return ApiResult<object?>.OkMsg();
    }
    public record PreviewInput(long ProductId, long OperationId, DateTime? Date, Dictionary<long, string?>? OperationAttributes = null, long? WorkOrderId = null, string? ExpectedVersion = null);
    [HttpPost("preview")]
    public async Task<ApiResult<ProcessingQuote>> Preview(PreviewInput input) => ApiResult<ProcessingQuote>.Ok(await new OperationPriceListService(db).PreviewAsync(input.ProductId, input.OperationId, JwtHelper.GetFactoryId(User), input.Date, input.OperationAttributes, input.WorkOrderId, input.ExpectedVersion));
    [HttpPost("{id:long}/price-change/preview")]
    public async Task<ApiResult<object>> PreviewChange(long id,PriceChangeInput input) => ApiResult<object>.Ok(await new PriceChangeService(db,drafts).PreviewAsync(id,input,JwtHelper.GetFactoryId(User),JwtHelper.GetUserId(User)));
    [HttpPost("{id:long}/price-change/commit")]
    public async Task<ApiResult<object>> CommitChange(long id,PriceChangeCommit input) => ApiResult<object>.Ok(await new PriceChangeService(db,drafts).CommitAsync(id,input,JwtHelper.GetFactoryId(User),JwtHelper.GetUserId(User)));
    [HttpGet("price-change/{id:long}")]
    public async Task<ApiResult<object>> GetChange(long id) => ApiResult<object>.Ok(await new PriceChangeService(db,drafts).GetAsync(id,JwtHelper.GetFactoryId(User),JwtHelper.GetUserId(User)));

    [HttpGet("{id:long}/price-changes")]
    public async Task<ApiResult<object>> Changes(long id,int page=1)
    {
        var fid=JwtHelper.GetFactoryId(User);
        await new OperationPriceListService(db).GetAsync(id,fid);
        var q=db.OperationPriceChanges.AsNoTracking().Where(x=>x.FactoryId==fid && x.PriceListId==id);
        return ApiResult<object>.Ok(new { total=await q.CountAsync(), list=await q.OrderByDescending(x=>x.Id).Skip((Math.Max(1,page)-1)*20).Take(20)
            .Select(x=>new {x.Id,x.ChangedBy,x.Reason,x.EffectiveFrom,x.RepriceCurrentMonth,x.CreatedAt}).ToListAsync() });
    }

}
