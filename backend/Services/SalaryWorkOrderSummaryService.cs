using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

/// <summary>按工单累计工资查询行（工序或人员）。</summary>
public class SalaryWorkOrderBucketRowDto
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public decimal PieceAmount { get; set; }
    public decimal TimeAmount { get; set; }
    public decimal FixedAmount { get; set; }
    public decimal Subtotal { get; set; }
}

/// <summary>单工单工资汇总（docs/420 维度一）。</summary>
public class SalaryWorkOrderSummaryDto
{
    public long OrderId { get; set; }
    public string OrderNo { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int Qty { get; set; }
    public byte Status { get; set; }
    /// <summary>已复核良品合计（review_status=1）。</summary>
    public int ReviewedGoodQty { get; set; }
    public decimal PayableTotal { get; set; }
    public decimal UnreviewedAmount { get; set; }
    public int UnpricedCount { get; set; }
    public decimal UnknownPriceTypeAmount { get; set; }
    public DateTime QueriedAt { get; set; }
    public List<SalaryWorkOrderBucketRowDto> ByOperation { get; set; } = new();
    public List<SalaryWorkOrderBucketRowDto> ByUser { get; set; } = new();
}

/// <summary>已完工台账行。</summary>
public class SalaryCompletedWorkOrderRowDto
{
    public long OrderId { get; set; }
    public string OrderNo { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int Qty { get; set; }
    public decimal PayableTotal { get; set; }
    public decimal UnreviewedAmount { get; set; }
}

/// <summary>已完工工单应付台账（docs/420 维度二）。</summary>
public class SalaryCompletedWorkOrdersDto
{
    public List<SalaryCompletedWorkOrderRowDto> List { get; set; } = new();
    public decimal PayableTotal { get; set; }
    public decimal UnreviewedAmount { get; set; }
    public int UnpricedCount { get; set; }
    public decimal UnknownPriceTypeAmount { get; set; }
    public DateTime QueriedAt { get; set; }
}

public partial class SalaryService
{
    public async Task<ApiResult<SalaryWorkOrderSummaryDto>> WorkOrderSummaryAsync(
        long? orderId, string? orderNo, long factoryId)
    {
        await TenantDataGuard.WorkOrdersAsync(_db, factoryId, orderId.HasValue ? new[] { orderId.Value } : null);
        var trimmedNo = orderNo?.Trim();
        if (!orderId.HasValue && string.IsNullOrEmpty(trimmedNo))
            throw ThrowHelper.BizUser("请输入工单号");

        ProdWorkOrder? order;
        if (orderId.HasValue)
        {
            order = await _db.WorkOrders.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == orderId.Value && o.FactoryId == factoryId);
        }
        else
        {
            order = await _db.WorkOrders.AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrderNo == trimmedNo && o.FactoryId == factoryId);
        }

        if (order == null)
            throw ThrowHelper.BizUser("工单不存在或无权限");
        if (order.Status == 3)
            throw ThrowHelper.BizUser("工单已取消，不纳入本查询");

        var product = await _db.Products.AsNoTracking()
            .Where(p => p.Id == order.ProductId && p.FactoryId == factoryId)
            .Select(p => new { p.Code, p.Name })
            .FirstOrDefaultAsync();

        var agg = await AggregateOrderAsync(order.Id, factoryId);
        return ApiResult<SalaryWorkOrderSummaryDto>.Ok(new SalaryWorkOrderSummaryDto
        {
            OrderId = order.Id,
            OrderNo = order.OrderNo,
            ProductCode = product?.Code ?? "",
            ProductName = product?.Name ?? "",
            Qty = order.Qty,
            Status = order.Status,
            ReviewedGoodQty = agg.ReviewedGoodQty,
            PayableTotal = Round2(agg.Payable),
            UnreviewedAmount = Round2(agg.Unreviewed),
            UnpricedCount = agg.UnpricedCount,
            UnknownPriceTypeAmount = Round2(agg.UnknownPriceTypeAmount),
            QueriedAt = DateTime.Now,
            ByOperation = ToBucketRows(agg.ByOperation),
            ByUser = ToBucketRows(agg.ByUser)
        });
    }

    public async Task<ApiResult<SalaryCompletedWorkOrdersDto>> CompletedWorkOrdersAsync(long factoryId)
    {
        await TenantDataGuard.WorkOrdersAsync(_db, factoryId);
        var orders = await (
            from o in _db.WorkOrders.AsNoTracking()
            where o.FactoryId == factoryId && o.Status == 2
            join p in _db.Products.AsNoTracking().Where(p => p.FactoryId == factoryId) on o.ProductId equals p.Id into pj
            from p in pj.DefaultIfEmpty()
            orderby o.OrderNo
            select new
            {
                o.Id,
                o.OrderNo,
                o.Qty,
                ProductCode = p != null ? p.Code : "",
                ProductName = p != null ? p.Name : ""
            }).ToListAsync();

        if (orders.Count == 0)
        {
            return ApiResult<SalaryCompletedWorkOrdersDto>.Ok(new SalaryCompletedWorkOrdersDto
            {
                QueriedAt = DateTime.Now
            });
        }

        var orderIds = orders.Select(x => x.Id).ToList();
        var reportRows = await LoadReportRowsAsync(orderIds, factoryId);
        var itemMap = await LoadItemNetMapAsync(reportRows.Select(r => r.ReportId).ToList());

        var list = new List<SalaryCompletedWorkOrderRowDto>();
        decimal payableSum = 0, unreviewedSum = 0, unknownSum = 0;
        var unpriced = 0;

        foreach (var o in orders)
        {
            var ofOrder = reportRows.Where(r => r.OrderId == o.Id);
            var acc = AggregateRows(ofOrder, itemMap);
            var payable = Round2(acc.Payable);
            var unreviewed = Round2(acc.Unreviewed);
            payableSum += payable;
            unreviewedSum += unreviewed;
            unknownSum += acc.UnknownPriceTypeAmount;
            unpriced += acc.UnpricedCount;
            list.Add(new SalaryCompletedWorkOrderRowDto
            {
                OrderId = o.Id,
                OrderNo = o.OrderNo,
                ProductCode = o.ProductCode ?? "",
                ProductName = o.ProductName ?? "",
                Qty = o.Qty,
                PayableTotal = payable,
                UnreviewedAmount = unreviewed
            });
        }

        return ApiResult<SalaryCompletedWorkOrdersDto>.Ok(new SalaryCompletedWorkOrdersDto
        {
            List = list,
            PayableTotal = Round2(payableSum),
            UnreviewedAmount = Round2(unreviewedSum),
            UnpricedCount = unpriced,
            UnknownPriceTypeAmount = Round2(unknownSum),
            QueriedAt = DateTime.Now
        });
    }

    private async Task<OrderAgg> AggregateOrderAsync(long orderId, long factoryId)
    {
        var rows = await LoadReportRowsAsync(new List<long> { orderId }, factoryId);
        var itemMap = await LoadItemNetMapAsync(rows.Select(r => r.ReportId).ToList());
        return AggregateRows(rows, itemMap);
    }

    private async Task<List<ReportRow>> LoadReportRowsAsync(List<long> orderIds, long factoryId)
    {
        if (orderIds.Count == 0) return new List<ReportRow>();
        return await (
            from r in _db.Reports.AsNoTracking()
            where r.FactoryId == factoryId && orderIds.Contains(r.OrderId)
            join op in _db.Operations.AsNoTracking().Where(o => o.FactoryId == factoryId) on r.OperationId equals op.Id into opj
            from op in opj.DefaultIfEmpty()
            join u in _db.Users.AsNoTracking().Where(u => u.FactoryId == factoryId) on r.UserId equals u.Id into uj
            from u in uj.DefaultIfEmpty()
            select new ReportRow(
                r.Id,
                r.OrderId,
                r.OperationId,
                op != null ? op.Name : $"工序#{r.OperationId}",
                r.UserId,
                u != null ? u.Name : $"用户#{r.UserId}",
                r.GoodQty,
                r.ReviewStatus,
                r.WageAmount,
                r.PriceSnapshot
            )).ToListAsync();
    }

    private async Task<Dictionary<long, ItemSolid>> LoadItemNetMapAsync(List<long> reportIds)
    {
        if (reportIds.Count == 0) return new Dictionary<long, ItemSolid>();
        var items = await _db.SalaryStatementItems.AsNoTracking()
            .Where(i => reportIds.Contains(i.ReportId))
            .Select(i => new { i.ReportId, i.Amount, i.DeductAmount, i.PriceType })
            .ToListAsync();
        return items.ToDictionary(
            x => x.ReportId,
            x => new ItemSolid(x.Amount - (x.DeductAmount ?? 0m), x.PriceType));
    }

    private static OrderAgg AggregateRows(
        IEnumerable<ReportRow> rows,
        Dictionary<long, ItemSolid> itemMap)
    {
        var agg = new OrderAgg();
        foreach (var r in rows)
        {
            if (r.ReviewStatus is not (0 or 1 or 2))
                throw ThrowHelper.Biz(nameof(AggregateRows),
                    $"报工{r.ReportId}复核状态异常({r.ReviewStatus})，无法汇总工单工资");

            if (r.ReviewStatus == 2) continue;

            decimal amt;
            byte? priceType;
            if (r.ReviewStatus == 1 && itemMap.TryGetValue(r.ReportId, out var solid))
            {
                // I01：已进工资单用固化应发；不论草稿/已确认
                amt = solid.Net;
                priceType = solid.PriceType;
            }
            else
            {
                // 待复核或未进单：只用 wage_amount；NULL → B14
                if (r.WageAmount.HasValue)
                {
                    amt = r.WageAmount.Value;
                }
                else
                {
                    amt = 0m;
                    agg.UnpricedCount++;
                }
                priceType = TryReadPriceType(r.PriceSnapshot);
            }

            var knownBucket = priceType is 1 or 2 or 3;
            if (!knownBucket)
                agg.UnknownPriceTypeAmount += amt;

            if (r.ReviewStatus == 0)
            {
                agg.Unreviewed += amt;
                continue;
            }

            // review_status == 1
            agg.Payable += amt;
            agg.ReviewedGoodQty += r.GoodQty;
            AddToBucket(agg.ByOperation, r.OperationId, r.OperationName, priceType, amt);
            AddToBucket(agg.ByUser, r.UserId, r.UserName, priceType, amt);
        }
        return agg;
    }

    private static void AddToBucket(
        Dictionary<long, BucketAcc> map, long id, string name, byte? priceType, decimal amt)
    {
        if (!map.TryGetValue(id, out var b))
        {
            b = new BucketAcc { Name = name };
            map[id] = b;
        }
        b.Subtotal += amt;
        if (priceType == 1) b.Piece += amt;
        else if (priceType == 2) b.Time += amt;
        else if (priceType == 3) b.Fixed += amt;
    }

    private static List<SalaryWorkOrderBucketRowDto> ToBucketRows(Dictionary<long, BucketAcc> map) =>
        map.OrderBy(kv => kv.Value.Name).ThenBy(kv => kv.Key)
            .Select(kv => new SalaryWorkOrderBucketRowDto
            {
                Id = kv.Key,
                Name = kv.Value.Name,
                PieceAmount = Round2(kv.Value.Piece),
                TimeAmount = Round2(kv.Value.Time),
                FixedAmount = Round2(kv.Value.Fixed),
                Subtotal = Round2(kv.Value.Subtotal)
            }).ToList();

    /// <summary>只读取 price_type；损坏/缺失不抛（走 B15）。</summary>
    private static byte? TryReadPriceType(string? snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson)) return null;
        try
        {
            var snap = JsonSerializer.Deserialize<ReportPriceSnapshot>(
                snapshotJson, OperationPricingService.Json);
            return snap?.PriceType;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record ReportRow(
        long ReportId, long OrderId, long OperationId, string OperationName,
        long UserId, string UserName, int GoodQty, byte ReviewStatus,
        decimal? WageAmount, string? PriceSnapshot);

    private sealed record ItemSolid(decimal Net, byte PriceType);

    private sealed class BucketAcc
    {
        public string Name { get; set; } = "";
        public decimal Piece;
        public decimal Time;
        public decimal Fixed;
        public decimal Subtotal;
    }

    private sealed class OrderAgg
    {
        public decimal Payable;
        public decimal Unreviewed;
        public int UnpricedCount;
        public decimal UnknownPriceTypeAmount;
        public int ReviewedGoodQty;
        public Dictionary<long, BucketAcc> ByOperation { get; } = new();
        public Dictionary<long, BucketAcc> ByUser { get; } = new();
    }
}
