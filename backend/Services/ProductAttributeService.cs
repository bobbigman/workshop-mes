using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public class ProductAttributeService(AppDbContext db)
{
    public async Task<Dictionary<long, string?>> GetAsync(long productId, long factoryId)
    {
        var fields = db.CustomFields.Where(f => f.FactoryId == factoryId && f.Target == "product").Select(f => f.Id);
        return await db.CustomFieldValues.AsNoTracking().Where(v => v.TargetId == productId && fields.Contains(v.FieldId)).ToDictionaryAsync(v => v.FieldId, v => v.Value);
    }
    public async Task SaveAsync(long productId, long factoryId, Dictionary<long, string?>? values)
    {
        if (values == null) return; // 兼容旧客户端：未传不清空。
        var fields = await db.CustomFields.Where(f => f.FactoryId == factoryId && f.Target == "product").ToDictionaryAsync(f => f.Id);
        var ids = fields.Keys.ToList();
        var old = await db.CustomFieldValues.Where(v => v.TargetId == productId && ids.Contains(v.FieldId)).ToListAsync();
        foreach (var (id, raw) in values)
        {
            if (!fields.TryGetValue(id, out var field)) throw ThrowHelper.BizUser("指定的产品属性字段不存在或不属于本厂产品。请到「产品属性」确认字段已创建且归属本厂后重新选择。");
            var value = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
            if (value?.Length > 256) throw ThrowHelper.BizUser($"属性{field.FieldName}的值超过256字上限。请把该属性值精简到256字以内再保存。");
            if (value != null && field.FieldType == "number")
            {
                if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) || number < 0)
                    throw ThrowHelper.BizUser($"属性{field.FieldName}必须填非负数字（不小于0）。请检查输入值，确保是非负数。");
                if (field.FieldKey is "print_panels" or "rib_count" && decimal.Truncate(number) != number)
                    throw ThrowHelper.BizUser($"属性{field.FieldName}必须填整数，不能带小数。请去掉小数部分后重新输入。");
                value = number.ToString(CultureInfo.InvariantCulture);
            }
            if (value != null && field.FieldType == "single" && !await db.CustomFieldOptions.AnyAsync(o => o.FieldId == id && o.Label == value))
                throw ThrowHelper.BizUser($"属性{field.FieldName}填的值不在允许选项内。请查看该属性的下拉选项列表，选择合法值。");
            var entity = old.SingleOrDefault(v => v.FieldId == id);
            if (entity != null) { if (value == null) db.CustomFieldValues.Remove(entity); else entity.Value = value; }
            else if (value != null) db.CustomFieldValues.Add(new SysCustomFieldValue { FieldId = id, TargetId = productId, Value = value });
        }
        if (values.Count == 0) db.CustomFieldValues.RemoveRange(old);
        await db.SaveChangesAsync();
    }
}
