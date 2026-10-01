using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public interface IPriceRuleService
{
    Task<ApiResult<PageResult<PriceRuleListDto>>> QueryAsync(PriceRuleQueryDto query, long factoryId);
    Task<ApiResult<object?>> CreateAsync(PriceRuleDto dto, long factoryId);
    Task<ApiResult<object?>> UpdateAsync(long id, PriceRuleDto dto, long factoryId);
    Task<ApiResult<object?>> DeleteAsync(long id, long factoryId);
}

public class PriceRuleQueryDto
{
    public long? ProductId { get; set; }
    public long? OperationId { get; set; }
    public long? DepartmentId { get; set; }
    public long? UserId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class PriceRuleDto
{
    public long? ProductId { get; set; }
    public long? OperationId { get; set; }
    public long? DepartmentId { get; set; }
    public long? UserId { get; set; }
    public byte PriceType { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal? DeductPrice { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public int Priority { get; set; }
}

public class PriceRuleListDto
{
    public long Id { get; set; }
    public long? ProductId { get; set; }
    public string? ProductName { get; set; }
    public long? OperationId { get; set; }
    public string? OperationName { get; set; }
    public long? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public long? UserId { get; set; }
    public string? UserName { get; set; }
    public byte PriceType { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal? DeductPrice { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public int Priority { get; set; }
}

public class PriceRuleService : IPriceRuleService
{
    private readonly AppDbContext _db;
    public PriceRuleService(AppDbContext db) { _db = db; }

    public async Task<ApiResult<PageResult<PriceRuleListDto>>> QueryAsync(PriceRuleQueryDto query, long factoryId)
    {
        var q = _db.PriceRules.AsNoTracking().Where(r => r.FactoryId == factoryId);
        if (query.ProductId.HasValue)
            q = q.Where(r => r.ProductId == query.ProductId);
        if (query.OperationId.HasValue)
            q = q.Where(r => r.OperationId == query.OperationId);
        if (query.DepartmentId.HasValue)
            q = q.Where(r => r.DepartmentId == query.DepartmentId);
        if (query.UserId.HasValue)
            q = q.Where(r => r.UserId == query.UserId);

        var total = await q.CountAsync();
        var rows = await (
            from r in q
            join p in _db.Products.AsNoTracking() on r.ProductId equals p.Id into pg
            from p in pg.DefaultIfEmpty()
            join op in _db.Operations.AsNoTracking() on r.OperationId equals op.Id into og
            from op in og.DefaultIfEmpty()
            orderby p.Code, p.Name, op.Name, r.Id
            select r
        ).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync();

        var productNames = await _db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name);
        var opNames = await _db.Operations.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name);
        var deptNames = await _db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name);
        var userNames = await _db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name);

        var list = rows.Select(r => new PriceRuleListDto
        {
            Id = r.Id,
            ProductId = r.ProductId,
            ProductName = r.ProductId == null ? null : productNames.GetValueOrDefault(r.ProductId.Value),
            OperationId = r.OperationId,
            OperationName = r.OperationId == null ? null : opNames.GetValueOrDefault(r.OperationId.Value),
            DepartmentId = r.DepartmentId,
            DepartmentName = r.DepartmentId == null ? null : deptNames.GetValueOrDefault(r.DepartmentId.Value),
            UserId = r.UserId,
            UserName = r.UserId == null ? null : userNames.GetValueOrDefault(r.UserId.Value),
            PriceType = r.PriceType,
            UnitPrice = r.UnitPrice,
            DeductPrice = r.DeductPrice,
            EffectiveFrom = r.EffectiveFrom,
            EffectiveTo = r.EffectiveTo,
            Priority = r.Priority
        }).ToList();

        return ApiResult<PageResult<PriceRuleListDto>>.Ok(new PageResult<PriceRuleListDto> { List = list, Total = total });
    }

    public async Task<ApiResult<object?>> CreateAsync(PriceRuleDto dto, long factoryId)
    {
        Validate(dto, nameof(CreateAsync));
        _db.PriceRules.Add(new BasePriceRule
        {
            FactoryId = factoryId,
            ProductId = dto.ProductId,
            OperationId = dto.OperationId,
            DepartmentId = dto.DepartmentId,
            UserId = dto.UserId,
            PriceType = dto.PriceType,
            UnitPrice = dto.UnitPrice,
            DeductPrice = dto.DeductPrice,
            EffectiveFrom = dto.EffectiveFrom,
            EffectiveTo = dto.EffectiveTo,
            Priority = dto.Priority,
            CreatedAt = DateTime.Now
        });
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> UpdateAsync(long id, PriceRuleDto dto, long factoryId)
    {
        Validate(dto, nameof(UpdateAsync));
        var row = await _db.PriceRules.FirstOrDefaultAsync(r => r.Id == id && r.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(UpdateAsync), "工价规则不存在");
        row.ProductId = dto.ProductId;
        row.OperationId = dto.OperationId;
        row.DepartmentId = dto.DepartmentId;
        row.UserId = dto.UserId;
        row.PriceType = dto.PriceType;
        row.UnitPrice = dto.UnitPrice;
        row.DeductPrice = dto.DeductPrice;
        row.EffectiveFrom = dto.EffectiveFrom;
        row.EffectiveTo = dto.EffectiveTo;
        row.Priority = dto.Priority;
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> DeleteAsync(long id, long factoryId)
    {
        var row = await _db.PriceRules.FirstOrDefaultAsync(r => r.Id == id && r.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(DeleteAsync), "工价规则不存在");
        _db.PriceRules.Remove(row);
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    private static void Validate(PriceRuleDto dto, string loc)
    {
        if (dto.PriceType is not (1 or 2 or 3))
            throw ThrowHelper.BizUser("请选择正确的计价方式");
        if (dto.UnitPrice <= 0)
            throw ThrowHelper.BizUser("单价必须大于0");
        if (dto.DeductPrice.HasValue && dto.DeductPrice.Value < 0)
            throw ThrowHelper.Biz(loc, "扣款单价不能为负");
        if (dto.EffectiveFrom == default)
            throw ThrowHelper.Biz(loc, "生效时间必填");
        if (dto.EffectiveTo.HasValue && dto.EffectiveTo.Value <= dto.EffectiveFrom)
            throw ThrowHelper.Biz(loc, "失效时间须晚于生效时间");
    }
}
