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
    Task<ApiResult<object?>> ForbidAsync(long id, long factoryId, long userId);
    Task<ApiResult<object?>> UnforbidAsync(long id, long factoryId, long userId);
    Task<ApiResult<object?>> AuditAsync(long id, long factoryId, long userId);
    Task<ApiResult<object?>> UnauditAsync(long id, long factoryId, long userId);
}

public class ProductQueryDto
{
    public string? Keyword { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    /// <summary>1启用 0禁用；与 FilterField=status 二选一。都未指定时默认只返回启用。</summary>
    public byte? Status { get; set; }
    /// <summary>true=含禁用（仅当未指定 Status / status 过滤时生效）。建单下拉勿传。</summary>
    public bool IncludeDisabled { get; set; }
    /// <summary>true=含未审核。建单下拉勿传；默认只返回已审核。</summary>
    public bool IncludeUnaudited { get; set; }

    /// <summary>
    /// 快捷过滤：keyword | code | name | supplier | isRiskFocus | status | auditStatus。
    /// 有值时优先于 Keyword。
    /// </summary>
    public string? FilterField { get; set; }
    public string? FilterOp { get; set; }
    public string? FilterValue { get; set; }
}

public class ProductCreateDto
{
    public Dictionary<long, string?>? CustomValues { get; set; }
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
    /// <summary>1启用 0禁用</summary>
    public byte Status { get; set; }
    /// <summary>0未审核 1已审核</summary>
    public byte AuditStatus { get; set; }
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
    public Dictionary<long, string?>? CustomValues { get; set; }
    public bool IsRiskFocus { get; set; }
    /// <summary>1启用 0禁用</summary>
    public byte Status { get; set; }
    /// <summary>0未审核 1已审核</summary>
    public byte AuditStatus { get; set; }
    public long? AuditedBy { get; set; }
    public string? AuditedByName { get; set; }
    public DateTime? AuditedAt { get; set; }
    public long? ForbidBy { get; set; }
    public string? ForbidByName { get; set; }
    public DateTime? ForbidAt { get; set; }
    public long? UnforbidBy { get; set; }
    public string? UnforbidByName { get; set; }
    public DateTime? UnforbidAt { get; set; }
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

    private sealed class ProductQueryJoin
    {
        public BaseProduct Product { get; set; } = null!;
        public string? UnitName { get; set; }
        public string? RoutingName { get; set; }
    }

    private static IQueryable<ProductQueryJoin> ApplyQuickFilter(IQueryable<ProductQueryJoin> q, ProductQueryDto query)
    {
        var field = query.FilterField!.Trim();
        var value = string.IsNullOrWhiteSpace(query.FilterValue) ? "" : query.FilterValue.Trim();
        if (value.Length > 256)
            throw ThrowHelper.Biz(nameof(QueryAsync), "筛选值太长，最多 256 个字符，请缩短后重试。");

        var op = string.IsNullOrWhiteSpace(query.FilterOp)
            ? ""
            : query.FilterOp.Trim().ToLowerInvariant();

        if (value.Length == 0)
            return q;

        if (string.IsNullOrEmpty(op))
            op = field is "isRiskFocus" or "status" ? "eq" : "contains";
        if (op is not ("eq" or "contains" or "gt" or "lt"))
            throw ThrowHelper.Biz(nameof(QueryAsync), "比较方式只支持：等于、包含、大于、小于。");

        switch (field)
        {
            case "keyword":
                if (op is "gt" or "lt")
                    throw ThrowHelper.Biz(nameof(QueryAsync), "「编号/名称」只支持等于或包含。");
                if (op == "eq")
                    return q.Where(x => x.Product.Code == value || x.Product.Name == value);
                return q.Where(x => x.Product.Code.Contains(value) || x.Product.Name.Contains(value));

            case "code":
                if (op is "gt" or "lt")
                    throw ThrowHelper.Biz(nameof(QueryAsync), "产品编号只支持等于或包含。");
                return op == "eq"
                    ? q.Where(x => x.Product.Code == value)
                    : q.Where(x => x.Product.Code.Contains(value));

            case "name":
                if (op is "gt" or "lt")
                    throw ThrowHelper.Biz(nameof(QueryAsync), "产品名称只支持等于或包含。");
                return op == "eq"
                    ? q.Where(x => x.Product.Name == value)
                    : q.Where(x => x.Product.Name.Contains(value));

            case "supplier":
                if (op is "gt" or "lt")
                    throw ThrowHelper.Biz(nameof(QueryAsync), "供应商只支持等于或包含。");
                return op == "eq"
                    ? q.Where(x => x.Product.Supplier == value)
                    : q.Where(x => x.Product.Supplier != null && x.Product.Supplier.Contains(value));

            case "isRiskFocus":
                if (op != "eq")
                    throw ThrowHelper.Biz(nameof(QueryAsync), "重点产品只支持「等于」。");
                var focus = value is "1" or "true" or "True" or "是" or "重点";
                var notFocus = value is "0" or "false" or "False" or "否";
                if (!focus && !notFocus)
                    throw ThrowHelper.Biz(nameof(QueryAsync), "重点产品请选择「是」或「否」。");
                return q.Where(x => x.Product.IsRiskFocus == focus);

            case "status":
                if (op != "eq")
                    throw ThrowHelper.Biz(nameof(QueryAsync), "启用状态只支持「等于」。");
                if (value is "all" or "*" or "全部")
                    return q;
                if (!byte.TryParse(value, out var st) || st > 1)
                    throw ThrowHelper.Biz(nameof(QueryAsync), "启用状态请选择「启用」或「禁用」。");
                return q.Where(x => x.Product.Status == st);

            case "auditStatus":
                if (op != "eq")
                    throw ThrowHelper.Biz(nameof(QueryAsync), "审核状态只支持「等于」。");
                if (value is "all" or "*" or "全部")
                    return q;
                if (!byte.TryParse(value, out var auditSt) || auditSt > 1)
                    throw ThrowHelper.Biz(nameof(QueryAsync), "审核状态请选择「未审核」或「已审核」。");
                return q.Where(x => x.Product.AuditStatus == auditSt);

            default:
                throw ThrowHelper.Biz(nameof(QueryAsync), $"不支持的筛选字段「{field}」。请重新选择。");
        }
    }

    public async Task<ApiResult<PageResult<ProductListDto>>> QueryAsync(ProductQueryDto query, long factoryId)
    {
        var q = from p in _db.Products.AsNoTracking()
                where p.FactoryId == factoryId
                join u in _db.Units.AsNoTracking() on p.UnitId equals u.Id into ug
                from u in ug.DefaultIfEmpty()
                join r in _db.Routings.AsNoTracking() on p.RoutingId equals r.Id into rg
                from r in rg.DefaultIfEmpty()
                select new ProductQueryJoin
                {
                    Product = p,
                    UnitName = u == null ? null : u.Name,
                    RoutingName = r == null ? null : r.Name
                };

        var fieldKey = string.IsNullOrWhiteSpace(query.FilterField) ? "" : query.FilterField.Trim();
        var filterIsStatus = fieldKey.Equals("status", StringComparison.OrdinalIgnoreCase);
        var filterIsAudit = fieldKey.Equals("auditStatus", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(query.FilterField))
            q = ApplyQuickFilter(q, query);
        else if (!string.IsNullOrWhiteSpace(query.Keyword))
            q = q.Where(x => x.Product.Code.Contains(query.Keyword) || x.Product.Name.Contains(query.Keyword));

        // 建单下拉默认：启用 + 已审核；产品定义页传 Include* 放宽
        if (!filterIsStatus)
        {
            if (query.Status.HasValue)
                q = q.Where(x => x.Product.Status == query.Status.Value);
            else if (!query.IncludeDisabled)
                q = q.Where(x => x.Product.Status == 1);
        }
        if (!filterIsAudit && !query.IncludeUnaudited)
            q = q.Where(x => x.Product.AuditStatus == 1);

        var total = await q.CountAsync();
        var list = await q.OrderByDescending(x => x.Product.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new ProductListDto
            {
                Id = x.Product.Id, Code = x.Product.Code, Name = x.Product.Name, UnitId = x.Product.UnitId,
                UnitName = x.UnitName, RoutingName = x.RoutingName,
                Supplier = x.Product.Supplier, Price = x.Product.Price, IsRiskFocus = x.Product.IsRiskFocus,
                Status = x.Product.Status, AuditStatus = x.Product.AuditStatus
            }).ToListAsync();

        return ApiResult<PageResult<ProductListDto>>.Ok(new PageResult<ProductListDto> { List = list, Total = total });
    }

    public async Task<ApiResult<ProductDetailDto>> GetAsync(long id, long factoryId)
    {
        var p = await _db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(GetAsync), "指定的产品在系统中不存在。请到「产品定义」核对产品编号是否正确，或新建该产品。");
        var userIds = new[] { p.AuditedBy, p.ForbidBy, p.UnforbidBy }.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var names = userIds.Count == 0
            ? new Dictionary<long, string>()
            : await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name);
        return ApiResult<ProductDetailDto>.Ok(new ProductDetailDto
        {
            CustomValues = await new ProductAttributeService(_db).GetAsync(p.Id, factoryId),
            Id = p.Id,
            Code = p.Code,
            Name = p.Name,
            UnitId = p.UnitId,
            RoutingId = p.RoutingId,
            Supplier = p.Supplier,
            Price = p.Price,
            IsRiskFocus = p.IsRiskFocus,
            Status = p.Status,
            AuditStatus = p.AuditStatus,
            AuditedBy = p.AuditedBy,
            AuditedByName = p.AuditedBy.HasValue ? names.GetValueOrDefault(p.AuditedBy.Value) : null,
            AuditedAt = p.AuditedAt,
            ForbidBy = p.ForbidBy,
            ForbidByName = p.ForbidBy.HasValue ? names.GetValueOrDefault(p.ForbidBy.Value) : null,
            ForbidAt = p.ForbidAt,
            UnforbidBy = p.UnforbidBy,
            UnforbidByName = p.UnforbidBy.HasValue ? names.GetValueOrDefault(p.UnforbidBy.Value) : null,
            UnforbidAt = p.UnforbidAt
        });
    }

    public async Task<ApiResult<object?>> CreateAsync(ProductCreateDto dto, long factoryId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        if (dto.CopyFromId.HasValue && !await _db.Products.AnyAsync(p => p.Id == dto.CopyFromId && p.FactoryId == factoryId))
            throw ThrowHelper.Biz(nameof(CreateAsync), "复制产品时指定的来源产品不存在或不属于本厂。请到「产品定义」确认来源产品编号正确且归属本厂。");
        await ValidateReferencesAsync(dto, factoryId);
        if (string.IsNullOrWhiteSpace(dto.Code) || string.IsNullOrWhiteSpace(dto.Name))
            throw ThrowHelper.Biz(nameof(CreateAsync), "产品编号和名称都必须填写。请填写完整的产品编号和名称后再保存。");

        // 三条硬规则：产品编号唯一
        if (await _db.Products.AnyAsync(p => p.FactoryId == factoryId && p.Code == dto.Code.Trim()))
            throw ThrowHelper.Biz(nameof(CreateAsync), "产品编号全局唯一，名称可以重复。请换一个未被占用的编号，可到「产品定义」搜索确认是否已存在。");

        var routingId = dto.RoutingId;
        if (dto.NewRouting != null)
        {
            var result = await new RoutingService(_db).CreateAsync(dto.NewRouting, factoryId);
            routingId = (long)result.Data!;
        }
        var product = new BaseProduct
        {
            FactoryId = factoryId,
            Code = dto.Code.Trim(),
            Name = dto.Name.Trim(),
            UnitId = dto.UnitId,
            RoutingId = routingId,
            Supplier = dto.Supplier,
            Price = dto.Price,
            IsRiskFocus = dto.IsRiskFocus ?? false,
            Status = 1,
            AuditStatus = 0,
            CreatedAt = DateTime.Now
        };
        _db.Products.Add(product);
        await _db.SaveChangesAsync();
        var attributes = dto.CustomValues;
        if (attributes == null && dto.CopyFromId.HasValue) attributes = await new ProductAttributeService(_db).GetAsync(dto.CopyFromId.Value, factoryId);
        await new ProductAttributeService(_db).SaveAsync(product.Id, factoryId, attributes);
        await tx.CommitAsync();
        return ApiResult<object?>.OkMsg();
    }

    private async Task ValidateReferencesAsync(ProductCreateDto dto, long factoryId)
    {
        if (dto.UnitId.HasValue && !await _db.Units.AnyAsync(x => x.Id == dto.UnitId && x.FactoryId == factoryId))
            throw ThrowHelper.Biz(nameof(ValidateReferencesAsync), "产品关联的单位不存在或不属于本厂。请到「单位管理」确认单位已创建且归属本厂，再回产品编辑页重新选择。");
        if (dto.RoutingId.HasValue)
        {
            var routing = await _db.Routings.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == dto.RoutingId && x.FactoryId == factoryId)
                ?? throw ThrowHelper.Biz(nameof(ValidateReferencesAsync), "产品关联的工艺路线不存在或不属于本厂。请到「工艺路线」确认路线已创建且归属本厂，再回产品编辑页重新选择。");
            if (routing.Status != 1 || routing.AuditStatus != 1)
                throw ThrowHelper.Biz(nameof(ValidateReferencesAsync), "所选工艺路线未审核或已禁用。请到「工艺路线」审核并启用后再选。");
        }
    }

    public async Task<ApiResult<object?>> UpdateAsync(long id, ProductCreateDto dto, long factoryId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        await ValidateReferencesAsync(dto, factoryId);
        if (dto.NewRouting != null)
            throw ThrowHelper.Biz(nameof(UpdateAsync), "「复制工艺并修改」只能在新建产品时使用，编辑已有产品不支持此操作。如需修改已有产品工艺，请直接编辑其工艺路线。");
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id && p.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(UpdateAsync), "产品不存在");
        if (product.AuditStatus == 1)
            throw ThrowHelper.Biz(nameof(UpdateAsync), "已审核的产品不能直接改。请先点「反审核」，改完再审核。");

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
        await new ProductAttributeService(_db).SaveAsync(product.Id, factoryId, dto.CustomValues);
        await tx.CommitAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> DeleteAsync(long id)
    {
        var msg = await DeleteGuard.CheckAsync(_db, "Product", id);
        if (msg != null) throw ThrowHelper.Biz(nameof(DeleteAsync), msg);

        var product = await _db.Products.FindAsync(id)
            ?? throw ThrowHelper.Biz(nameof(DeleteAsync), "产品不存在");
        if (product.AuditStatus == 1)
            throw ThrowHelper.Biz(nameof(DeleteAsync), "已审核的产品不能直接删。请先反审核，确认无工单引用后再删。");
        _db.Products.Remove(product);
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> ForbidAsync(long id, long factoryId, long userId)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id && p.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(ForbidAsync), "产品不存在");
        if (product.Status == 0)
            throw ThrowHelper.Biz(nameof(ForbidAsync), "该产品已经是禁用状态，无需再禁用。");
        product.Status = 0;
        product.ForbidBy = userId;
        product.ForbidAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return new ApiResult<object?> { Code = 0, Msg = "已禁用；新建工单时不能再选这个产品，历史工单不受影响。" };
    }

    public async Task<ApiResult<object?>> UnforbidAsync(long id, long factoryId, long userId)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id && p.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(UnforbidAsync), "产品不存在");
        if (product.Status == 1)
            throw ThrowHelper.Biz(nameof(UnforbidAsync), "该产品已经是启用状态，无需再反禁用。");
        product.Status = 1;
        product.UnforbidBy = userId;
        product.UnforbidAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return new ApiResult<object?> { Code = 0, Msg = "已反禁用；若已审核则可再次用于新建工单。" };
    }

    public async Task<ApiResult<object?>> AuditAsync(long id, long factoryId, long userId)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id && p.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(AuditAsync), "产品不存在");
        if (product.AuditStatus == 1)
            throw ThrowHelper.Biz(nameof(AuditAsync), "该产品已经审核过了，无需再审。");
        if (product.RoutingId.HasValue)
        {
            var routing = await _db.Routings.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == product.RoutingId.Value && r.FactoryId == factoryId);
            if (routing != null && (routing.Status != 1 || routing.AuditStatus != 1))
                throw ThrowHelper.Biz(nameof(AuditAsync), "产品挂的工艺路线未审核或已禁用，请先到「工艺路线」处理后再审产品。");
        }
        product.AuditStatus = 1;
        product.AuditedBy = userId;
        product.AuditedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return new ApiResult<object?> { Code = 0, Msg = "已审核；启用状态下可用于新建工单。" };
    }

    public async Task<ApiResult<object?>> UnauditAsync(long id, long factoryId, long userId)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id && p.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(UnauditAsync), "产品不存在");
        if (product.AuditStatus != 1)
            throw ThrowHelper.Biz(nameof(UnauditAsync), "当前不需要反审核。");
        product.AuditStatus = 0;
        // 保留 audited_by/at 作为末次审核痕迹；再审时覆盖
        _ = userId;
        await _db.SaveChangesAsync();
        return new ApiResult<object?> { Code = 0, Msg = "已反审核；请改完后再点审核，未审核不能新建工单选用。" };
    }
}
