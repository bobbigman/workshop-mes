using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;

namespace ahu.MicrosoftMes.Services;

// ============ 排产评分（docs/36 · ①档 本机闭环） ============
// 五维打分 × 权重 → 总分 / 排名 / 建议开工顺序。
// 交期紧迫度自动算（用 prod_work_order.due_date）；客户/金额/换型/齐套先手填（manual）或中性分兜底。
// 齐套 < 60 不硬排：仍给 rank，但 suggestSeq 标「建议顺延」并备注缺料。

/// <summary>单张工单的四维手填分（0-100；可空=未提供，用中性分兜底）</summary>
public class ScheduleDimScores
{
    public int? Customer { get; set; }     // 客户等级分
    public int? Amount { get; set; }       // 订单金额分
    public int? Changeover { get; set; }   // 换型相似度分
    public int? Kit { get; set; }          // 齐套分；<60 触发顺延
}

/// <summary>五维权重（默认对齐 docs/36 §8.7）</summary>
public class ScheduleWeights
{
    public double Due { get; set; } = 0.30;
    public double Kit { get; set; } = 0.25;
    public double Customer { get; set; } = 0.20;
    public double Changeover { get; set; } = 0.15;
    public double Amount { get; set; } = 0.10;
}

/// <summary>排产评分结果行（对齐「排产示例.xlsx」列）</summary>
public class ScheduleRowDto
{
    public string MoNo { get; set; } = "";         // 工单号（=金蝶生产订单号）
    public string ProductCode { get; set; } = "";  // 产品编号
    public string ProductName { get; set; } = "";
    public int Qty { get; set; }
    public DateTime? DueDate { get; set; }
    public int ScoreDue { get; set; }
    public int ScoreCustomer { get; set; }
    public int ScoreAmount { get; set; }
    public int ScoreChangeover { get; set; }
    public int? ScoreKit { get; set; }             // null=齐套未知
    public double Total { get; set; }
    public int Rank { get; set; }                  // 1=最高
    public int SuggestSeq { get; set; }            // 建议开工顺序；齐套低时同样给序号但备注顺延
    public string Remark { get; set; } = "";
}

public interface IScheduleScoreService
{
    Task<List<ScheduleRowDto>> ScoreAsync(long factoryId, Dictionary<string, ScheduleDimScores>? manual, ScheduleWeights? weights);
    byte[] ExportXlsx(List<ScheduleRowDto> rows, ScheduleWeights weights);
}

public class ScheduleScoreService : IScheduleScoreService
{
    private readonly AppDbContext _db;
    public ScheduleScoreService(AppDbContext db) { _db = db; }

    /// <summary>交期紧迫度：距完工日 D 天反向打分（docs/36 §8.6）</summary>
    public static int ScoreDueDate(DateTime? dueDate)
    {
        if (dueDate == null) return 40;                      // 未设交期
        var d = (int)(dueDate.Value.Date - DateTime.Today).TotalDays;
        if (d <= 0) return 100;                              // 已超期
        if (d <= 3) return 90;
        if (d <= 7) return 70;
        if (d <= 14) return 50;
        return 30;
    }

    public async Task<List<ScheduleRowDto>> ScoreAsync(long factoryId, Dictionary<string, ScheduleDimScores>? manual, ScheduleWeights? weights)
    {
        weights ??= new ScheduleWeights();

        // 只排未结束工单：0待生产 / 1生产中（排除 2完成、3已取消）
        var orders = await _db.WorkOrders
            .AsNoTracking()
            .Where(o => o.FactoryId == factoryId && (o.Status == 0 || o.Status == 1))
            .OrderBy(o => o.Id)
            .ToListAsync();

        var productIds = orders.Select(o => o.ProductId).Distinct().ToList();
        var products = await _db.Products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        var rows = new List<ScheduleRowDto>(orders.Count);
        foreach (var o in orders)
        {
            ScheduleDimScores? m = null;
            if (manual != null) manual.TryGetValue(o.OrderNo, out m);
            var due = ScoreDueDate(o.DueDate);

            // 四维：手填优先，否则中性分兜底（客户50 / 金额60 / 换型60 / 齐套未知）
            var customer = m?.Customer ?? 50;
            var amount = m?.Amount ?? 60;
            var changeover = m?.Changeover ?? 60;
            int? kit = m?.Kit;               // null=未知

            // 齐套未知按中性 60 参与加权，但不触发顺延；显式提供且 <60 才顺延
            var kitEffective = kit ?? 60;
            var total = due * weights.Due
                      + kitEffective * weights.Kit
                      + customer * weights.Customer
                      + changeover * weights.Changeover
                      + amount * weights.Amount;

            var remark = "";
            if (kit == null) remark = "齐套未知（未接金蝶）";
            else if (kit < 60) remark = $"缺料（齐套分{kit}），需催货或顺延，勿硬排";
            else remark = "料已齐";

            products.TryGetValue(o.ProductId, out var prod);
            rows.Add(new ScheduleRowDto
            {
                MoNo = o.OrderNo,
                ProductCode = prod?.Code ?? "",
                ProductName = prod?.Name ?? "",
                Qty = o.Qty,
                DueDate = o.DueDate,
                ScoreDue = due,
                ScoreCustomer = customer,
                ScoreAmount = amount,
                ScoreChangeover = changeover,
                ScoreKit = kit,
                Total = Math.Round(total, 2),
                Remark = remark
            });
        }

        // 排名：总分降序，1=最高
        var ordered = rows.OrderByDescending(r => r.Total).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Rank = i + 1;
            ordered[i].SuggestSeq = i + 1;
        }
        return ordered;
    }

    public byte[] ExportXlsx(List<ScheduleRowDto> rows, ScheduleWeights weights)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("优先级评分试算");

        // 标题 + 口径
        ws.Cell(1, 1).Value = "优先级评分试算（示例数据，可直接替换为真实任务单）";
        ws.Cell(2, 1).Value =
            "评分口径：每个维度 0-100 分，越需要优先给越高分；加权总分 = 各维度得分 × 权重" +
            $"（交期{weights.Due:P0} / 齐套{weights.Kit:P0} / 客户{weights.Customer:P0} / 换型{weights.Changeover:P0} / 金额{weights.Amount:P0}）";
        ws.Range(1, 1, 1, 14).Merge();
        ws.Range(2, 1, 2, 14).Merge();
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        // 表头（第4行）
        string[] headers =
        {
            "序号", "任务单号", "产品", "数量", "要求完工日",
            "交期紧迫度", "客户等级", "订单金额", "换型相似度", "齐套性",
            "加权总分", "优先级排名", "建议开工顺序", "备注"
        };
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(4, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9E1F2");
        }

        // 数据
        var r = 5;
        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = row.SuggestSeq;              // 序号按建议顺序编
            ws.Cell(r, 2).Value = row.MoNo;
            ws.Cell(r, 3).Value = row.ProductCode;
            ws.Cell(r, 4).Value = row.Qty;
            ws.Cell(r, 5).Value = row.DueDate?.ToString("yyyy-MM-dd") ?? "";
            ws.Cell(r, 6).Value = row.ScoreDue;
            ws.Cell(r, 7).Value = row.ScoreCustomer;
            ws.Cell(r, 8).Value = row.ScoreAmount;
            ws.Cell(r, 9).Value = row.ScoreChangeover;
            ws.Cell(r, 10).Value = row.ScoreKit?.ToString() ?? "";
            ws.Cell(r, 11).Value = row.Total;
            ws.Cell(r, 12).Value = row.Rank;
            ws.Cell(r, 13).Value = row.SuggestSeq;
            ws.Cell(r, 14).Value = row.Remark;

            // 加权总分列高亮（对齐样表浅绿/黄）
            ws.Cell(r, 11).Style.Fill.BackgroundColor = XLColor.FromHtml("#E2EFDA");
            r++;
        }

        // 说明
        var noteRow = r + 1;
        ws.Cell(noteRow, 1).Value = "说明：";
        ws.Cell(noteRow, 1).Style.Font.Bold = true;
        var notes = new[]
        {
            "① 交期紧迫度按「距要求完工日的天数」反向打分；换型相似度按「与上一张已排单的同规格程度」打分。",
            "② 优先级排名：加权总分越高排名越靠前（1 = 最高）。",
            "③ 齐套性得分低的单（如备注「缺料」），即使交期最紧，也应先催货或顺延，不要硬排。",
            "④ 调整方法：回到「排产参数配置」修改权重，本表加权总分与排名会立即变化，验证无误后再套用到正式排产表。"
        };
        for (var i = 0; i < notes.Length; i++)
            ws.Cell(noteRow + 1 + i, 1).Value = notes[i];

        ws.Columns(1, 14).AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
