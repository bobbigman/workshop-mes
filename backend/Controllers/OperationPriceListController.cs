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
public class OperationPriceListController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ApiResult<object>> Query(string? keyword, long? operationId, byte? status, DateTime? date, int page = 1, int pageSize = 20)
    {
        var fid = JwtHelper.GetFactoryId(User);
        var q = db.OperationPriceLists.AsNoTracking().Where(t => t.FactoryId == fid);
        if (!string.IsNullOrWhiteSpace(keyword)) q = q.Where(t => t.Code.Contains(keyword) || t.Name.Contains(keyword));
        if (operationId.HasValue) q = q.Where(t => t.OperationId == operationId);
        if (status.HasValue) q = q.Where(t => t.Status == status);
        if (date.HasValue) q = q.Where(t => t.EffectiveFrom <= date.Value.Date && (t.EffectiveTo == null || t.EffectiveTo >= date.Value.Date));
        var total = await q.CountAsync();
        var tables = await q.OrderBy(t => t.Code).ThenBy(t => t.Id).Skip((Math.Max(1, page)-1)*Math.Clamp(pageSize,1,200)).Take(Math.Clamp(pageSize,1,200)).ToListAsync();
        return ApiResult<object>.Ok(new { Total = total, List = tables.Select(OperationPriceListService.View) });
    }
    [HttpGet("{id:long}")]
    public async Task<ApiResult<object>> Get(long id) => ApiResult<object>.Ok(OperationPriceListService.View(await new OperationPriceListService(db).GetAsync(id, JwtHelper.GetFactoryId(User))));
    [HttpPost]
    public async Task<ApiResult<long>> Create(PriceListInput input) => ApiResult<long>.Ok(await new OperationPriceListService(db).SaveAsync(null, input, JwtHelper.GetFactoryId(User)));
    [HttpPut("{id:long}")]
    public async Task<ApiResult<long>> Update(long id, PriceListInput input) => ApiResult<long>.Ok(await new OperationPriceListService(db).SaveAsync(id, input, JwtHelper.GetFactoryId(User)));
    [HttpGet("mode/{productId:long}")]
    public async Task<ApiResult<object>> Modes(long productId) => ApiResult<object>.Ok(await db.ProductOperationPricings.AsNoTracking().Where(m => m.FactoryId == JwtHelper.GetFactoryId(User) && m.ProductId == productId).Select(m => new { m.OperationId, m.AttributePricing }).ToListAsync());
    [HttpPut("mode/{productId:long}/{operationId:long}")]
    public async Task<ApiResult<object?>> Mode(long productId, long operationId, [FromBody] bool enabled)
    {
        await new OperationPriceListService(db).SetModeAsync(productId, operationId, JwtHelper.GetFactoryId(User), enabled);
        return ApiResult<object?>.OkMsg();
    }
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
    [HttpGet("legacy-conversion")]
    public async Task<ApiResult<object>> LegacyConversion()
    {
        var tables = await db.OperationPriceLists.AsNoTracking().Where(t => t.FactoryId == JwtHelper.GetFactoryId(User)).ToListAsync();
        return ApiResult<object>.Ok(tables.Where(t => OperationPriceListService.Details(t).SchemaVersion != 2).Select(t => new { t.FactoryId, t.Id, t.Code, t.OperationId, Rows = OperationPriceListService.Details(t).Rows.Select(r => new { r.RowId, r.ProductId, r.Source }) }));
    }
    public record PreviewInput(long ProductId, long OperationId, DateTime? Date, Dictionary<long, string?>? OperationAttributes = null, long? WorkOrderId = null, string? ExpectedVersion = null);
    [HttpPost("preview")]
    public async Task<ApiResult<ProcessingQuote>> Preview(PreviewInput input) => ApiResult<ProcessingQuote>.Ok(await new OperationPriceListService(db).PreviewAsync(input.ProductId, input.OperationId, JwtHelper.GetFactoryId(User), input.Date, input.OperationAttributes, input.WorkOrderId, input.ExpectedVersion));
}
