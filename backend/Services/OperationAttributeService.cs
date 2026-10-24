using System.Globalization;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;
using Microsoft.EntityFrameworkCore;

namespace ahu.MicrosoftMes.Services;

/// <summary>工艺值只归属当前工单任务，不能与产品/全局字段混用。</summary>
public class OperationAttributeService(AppDbContext db)
{
    public async Task<Dictionary<long, SysCustomField>> FieldsAsync(long factoryId, long operationId) =>
        await db.CustomFields.AsNoTracking().Where(f => f.FactoryId == factoryId && (f.Target == "product" || (f.Target == "work_order_task" && f.OperationId == operationId))).ToDictionaryAsync(f => f.Id);

    public async Task<Dictionary<long, string?>> ValidateAsync(long factoryId, long operationId, Dictionary<long, string?>? input)
    {
        var fields = await db.CustomFields.AsNoTracking().Where(f => f.FactoryId == factoryId && f.Target == "work_order_task" && f.OperationId == operationId).ToDictionaryAsync(f => f.Id);
        var result = new Dictionary<long, string?>();
        foreach (var (id, raw) in input ?? new())
        {
            OperationPriceListService.Require(fields.TryGetValue(id, out var field), "工艺字段不存在、非本厂或不属于当前工序，不能覆盖产品固有属性");
            var value = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
            OperationPriceListService.Require(value?.Length is not > 256, $"工艺属性{field!.FieldName}超过256字");
            if (value != null && field.FieldType == "number")
            {
                OperationPriceListService.Require(decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) && number >= 0, $"{field.FieldName}须为非负数字");
                OperationPriceListService.Require(field.FieldKey is not ("rib_count" or "print_panels") || decimal.Truncate(number) == number, $"{field.FieldName}须为整数");
                value = number.ToString(CultureInfo.InvariantCulture);
            }
            if (value != null && field.FieldType == "single")
                OperationPriceListService.Require(await db.CustomFieldOptions.AnyAsync(o => o.FieldId == id && o.Label == value), $"{field.FieldName}不在允许选项内");
            result[id] = value;
        }
        return result;
    }

    public async Task<Dictionary<long, string?>> GetAsync(long taskId, long factoryId, long operationId)
    {
        var ids = db.CustomFields.Where(f => f.FactoryId == factoryId && f.Target == "work_order_task" && f.OperationId == operationId).Select(f => f.Id);
        return await db.CustomFieldValues.AsNoTracking().Where(v => v.TargetId == taskId && ids.Contains(v.FieldId)).ToDictionaryAsync(v => v.FieldId, v => v.Value);
    }

    public async Task SaveOrderAsync(ProdWorkOrder order, Dictionary<long, Dictionary<long, string?>>? inputs)
    {
        if (inputs == null) return;
        var tasks = await db.WorkOrderOperations.Where(t => t.WorkOrderId == order.Id).ToListAsync();
        OperationPriceListService.Require(inputs.Keys.All(id => tasks.Any(t => t.OperationId == id)), "工艺输入含非本工单工序");
        foreach (var (operationId, input) in inputs)
        {
            var task = tasks.Single(t => t.OperationId == operationId);
            var values = await ValidateAsync(order.FactoryId, operationId, input);
            var old = await GetAsync(task.Id, order.FactoryId, operationId);
            if (task.ProcessingPrice != null)
            {
                OperationPriceListService.Require(values.Count(v => v.Value != null) == old.Count && values.Where(v => v.Value != null).All(v => old.GetValueOrDefault(v.Key) == v.Value), "本工序已锁价，不能修改工艺属性，请新建或复制工单");
                continue;
            }
            var ids = db.CustomFields.Where(f => f.FactoryId == order.FactoryId && f.Target == "work_order_task" && f.OperationId == operationId).Select(f => f.Id);
            db.CustomFieldValues.RemoveRange(await db.CustomFieldValues.Where(v => v.TargetId == task.Id && ids.Contains(v.FieldId)).ToListAsync());
            foreach (var (fieldId, value) in values.Where(v => v.Value != null)) db.CustomFieldValues.Add(new SysCustomFieldValue { FieldId = fieldId, TargetId = task.Id, Value = value });
        }
        await db.SaveChangesAsync();
    }

    public async Task<Dictionary<long, Dictionary<long, string?>>> ReadOrderAsync(long orderId, long factoryId)
    {
        var result = new Dictionary<long, Dictionary<long, string?>>();
        var tasks = await db.WorkOrderOperations.AsNoTracking().Where(t => t.WorkOrderId == orderId && db.WorkOrders.Any(o => o.Id == orderId && o.FactoryId == factoryId)).ToListAsync();
        foreach (var t in tasks) result[t.OperationId] = await GetAsync(t.Id, factoryId, t.OperationId);
        return result;
    }

    public async Task RemoveTasksAsync(long orderId, long factoryId)
    {
        var taskIds = db.WorkOrderOperations.Where(t => t.WorkOrderId == orderId).Select(t => t.Id);
        var fieldIds = db.CustomFields.Where(f => f.FactoryId == factoryId && f.Target == "work_order_task").Select(f => f.Id);
        db.CustomFieldValues.RemoveRange(await db.CustomFieldValues.Where(v => taskIds.Contains(v.TargetId) && fieldIds.Contains(v.FieldId)).ToListAsync());
        await db.SaveChangesAsync();
    }
}
