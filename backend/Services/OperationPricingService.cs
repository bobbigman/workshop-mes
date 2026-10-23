using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public class ProcessingConfig
{
    public string Version { get; set; } = "";
    public List<ProcessingGroup> Groups { get; set; } = new();
    public List<ProcessingBase> Bases { get; set; } = new();
}
public class ProcessingGroup
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<ProcessingOption> Options { get; set; } = new();
}
public class ProcessingOption
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal? Extra { get; set; }
}
public class ProcessingBase
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public long ProductId { get; set; }
    public decimal? Price { get; set; }
    public decimal DeductPrice { get; set; }
    public string PriceUnit { get; set; } = "";
    public long ReportUnitId { get; set; }
    public decimal UnitsPerPriceUnit { get; set; } = 1;
    // 组ID -> 可选值ID；显式列出允许范围，不用空白通配。
    public Dictionary<string, List<string>> Allowed { get; set; } = new();
    public List<string> Included { get; set; } = new();
}
public class ProcessingChoice
{
    public string BaseId { get; set; } = "";
    public Dictionary<string, string> Values { get; set; } = new();
    public string? QuoteToken { get; set; }
}
public record ProcessingLine(string Group, string Option, decimal Extra, bool Included);
public class ProcessingQuote
{
    public long OperationId { get; set; }
    public string Version { get; set; } = "";
    public ProcessingChoice Choice { get; set; } = new();
    public string BaseName { get; set; } = "";
    public decimal BasePrice { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DeductPrice { get; set; }
    public string PriceUnit { get; set; } = "";
    public long ReportUnitId { get; set; }
    public decimal UnitsPerPriceUnit { get; set; }
    public List<ProcessingLine> Lines { get; set; } = new();
    public string QuoteToken { get; set; } = "";
}

/// <summary>方案A：只负责加工选项定价与任务锁价，报工金额仍共用工资计算底座。</summary>
public class OperationPricingService(AppDbContext db)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static T Read<T>(string value)
    {
        try { return JsonSerializer.Deserialize<T>(value, Json) ?? throw ThrowHelper.BizUser("加工计价资料为空，请核查"); }
        catch (JsonException ex) { throw ThrowHelper.BizUser($"加工计价资料损坏：{ex.Message}"); }
    }
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok, string message) { if (!ok) throw ThrowHelper.BizUser(message); }
    private static bool Money(decimal? value) => value is >= 0 and <= 1000000 && decimal.Round(value.Value, 4) == value;
    private static bool Named(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128;

    public static void Validate(ProcessingConfig config)
    {
        Require(config.Groups != null && config.Bases != null, "加工计价分组与基础价不能为空");
        Require(config.Groups!.Count <= 12 && config.Bases!.Count is > 0 and <= 500, "最多12个加工组、500个基础价，至少维护一个基础价");
        var ids = new HashSet<string>();
        foreach (var group in config.Groups)
        {
            Require(group != null, "加工组不能为空");
            Require(Named(group.Id) && Named(group.Name) && ids.Add(group.Id), "加工组编号/名称无效或重复");
            Require(group.Options != null && group.Options.Count is > 0 and <= 30, "每组须有1至30个选项");
            foreach (var option in group.Options!)
                Require(option != null && Named(option.Id) && Named(option.Name) && ids.Add(option.Id) && Money(option.Extra), "选项编号重复、名称无效或加价未填写（零加价请明确填0）");
        }
        var baseIds = new HashSet<string>();
        foreach (var price in config.Bases)
        {
            Require(price != null, "基础价不能为空");
            Require(Named(price.Id) && Named(price.Name) && baseIds.Add(price.Id) && price.ProductId > 0 && Money(price.Price) && Money(price.DeductPrice), "基础价编号/名称/产品/金额无效");
            Require(Named(price.PriceUnit) && price.ReportUnitId > 0 && price.UnitsPerPriceUnit is > 0 and <= 1000000 && decimal.Round(price.UnitsPerPriceUnit, 4) == price.UnitsPerPriceUnit, "请明确计价单位、报工单位与换算数量（最多4位小数）");
            Require(price.Allowed != null && price.Included != null && price.Allowed.Count == config.Groups.Count, "每个基础价必须明确各组允许选项");
            var allowed = new HashSet<string>();
            foreach (var group in config.Groups)
            {
                Require(price.Allowed!.TryGetValue(group.Id, out var values) && values != null && values.Count > 0, $"基础价“{price.Name}”未指定“{group.Name}”的允许选项");
                var values2 = price.Allowed[group.Id];
                Require(values2.Distinct().Count() == values2.Count && values2.All(id => group.Options.Any(o => o.Id == id)), "基础价含重复或失效选项");
                allowed.UnionWith(values2);
            }
            Require(price.Included!.Distinct().Count() == price.Included.Count && price.Included.All(allowed.Contains), "已包含项目必须属于当前基础价允许选项");
        }
    }

    public static ProcessingQuote Calculate(ProcessingConfig config, long operationId, long productId, long reportUnitId, ProcessingChoice choice)
    {
        Validate(config);
        var price = config.Bases.SingleOrDefault(b => b.Id == choice.BaseId && b.ProductId == productId)
            ?? throw ThrowHelper.BizUser("基础价不适用于当前产品，请重新选择");
        Require(price.ReportUnitId == reportUnitId, "产品报工单位已变化，请核查基础价的换算设置");
        Require(choice.Values != null && choice.Values.Count == config.Groups.Count, "请为每个加工组选一个值");
        var quote = new ProcessingQuote { OperationId = operationId, Version = config.Version,
            Choice = new ProcessingChoice { BaseId = choice.BaseId, Values = choice.Values!.OrderBy(v => v.Key).ToDictionary(v => v.Key, v => v.Value) },
            BaseName = price.Name, BasePrice = price.Price!.Value, UnitPrice = price.Price.Value,
            DeductPrice = price.DeductPrice, PriceUnit = price.PriceUnit, ReportUnitId = price.ReportUnitId, UnitsPerPriceUnit = price.UnitsPerPriceUnit };
        foreach (var group in config.Groups)
        {
            Require(choice.Values!.TryGetValue(group.Id, out var id) && price.Allowed[group.Id].Contains(id), $"“{group.Name}”未选或不适用于此基础价");
            var option = group.Options.Single(o => o.Id == choice.Values[group.Id]);
            var included = price.Included.Contains(option.Id);
            var extra = included ? 0 : option.Extra!.Value;
            quote.Lines.Add(new(group.Name, option.Name, extra, included));
            quote.UnitPrice += extra;
        }
        Require(quote.UnitPrice / quote.UnitsPerPriceUnit <= 1000000 && quote.DeductPrice / quote.UnitsPerPriceUnit <= 1000000,
            "换算后的每报工单位单价或扣价超过100万元，请核查单位和金额");
        quote.QuoteToken = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Write(quote))));
        return quote;
    }

    public async Task<ProcessingConfig?> GetAsync(long operationId, long factoryId)
    {
        var op = await db.Operations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == operationId && o.FactoryId == factoryId)
            ?? throw ThrowHelper.BizUser("工序不存在或不属于本厂");
        return op.PricingConfig == null ? null : Read<ProcessingConfig>(op.PricingConfig);
    }
    public async Task SaveAsync(long operationId, long factoryId, ProcessingConfig config)
    {
        Validate(config);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var op = await db.Operations.FirstOrDefaultAsync(o => o.Id == operationId && o.FactoryId == factoryId)
            ?? throw ThrowHelper.BizUser("工序不存在或不属于本厂");
        Require((op.PricingConfig == null ? "" : Read<ProcessingConfig>(op.PricingConfig).Version) == config.Version, "加工计价已被其他人修改，请重新打开后编辑");
        var products = config.Bases.Select(b => b.ProductId).Distinct().ToList();
        Require(await db.Products.CountAsync(p => p.FactoryId == factoryId && products.Contains(p.Id)) == products.Count, "基础价含非本厂产品");
        var units = config.Bases.Select(b => b.ReportUnitId).Distinct().ToList();
        Require(await db.Units.CountAsync(u => u.FactoryId == factoryId && units.Contains(u.Id)) == units.Count, "报工单位不存在或不属于本厂");
        var productUnits = await db.Products.Where(p => products.Contains(p.Id) && p.FactoryId == factoryId).ToDictionaryAsync(p => p.Id, p => p.UnitId);
        Require(config.Bases.All(b => productUnits[b.ProductId] == b.ReportUnitId), "基础价报工单位必须等于产品当前单位");
        config.Version = Guid.NewGuid().ToString("N");
        op.PricingConfig = Write(config);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
    public async Task<List<object>> ProductAsync(long productId, long factoryId)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId && p.FactoryId == factoryId)
            ?? throw ThrowHelper.BizUser("产品不存在或不属于本厂");
        var operations = await (from s in db.RoutingSteps.AsNoTracking()
            join o in db.Operations.AsNoTracking() on s.OperationId equals o.Id
            where s.RoutingId == product.RoutingId && o.FactoryId == factoryId && o.PricingConfig != null
            orderby s.Seq select o).ToListAsync();
        return operations.Select(o => (object)new { o.Id, o.Name, Config = Read<ProcessingConfig>(o.PricingConfig!), product.UnitId }).ToList();
    }
    public async Task<ProcessingQuote> PreviewAsync(long operationId, long productId, long factoryId, ProcessingChoice choice)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId && p.FactoryId == factoryId)
            ?? throw ThrowHelper.BizUser("产品不存在或不属于本厂");
        Require(await db.RoutingSteps.AnyAsync(s => s.RoutingId == product.RoutingId && s.OperationId == operationId), "工序不在产品当前工艺中");
        var config = await GetAsync(operationId, factoryId) ?? throw ThrowHelper.BizUser("该工序未配置加工计价");
        return Calculate(config, operationId, productId, product.UnitId ?? 0, choice);
    }
    public async Task BindAsync(ProdWorkOrder order, Dictionary<long, ProcessingChoice>? choices)
    {
        var tasks = await db.WorkOrderOperations.Where(t => t.WorkOrderId == order.Id).ToListAsync();
        Require(choices == null || choices.Keys.All(id => tasks.Any(t => t.OperationId == id)), "加工选择含非本工单工序");
        foreach (var task in tasks)
        {
            var choice = choices?.GetValueOrDefault(task.OperationId);
            if (choice != null && task.ProcessingPrice != null)
            {
                var old = Read<ProcessingQuote>(task.ProcessingPrice);
                if (SameChoice(old.Choice, choice) && choice.QuoteToken == old.QuoteToken) continue;
            }
            if (choice == null && task.ProcessingPrice != null) continue;
            if (choice == null && await db.Reports.AnyAsync(r => r.OrderId == order.Id && r.OperationId == task.OperationId)) continue;
            var config = await GetAsync(task.OperationId, order.FactoryId);
            if (config == null)
            {
                Require(choice == null, "该工序未配置加工计价，不能提交加工选择");
                continue;
            }
            Require(choice != null, $"请为工序 {task.OperationId} 补齐加工选项和工价");
            Require(!await db.Reports.AnyAsync(r => r.OrderId == order.Id && r.OperationId == task.OperationId), "该工序已有报工，禁止更改加工选择或重新计价");
            var quote = await PreviewAsync(task.OperationId, order.ProductId, order.FactoryId, choice!);
            Require(quote.QuoteToken == choice!.QuoteToken, "加工价格已变化或未预览，请重新预览后保存");
            task.ProcessingPrice = Write(quote);
        }
        await db.SaveChangesAsync();
    }
    public static bool SameChoice(ProcessingChoice a, ProcessingChoice b) => a.Values != null && b.Values != null && a.BaseId == b.BaseId && a.Values.Count == b.Values.Count && a.Values.All(v => b.Values.GetValueOrDefault(v.Key) == v.Value);

    public async Task<ReportPriceSnapshot?> LockedAsync(long orderId, long operationId, long factoryId)
    {
        var task = await (from t in db.WorkOrderOperations.AsNoTracking() join o in db.WorkOrders.AsNoTracking() on t.WorkOrderId equals o.Id
            where o.Id == orderId && o.FactoryId == factoryId && t.OperationId == operationId select t).FirstOrDefaultAsync();
        if (task?.ProcessingPrice != null)
        {
            var quote = Read<ProcessingQuote>(task.ProcessingPrice);
            var currentUnit = await (from o in db.WorkOrders join p in db.Products on o.ProductId equals p.Id
                where o.Id == orderId && o.FactoryId == factoryId select p.UnitId).FirstAsync();
            Require(currentUnit == quote.ReportUnitId, "产品报工单位与锁价依据不一致，请核查单位；不可自动套价");
            return new ReportPriceSnapshot(-task.Id, 1, quote.UnitPrice, quote.DeductPrice) { Processing = quote };
        }
        if ((await GetAsync(operationId, factoryId)) != null)
        {
            // 已有旧报工的历史任务继续旧计价，不能半途切换口径；无报工任务须先补齐锁价。
            if (task != null && await db.Reports.AnyAsync(r => r.OrderId == orderId && r.OperationId == operationId && r.FactoryId == factoryId)) return null;
            throw ThrowHelper.BizUser("该工序尚未锁定加工工价，请管理员在PC工单编辑中补齐；不能按零价报工");
        }
        return null;
    }
}
