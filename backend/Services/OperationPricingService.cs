using System.Text.Json;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;
using Microsoft.EntityFrameworkCore;

namespace ahu.MicrosoftMes.Services;

public record ProcessingLine(string Group, string Option, decimal Extra, bool Included);
public class ProcessingQuote
{
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
        try { return JsonSerializer.Deserialize<T>(value, Json) ?? throw ThrowHelper.BizUser("价目表报价资料为空，请核查"); }
        catch (JsonException ex) { throw ThrowHelper.BizUser($"价目表报价资料损坏：{ex.Message}"); }
    }
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Json);

    public static void ValidateQuote(ProcessingQuote quote)
    {
        OperationPriceListService.Require(quote.SchemaVersion == 2 && quote.PriceType is 1 or 2 or 3 && quote.FactoryId > 0 && quote.ProductId > 0 && quote.OperationId > 0 && quote.AttributeEvidence?.PriceListId > 0 && !string.IsNullOrWhiteSpace(quote.Version), "缺少新版价目表锁价依据，请核查并重建开发测试单");
        OperationPriceListService.Require(quote.UnitPrice >= 0 && quote.DeductPrice >= 0 && quote.UnitsPerPriceUnit > 0 && quote.ReportUnitId > 0, "锁价金额、报工单位或换算无效");
        OperationPriceListService.Require(quote.PriceType == 1 || (quote.UnitsPerPriceUnit == 1 && quote.PriceUnit == (quote.PriceType == 2 ? "小时" : "笔") && quote.Lines.Count == 0), "计时/固定锁价单位或附加项无效");
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
        OperationPriceListService.Require(tokens == null || tokens.Keys.All(id => tasks.Any(t => t.OperationId == id)), "预览指纹含非本工单工序");
        foreach (var task in tasks)
        {
            if (task.ProcessingPrice != null)
            {
                var locked = await LockedAsync(order.Id, task.OperationId, order.FactoryId);
                OperationPriceListService.Require(tokens == null || !tokens.TryGetValue(task.OperationId, out var token) || token == locked.Processing!.QuoteToken, "已有工序锁价不可替换，请新建或复制工单");
                continue;
            }
            OperationPriceListService.Require(!await db.Reports.AnyAsync(r => r.OrderId == order.Id && r.OperationId == task.OperationId), "已有报工但没有新版锁价，不能补套当前价，请核查开发测试单");
            var attrs = await new OperationAttributeService(db).GetAsync(task.Id, order.FactoryId, task.OperationId);
            var quote = await new OperationPriceListService(db).PreviewAsync(order.ProductId, task.OperationId, order.FactoryId, operationAttributes: attrs);
            OperationPriceListService.Require(tokens == null || !tokens.TryGetValue(task.OperationId, out var expected) || expected == quote.QuoteToken, "产品属性或价目表已变化，请重新预览后保存");
            ValidateQuote(quote); task.ProcessingPrice = Write(quote);
        }
        await db.SaveChangesAsync();
    }

    public async Task<ReportPriceSnapshot> LockedAsync(long orderId, long operationId, long factoryId)
    {
        var order = await db.WorkOrders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId && o.FactoryId == factoryId) ?? throw ThrowHelper.BizUser("工单不存在或非本厂");
        var task = await db.WorkOrderOperations.AsNoTracking().SingleOrDefaultAsync(t => t.WorkOrderId == orderId && t.OperationId == operationId);
        OperationPriceListService.Require(task?.ProcessingPrice != null, $"工单{order.OrderNo}·工序{operationId}缺少价目表锁价，不可回退或按零价报工");
        var quote = Read<ProcessingQuote>(task!.ProcessingPrice!); ValidateQuote(quote);
        OperationPriceListService.Require(quote.FactoryId == factoryId && quote.ProductId == order.ProductId && quote.OperationId == operationId, "锁价来源与本厂产品工序不一致");
        var unit = await db.Products.Where(p => p.Id == order.ProductId && p.FactoryId == factoryId).Select(p => p.UnitId).SingleAsync();
        OperationPriceListService.Require(unit == quote.ReportUnitId, "产品报工单位与锁价依据不一致，不能自动套价");
        return new ReportPriceSnapshot(quote.AttributeEvidence!.PriceListId, quote.PriceType, quote.UnitPrice, quote.DeductPrice) { Processing = quote };
    }
}
