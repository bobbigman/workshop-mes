using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public interface IProductService
{
    Task<ApiResult<PageResult<ProductListDto>>> QueryAsync(ProductQueryDto query, long factoryId);
    Task<ApiResult<ProductDetailDto>> GetAsync(long id, long factoryId);
    Task<ApiResult<object?>> CreateAsync(ProductCreateDto dto, long factoryId);
    Task<ApiResult<object?>> UpdateAsync(long id, ProductCreateDto dto, long factoryId);
    Task<ApiResult<object?>> DeleteAsync(long id);
}

public class ProductQueryDto
{
    public string? Keyword { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class ProductCreateDto
{
    public bool? IsRiskFocus { get; set; }
    public long? CopyFromId { get; set; }
    public RoutingCreateDto? NewRouting { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public long? UnitId { get; set; }
    public long? RoutingId { get; set; }
    public string? Supplier { get; set; }
    public decimal? Price { get; set; }
}

public class ProductListDto
{
    public long? UnitId { get; set; }
    public bool IsRiskFocus { get; set; }
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? UnitName { get; set; }
    public string? RoutingName { get; set; }
    public string? Supplier { get; set; }
    public decimal? Price { get; set; }
}

public class ProductDetailDto
{
    public bool IsRiskFocus { get; set; }
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public long? UnitId { get; set; }
    public long? RoutingId { get; set; }
    public string? Supplier { get; set; }
    public decimal? Price { get; set; }
}

public class ProductService : IProductService
{
    private readonly AppDbContext _db;
    public ProductService(AppDbContext db) { _db = db; }

    public async Task<ApiResult<PageResult<ProductListDto>>> QueryAsync(ProductQueryDto query, long factoryId)
    {
        var q = from p in _db.Products.AsNoTracking()
                where p.FactoryId == factoryId
                join u in _db.Units.AsNoTracking() on p.UnitId equals u.Id into ug
                from u in ug.DefaultIfEmpty()
                join r in _db.Routings.AsNoTracking() on p.RoutingId equals r.Id into rg
                from r in rg.DefaultIfEmpty()
                select new { p, UnitName = u == null ? null : u.Name, RoutingName = r == null ? null : r.Name };

        if (!string.IsNullOrWhiteSpace(query.Keyword))
            q = q.Where(x => x.p.Code.Contains(query.Keyword) || x.p.Name.Contains(query.Keyword));

        var total = await q.CountAsync();
        var list = await q.OrderByDescending(x => x.p.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new ProductListDto
            {
                Id = x.p.Id, Code = x.p.Code, Name = x.p.Name, UnitId = x.p.UnitId,
                UnitName = x.UnitName, RoutingName = x.RoutingName,
                Supplier = x.p.Supplier, Price = x.p.Price, IsRiskFocus = x.p.IsRiskFocus
            }).ToListAsync();

        return ApiResult<PageResult<ProductListDto>>.Ok(new PageResult<ProductListDto> { List = list, Total = total });
    }

    public async Task<ApiResult<ProductDetailDto>> GetAsync(long id, long factoryId)
    {
        var p = await _db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(GetAsync), "产品不存在");
        return ApiResult<ProductDetailDto>.Ok(new ProductDetailDto
        {
            Id = p.Id,
            Code = p.Code,
            Name = p.Name,
            UnitId = p.UnitId,
            RoutingId = p.RoutingId,
            Supplier = p.Supplier,
            Price = p.Price,
            IsRiskFocus = p.IsRiskFocus
        });
    }

    public async Task<ApiResult<object?>> CreateAsync(ProductCreateDto dto, long factoryId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        if (dto.CopyFromId.HasValue && !await _db.Products.AnyAsync(p => p.Id == dto.CopyFromId && p.FactoryId == factoryId))
            throw ThrowHelper.Biz(nameof(CreateAsync), "复制来源产品不存在或不属于本厂");
        await ValidateReferencesAsync(dto, factoryId);
        if (string.IsNullOrWhiteSpace(dto.Code) || string.IsNullOrWhiteSpace(dto.Name))
            throw ThrowHelper.Biz(nameof(CreateAsync), "产品编号和名称不能为空");

        // 三条硬规则：产品编号唯一
        if (await _db.Products.AnyAsync(p => p.FactoryId == factoryId && p.Code == dto.Code.Trim()))
            throw ThrowHelper.Biz(nameof(CreateAsync), "产品编号不可重复");

        var routingId = dto.RoutingId;
        if (dto.NewRouting != null)
        {
            var result = await new RoutingService(_db).CreateAsync(dto.NewRouting, factoryId);
            routingId = (long)result.Data!;
        }
        _db.Products.Add(new BaseProduct
        {
            FactoryId = factoryId,
            Code = dto.Code.Trim(),
            Name = dto.Name.Trim(),
            UnitId = dto.UnitId,
            RoutingId = routingId,
            Supplier = dto.Supplier,
            Price = dto.Price,
            IsRiskFocus = dto.IsRiskFocus ?? false,
            CreatedAt = DateTime.Now
        });
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return ApiResult<object?>.OkMsg();
    }

    private async Task ValidateReferencesAsync(ProductCreateDto dto, long factoryId)
    {
        if (dto.UnitId.HasValue && !await _db.Units.AnyAsync(x => x.Id == dto.UnitId && x.FactoryId == factoryId))
            throw ThrowHelper.Biz(nameof(ValidateReferencesAsync), "单位不存在或不属于本厂");
        if (dto.RoutingId.HasValue && !await _db.Routings.AnyAsync(x => x.Id == dto.RoutingId && x.FactoryId == factoryId))
            throw ThrowHelper.Biz(nameof(ValidateReferencesAsync), "工艺路线不存在或不属于本厂");
    }

    public async Task<ApiResult<object?>> UpdateAsync(long id, ProductCreateDto dto, long factoryId)
    {
        await ValidateReferencesAsync(dto, factoryId);
        if (dto.NewRouting != null)
            throw ThrowHelper.Biz(nameof(UpdateAsync), "复制工艺并修改仅用于新建产品");
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id && p.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(UpdateAsync), "产品不存在");

        if (await _db.Products.AnyAsync(p => p.FactoryId == factoryId && p.Code == dto.Code.Trim() && p.Id != id))
            throw ThrowHelper.Biz(nameof(UpdateAsync), "产品编号不可重复");

        product.Code = dto.Code.Trim();
        product.Name = dto.Name.Trim();
        product.UnitId = dto.UnitId;
        product.RoutingId = dto.RoutingId;
        product.Supplier = dto.Supplier;
        product.Price = dto.Price;
        if (dto.IsRiskFocus.HasValue) product.IsRiskFocus = dto.IsRiskFocus.Value;
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> DeleteAsync(long id)
    {
        var msg = await DeleteGuard.CheckAsync(_db, "Product", id);
        if (msg != null) throw ThrowHelper.Biz(nameof(DeleteAsync), msg);

        var product = await _db.Products.FindAsync(id)
            ?? throw ThrowHelper.Biz(nameof(DeleteAsync), "产品不存在");
        _db.Products.Remove(product);
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }
}
