using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;

namespace ahu.MicrosoftMes.Services;

public class MyStatementDto
{
    public long Id { get; set; }
    public byte PeriodType { get; set; }
    public string PeriodValue { get; set; } = "";
    public decimal GrossAmount { get; set; }
    public decimal DeductAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public byte Status { get; set; }
}

public partial class SalaryService
{
    // 与既有结算周期一致，严格限制 ISO 周，避免 W00/W54 被归入其他年份。
    internal static DateTime MyStatementPeriodEnd(byte type, string value)
    {
        if (type == 1 && DateTime.TryParseExact(value, "yyyy-MM", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var month) && month.Year < 9999)
            return month.AddMonths(1);
        if (type == 2 && value.Length == 8 && value[4..6] == "-W"
            && int.TryParse(value[..4], out var year) && year is >= 1 and < 9999
            && int.TryParse(value[6..], out var week) && week >= 1 && week <= ISOWeek.GetWeeksInYear(year))
            return ISOWeek.ToDateTime(year, week, DayOfWeek.Monday).AddDays(7);
        throw ThrowHelper.BizCode("工资结算的周期填得不合法。请检查起止日期或周期类型是否符合要求格式。", 400);
    }

    public async Task<ApiResult<List<MyStatementDto>>> MyStatementsAsync(
        byte? periodType, string? periodValue, long factoryId, long userId)
    {
        var query = _db.SalaryStatements.AsNoTracking().Where(s => s.FactoryId == factoryId
            && s.UserId == userId && (s.Status == 1 || s.Status == 2));
        if (periodType.HasValue || periodValue != null)
        {
            if (!periodType.HasValue || string.IsNullOrEmpty(periodValue))
                throw ThrowHelper.BizCode("周期参数不合法", 400);
            _ = MyStatementPeriodEnd(periodType.Value, periodValue);
            query = query.Where(s => s.PeriodType == periodType.Value && s.PeriodValue == periodValue);
        }
        var statements = await query.ToListAsync();
        // 月/周不能直接比较字符串；同日结束按 ID 倒序确定唯一结果。
        var selected = statements.OrderByDescending(s => MyStatementPeriodEnd(s.PeriodType, s.PeriodValue))
            .ThenByDescending(s => s.Id).Take(1).ToList();
        var result = new List<MyStatementDto>();
        foreach (var s in selected)
        {
            var totals = await _db.SalaryStatementItems.AsNoTracking().Where(i => i.StatementId == s.Id)
                .GroupBy(i => i.StatementId).Select(g => new
                {
                    Gross = g.Sum(i => i.Amount), Deduct = g.Sum(i => i.DeductAmount ?? 0m)
                }).FirstOrDefaultAsync();
            result.Add(new MyStatementDto { Id = s.Id, PeriodType = s.PeriodType, PeriodValue = s.PeriodValue,
                GrossAmount = totals?.Gross ?? 0m, DeductAmount = totals?.Deduct ?? 0m,
                TotalAmount = s.TotalAmount, Status = s.Status });
        }
        return ApiResult<List<MyStatementDto>>.Ok(result);
    }

    public async Task<ApiResult<List<SalaryItemDto>>> MyStatementItemsAsync(long id, long factoryId, long userId)
    {
        // 状态条件也放在明细 SQL 中，撤回后不能通过旧 ID 继续读取。
        var visible = _db.SalaryStatements.AsNoTracking().Where(s => s.Id == id && s.FactoryId == factoryId
            && s.UserId == userId && (s.Status == 1 || s.Status == 2));
        if (!await visible.AnyAsync()) throw ThrowHelper.BizCode("你没有权限查看这张工资单，只能看自己的；如需查看他人的请联系管理员。", 403);
        var items = await (from i in _db.SalaryStatementItems.AsNoTracking()
            join s in visible on i.StatementId equals s.Id
            join r in _db.Reports.AsNoTracking() on i.ReportId equals r.Id
            join o in _db.WorkOrders.AsNoTracking() on r.OrderId equals o.Id
            join op in _db.Operations.AsNoTracking() on r.OperationId equals op.Id
            where r.FactoryId == factoryId && o.FactoryId == factoryId && op.FactoryId == factoryId
            orderby r.ReportTime, i.Id
            select new SalaryItemDto
            {
                Id = i.Id, ReportId = i.ReportId, OrderNo = o.OrderNo, OperationName = op.Name,
                GoodQty = r.GoodQty, DefectQty = r.DefectQty, CalcQty = i.CalcQty,
                PriceType = i.PriceType, DurationMinutes = i.DurationMinutes, UnitPrice = i.UnitPrice,
                WageCoeff = i.WageCoeff, Amount = i.Amount, DeductAmount = i.DeductAmount ?? 0m,
                NetAmount = i.Amount - (i.DeductAmount ?? 0m), ReportTime = r.ReportTime
            }).ToListAsync();
        return ApiResult<List<SalaryItemDto>>.Ok(items);
    }
}
