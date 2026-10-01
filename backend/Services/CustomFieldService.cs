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
    public string Target { get; set; } = "";
    public string FieldName { get; set; } = "";
    public string FieldType { get; set; } = "";
    /// <summary>业务编码；空=普通字段。色码约定 color/spec。</summary>
    public string? FieldKey { get; set; }
    public List<string> Options { get; set; } = new();
    /// <summary>省略时：创建默认关闭；更新保留原值。仅 work_order 可设为 true。</summary>
    public bool? ShowInOrderList { get; set; }
}

public class CustomFieldDto
{
    public long Id { get; set; }
    public string Target { get; set; } = "";
    public string FieldName { get; set; } = "";
    public string FieldType { get; set; } = "";
    public string? FieldKey { get; set; }
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
            Target = f.Target,
            FieldName = f.FieldName,
            FieldType = f.FieldType,
            FieldKey = f.FieldKey,
            ShowInOrderList = f.ShowInOrderList,
            Options = opts.Where(o => o.FieldId == f.Id).Select(o => o.Label).ToList()
        }).ToList();

        return ApiResult<List<CustomFieldDto>>.Ok(list);
    }

    public async Task<ApiResult<object?>> CreateAsync(CustomFieldCreateDto dto, long factoryId)
    {
        if (string.IsNullOrWhiteSpace(dto.FieldName) || string.IsNullOrWhiteSpace(dto.FieldType))
            throw ThrowHelper.Biz(nameof(CreateAsync), "字段名称和类型不能为空");

        if (dto.FieldType == "single" && (dto.Options == null || dto.Options.All(string.IsNullOrWhiteSpace)))
            throw ThrowHelper.Biz(nameof(CreateAsync), "单选字段必须添加选项");

        var show = dto.ShowInOrderList ?? false;
        if (show && dto.Target != "work_order")
            throw ThrowHelper.Biz(nameof(CreateAsync), "仅工单自定义字段可开启「在工单列表显示」");

        var fieldKey = NormalizeFieldKey(dto.FieldKey);
        await EnsureFieldKeyUniqueAsync(factoryId, dto.Target, fieldKey, excludeId: null);

        var field = new SysCustomField
        {
            FactoryId = factoryId,
            Target = dto.Target,
            FieldName = dto.FieldName.Trim(),
            FieldType = dto.FieldType,
            FieldKey = fieldKey,
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
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> DeleteAsync(long id, long factoryId)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var field = await _db.CustomFields.FirstOrDefaultAsync(f => f.Id == id && f.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(DeleteAsync), "字段不存在");

        if (await _db.CustomFieldValues.AnyAsync(v => v.FieldId == id))
            throw ThrowHelper.Biz(nameof(DeleteAsync), $"字段“{field.FieldName}”已有业务数据，不能删除");

        _db.CustomFieldOptions.RemoveRange(await _db.CustomFieldOptions.Where(o => o.FieldId == id).ToListAsync());
        await _db.SaveChangesAsync();
        _db.CustomFields.Remove(field);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> UpdateAsync(long id, CustomFieldCreateDto dto, long factoryId)
    {
        var field = await _db.CustomFields.FirstOrDefaultAsync(f => f.Id == id && f.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(UpdateAsync), "字段不存在");

        if (string.IsNullOrWhiteSpace(dto.FieldName))
            throw ThrowHelper.Biz(nameof(UpdateAsync), "字段名称不能为空");

        if (field.FieldType == "single")
        {
            if (dto.Options == null || dto.Options.All(string.IsNullOrWhiteSpace))
                throw ThrowHelper.Biz(nameof(UpdateAsync), "单选字段必须添加选项");
        }

        field.FieldName = dto.FieldName.Trim();

        var fieldKey = NormalizeFieldKey(dto.FieldKey);
        await EnsureFieldKeyUniqueAsync(factoryId, field.Target, fieldKey, excludeId: id);
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
        return ApiResult<object?>.OkMsg();
    }

    /// <summary>空/空白 → null；否则小写校验格式。</summary>
    internal static string? NormalizeFieldKey(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var key = raw.Trim().ToLowerInvariant();
        if (!FieldKeyPattern.IsMatch(key))
            throw ThrowHelper.Biz(nameof(NormalizeFieldKey), "字段编码仅允许小写字母开头，后接小写字母/数字/下划线，最长 32");
        return key;
    }

    private async Task EnsureFieldKeyUniqueAsync(long factoryId, string target, string? fieldKey, long? excludeId)
    {
        if (fieldKey == null) return;
        var exists = await _db.CustomFields.AsNoTracking()
            .AnyAsync(f => f.FactoryId == factoryId
                           && f.Target == target
                           && f.FieldKey == fieldKey
                           && (!excludeId.HasValue || f.Id != excludeId.Value));
        if (exists)
            throw ThrowHelper.Biz(nameof(EnsureFieldKeyUniqueAsync), $"字段编码「{fieldKey}」已存在，同归属对象下不可重复");
    }
}
