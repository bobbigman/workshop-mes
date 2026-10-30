using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public interface IRoutingService
{
    Task<ApiResult<PageResult<RoutingListDto>>> QueryAsync(RoutingQueryDto query, long factoryId);
    Task<ApiResult<RoutingDetailDto>> GetAsync(long id, long factoryId);
    Task<ApiResult<object?>> CreateAsync(RoutingCreateDto dto, long factoryId);
    Task<ApiResult<object?>> UpdateAsync(long id, RoutingCreateDto dto, long factoryId);
    Task<ApiResult<object?>> DeleteAsync(long id);
}

public class RoutingQueryDto
{
    public string? Keyword { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class RoutingStepDto
{
    public long OperationId { get; set; }
    public int Seq { get; set; }
}

public class RoutingCreateDto
{
    public long? CopyFromId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public List<RoutingStepDto> Steps { get; set; } = new();
}

public class RoutingListDto
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string StepNames { get; set; } = "";
}

public class RoutingDetailDto
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public List<RoutingStepDto> Steps { get; set; } = new();
}

public class RoutingService : IRoutingService
{
    private readonly AppDbContext _db;
    public RoutingService(AppDbContext db) { _db = db; }

    public async Task<ApiResult<PageResult<RoutingListDto>>> QueryAsync(RoutingQueryDto query, long factoryId)
    {
        var q = _db.Routings.AsNoTracking().Where(r => r.FactoryId == factoryId);
        if (!string.IsNullOrWhiteSpace(query.Keyword))
            q = q.Where(r => r.Code.Contains(query.Keyword) || r.Name.Contains(query.Keyword));

        var total = await q.CountAsync();
        var routings = await q.OrderByDescending(r => r.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync();

        var rids = routings.Select(r => r.Id).ToList();
        var steps = await _db.RoutingSteps.AsNoTracking()
            .Where(s => rids.Contains(s.RoutingId)).OrderBy(s => s.Seq).ToListAsync();
        var ops = await _db.Operations.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name);

        var list = routings.Select(r => new RoutingListDto
        {
            Id = r.Id,
            Code = r.Code,
            Name = r.Name,
            StepNames = string.Join(" → ", steps.Where(s => s.RoutingId == r.Id)
                .Select(s => ops.GetValueOrDefault(s.OperationId, "")))
        }).ToList();

        return ApiResult<PageResult<RoutingListDto>>.Ok(new PageResult<RoutingListDto> { List = list, Total = total });
    }

    public async Task<ApiResult<RoutingDetailDto>> GetAsync(long id, long factoryId)
    {
        var r = await _db.Routings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(GetAsync), "指定的工艺路线在系统中不存在。请到「工艺路线」列表核对编号是否正确。");
        var steps = await _db.RoutingSteps.AsNoTracking()
            .Where(s => s.RoutingId == id).OrderBy(s => s.Seq)
            .Select(s => new RoutingStepDto { OperationId = s.OperationId, Seq = s.Seq }).ToListAsync();
        return ApiResult<RoutingDetailDto>.Ok(new RoutingDetailDto
        {
            Id = r.Id,
            Code = r.Code,
            Name = r.Name,
            Steps = steps
        });
    }

    public async Task<ApiResult<object?>> CreateAsync(RoutingCreateDto dto, long factoryId)
    {
        await using var ownedTx = _db.Database.CurrentTransaction == null ? await _db.Database.BeginTransactionAsync() : null;
        if (dto.CopyFromId.HasValue && !await _db.Routings.AnyAsync(r => r.Id == dto.CopyFromId && r.FactoryId == factoryId))
            throw ThrowHelper.Biz(nameof(CreateAsync), "复制工艺路线时指定的来源路线不存在或不属于本厂。请到「工艺路线」确认来源路线编号正确且归属本厂。");
        if (string.IsNullOrWhiteSpace(dto.Code) || string.IsNullOrWhiteSpace(dto.Name))
            throw ThrowHelper.Biz(nameof(CreateAsync), "工艺路线的编号和名称都必须填写。请填写完整的路线编号和名称后再保存。");
        if (dto.Steps == null || dto.Steps.Count == 0)
            throw ThrowHelper.Biz(nameof(CreateAsync), "工艺路线至少要包含一道工序。请在路线中添加至少一个工序步骤后再保存。");
        await ValidateStepsAsync(dto.Steps, factoryId);

        var routing = new BaseRouting
        {
            FactoryId = factoryId,
            Code = dto.Code.Trim(),
            Name = dto.Name.Trim(),
            CreatedAt = DateTime.Now
        };
        _db.Routings.Add(routing);
        await _db.SaveChangesAsync();
        await SaveSteps(routing.Id, dto.Steps);
        if (ownedTx != null) await ownedTx.CommitAsync();
        return ApiResult<object?>.Ok(routing.Id);
    }

    private async Task ValidateStepsAsync(List<RoutingStepDto> steps, long factoryId)
    {
        var ids = steps.Select(s => s.OperationId).Distinct().ToList();
        if (ids.Count != steps.Count)
            throw ThrowHelper.Biz(nameof(ValidateStepsAsync), "同一条工艺路线中不能重复添加同一道工序。请检查工序列表，移除重复工序条目。");
        if (await _db.Operations.CountAsync(o => o.FactoryId == factoryId && ids.Contains(o.Id)) != ids.Count)
            throw ThrowHelper.Biz(nameof(ValidateStepsAsync), "路线中添加的某道工序不存在或不属于本厂。请到「工序」确认该工序已创建且归属本厂，再回路线编辑页修正。");
    }

    public async Task<ApiResult<object?>> UpdateAsync(long id, RoutingCreateDto dto, long factoryId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        var routing = await _db.Routings.FirstOrDefaultAsync(r => r.Id == id && r.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(UpdateAsync), "工艺路线不存在");
        if (dto.Steps == null || dto.Steps.Count == 0)
            throw ThrowHelper.Biz(nameof(UpdateAsync), "请至少添加一道工序");
        await ValidateStepsAsync(dto.Steps, factoryId);

        routing.Code = dto.Code.Trim();
        routing.Name = dto.Name.Trim();
        _db.RoutingSteps.RemoveRange(_db.RoutingSteps.Where(s => s.RoutingId == id));
        await _db.SaveChangesAsync();
        await SaveSteps(id, dto.Steps);
        await tx.CommitAsync();
        return ApiResult<object?>.OkMsg();
    }

    private async Task SaveSteps(long routingId, List<RoutingStepDto> steps)
    {
        var seq = 1;
        foreach (var s in steps)
        {
            _db.RoutingSteps.Add(new BaseRoutingStep
            {
                RoutingId = routingId,
                OperationId = s.OperationId,
                Seq = seq++
            });
        }
        await _db.SaveChangesAsync();
    }

    public async Task<ApiResult<object?>> DeleteAsync(long id)
    {
        var msg = await DeleteGuard.CheckAsync(_db, "Routing", id);
        if (msg != null) throw ThrowHelper.Biz(nameof(DeleteAsync), msg);

        _db.RoutingSteps.RemoveRange(_db.RoutingSteps.Where(s => s.RoutingId == id));
        var routing = await _db.Routings.FindAsync(id)
            ?? throw ThrowHelper.Biz(nameof(DeleteAsync), "工艺路线不存在");
        _db.Routings.Remove(routing);
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }
}
