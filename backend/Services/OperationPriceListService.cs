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
        Require(pricingType is 1 or 2 or 3, "价目表的计价类型设置无效。请到「工序价目表」编辑该价目表，选择系统支持的计价类型（计件、计时、固定等）后重新保存。");
        Require(details != null && details.SchemaVersion == 2 && details.Rows != null && details.Groups != null && details.RequiredFieldIds != null, "价目表的明细结构不符合要求。请到「工序价目表」编辑该价目表，检查收费组和价格行的层级关系是否正确。");
        if (pricingType != 1)
        {
            Require(details.Groups.Count == 0, "计时或固定类型的价目表不允许设置收费组。请到「工序价目表」编辑该价目表，移除收费组设置或改为计件类型。");
            Require(details.Rows.All(r => r.Kind != "extra"), "计时或固定类型的价目表不允许添加附加行。请到「工序价目表」编辑该价目表，移除附加行或改为计件类型。");
        }
        Require(details.Groups.All(g => g != null && g.Conditions != null) && details.Rows.All(r => r != null && r.Conditions != null && r.IncludedRowIds != null), "价目表的收费组或价格行为空。请到「工序价目表」编辑该价目表，至少添加一个收费组或价格行后再保存。");
        Require(details!.Rows.Count <= 1000 && (!enabling || details.Rows.Count > 0), "价目表最多支持1000行，且启用时不能为空。请检查行数是否超限，并确保启用状态下至少有一行有效数据。");
        Require(details.Groups.Select(g => g.Key).Distinct().Count() == details.Groups.Count && details.Groups.All(g => !string.IsNullOrWhiteSpace(g.Key) && !string.IsNullOrWhiteSpace(g.Name)), "价目表中有重复或为空的收费组代号。请到「工序价目表」编辑该价目表，确保每个收费组代号唯一且非空。");
        Require(details.Rows.Select(r => r.RowId).Distinct().Count() == details.Rows.Count && details.Rows.All(r => !string.IsNullOrWhiteSpace(r.RowId)), "价目表中有重复或为空的价格行编号。请到「工序价目表」编辑该价目表，确保每个价格行编号唯一且非空。");
        Require(details.RequiredFieldIds.All(fields.ContainsKey), "价目表的必需属性中引用了不属于当前工厂的产品字段。请到「工序价目表」编辑该价目表，移除或换成本厂产品的字段。");
        foreach (var c in details.Groups.SelectMany(g => g.Conditions).Concat(details.Rows.SelectMany(r => r.Conditions)))
        {
            Require(c != null, "价目表的取价条件不能为空。请到「工序价目表」编辑该价目表，至少设置一个取价条件后再保存。");
            Require(fields.ContainsKey(c.FieldId), "条件里引用的产品字段在本厂找不到。请到「产品定义」确认该字段是否已创建并归属本厂，再回到价目表编辑条件。");
            var field = fields[c.FieldId];
            Require(field.FieldType is "number" or "single", "取价条件只支持数字类型或单选类型的字段。请到「产品属性」检查所选字段类型，换成数字或单选字段后重新设置条件。");
            Require(c.Operator is "eq" or "range", "条件的匹配方式只支持「等于」和「区间」两种。请把当前条件的匹配方式改成等于或区间。");
            if (c.Operator == "eq")
            {
                Require(!string.IsNullOrWhiteSpace(c.Value), "选了「等于」条件但没有填具体值，系统不会当作不限处理。请在条件值栏填入明确的匹配值，或取消该条件。");
                if (field.FieldType == "number") { Require(Number(c.Value, out var n) && n >= 0, $"{field.FieldName}的条件值必须是非负数字（不小于0）。请检查对应条件行，把负数改成合法的非负值。"); Require(field.FieldKey is not ("print_panels" or "rib_count") || decimal.Truncate(n) == n, $"{field.FieldName}的条件值必须是整数，不能带小数。请检查对应条件行，去掉小数部分后保存。"); }
            }
            else
                Require(field.FieldType == "number" && (!c.Min.HasValue || c.Min >= 0) && (!c.Max.HasValue || c.Max >= 0) && (c.Min.HasValue || c.Max.HasValue) &&
                    (!c.Min.HasValue || !c.Max.HasValue || c.Min < c.Max || (c.Min == c.Max && c.IncludeMin && c.IncludeMax)), "区间条件的上下界设置不正确，或所选字段不支持区间匹配。请检查区间起止值是否合法、字段是否为数字类型。");
        }
        foreach (var row in details.Rows)
        {
            Require(row.ProductId == null, "新版价目表的产品只能在表头选择，不能在每一行单独指定产品。请到价目表表头选好产品，再把行上指定的产品删掉。");
            Require(row.Label != null && row.Label.Length <= 256 && row.Note != null && row.Source != null, "某一行的规格、备注或来源填得无效，或规格文字超过256字上限。请逐行检查规格、备注、来源，缩短超长内容或修正无效值。");
            Require(!enabling || !row.Note.Contains("待确认", StringComparison.Ordinal), $"行{row.RowId}的备注还是待确认状态，不能启用。请先编辑该行把备注确认完毕，再启用整张价目表。");
            Require(row.Kind == "extra" || row.GroupKey == null, "基础价行和最终价行不允许挂收费组。请把这两类行上的收费组关联去掉，收费组只能用在附加行上。");
            Require(row.Kind is "base" or "extra" or "final", "价格性质填的值不在系统允许范围内。请检查该行价格性质的下拉选项，选择一个有效值。");
            Require(row.Conditions != null && row.IncludedRowIds != null, "取价条件列表和包含项列表不能同时为空。请至少填写一个匹配条件或一个包含项，否则无法确定这张表适用于哪些工序。");
            Require(!enabling || (row.Confirmed && row.Amount.HasValue && !string.IsNullOrWhiteSpace(row.Source)), $"行{row.RowId}处于待定状态，或缺少金额、来源信息，无法启用。请补全该行的金额和来源，取消待定标记后再启用。");
            Require((!row.Amount.HasValue || Money(row.Amount.Value)) && Money(row.DeductPrice), "金额必须不小于0，且最多保留4位小数。请检查所有价格行的金额，修正超出精度或负数的值。");
            Require(row.Kind != "extra" || (details.Groups.Any(g => g.Key == row.GroupKey) && row.DeductPrice == 0 && row.IncludedRowIds.Count == 0), "附加行必须归属于某个收费组，且附加行本身不能设置扣价或包含项。请给附加行选一个收费组，并移除其上的扣价/包含项设置。");
            Require(row.IncludedRowIds.Distinct().Count() == row.IncludedRowIds.Count && row.IncludedRowIds.All(id => details.Rows.Any(r => r.RowId == id && r.Kind == "extra")), "包含项只能引用同一张价目表内的附加行，且不能重复引用同一附加行。请检查包含项列表，确保引用的都是本表附加行且无重复。");
        }
        Require(!enabling || details.Rows.Any(r => r.Kind != "extra"), "要启用的价目表必须至少有一条基础价或最终价行。请先添加一条基础价或最终价，再执行启用。");
        if (enabling)
        {
            for (var i = 0; i < details.Rows.Count; i++)
            for (var j = i + 1; j < details.Rows.Count; j++)
            {
                var a = details.Rows[i]; var b = details.Rows[j];
                if ((a.Kind == "extra") != (b.Kind == "extra")) continue;
                if (a.Kind == "extra" && a.GroupKey != b.GroupKey) continue;
                Require(!MayOverlap(a.Conditions, b.Conditions, fields), $"价格行{a.RowId}与{b.RowId}的匹配条件重叠，系统无法判断用哪一行。请调整其中一行的条件范围使其不再重叠，然后再启用。");
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
                Require(Number(value, out var n), $"产品属性{fields[c.FieldId].FieldName}不是有效数字，无法参与计算。请到「产品定义」检查该产品的属性值，确保数字类属性填了合法数值。");
                if (c.Operator == "eq") { if (!Number(c.Value, out var expected) || n != expected) return false; }
                else if ((c.Min.HasValue && (n < c.Min || (n == c.Min && !c.IncludeMin))) || (c.Max.HasValue && (n > c.Max || (n == c.Max && !c.IncludeMax)))) return false;
            }
            else if (!string.Equals(value, c.Value, StringComparison.Ordinal)) return false;
        }
        foreach (var c in list)
            Require(values.TryGetValue(c.FieldId, out var value) && !string.IsNullOrWhiteSpace(value), $"计算工价需要用到产品属性{fields[c.FieldId].FieldName}，但该属性值为空，无法确定工价。请到「产品定义」补齐该产品缺失的属性值后重试。");
        return true;
    }

    public static ProcessingQuote Calculate(OperationPriceList table, long productId, long reportUnitId, Dictionary<long, string?> values, Dictionary<long, SysCustomField> fields, DateTime date)
    {
        var details = Details(table);
        Validate(details, fields, true, table.PricingType);
        Require(table.Status == 1 && date.Date >= table.EffectiveFrom.Date && (!table.EffectiveTo.HasValue || date.Date <= table.EffectiveTo.Value.Date), "这张价目表还没启用，或已经过了有效期。请到「工序价目表」页面检查该表的启用状态和有效期。");
        Require(table.ReportUnitId == reportUnitId && table.UnitsPerPriceUnit > 0, "报工单位和折算方式对不上，系统没法算价。请联系管理员检查这道工序单价表里的单位和折算设置。");
        Require(table.PricingType == 1 || (table.UnitsPerPriceUnit == 1 && table.PriceUnit == (table.PricingType == 2 ? "小时" : "笔")), "计时或固定类型的价目表，换算数必须为1，计价单位必须是小时或笔。请到价目表编辑页修正换算数和计价单位。");
        foreach (var id in details.RequiredFieldIds) Require(values.TryGetValue(id, out var value) && !string.IsNullOrWhiteSpace(value), $"计算工价时缺少产品属性：{fields[id].FieldName}。请到「产品定义」找到该产品，补齐缺失属性后重新取价。");
        Require(table.ProductId == null || table.ProductId == productId, "当前使用的价目表不属于这个产品。请确认价目表表头选的产品是否正确，或换一张属于该产品的价目表。");
        var applicable = details.Rows;
        var primary = applicable.Where(r => r.Kind != "extra" && Matches(r.Conditions, values, fields)).ToList();
        Require(primary.Count == 1, primary.Count == 0 ? $"产品{productId}·工序{table.OperationId}·表{table.Code}缺规格的价：{string.Join("；", details.Rows.Where(r => r.Kind != "extra").SelectMany(r => r.Conditions).Select(c => $"{fields[c.FieldId].FieldName}实际={values.GetValueOrDefault(c.FieldId) ?? "未填"}，要求{c.Operator}:{c.Value ?? $"{c.Min}~{c.Max}"}"))}" : "有多张价目表同时命中了这道工序的基础/最终工价，系统无法判断用哪张。请到「工序价目表」消除重复或冲突的取价条件。");
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
            Require(extras.Count <= 1, $"收费组“{group.Name}”命中了多行价格，无法确定用哪个。请到价目表检查该收费组的区间或条件设置，消除重复命中。");
            Require(!group.Required || extras.Count == 1, $"收费组“{group.Name}”没有匹配到任何价格行，不能按免费处理。请到价目表中为该收费组补充对应价格行。");
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
        Require(quote.UnitPrice / quote.UnitsPerPriceUnit <= 1000000 && quote.DeductPrice / quote.UnitsPerPriceUnit <= 1000000, "换算后的单价超出了系统允许范围。请检查价目表的换算数和原始单价，确保换算结果在合理区间内。");
        quote.QuoteToken = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(OperationPricingService.Write(quote))));
        return quote;
    }

    public static OperationPriceList SelectTable(IEnumerable<OperationPriceList> candidates, long productId, long operationId)
    {
        var all = candidates.ToList();
        Require(!all.Any(t => Details(t).SchemaVersion != 2), $"工序{operationId}还有旧版行级价格设置没转换完，新单无法取价。请查看系统提示的转换清单，完成旧设置迁移后再建新单。");
        var specific = all.Where(t => t.ProductId == productId).ToList();
        var common = all.Where(t => t.ProductId == null).ToList();
        Require(specific.Count <= 1 && common.Count <= 1, $"工序{operationId}在同一范围内有多张价目表同时生效，产生冲突。请到「工序价目表」停用多余表或调整有效期，确保同一时段只有一张表生效。");
        var selected = specific.SingleOrDefault() ?? common.SingleOrDefault();
        Require(selected != null, $"产品{productId}·工序{operationId}没有找到任何生效的专属或通用价目表，不能按普通工价代替。请到「工序价目表」新建一张并启用。");
        return selected!;
    }

    public async Task<ProcessingQuote> PreviewAsync(long productId, long operationId, long factoryId, DateTime? date = null,
        Dictionary<long, string?>? operationAttributes = null, long? workOrderId = null, string? expectedVersion = null)
    {
        var p = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == productId && p.FactoryId == factoryId) ?? throw ThrowHelper.BizUser("预览取价时指定的产品不存在或不属于本厂。请检查输入的产品编号是否正确，或到「产品定义」确认该产品归属。");
        Require(await db.Operations.AnyAsync(o => o.Id == operationId && o.FactoryId == factoryId) && await db.RoutingSteps.AnyAsync(s => s.RoutingId == p.RoutingId && s.OperationId == operationId), "指定的工序不在该产品的工艺路线中。请到「产品定义」查看该产品的工艺路线，确认工序是否已加入路线。");
        if (workOrderId.HasValue)
        {
            Require(operationAttributes == null, "已有工单的预览不接受手动覆盖工艺内容。请直接用工单自带的工艺信息预览，不要额外填写工艺覆盖值。");
            Require(await db.WorkOrders.AnyAsync(o => o.Id == workOrderId && o.FactoryId == factoryId && o.ProductId == productId), "指定的工单不存在、所属产品与当前不一致或不属于本厂。请核实工单编号与产品的对应关系，确保工单归属正确。");
            Require(await db.WorkOrderOperations.AnyAsync(t => t.WorkOrderId == workOrderId && t.OperationId == operationId), "指定的工序不属于当前工单。请检查工单包含的工序列表，确认所选工序确实在这张工单内。");
            // docs/424：无锁价时由 BudgetAsync 懒绑定，不再要求先手工保存/复制
            return await new OperationPricingService(db).BudgetAsync(workOrderId.Value, operationId, factoryId);
        }
        var day = (date ?? DateTime.Today).Date;
        var attrs = new OperationAttributeService(db);
        var fields = await attrs.FieldsAsync(factoryId, operationId);
        var values = await new ProductAttributeService(db).GetAsync(productId, factoryId);
        foreach (var v in await attrs.ValidateAsync(factoryId, operationId, operationAttributes)) { if (v.Value != null) values.Add(v.Key, v.Value); }
        foreach (var (id, value) in values.Where(v => !string.IsNullOrWhiteSpace(v.Value)))
        {
            var f = fields[id];
            if (f.FieldType == "single") Require(await db.CustomFieldOptions.AnyAsync(o => o.FieldId == id && o.Label == value), $"属性{f.FieldName}填的选项无效。请到「产品属性」查看该字段的可选值列表，选择合法选项。");
        }
        var quote = await new PriceTimelineService(db).QuoteAsync(factoryId,productId,operationId,p.UnitId ?? 0,values,day);
        Require(expectedVersion == null || quote.Version == expectedVersion, "你上次预览之后价目表被人改过，之前的预览结果已失效。请点「重新预览」获取最新价格后再保存。");
        return quote;
    }

    public async Task<OperationPriceList> GetAsync(long id, long factoryId) => await db.OperationPriceLists.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && t.FactoryId == factoryId) ?? throw ThrowHelper.BizUser("指定的价目表不存在或不属于本厂。请到「工序价目表」列表确认该表是否存在及归属。");
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
        Require(!string.IsNullOrWhiteSpace(input.Code) && input.Code.Length <= 64 && !string.IsNullOrWhiteSpace(input.Name) && input.Name.Length <= 128, "价目表的编号和名称都必须填写，且不能超长。请填写完整的编号和名称后再保存。");
        Require(input.PricingType is 1 or 2 or 3, "计价方式无效");
        Require(input.Status <= 2 && (!input.EffectiveTo.HasValue || input.EffectiveTo.Value.Date >= input.EffectiveFrom.Date), "价目表的状态或有效期日期不合法。请检查启用/停用状态以及起止日期格式是否正确。");
        Require(input.Remark != null && input.Remark.Length <= 1000, "价目表表头备注超过1000字上限。请把备注精简到1000字以内再保存。");
        Require(!string.IsNullOrWhiteSpace(input.PriceUnit) && input.PriceUnit.Length <= 32 && input.UnitsPerPriceUnit is > 0 and <= 1000000 && decimal.Round(input.UnitsPerPriceUnit, 4) == input.UnitsPerPriceUnit, "计价单位或换算数填得不合法。请检查这两项，确认填了系统支持的单位和正数换算值。");
        Require(input.PricingType == 1 || (input.UnitsPerPriceUnit == 1 && input.PriceUnit == (input.PricingType == 2 ? "小时" : "笔")), "计时或固定类型的价目表，换算数必须为1、计价单位必须为小时或笔。请修正这两项后保存。");
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await LockFactoryAsync(db, factoryId);
        var table = id.HasValue ? await db.OperationPriceLists.SingleOrDefaultAsync(t => t.Id == id && t.FactoryId == factoryId) ?? throw ThrowHelper.BizUser("指定的价目表不存在或不属于本厂。请到「工序价目表」列表确认该表是否存在及归属。") : new OperationPriceList { FactoryId = factoryId, CreatedAt = DateTime.Now };
        Require(!id.HasValue || table.Version == input.ExpectedVersion, "这张价目表在你编辑期间被别人改过。请刷新页面重新加载最新版本后再编辑保存。");
        Require(!id.HasValue || Details(table).SchemaVersion == 2, "该工序还有旧版行级价格设置没转换完，不能直接覆盖保存。请先完成旧设置转换，或新建一张表级价目表替代。");
        Require(!id.HasValue || table.Code == input.Code.Trim(), "已保存过的价目表编号不允许修改。如需更改编号，请用「复制新建」生成新表后再改编号。");
        Require(table.ActivatedAt == null || table.OperationId == input.OperationId, "价目表首次启用后，关联的工序就不能再改了。如需更换工序，请用「复制新建」创建新表并选择正确工序。");
        Require(table.ActivatedAt == null || table.PricingType == input.PricingType, "价目表首次启用后，计件/计时/固定的计价类型就锁定不能再改。如需更换，请用「复制新建」创建新表。");
        Require(await db.Operations.AnyAsync(o => o.Id == input.OperationId && o.FactoryId == factoryId) && await db.Units.AnyAsync(u => u.Id == input.ReportUnitId && u.FactoryId == factoryId), "保存单价表前，请确认工序和报工单位都存在且属于本厂。");
        Require(input.ProductId == null || await db.Products.AnyAsync(p => p.Id == input.ProductId && p.FactoryId == factoryId), "价目表表头选择的产品不存在或不属于本厂。请到「产品定义」确认产品编号正确且归属本厂后重新选择。");
        Require(!await db.OperationPriceLists.AnyAsync(t => t.FactoryId == factoryId && t.Code == input.Code.Trim() && t.Id != id), "这个价目表编号已被占用。请换一个不重复的编号，或到「工序价目表」列表查看已有编号避免重复。");
        var fields = await new OperationAttributeService(db).FieldsAsync(factoryId, input.OperationId);
        Validate(input.Details, fields, false, input.PricingType);
        var status = targetStatus ?? (table.Status == 2 ? (byte)2 : table.Status == 1 ? (byte)1 : Incomplete(input.Details) ? (byte)0 : (byte)1);
        Require(targetStatus == null || targetStatus is 1 or 2, "状态变更操作只接受「启用」或「停用」。请确认要执行的是启用还是停用，不要选其他状态。");
        Require(targetStatus != 2 || table.Status == 1, "只有当前处于启用状态的价目表才能执行停用操作。请确认该表当前确实是启用状态。");
        Require(targetStatus != 1 || table.Status == 2, "只有已停用的价目表才能重新启用；草稿状态的表请先保存再启用。请按当前状态选择正确操作。");
        Validate(input.Details, fields, status == 1, input.PricingType);
        var options = await db.CustomFieldOptions.AsNoTracking().Where(o => fields.Keys.Contains(o.FieldId)).ToListAsync();
        foreach (var c in input.Details.Rows.SelectMany(r => r.Conditions).Concat(input.Details.Groups.SelectMany(g => g.Conditions)))
            if (fields[c.FieldId].FieldType == "single") Require(options.Any(o => o.FieldId == c.FieldId && o.Label == c.Value), "条件中选择的单选值不在该字段预设选项内。请到「产品属性」查看该单选字段的可选值，选择合法选项。");
        if (status == 1)
            Require(!await db.OperationPriceLists.AnyAsync(t => t.FactoryId == factoryId && t.Id != id && t.OperationId == input.OperationId && t.ProductId == input.ProductId && t.Status == 1 && (!input.EffectiveTo.HasValue || t.EffectiveFrom <= input.EffectiveTo.Value.Date) && (!t.EffectiveTo.HasValue || t.EffectiveTo >= input.EffectiveFrom.Date)), "同一产品范围内已有一张启用中的价目表与当前表有效期重叠。请先停用重叠的旧表，或调整当前表的起止日期避开重叠。");
        var timeline = new PriceTimelineService(db);
        if (id.HasValue)
        {
            if (table.ActivatedAt != null || table.Status == 1)
            {
                var priorRows = Details(table).Rows.ToDictionary(x => x.RowId);
                Require(input.Details.Rows.All(x => !priorRows.TryGetValue(x.RowId,out var prior) || (x.Amount == prior.Amount && x.DeductPrice == prior.DeductPrice)), "已启用的价目表不能直接编辑价格行。请通过「调整工价」功能修改已启用表的价格。");
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
            Require(status == 2 && legacy.Status == 1, "旧版行级价格设置只允许停用，不能重新启用或修改内容。如需调整，请新建一张新版表级价目表替代。");
            Require(legacy.Version == expectedVersion, "这张价目表在你操作期间被其他人修改了。请刷新页面获取最新版本后再执行状态变更。");
            legacy.Status = 2; legacy.Version = Guid.NewGuid().ToString("N"); legacy.UpdatedAt = DateTime.Now;
            await db.SaveChangesAsync(); await tx.CommitAsync(); return;
        }
        await SaveAsync(id, new PriceListInput { Code = t.Code, Name = t.Name, OperationId = t.OperationId, ProductId = t.ProductId, PricingType = t.PricingType, Remark = t.Remark, Status = t.Status, EffectiveFrom = t.EffectiveFrom, EffectiveTo = t.EffectiveTo, PriceUnit = t.PriceUnit, ReportUnitId = t.ReportUnitId, UnitsPerPriceUnit = t.UnitsPerPriceUnit, ExpectedVersion = expectedVersion, Details = Details(t) }, factoryId, status);
    }

    public async Task DeleteAsync(long id, string expectedVersion, long factoryId)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await LockFactoryAsync(db, factoryId);
        var t = await db.OperationPriceLists.SingleOrDefaultAsync(t => t.Id == id && t.FactoryId == factoryId) ?? throw ThrowHelper.BizUser("指定的价目表不存在或不属于本厂。请到「工序价目表」列表确认该表是否存在及归属。");
        Require(t.Version == expectedVersion, "你要编辑的价目表不存在，或已被他人修改、删除，当前内容已过时。请关闭后重新打开该价目表，按最新版本编辑。");
        Require(t.Status == 0 && t.ActivatedAt == null && Details(t).SchemaVersion == 2, "只有从未启用过的新版草稿价目表才能删除；已启用过的表请用「停用」处理，不要删除。");
        // 快照JSON为自包含依据；保守检查全部当前任务引用，不能因草稿状态误删。
        var snapshots = await db.WorkOrderOperations.Where(x => x.ProcessingPrice != null && db.WorkOrders.Any(o => o.Id == x.WorkOrderId && o.FactoryId == factoryId)).Select(x => x.ProcessingPrice!).ToListAsync();
        Require(!snapshots.Any(x => OperationPricingService.Read<ProcessingQuote>(x).AttributeEvidence?.PriceListId == id), "这张价目表已被工单引用，不能删除。如确实不需要，请先停用该表；若要彻底移除，须先解除所有关联工单的引用。");
        db.OperationPriceRevisions.RemoveRange(await db.OperationPriceRevisions.Where(x => x.FactoryId == factoryId && x.PriceListId == id).ToListAsync());
        db.OperationPriceLists.Remove(t); await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
