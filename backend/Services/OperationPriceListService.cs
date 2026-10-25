using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public class AttributeCondition
{
    public long FieldId { get; set; }
    public string Operator { get; set; } = "eq";
    public string? Value { get; set; }
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
    public bool IncludeMin { get; set; } = true;
    public bool IncludeMax { get; set; } = true;
}
public class PriceChargeGroup
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Required { get; set; } = true;
    public List<AttributeCondition> Conditions { get; set; } = new();
}
public class AttributePriceRow
{
    public string RowId { get; set; } = "";
    public string Kind { get; set; } = "base";
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    // 仅为读取/拒绝旧schemaVersion=1行级绑定而保留；新版写入必须为空。
    public long? ProductId { get; set; }
    public string Label { get; set; } = "";
    public List<AttributeCondition> Conditions { get; set; } = new();
    public decimal? Amount { get; set; }
    public decimal DeductPrice { get; set; }
    public string? GroupKey { get; set; }
    public List<string> IncludedRowIds { get; set; } = new();
    public bool Confirmed { get; set; }
    public string Source { get; set; } = "";
    public string Note { get; set; } = "";
}
public class AttributePriceDetails
{
    public int SchemaVersion { get; set; } = 1;
    public List<long> RequiredFieldIds { get; set; } = new();
    public List<PriceChargeGroup> Groups { get; set; } = new();
    public List<AttributePriceRow> Rows { get; set; } = new();
}
public class PriceListInput
{
    public long? ProductId { get; set; }
    public string Remark { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public long OperationId { get; set; }
    /// <summary>计价方式：1计件 2计时 3单笔固定。首次启用后锁定。</summary>
    public byte PricingType { get; set; } = 1;
    public byte Status { get; set; }
    public DateTime EffectiveFrom { get; set; } = DateTime.Today;
    public DateTime? EffectiveTo { get; set; }
    public string PriceUnit { get; set; } = "";
    public long ReportUnitId { get; set; }
    public decimal UnitsPerPriceUnit { get; set; } = 1;
    public string ExpectedVersion { get; set; } = "";
    public AttributePriceDetails Details { get; set; } = new();
}
public record PriceRowTrace(string RowId, string Kind, bool Matched, bool Included, decimal Amount, string Reason, string Source);
public class AttributeQuoteEvidence
{
    public long PriceListId { get; set; }
    public string PriceListCode { get; set; } = "";
    public DateTime PricingDate { get; set; }
    public Dictionary<long, string?> Attributes { get; set; } = new();
    public List<PriceRowTrace> Rows { get; set; } = new();
}

/// <summary>工序价目表按产品事实取价；历史金额仍使用原有Processing报价快照底座。</summary>
public class OperationPriceListService(AppDbContext db)
{
    public static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok, string message) { if (!ok) throw ThrowHelper.BizUser(message); }
    private static bool Money(decimal value) => value is >= 0 and <= 1000000 && decimal.Round(value, 4) == value;
    private static bool Number(string? s, out decimal value) => decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    public static AttributePriceDetails Details(OperationPriceList table) => OperationPricingService.Read<AttributePriceDetails>(table.Details);

    public static void Validate(AttributePriceDetails details, IReadOnlyDictionary<long, SysCustomField> fields, bool enabling, byte pricingType = 1)
    {
        Require(pricingType is 1 or 2 or 3, "计价方式无效");
        Require(details != null && details.SchemaVersion == 2 && details.Rows != null && details.Groups != null && details.RequiredFieldIds != null, "价目表明细结构无效");
        if (pricingType != 1)
        {
            Require(details.Groups.Count == 0, "计时/固定价目表不允许收费组");
            Require(details.Rows.All(r => r.Kind != "extra"), "计时/固定价目表不允许附加行");
        }
        Require(details.Groups.All(g => g != null && g.Conditions != null) && details.Rows.All(r => r != null && r.Conditions != null && r.IncludedRowIds != null), "收费组或价格行结构为空");
        Require(details!.Rows.Count <= 1000 && (!enabling || details.Rows.Count > 0), "价目表最多1000行，启用时不得为空");
        Require(details.Groups.Select(g => g.Key).Distinct().Count() == details.Groups.Count && details.Groups.All(g => !string.IsNullOrWhiteSpace(g.Key) && !string.IsNullOrWhiteSpace(g.Name)), "收费组编码重复或为空");
        Require(details.Rows.Select(r => r.RowId).Distinct().Count() == details.Rows.Count && details.Rows.All(r => !string.IsNullOrWhiteSpace(r.RowId)), "价格行编号重复或为空");
        Require(details.RequiredFieldIds.All(fields.ContainsKey), "必需属性包含非本厂产品字段");
        foreach (var c in details.Groups.SelectMany(g => g.Conditions).Concat(details.Rows.SelectMany(r => r.Conditions)))
        {
            Require(c != null, "取价条件不能为空");
            Require(fields.ContainsKey(c.FieldId), "条件引用的产品字段不存在或不属于本厂");
            var field = fields[c.FieldId];
            Require(field.FieldType is "number" or "single", "取价条件只支持数字或单选字段");
            Require(c.Operator is "eq" or "range", "条件只支持等于或区间");
            if (c.Operator == "eq")
            {
                Require(!string.IsNullOrWhiteSpace(c.Value), "等于条件未填写值，不能当作不限");
                if (field.FieldType == "number") { Require(Number(c.Value, out var n) && n >= 0, $"{field.FieldName}条件须为非负数字"); Require(field.FieldKey is not ("print_panels" or "rib_count") || decimal.Truncate(n) == n, $"{field.FieldName}条件须为整数"); }
            }
            else
                Require(field.FieldType == "number" && (!c.Min.HasValue || c.Min >= 0) && (!c.Max.HasValue || c.Max >= 0) && (c.Min.HasValue || c.Max.HasValue) &&
                    (!c.Min.HasValue || !c.Max.HasValue || c.Min < c.Max || (c.Min == c.Max && c.IncludeMin && c.IncludeMax)), "区间上下界或字段类型无效");
        }
        foreach (var row in details.Rows)
        {
            Require(row.ProductId == null, "新版价目表行不接受产品绑定，请在表头选择产品");
            Require(row.Label != null && row.Label.Length <= 256 && row.Note != null && row.Source != null, "行规格/备注/来源无效或规格超过256字");
            Require(!enabling || !row.Note.Contains("待确认", StringComparison.Ordinal), $"行{row.RowId}备注仍待确认，不能启用");
            Require(row.Kind == "extra" || row.GroupKey == null, "基础/最终行不能带收费组");
            Require(row.Kind is "base" or "extra" or "final", "价格性质无效");
            Require(row.Conditions != null && row.IncludedRowIds != null, "条件和包含项不能为空集合");
            Require(!enabling || (row.Confirmed && row.Amount.HasValue && !string.IsNullOrWhiteSpace(row.Source)), $"行{row.RowId}待定、缺金额或来源，不能启用");
            Require((!row.Amount.HasValue || Money(row.Amount.Value)) && Money(row.DeductPrice), "金额须为非负数且最多4位小数");
            Require(row.Kind != "extra" || (details.Groups.Any(g => g.Key == row.GroupKey) && row.DeductPrice == 0 && row.IncludedRowIds.Count == 0), "附加行须属于收费组且不能定义扣价/包含项");
            Require(row.IncludedRowIds.Distinct().Count() == row.IncludedRowIds.Count && row.IncludedRowIds.All(id => details.Rows.Any(r => r.RowId == id && r.Kind == "extra")), "包含项只能引用本表附加行，不能重复");
        }
        Require(!enabling || details.Rows.Any(r => r.Kind != "extra"), "启用价目表须有基础或最终价");
        if (enabling)
        {
            for (var i = 0; i < details.Rows.Count; i++)
            for (var j = i + 1; j < details.Rows.Count; j++)
            {
                var a = details.Rows[i]; var b = details.Rows[j];
                if ((a.Kind == "extra") != (b.Kind == "extra")) continue;
                if (a.Kind == "extra" && a.GroupKey != b.GroupKey) continue;
                Require(!MayOverlap(a.Conditions, b.Conditions, fields), $"价格行{a.RowId}与{b.RowId}条件重叠，不能启用");
            }
        }
    }

    private static bool MayOverlap(List<AttributeCondition> left, List<AttributeCondition> right, IReadOnlyDictionary<long, SysCustomField> fields)
    {
        foreach (var id in left.Select(c => c.FieldId).Intersect(right.Select(c => c.FieldId)))
        {
            var constraints = left.Concat(right).Where(c => c.FieldId == id).ToList();
            if (fields[id].FieldType != "number")
            {
                if (constraints.Select(c => c.Value).Distinct().Count() > 1) return false;
                continue;
            }
            decimal? min = null, max = null; var includeMin = true; var includeMax = true;
            foreach (var c in constraints)
            {
                var low = c.Operator == "eq" ? decimal.Parse(c.Value!, CultureInfo.InvariantCulture) : c.Min;
                var high = c.Operator == "eq" ? low : c.Max;
                var lowIncluded = c.Operator == "eq" || c.IncludeMin;
                var highIncluded = c.Operator == "eq" || c.IncludeMax;
                if (low.HasValue && (!min.HasValue || low > min)) { min = low; includeMin = lowIncluded; }
                else if (low.HasValue && low == min) includeMin &= lowIncluded;
                if (high.HasValue && (!max.HasValue || high < max)) { max = high; includeMax = highIncluded; }
                else if (high.HasValue && high == max) includeMax &= highIncluded;
            }
            if (min.HasValue && max.HasValue && (min > max || (min == max && (!includeMin || !includeMax)))) return false;
        }
        return true;
    }

    // 先检查已知不匹配条件，再检查缺失值，防止不适用行要求填写无关属性。
    public static bool Matches(IEnumerable<AttributeCondition> conditions, IReadOnlyDictionary<long, string?> values, IReadOnlyDictionary<long, SysCustomField> fields)
    {
        var list = conditions.ToList();
        foreach (var c in list)
        {
            if (!values.TryGetValue(c.FieldId, out var value) || string.IsNullOrWhiteSpace(value)) continue;
            if (fields[c.FieldId].FieldType == "number")
            {
                Require(Number(value, out var n), $"产品属性{fields[c.FieldId].FieldName}不是有效数字");
                if (c.Operator == "eq") { if (!Number(c.Value, out var expected) || n != expected) return false; }
                else if ((c.Min.HasValue && (n < c.Min || (n == c.Min && !c.IncludeMin))) || (c.Max.HasValue && (n > c.Max || (n == c.Max && !c.IncludeMax)))) return false;
            }
            else if (!string.Equals(value, c.Value, StringComparison.Ordinal)) return false;
        }
        foreach (var c in list)
            Require(values.TryGetValue(c.FieldId, out var value) && !string.IsNullOrWhiteSpace(value), $"缺少产品属性：{fields[c.FieldId].FieldName}，不能确定工价");
        return true;
    }

    public static ProcessingQuote Calculate(OperationPriceList table, long productId, long reportUnitId, Dictionary<long, string?> values, Dictionary<long, SysCustomField> fields, DateTime date)
    {
        var details = Details(table);
        Validate(details, fields, true, table.PricingType);
        Require(table.Status == 1 && date.Date >= table.EffectiveFrom.Date && (!table.EffectiveTo.HasValue || date.Date <= table.EffectiveTo.Value.Date), "价目表未启用或不在有效期");
        Require(table.ReportUnitId == reportUnitId && table.UnitsPerPriceUnit > 0, "报工单位或换算不匹配");
        Require(table.PricingType == 1 || (table.UnitsPerPriceUnit == 1 && table.PriceUnit == (table.PricingType == 2 ? "小时" : "笔")), "计时/固定价目表换算数须为1且计价单位须为小时/笔");
        foreach (var id in details.RequiredFieldIds) Require(values.TryGetValue(id, out var value) && !string.IsNullOrWhiteSpace(value), $"缺少产品属性：{fields[id].FieldName}");
        Require(table.ProductId == null || table.ProductId == productId, "价目表不属于当前产品");
        var applicable = details.Rows;
        var primary = applicable.Where(r => r.Kind != "extra" && Matches(r.Conditions, values, fields)).ToList();
        Require(primary.Count == 1, primary.Count == 0 ? $"产品{productId}·工序{table.OperationId}·表{table.Code}缺规格的价：{string.Join("；", details.Rows.Where(r => r.Kind != "extra").SelectMany(r => r.Conditions).Select(c => $"{fields[c.FieldId].FieldName}实际={values.GetValueOrDefault(c.FieldId) ?? "未填"}，要求{c.Operator}:{c.Value ?? $"{c.Min}~{c.Max}"}"))}" : "重复命中基础/最终工价，请消除条件冲突");
        var price = primary[0];
        var evidence = new AttributeQuoteEvidence { PriceListId = table.Id, PriceListCode = table.Code, PricingDate = date.Date, Attributes = values.OrderBy(v => v.Key).ToDictionary(v => v.Key, v => v.Value) };
        var quote = new ProcessingQuote { SchemaVersion = 2, FactoryId = table.FactoryId, ProductId = productId, PriceType = table.PricingType, OperationId = table.OperationId, Version = table.Version, BaseName = $"{table.Code} / {price.RowId}", BasePrice = price.Amount!.Value,
            UnitPrice = price.Amount.Value, DeductPrice = price.DeductPrice, PriceUnit = table.PriceUnit, ReportUnitId = table.ReportUnitId, UnitsPerPriceUnit = table.UnitsPerPriceUnit,
            AttributeEvidence = evidence };
        var chosen = new HashSet<string> { price.RowId };
        var included = new HashSet<string>();
        foreach (var group in details.Groups)
        {
            if (!Matches(group.Conditions, values, fields)) continue;
            var extras = applicable.Where(r => r.Kind == "extra" && r.GroupKey == group.Key && Matches(r.Conditions, values, fields)).ToList();
            Require(extras.Count <= 1, $"收费组“{group.Name}”重复命中，请核查区间或条件");
            Require(!group.Required || extras.Count == 1, $"收费组“{group.Name}”缺价，不能当作免费");
            if (extras.Count == 0) continue;
            var extra = extras[0];
            var isIncluded = price.IncludedRowIds.Contains(extra.RowId);
            chosen.Add(extra.RowId);
            if (isIncluded) included.Add(extra.RowId);
            var amount = isIncluded ? 0 : extra.Amount!.Value;
            quote.UnitPrice += amount;
            quote.Lines.Add(new ProcessingLine(group.Name, extra.RowId, amount, isIncluded));
        }
        foreach (var row in details.Rows)
            evidence.Rows.Add(new(row.RowId, row.Kind, chosen.Contains(row.RowId), included.Contains(row.RowId), row.Amount!.Value,
                included.Contains(row.RowId) ? "已包含，不另加价" : chosen.Contains(row.RowId) ? "条件命中" : "不在产品范围、条件不符或收费组不适用", row.Source));
        Require(quote.UnitPrice / quote.UnitsPerPriceUnit <= 1000000 && quote.DeductPrice / quote.UnitsPerPriceUnit <= 1000000, "换算后的单价超过范围");
        quote.QuoteToken = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(OperationPricingService.Write(quote))));
        return quote;
    }

    public static OperationPriceList SelectTable(IEnumerable<OperationPriceList> candidates, long productId, long operationId)
    {
        var all = candidates.ToList();
        Require(!all.Any(t => Details(t).SchemaVersion != 2), $"工序{operationId}存在旧行级配置待转换，禁止新单取价；请查看转换清单");
        var specific = all.Where(t => t.ProductId == productId).ToList();
        var common = all.Where(t => t.ProductId == null).ToList();
        Require(specific.Count <= 1 && common.Count <= 1, $"工序{operationId}同范围多张价目表生效，需消除冲突");
        var selected = specific.SingleOrDefault() ?? common.SingleOrDefault();
        Require(selected != null, $"产品{productId}·工序{operationId}无生效专属或通用价目表，不能退回普通工价");
        return selected!;
    }

    public async Task<ProcessingQuote> PreviewAsync(long productId, long operationId, long factoryId, DateTime? date = null,
        Dictionary<long, string?>? operationAttributes = null, long? workOrderId = null, string? expectedVersion = null)
    {
        var p = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == productId && p.FactoryId == factoryId) ?? throw ThrowHelper.BizUser("产品不存在或非本厂");
        Require(await db.Operations.AnyAsync(o => o.Id == operationId && o.FactoryId == factoryId) && await db.RoutingSteps.AnyAsync(s => s.RoutingId == p.RoutingId && s.OperationId == operationId), "工序不在本厂产品工艺内");
        if (workOrderId.HasValue)
        {
            Require(operationAttributes == null, "已有工单预览不接受工艺覆盖输入");
            Require(await db.WorkOrders.AnyAsync(o => o.Id == workOrderId && o.FactoryId == factoryId && o.ProductId == productId), "工单不存在、产品不一致或非本厂");
            var task = await db.WorkOrderOperations.AsNoTracking().SingleOrDefaultAsync(t => t.WorkOrderId == workOrderId && t.OperationId == operationId) ?? throw ThrowHelper.BizUser("工序不属于当前工单");
            Require(task.ProcessingPrice != null, "该单没有可靠锁价，请使用新建/复制流程");
            return await new OperationPricingService(db).BudgetAsync(workOrderId.Value,operationId,factoryId);
        }
        var day = (date ?? DateTime.Today).Date;
        var attrs = new OperationAttributeService(db);
        var fields = await attrs.FieldsAsync(factoryId, operationId);
        var values = await new ProductAttributeService(db).GetAsync(productId, factoryId);
        foreach (var v in await attrs.ValidateAsync(factoryId, operationId, operationAttributes)) { if (v.Value != null) values.Add(v.Key, v.Value); }
        foreach (var (id, value) in values.Where(v => !string.IsNullOrWhiteSpace(v.Value)))
        {
            var f = fields[id];
            if (f.FieldType == "single") Require(await db.CustomFieldOptions.AnyAsync(o => o.FieldId == id && o.Label == value), $"属性{f.FieldName}的选项无效");
        }
        var quote = await new PriceTimelineService(db).QuoteAsync(factoryId,productId,operationId,p.UnitId ?? 0,values,day);
        Require(expectedVersion == null || quote.Version == expectedVersion, "价目表已变化，请重新预览");
        return quote;
    }

    public async Task<OperationPriceList> GetAsync(long id, long factoryId) => await db.OperationPriceLists.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && t.FactoryId == factoryId) ?? throw ThrowHelper.BizUser("价目表不存在或非本厂");
    public static object View(OperationPriceList t) => new { t.Id, t.Code, t.Name, t.OperationId, t.ProductId, t.PricingType, t.Remark, t.ActivatedAt, t.Status, t.EffectiveFrom, t.EffectiveTo, t.PriceUnit, t.ReportUnitId, t.UnitsPerPriceUnit, t.Version, Details = Details(t), t.CreatedAt, t.UpdatedAt };
    public static bool Incomplete(AttributePriceDetails d) => d.Rows.Count == 0 || d.Rows.Any(r => !r.Confirmed || r.Amount == null || r.Note.Contains("待确认", StringComparison.Ordinal));

    // 管理端写操作低频，按厂串行锁避免不同编号插入的范围锁升级死锁。
    internal static async Task LockFactoryAsync(AppDbContext context, long factoryId)
    {
        if (!context.Database.IsSqlServer()) return;
        var resource = $"operation-price-list-factory-{factoryId}";
        await context.Database.ExecuteSqlInterpolatedAsync($"DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource={resource}, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=15000; IF @result<0 THROW 51000,N'价目表正在被修改，请稍后重试',1;");
    }

    public async Task<long> SaveAsync(long? id, PriceListInput input, long factoryId, byte? targetStatus = null)
    {
        Require(!string.IsNullOrWhiteSpace(input.Code) && input.Code.Length <= 64 && !string.IsNullOrWhiteSpace(input.Name) && input.Name.Length <= 128, "编号/名称必填且不得超长");
        Require(input.PricingType is 1 or 2 or 3, "计价方式无效");
        Require(input.Status <= 2 && (!input.EffectiveTo.HasValue || input.EffectiveTo.Value.Date >= input.EffectiveFrom.Date), "状态或有效日期无效");
        Require(input.Remark != null && input.Remark.Length <= 1000, "表头备注超过1000字");
        Require(!string.IsNullOrWhiteSpace(input.PriceUnit) && input.PriceUnit.Length <= 32 && input.UnitsPerPriceUnit is > 0 and <= 1000000 && decimal.Round(input.UnitsPerPriceUnit, 4) == input.UnitsPerPriceUnit, "计价单位/换算数无效");
        Require(input.PricingType == 1 || (input.UnitsPerPriceUnit == 1 && input.PriceUnit == (input.PricingType == 2 ? "小时" : "笔")), "计时/固定价目表换算数须为1，计价单位须为小时/笔");
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await LockFactoryAsync(db, factoryId);
        var table = id.HasValue ? await db.OperationPriceLists.SingleOrDefaultAsync(t => t.Id == id && t.FactoryId == factoryId) ?? throw ThrowHelper.BizUser("价目表不存在或非本厂") : new OperationPriceList { FactoryId = factoryId, CreatedAt = DateTime.Now };
        Require(!id.HasValue || table.Version == input.ExpectedVersion, "价目表已被修改，请重新打开后编辑");
        Require(!id.HasValue || Details(table).SchemaVersion == 2, "旧行级配置待转换，不可直接覆盖，请新建表级价目表");
        Require(!id.HasValue || table.Code == input.Code.Trim(), "已保存价目表编号不能修改，请复制新建");
        Require(table.ActivatedAt == null || table.OperationId == input.OperationId, "价目表首次启用后工序永久锁定，请复制新建");
        Require(table.ActivatedAt == null || table.PricingType == input.PricingType, "价目表首次启用后计价方式永久锁定，请复制新建");
        Require(await db.Operations.AnyAsync(o => o.Id == input.OperationId && o.FactoryId == factoryId) && await db.Units.AnyAsync(u => u.Id == input.ReportUnitId && u.FactoryId == factoryId), "工序/报工单位不存在或非本厂");
        Require(input.ProductId == null || await db.Products.AnyAsync(p => p.Id == input.ProductId && p.FactoryId == factoryId), "表头产品不存在或非本厂");
        Require(!await db.OperationPriceLists.AnyAsync(t => t.FactoryId == factoryId && t.Code == input.Code.Trim() && t.Id != id), "价目表编号已存在");
        var fields = await new OperationAttributeService(db).FieldsAsync(factoryId, input.OperationId);
        Validate(input.Details, fields, false, input.PricingType);
        var status = targetStatus ?? (table.Status == 2 ? (byte)2 : table.Status == 1 ? (byte)1 : Incomplete(input.Details) ? (byte)0 : (byte)1);
        Require(targetStatus == null || targetStatus is 1 or 2, "状态操作只接受启用或停用");
        Require(targetStatus != 2 || table.Status == 1, "仅启用表可以停用");
        Require(targetStatus != 1 || table.Status == 2, "仅停用表可以重新启用，草稿请保存");
        Validate(input.Details, fields, status == 1, input.PricingType);
        var options = await db.CustomFieldOptions.AsNoTracking().Where(o => fields.Keys.Contains(o.FieldId)).ToListAsync();
        foreach (var c in input.Details.Rows.SelectMany(r => r.Conditions).Concat(input.Details.Groups.SelectMany(g => g.Conditions)))
            if (fields[c.FieldId].FieldType == "single") Require(options.Any(o => o.FieldId == c.FieldId && o.Label == c.Value), "条件单选值不在字段选项内");
        if (status == 1)
            Require(!await db.OperationPriceLists.AnyAsync(t => t.FactoryId == factoryId && t.Id != id && t.OperationId == input.OperationId && t.ProductId == input.ProductId && t.Status == 1 && (!input.EffectiveTo.HasValue || t.EffectiveFrom <= input.EffectiveTo.Value.Date) && (!t.EffectiveTo.HasValue || t.EffectiveTo >= input.EffectiveFrom.Date)), "同产品范围已有日期重叠的启用价目表，请先停用或调整日期");
        var timeline = new PriceTimelineService(db);
        if (id.HasValue)
        {
            if (table.ActivatedAt != null || table.Status == 1)
            {
                var priorRows = Details(table).Rows.ToDictionary(x => x.RowId);
                Require(input.Details.Rows.All(x => !priorRows.TryGetValue(x.RowId,out var prior) || (x.Amount == prior.Amount && x.DeductPrice == prior.DeductPrice)), "已启用价表的价格请通过调整工价修改");
            }
            await timeline.EnsureBaselineAsync(table);
        }
        table.Code = input.Code.Trim(); table.Name = input.Name.Trim(); table.OperationId = input.OperationId; table.Status = status;
        table.PricingType = input.PricingType; table.ProductId = input.ProductId; table.Remark = input.Remark;
        if (status == 1) table.ActivatedAt ??= DateTime.Now;
        table.EffectiveFrom = input.EffectiveFrom.Date; table.EffectiveTo = input.EffectiveTo?.Date; table.PriceUnit = input.PriceUnit.Trim(); table.ReportUnitId = input.ReportUnitId;
        table.UnitsPerPriceUnit = input.UnitsPerPriceUnit; table.Details = OperationPricingService.Write(input.Details); table.Version = Guid.NewGuid().ToString("N"); table.UpdatedAt = DateTime.Now;
        if (!id.HasValue) db.OperationPriceLists.Add(table);
        await db.SaveChangesAsync();
        await timeline.RecordAsync(table, id.HasValue ? (table.EffectiveFrom > PriceTimelineService.Today ? table.EffectiveFrom : PriceTimelineService.Today) : table.EffectiveFrom, 0, fields);
        await tx.CommitAsync(); return table.Id;
    }

    public async Task SetStatusAsync(long id, byte status, string expectedVersion, long factoryId)
    {
        var t = await GetAsync(id, factoryId);
        if (Details(t).SchemaVersion != 2)
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await LockFactoryAsync(db, factoryId);
            var legacy = await db.OperationPriceLists.SingleAsync(x => x.Id == id && x.FactoryId == factoryId);
            Require(status == 2 && legacy.Status == 1, "旧行级配置只能停用，不能重新启用或修改");
            Require(legacy.Version == expectedVersion, "价目表已被修改，请刷新");
            legacy.Status = 2; legacy.Version = Guid.NewGuid().ToString("N"); legacy.UpdatedAt = DateTime.Now;
            await db.SaveChangesAsync(); await tx.CommitAsync(); return;
        }
        await SaveAsync(id, new PriceListInput { Code = t.Code, Name = t.Name, OperationId = t.OperationId, ProductId = t.ProductId, PricingType = t.PricingType, Remark = t.Remark, Status = t.Status, EffectiveFrom = t.EffectiveFrom, EffectiveTo = t.EffectiveTo, PriceUnit = t.PriceUnit, ReportUnitId = t.ReportUnitId, UnitsPerPriceUnit = t.UnitsPerPriceUnit, ExpectedVersion = expectedVersion, Details = Details(t) }, factoryId, status);
    }

    public async Task DeleteAsync(long id, string expectedVersion, long factoryId)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await LockFactoryAsync(db, factoryId);
        var t = await db.OperationPriceLists.SingleOrDefaultAsync(t => t.Id == id && t.FactoryId == factoryId) ?? throw ThrowHelper.BizUser("价目表不存在或非本厂");
        Require(t.Version == expectedVersion, "价目表已被修改，请刷新");
        Require(t.Status == 0 && t.ActivatedAt == null && Details(t).SchemaVersion == 2, "仅未启用过的新版草稿可以删除，其他表请停用");
        // 快照JSON为自包含依据；保守检查全部当前任务引用，不能因草稿状态误删。
        var snapshots = await db.WorkOrderOperations.Where(x => x.ProcessingPrice != null && db.WorkOrders.Any(o => o.Id == x.WorkOrderId && o.FactoryId == factoryId)).Select(x => x.ProcessingPrice!).ToListAsync();
        Require(!snapshots.Any(x => OperationPricingService.Read<ProcessingQuote>(x).AttributeEvidence?.PriceListId == id), "价目表已有工单引用，不能删除");
        db.OperationPriceRevisions.RemoveRange(await db.OperationPriceRevisions.Where(x => x.FactoryId == factoryId && x.PriceListId == id).ToListAsync());
        db.OperationPriceLists.Remove(t); await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
