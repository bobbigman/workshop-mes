using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public interface ICustomFieldService
{
    Task<ApiResult<List<CustomFieldDto>>> QueryByTargetAsync(string target, long factoryId);
    Task<ApiResult<object?>> CreateAsync(CustomFieldCreateDto dto, long factoryId);
    Task<ApiResult<object?>> UpdateAsync(long id, CustomFieldCreateDto dto, long factoryId);
    Task<ApiResult<object?>> DeleteAsync(long id, long factoryId);
}

public class CustomFieldCreateDto
{
    public long? OperationId { get; set; }
    public string Target { get; set; } = "";
    public string FieldName { get; set; } = "";
    public string FieldType { get; set; } = "";
    /// <summary>业务编码；空=普通字段。色码约定 color/spec。</summary>
    public string? FieldKey { get; set; }
    public string? UnitText { get; set; }
    public string? Description { get; set; }
    public bool PricingRequired { get; set; }
    public List<string> Options { get; set; } = new();
    /// <summary>省略时：创建默认关闭；更新保留原值。仅 work_order 可设为 true。</summary>
    public bool? ShowInOrderList { get; set; }
}

public class CustomFieldDto
{
    public long Id { get; set; }
    public long? OperationId { get; set; }
    public string Target { get; set; } = "";
    public string FieldName { get; set; } = "";
    public string FieldType { get; set; } = "";
    public string? FieldKey { get; set; }
    public string? UnitText { get; set; }
    public string? Description { get; set; }
    public bool PricingRequired { get; set; }
    public bool ShowInOrderList { get; set; }
    public List<string> Options { get; set; } = new();
}

public class CustomFieldService : ICustomFieldService
{
    private static readonly Regex FieldKeyPattern = new(@"^[a-z][a-z0-9_]{0,31}$", RegexOptions.Compiled);

    private readonly AppDbContext _db;
    public CustomFieldService(AppDbContext db) { _db = db; }

    public async Task<ApiResult<List<CustomFieldDto>>> QueryByTargetAsync(string target, long factoryId)
    {
        var fields = await _db.CustomFields.AsNoTracking()
            .Where(f => f.FactoryId == factoryId && f.Target == target)
            .OrderBy(f => f.Id).ToListAsync();
        var ids = fields.Select(f => f.Id).ToList();
        var opts = await _db.CustomFieldOptions.AsNoTracking()
            .Where(o => ids.Contains(o.FieldId)).OrderBy(o => o.Seq).ToListAsync();

        var list = fields.Select(f => new CustomFieldDto
        {
            Id = f.Id,
            Target = f.Target, OperationId = f.OperationId,
            FieldName = f.FieldName,
            FieldType = f.FieldType,
            FieldKey = f.FieldKey, UnitText = f.UnitText, Description = f.Description, PricingRequired = f.PricingRequired,
            ShowInOrderList = f.ShowInOrderList,
            Options = opts.Where(o => o.FieldId == f.Id).Select(o => o.Label).ToList()
        }).ToList();

        return ApiResult<List<CustomFieldDto>>.Ok(list);
    }

    public async Task<ApiResult<object?>> CreateAsync(CustomFieldCreateDto dto, long factoryId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        await OperationPriceListService.LockFactoryAsync(_db, factoryId);
        ValidateMetadata(dto);
        await ValidateOperationAsync(dto, factoryId);
        if (string.IsNullOrWhiteSpace(dto.FieldName) || string.IsNullOrWhiteSpace(dto.FieldType))
            throw ThrowHelper.Biz(nameof(CreateAsync), "新建自定义字段时，名称和类型都必须填写。请填写字段名称并选择字段类型后再保存。");

        if (dto.FieldType == "single" && (dto.Options == null || dto.Options.All(string.IsNullOrWhiteSpace)))
            throw ThrowHelper.Biz(nameof(CreateAsync), "单选类型的字段至少要添加一个选项。请在选项中录入至少一个可选值后再保存。");

        var show = dto.ShowInOrderList ?? false;
        if (show && dto.Target != "work_order")
            throw ThrowHelper.Biz(nameof(CreateAsync), "只有归属工单的自定义字段才能开启「在工单列表显示」。如需在列表中展示，请把字段归属改为工单类型。");

        var fieldKey = NormalizeFieldKey(dto.FieldKey);
        await EnsureFieldKeyUniqueAsync(factoryId, dto.Target, fieldKey, excludeId: null, operationId: dto.OperationId);

        var field = new SysCustomField
        {
            FactoryId = factoryId,
            Target = dto.Target, OperationId = dto.OperationId,
            FieldName = dto.FieldName.Trim(),
            FieldType = dto.FieldType,
            FieldKey = fieldKey, UnitText = dto.UnitText, Description = dto.Description, PricingRequired = dto.PricingRequired,
            ShowInOrderList = show,
            CreatedAt = DateTime.Now
        };
        _db.CustomFields.Add(field);
        await _db.SaveChangesAsync();

        if (dto.FieldType == "single")
        {
            var seq = 0;
            foreach (var opt in dto.Options.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                _db.CustomFieldOptions.Add(new SysCustomFieldOption
                {
                    FieldId = field.Id,
                    Label = opt.Trim(),
                    Seq = seq++
                });
            }
            await _db.SaveChangesAsync();
        }
        await tx.CommitAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> DeleteAsync(long id, long factoryId)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await OperationPriceListService.LockFactoryAsync(_db, factoryId);
        var field = await _db.CustomFields.FirstOrDefaultAsync(f => f.Id == id && f.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(DeleteAsync), "要删除的字段不存在或已被删除。请刷新页面后重试。");

        if (await ReferencedByPriceListAsync(field))
            throw ThrowHelper.BizUser("该字段正在被工序价目表引用，不能直接删除。如需删除，请先在「工序价目表」中移除对该字段的引用。");
        if (await _db.CustomFieldValues.AnyAsync(v => v.FieldId == id))
            throw ThrowHelper.Biz(nameof(DeleteAsync), $"字段“{field.FieldName}”已有业务数据在使用，不能删除，以免数据丢失。如确需停用，建议保留字段但停止使用，或先清理相关业务数据。");

        _db.CustomFieldOptions.RemoveRange(await _db.CustomFieldOptions.Where(o => o.FieldId == id).ToListAsync());
        await _db.SaveChangesAsync();
        _db.CustomFields.Remove(field);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> UpdateAsync(long id, CustomFieldCreateDto dto, long factoryId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        await OperationPriceListService.LockFactoryAsync(_db, factoryId);
        var field = await _db.CustomFields.FirstOrDefaultAsync(f => f.Id == id && f.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(UpdateAsync), "字段不存在");

        if (string.IsNullOrEmpty(dto.Target)) { dto.Target = field.Target; dto.OperationId = field.OperationId; }
        if (string.IsNullOrEmpty(dto.FieldType)) dto.FieldType = field.FieldType;
        ValidateMetadata(dto);
        await ValidateOperationAsync(dto, factoryId);
        if (field.Target != dto.Target || field.OperationId != dto.OperationId || field.FieldType != dto.FieldType)
            throw ThrowHelper.BizUser("字段的归属类别、关联工序和数据类型创建后不能修改。如需调整，请新建一个字段替代。");
        if (string.IsNullOrWhiteSpace(dto.FieldName))
            throw ThrowHelper.Biz(nameof(UpdateAsync), "字段名称不能为空。请填写字段名称后再保存。");

        if (field.FieldType == "single")
        {
            if (dto.Options == null || dto.Options.All(string.IsNullOrWhiteSpace))
                throw ThrowHelper.Biz(nameof(UpdateAsync), "单选字段必须添加选项");
        }

        if (await ReferencedByPriceListAsync(field))
        {
            if (NormalizeFieldKey(dto.FieldKey) != field.FieldKey)
                throw ThrowHelper.BizUser("该字段已被工序价目表引用，代号不能修改。如需改代号，请先在「工序价目表」中解除引用，或新建字段替代。");
            var oldOptions = await _db.CustomFieldOptions.Where(o => o.FieldId == id).OrderBy(o => o.Seq).Select(o => o.Label).ToListAsync();
            if (field.FieldType == "single" && oldOptions.Except(dto.Options.Select(x => x.Trim())).Any())
                throw ThrowHelper.BizUser("该字段已被价目表引用，已有的选项值不能删除；可以新增选项，但不能删掉正在使用的选项。");
        }
        field.UnitText = dto.UnitText; field.Description = dto.Description; field.PricingRequired = dto.PricingRequired;
        field.FieldName = dto.FieldName.Trim();

        var fieldKey = NormalizeFieldKey(dto.FieldKey);
        await EnsureFieldKeyUniqueAsync(factoryId, field.Target, fieldKey, excludeId: id, operationId: field.OperationId);
        field.FieldKey = fieldKey;

        if (dto.ShowInOrderList.HasValue)
        {
            if (dto.ShowInOrderList.Value && field.Target != "work_order")
                throw ThrowHelper.Biz(nameof(UpdateAsync), "仅工单自定义字段可开启「在工单列表显示」");
            field.ShowInOrderList = dto.ShowInOrderList.Value;
        }

        if (field.FieldType == "single")
        {
            _db.CustomFieldOptions.RemoveRange(_db.CustomFieldOptions.Where(o => o.FieldId == id));
            await _db.SaveChangesAsync();
            var seq = 0;
            foreach (var opt in dto.Options.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                _db.CustomFieldOptions.Add(new SysCustomFieldOption
                {
                    FieldId = id,
                    Label = opt.Trim(),
                    Seq = seq++
                });
            }
        }

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return ApiResult<object?>.OkMsg();
    }

    private async Task ValidateOperationAsync(CustomFieldCreateDto dto, long factoryId)
    {
        if (dto.Target == "work_order_task")
        {
            if (!dto.OperationId.HasValue || !await _db.Operations.AnyAsync(o => o.Id == dto.OperationId && o.FactoryId == factoryId))
                throw ThrowHelper.BizUser("工序工艺类型的字段必须指定一道本厂工序。请在字段设置中选择正确的所属工序后再保存。");
        }
        else if (dto.OperationId.HasValue) throw ThrowHelper.BizUser("只有工序工艺类型的字段才能指定所属工序。当前字段不是工序工艺类型，不需要也不能设置所属工序。");
    }

    private static void ValidateMetadata(CustomFieldCreateDto dto)
    {
        if (dto.Target is not ("product" or "work_order" or "operation" or "report" or "work_order_task") || dto.FieldType is not ("text" or "number" or "single")) throw ThrowHelper.BizUser("字段的归属类别或数据类型设置无效。请检查字段设置，确保归属和类型搭配正确后重新保存。");
        if (dto.FieldName.Length > 64 || dto.UnitText?.Length > 16 || dto.Description?.Length > 256) throw ThrowHelper.BizUser("字段名称、单位或说明文字超过了长度上限。请缩短相关内容后重新保存。");
        if (dto.Options == null || dto.Options.Any(o => string.IsNullOrWhiteSpace(o) || o.Length > 64) || dto.Options.Select(o=>o.Trim()).Distinct().Count() != dto.Options.Count) throw ThrowHelper.BizUser("字段选项有问题：为空、重复或单个选项超过64字。请检查选项列表，确保每个选项非空、不重复且不超过64字。");
    }

    private async Task<bool> ReferencedByPriceListAsync(SysCustomField field)
    {
        if (field.Target is not ("product" or "work_order_task")) return false;
        var tables = await _db.OperationPriceLists.AsNoTracking().Where(t => t.FactoryId == field.FactoryId).ToListAsync();
        return tables.Select(OperationPriceListService.Details).Any(d => d.RequiredFieldIds.Contains(field.Id) || d.Rows.SelectMany(r => r.Conditions).Concat(d.Groups.SelectMany(g => g.Conditions)).Any(c => c.FieldId == field.Id));
    }

    /// <summary>空/空白 → null；否则小写校验格式。</summary>
    internal static string? NormalizeFieldKey(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var key = raw.Trim().ToLowerInvariant();
        if (!FieldKeyPattern.IsMatch(key))
            throw ThrowHelper.Biz(nameof(NormalizeFieldKey), "字段代号格式不对。请以小写字母开头，后面只能用小写字母、数字和下划线，总长不超过32位，例如 part_count。");
        return key;
    }

    private async Task EnsureFieldKeyUniqueAsync(long factoryId, string target, string? fieldKey, long? excludeId, long? operationId)
    {
        if (fieldKey == null) return;
        var exists = await _db.CustomFields.AsNoTracking()
            .AnyAsync(f => f.FactoryId == factoryId
                           && f.Target == target && f.OperationId == operationId
                           && f.FieldKey == fieldKey
                           && (!excludeId.HasValue || f.Id != excludeId.Value));
        if (exists)
            throw ThrowHelper.Biz(nameof(EnsureFieldKeyUniqueAsync), $"字段代号「{fieldKey}」已存在，同一归属类别下不能重复。请换一个未使用过的代号，或到「自定义字段」页面查看已有代号避免冲突。");
    }
}
