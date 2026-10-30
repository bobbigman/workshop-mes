using System.Text.Json;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;
using Microsoft.EntityFrameworkCore;

namespace ahu.MicrosoftMes.Services;

public record ProcessingLine(string Group, string Option, decimal Extra, bool Included);
public class ProcessingQuote
{
    public long? PriceRevisionId { get; set; }
    public Dictionary<long, SysCustomField>? FieldDefinitions { get; set; }
    public int SchemaVersion { get; set; }
    public long FactoryId { get; set; }
    public long ProductId { get; set; }
    public byte PriceType { get; set; } = 1;
    public AttributeQuoteEvidence? AttributeEvidence { get; set; }
    public long OperationId { get; set; }
    public string Version { get; set; } = "";
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

/// <summary>唯一工序价目表的任务锁价与报工快照，不含旧加工手选/普通工价回退。</summary>
public class OperationPricingService(AppDbContext db)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static T Read<T>(string value)
    {
        try { return JsonSerializer.Deserialize<T>(value, Json) ?? throw ThrowHelper.BizUser("这张价目表没有任何价格行可供计算。请到「工序价目表」打开该表，补充至少一条基础价或最终价行。"); }
        catch (JsonException ex) { throw ThrowHelper.BizUser($"这张价目表的报价资料损坏，无法正常读取。请联系技术人员检查该价目表数据是否完整，必要时重建该表。"); }
    }
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Json);

    public static void ValidateQuote(ProcessingQuote quote)
    {
        OperationPriceListService.Require(quote.SchemaVersion == 2 && quote.PriceType is 1 or 2 or 3 && quote.FactoryId > 0 && quote.ProductId > 0 && quote.OperationId > 0 && quote.AttributeEvidence?.PriceListId > 0 && !string.IsNullOrWhiteSpace(quote.Version), "该工序缺少新版价目表的锁价依据，无法自动定价。请到「工序价目表」确认有生效价目表覆盖该工序，然后重新建单或补锁价。");
        OperationPriceListService.Require(quote.UnitPrice >= 0 && quote.DeductPrice >= 0 && quote.UnitsPerPriceUnit > 0 && quote.ReportUnitId > 0, "锁价的金额、报工单位或换算数存在无效值。请检查该工序的锁价记录，确保金额非负、单位和换算数合法。");
        OperationPriceListService.Require(quote.PriceType == 1 || (quote.UnitsPerPriceUnit == 1 && quote.PriceUnit == (quote.PriceType == 2 ? "小时" : "笔") && quote.Lines.Count == 0), "计时或固定类型的锁价记录中，单位或附加项设置不合法。请检查锁价详情，确保单位符合计时/固定要求且附加项引用正确。");
    }

    public async Task<List<object>> ProductAsync(long productId, long factoryId)
    {
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == productId && p.FactoryId == factoryId) ?? throw ThrowHelper.BizUser("产品不存在或非本厂");
        return await (from s in db.RoutingSteps.AsNoTracking() join o in db.Operations.AsNoTracking() on s.OperationId equals o.Id
            where s.RoutingId == product.RoutingId && o.FactoryId == factoryId orderby s.Seq
            select (object)new { o.Id, o.Name }).ToListAsync();
    }

    public async Task BindAsync(ProdWorkOrder order, Dictionary<long, string>? tokens)
    {
        var tasks = await db.WorkOrderOperations.Where(t => t.WorkOrderId == order.Id).ToListAsync();
        OperationPriceListService.Require(tokens == null || tokens.Keys.All(id => tasks.Any(t => t.OperationId == id)), "预览结果里包含了不属于当前工单的工序。请重新预览当前工单，确保只包含本工单的工序。");
        foreach (var task in tasks)
        {
            if (task.ProcessingPrice != null)
            {
                var budget = await BudgetAsync(order.Id,task.OperationId,order.FactoryId);
                OperationPriceListService.Require(tokens == null || !tokens.TryGetValue(task.OperationId, out var token) || token == budget.QuoteToken, "该工序已经锁定价格，不能替换。如需更换价格，请新建或复制一张工单重新锁价。");
                continue;
            }
            OperationPriceListService.Require(!await db.Reports.AnyAsync(r => r.OrderId == order.Id && r.OperationId == task.OperationId), "该工序已有报工记录但缺少新版锁价，系统不能自动按当前价补上。请到「工序价目表」确认价目表设置并手动锁进本单后重试。");
            var attrs = await new OperationAttributeService(db).GetAsync(task.Id, order.FactoryId, task.OperationId);
            var quote = await new OperationPriceListService(db).PreviewAsync(order.ProductId, task.OperationId, order.FactoryId, operationAttributes: attrs);
            OperationPriceListService.Require(tokens == null || !tokens.TryGetValue(task.OperationId, out var expected) || expected == quote.QuoteToken, "产品属性或价目表在你预览后被修改过，之前的预览结果已失效。请重新预览获取最新价格后再保存工单。");
            ValidateQuote(quote); task.ProcessingPrice = Write(quote);
        }
        await db.SaveChangesAsync();
    }

    public async Task<ProcessingQuote> BudgetAsync(long orderId, long operationId, long factoryId)
    {
        var order = await db.WorkOrders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId && o.FactoryId == factoryId) ?? throw ThrowHelper.BizUser("指定的工单不存在或不属于本厂。请核对工单编号是否正确，或到「工单」确认该工单归属。");
        // 可写跟踪：缺锁价时懒绑定（docs/424）；已有锁价只读亦可同一实例
        var task = await db.WorkOrderOperations.SingleOrDefaultAsync(t => t.WorkOrderId == orderId && t.OperationId == operationId)
            ?? throw ThrowHelper.BizUser("工序不属于当前工单");
        if (task.ProcessingPrice == null)
        {
            OperationPriceListService.Require(
                !await db.Reports.AnyAsync(r => r.OrderId == orderId && r.OperationId == operationId),
                $"工单{order.OrderNo}·工序{operationId}已有报工但缺少价目表锁价，系统不能自动补价。请到「工序价目表」确认单价并锁进本单。");
            var attrs = await new OperationAttributeService(db).GetAsync(task.Id, factoryId, operationId);
            // 禁止带 workOrderId 调 Preview，避免与 BudgetAsync 循环
            var fresh = await new OperationPriceListService(db).PreviewAsync(order.ProductId, operationId, factoryId, operationAttributes: attrs);
            ValidateQuote(fresh);
            task.ProcessingPrice = Write(fresh);
            await db.SaveChangesAsync();
            return fresh;
        }
        var quote = Read<ProcessingQuote>(task.ProcessingPrice); ValidateQuote(quote);
        OperationPriceListService.Require(quote.FactoryId == factoryId && quote.ProductId == order.ProductId && quote.OperationId == operationId, "锁价记录的来源与本厂的产品工序对不上。请检查锁价来源是否正确指向本厂产品和工序，必要时重新从价目表锁价。");
        return quote;
    }

    public async Task<ReportPriceSnapshot> LockedAsync(long orderId,long operationId,long factoryId,DateTime? date=null)
    {
        var quote = await BudgetAsync(orderId,operationId,factoryId);
        OperationPriceListService.Require(quote.AttributeEvidence != null, "缺少产品的工艺属性资料，无法完成锁价核对。请到「产品定义」确认该产品的工艺属性已完整填写。");
        quote = await new PriceTimelineService(db).QuoteAsync(factoryId,quote.ProductId,operationId,quote.ReportUnitId,
            quote.AttributeEvidence!.Attributes,date ?? DateTime.Now);
        return new ReportPriceSnapshot(quote.AttributeEvidence!.PriceListId, quote.PriceType, quote.UnitPrice, quote.DeductPrice)
            { Processing = quote, PriceRevisionId = quote.PriceRevisionId };
    }
}
