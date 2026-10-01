using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public interface ISalaryService
{
    Task<ApiResult<object?>> GenerateAsync(SalaryGenerateDto dto, long factoryId);
    Task<ApiResult<PageResult<SalaryListDto>>> QueryAsync(SalaryQueryDto query, long factoryId);
    Task<ApiResult<SalaryDetailDto>> GetAsync(long id, long factoryId);
    Task<ApiResult<object?>> UpdateComponentsAsync(long id, SalaryComponentsDto dto, long factoryId, long operatorUserId);
    Task<ApiResult<object?>> ConfirmAsync(long id, long factoryId);
    /// <summary>草稿按当前工价原地重算计件明细，保留手工项（docs/133）。</summary>
    Task<ApiResult<object?>> RecalcDraftAsync(long id, long factoryId, long operatorUserId);
    Task<ApiResult<object?>> RevokeAsync(long id, long factoryId, long operatorUserId);
    Task<ApiResult<object?>> DeleteDraftAsync(long id, long factoryId);
    Task<(byte[] Content, string FileName)> ExportCsvAsync(byte periodType, string periodValue, long factoryId);
    /// <summary>工人本人工资预估（docs/103）。</summary>
    Task<ApiResult<MyWageDto>> MineAsync(MyWageQueryDto query, long factoryId, long userId);
    /// <summary>工资按色码汇总（docs/103）。</summary>
    Task<ApiResult<SalarySkuSummaryDto>> SkuSummaryAsync(SalarySkuSummaryQueryDto query, long factoryId);
    Task<(byte[] Content, string FileName)> ExportSkuSummaryCsvAsync(SalarySkuSummaryQueryDto query, long factoryId);
}

public class SalaryGenerateDto
{
    public byte PeriodType { get; set; } = 1;
    public string PeriodValue { get; set; } = "";
    public long? UserId { get; set; }
}

public class SalaryQueryDto
{
    public byte? PeriodType { get; set; }
    public string? PeriodValue { get; set; }
    public long? UserId { get; set; }
    public byte? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class SalaryListDto
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string UserName { get; set; } = "";
    public byte PeriodType { get; set; }
    public string PeriodValue { get; set; } = "";
    public decimal BaseSalary { get; set; }
    public decimal MealAllowance { get; set; }
    public decimal OtherAllowance { get; set; }
    public decimal SocialTax { get; set; }
    public decimal OtherDeduction { get; set; }
    public decimal TotalAmount { get; set; }
    public byte Status { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SalaryDetailDto
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string UserName { get; set; } = "";
    public byte PeriodType { get; set; }
    public string PeriodValue { get; set; } = "";
    public decimal TotalAmount { get; set; }
    public decimal PieceworkAmount { get; set; }
    public decimal BaseSalary { get; set; }
    public decimal MealAllowance { get; set; }
    public decimal OtherAllowance { get; set; }
    public decimal SocialTax { get; set; }
    public decimal OtherDeduction { get; set; }
    public bool AllowManualComponents { get; set; }
    public byte Status { get; set; }
    public List<SalaryItemDto> Items { get; set; } = new();
}

public class SalaryComponentsDto
{
    public decimal BaseSalary { get; set; }
    public decimal MealAllowance { get; set; }
    public decimal OtherAllowance { get; set; }
    public decimal SocialTax { get; set; }
    public decimal OtherDeduction { get; set; }
}

public class SalaryItemDto
{
    public long Id { get; set; }
    public long ReportId { get; set; }
    public string OrderNo { get; set; } = "";
    public string OperationName { get; set; } = "";
    public int GoodQty { get; set; }
    public int DefectQty { get; set; }
    public int DurationMinutes { get; set; }
    public long? RuleId { get; set; }
    public int CalcQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public decimal DeductAmount { get; set; }
    public decimal NetAmount { get; set; }
    public bool Matched { get; set; }
    public DateTime ReportTime { get; set; }
}

public class MyWageQueryDto
{
    /// <summary>day / week / month（默认 month）</summary>
    public string Period { get; set; } = "month";
    /// <summary>锚定日，默认今天</summary>
    public DateTime? Date { get; set; }
}

public class MyWageDto
{
    public string Period { get; set; } = "month";
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public bool SkuEnabled { get; set; }
    /// <summary>仅已复核计入（已结算用固化，未结算用快照）</summary>
    public decimal TotalAmount { get; set; }
    public int TotalGoodQty { get; set; }
    public List<MyWageItemDto> Items { get; set; } = new();
}

public class MyWageItemDto
{
    public long ReportId { get; set; }
    public string OrderNo { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string OperationName { get; set; } = "";
    public string Color { get; set; } = "";
    public string Spec { get; set; } = "";
    public int GoodQty { get; set; }
    public int DefectQty { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? Amount { get; set; }
    public byte ReviewStatus { get; set; }
    public bool Settled { get; set; }
    /// <summary>是否计入累计</summary>
    public bool CountedInTotal { get; set; }
    public DateTime ReportTime { get; set; }
}

public class SalarySkuSummaryQueryDto
{
    public byte PeriodType { get; set; } = 1;
    public string PeriodValue { get; set; } = "";
    public string? ProductCode { get; set; }
    public string? Color { get; set; }
    public string? Spec { get; set; }
}

public class SalarySkuSummaryDto
{
    public bool Enabled { get; set; }
    public List<SalarySkuSummaryItemDto> Items { get; set; } = new();
}

public class SalarySkuSummaryItemDto
{
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Color { get; set; } = "";
    public string Spec { get; set; } = "";
    public int Qty { get; set; }
    public decimal Amount { get; set; }
    public int ReportCount { get; set; }
}

public class SalaryService : ISalaryService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<SalaryService> _logger;

    public SalaryService(AppDbContext db, IConfiguration config, ILogger<SalaryService> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    private bool AllowManualComponents =>
        _config.GetValue("Salary:AllowManualComponents", true);

    public async Task<ApiResult<object?>> GenerateAsync(SalaryGenerateDto dto, long factoryId)
    {
        if (dto.PeriodType is not (1 or 2))
            throw ThrowHelper.Biz(nameof(GenerateAsync), "周期类型须为 1月 / 2周");
        var periodValue = (dto.PeriodValue ?? "").Trim();
        if (string.IsNullOrEmpty(periodValue))
            throw ThrowHelper.Biz(nameof(GenerateAsync), "周期值不能为空");
        var (start, end) = ParsePeriod(dto.PeriodType, periodValue, nameof(GenerateAsync));

        // docs/21：只取已复核通过且未结算
        var reportsQ = _db.Reports.Where(r =>
            r.FactoryId == factoryId
            && r.ReviewStatus == 1
            && !r.SettledFlag
            && r.ReportTime >= start && r.ReportTime < end);
        if (dto.UserId.HasValue)
            reportsQ = reportsQ.Where(r => r.UserId == dto.UserId.Value);

        var reports = await reportsQ.OrderBy(r => r.UserId).ThenBy(r => r.ReportTime).ToListAsync();
        if (reports.Count == 0)
            throw ThrowHelper.Biz(nameof(GenerateAsync), "该周期无可结算报工（须已复核通过且未结算）");

        var userIds = reports.Select(r => r.UserId).Distinct().ToList();
        var existed = await _db.SalaryStatements.AsNoTracking()
            .Where(s => s.FactoryId == factoryId && s.PeriodType == dto.PeriodType
                        && s.PeriodValue == periodValue && userIds.Contains(s.UserId))
            .Select(s => s.UserId).ToListAsync();
        if (existed.Count > 0)
            throw ThrowHelper.Biz(nameof(GenerateAsync), "部分人员该周期工资单已存在，请先删除草稿后再生成");

        var rules = await _db.PriceRules.AsNoTracking().Where(r => r.FactoryId == factoryId).ToListAsync();
        var now = DateTime.Now;

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var created = 0;
            var unmatched = 0;
            foreach (var group in reports.GroupBy(r => r.UserId))
            {
                var statement = new SalaryStatement
                {
                    FactoryId = factoryId,
                    UserId = group.Key,
                    PeriodType = dto.PeriodType,
                    PeriodValue = periodValue,
                    TotalAmount = 0,
                    BaseSalary = 0,
                    MealAllowance = 0,
                    OtherAllowance = 0,
                    SocialTax = 0,
                    OtherDeduction = 0,
                    Status = 0,
                    CreatedAt = now
                };
                _db.SalaryStatements.Add(statement);
                await _db.SaveChangesAsync(); // 取 Id

                decimal total = 0;
                foreach (var report in group)
                {
                    var productId = report.ProductId;
                    var rule = PriceRuleMatcher.Match(
                        rules, factoryId, productId, report.OperationId,
                        report.DepartmentId, report.UserId, report.ReportTime);
                    var calc = PriceRuleMatcher.Calculate(rule, report.GoodQty, report.DefectQty, report.DurationMinutes);
                    if (!calc.Matched) unmatched++;

                    _db.SalaryStatementItems.Add(new SalaryStatementItem
                    {
                        StatementId = statement.Id,
                        ReportId = report.Id,
                        RuleId = calc.RuleId,
                        CalcQty = calc.CalcQty,
                        UnitPrice = calc.UnitPrice,
                        Amount = calc.Amount,
                        DeductAmount = calc.DeductAmount
                    });

                    // 回填预览快照，不置 settled
                    report.UnitPrice = calc.Matched ? calc.UnitPrice : null;
                    report.WageAmount = calc.Matched ? calc.WageAmount : null;
                    total += calc.WageAmount;
                }
                // 手工列默认 0，应发合计 = 计件合计
                statement.TotalAmount = Math.Round(total, 2, MidpointRounding.AwayFromZero);
                created++;
            }
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return ApiResult<object?>.Ok(new { created, unmatchedCount = unmatched });
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<ApiResult<PageResult<SalaryListDto>>> QueryAsync(SalaryQueryDto query, long factoryId)
    {
        var q = _db.SalaryStatements.AsNoTracking().Where(s => s.FactoryId == factoryId);
        if (query.PeriodType.HasValue) q = q.Where(s => s.PeriodType == query.PeriodType);
        if (!string.IsNullOrWhiteSpace(query.PeriodValue)) q = q.Where(s => s.PeriodValue == query.PeriodValue);
        if (query.UserId.HasValue) q = q.Where(s => s.UserId == query.UserId);
        if (query.Status.HasValue) q = q.Where(s => s.Status == query.Status);

        var total = await q.CountAsync();
        var rows = await q.OrderByDescending(s => s.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync();
        var names = await _db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name);
        var list = rows.Select(s => new SalaryListDto
        {
            Id = s.Id,
            UserId = s.UserId,
            UserName = names.GetValueOrDefault(s.UserId, ""),
            PeriodType = s.PeriodType,
            PeriodValue = s.PeriodValue,
            BaseSalary = s.BaseSalary,
            MealAllowance = s.MealAllowance,
            OtherAllowance = s.OtherAllowance,
            SocialTax = s.SocialTax,
            OtherDeduction = s.OtherDeduction,
            TotalAmount = s.TotalAmount,
            Status = s.Status,
            ConfirmedAt = s.ConfirmedAt,
            CreatedAt = s.CreatedAt
        }).ToList();
        return ApiResult<PageResult<SalaryListDto>>.Ok(new PageResult<SalaryListDto> { List = list, Total = total });
    }

    public async Task<ApiResult<SalaryDetailDto>> GetAsync(long id, long factoryId)
    {
        var s = await _db.SalaryStatements.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(GetAsync), "工资单不存在");
        var userName = await _db.Users.AsNoTracking().Where(u => u.Id == s.UserId).Select(u => u.Name).FirstOrDefaultAsync() ?? "";
        var items = await (
            from i in _db.SalaryStatementItems.AsNoTracking()
            where i.StatementId == id
            join r in _db.Reports.AsNoTracking() on i.ReportId equals r.Id
            join o in _db.WorkOrders.AsNoTracking() on r.OrderId equals o.Id
            join op in _db.Operations.AsNoTracking() on r.OperationId equals op.Id
            orderby r.ReportTime
            select new SalaryItemDto
            {
                Id = i.Id,
                ReportId = i.ReportId,
                OrderNo = o.OrderNo,
                OperationName = op.Name,
                GoodQty = r.GoodQty,
                DefectQty = r.DefectQty,
                DurationMinutes = r.DurationMinutes,
                RuleId = i.RuleId,
                CalcQty = i.CalcQty,
                UnitPrice = i.UnitPrice,
                Amount = i.Amount,
                DeductAmount = i.DeductAmount ?? 0,
                NetAmount = i.Amount - (i.DeductAmount ?? 0),
                Matched = i.RuleId != null,
                ReportTime = r.ReportTime
            }).ToListAsync();

        var piecework = Math.Round(items.Sum(i => i.NetAmount), 2, MidpointRounding.AwayFromZero);
        return ApiResult<SalaryDetailDto>.Ok(new SalaryDetailDto
        {
            Id = s.Id,
            UserId = s.UserId,
            UserName = userName,
            PeriodType = s.PeriodType,
            PeriodValue = s.PeriodValue,
            TotalAmount = s.TotalAmount,
            PieceworkAmount = piecework,
            BaseSalary = s.BaseSalary,
            MealAllowance = s.MealAllowance,
            OtherAllowance = s.OtherAllowance,
            SocialTax = s.SocialTax,
            OtherDeduction = s.OtherDeduction,
            AllowManualComponents = AllowManualComponents,
            Status = s.Status,
            Items = items
        });
    }

    public async Task<ApiResult<object?>> UpdateComponentsAsync(long id, SalaryComponentsDto dto, long factoryId, long operatorUserId)
    {
        if (!AllowManualComponents)
        {
            _logger.LogWarning(
                "工资单手工项保存被拒（功能关闭） statementId={Id} operator={Op}",
                id, operatorUserId);
            throw ThrowHelper.BizUser("该功能已关闭");
        }

        if (dto.BaseSalary < 0 || dto.MealAllowance < 0 || dto.OtherAllowance < 0
            || dto.SocialTax < 0 || dto.OtherDeduction < 0)
        {
            _logger.LogWarning(
                "工资单手工项保存被拒（负数） statementId={Id} operator={Op} dto={@Dto}",
                id, operatorUserId, dto);
            throw ThrowHelper.BizUser("不能填负数");
        }

        var statement = await _db.SalaryStatements
            .FirstOrDefaultAsync(s => s.Id == id && s.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(UpdateComponentsAsync), "工资单不存在");

        if (statement.Status != 0)
        {
            _logger.LogWarning(
                "工资单手工项保存被拒（非草稿） statementId={Id} status={Status} operator={Op}",
                id, statement.Status, operatorUserId);
            throw ThrowHelper.BizUser("已确认工资单不能修改");
        }

        var before = new
        {
            statement.BaseSalary,
            statement.MealAllowance,
            statement.OtherAllowance,
            statement.SocialTax,
            statement.OtherDeduction,
            statement.TotalAmount
        };

        var piecework = await SumPieceworkAsync(id);
        var total = CalcTotalAmount(
            piecework,
            dto.BaseSalary, dto.MealAllowance, dto.OtherAllowance,
            dto.SocialTax, dto.OtherDeduction);

        statement.BaseSalary = Round2(dto.BaseSalary);
        statement.MealAllowance = Round2(dto.MealAllowance);
        statement.OtherAllowance = Round2(dto.OtherAllowance);
        statement.SocialTax = Round2(dto.SocialTax);
        statement.OtherDeduction = Round2(dto.OtherDeduction);
        statement.TotalAmount = total;

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "工资单手工项已保存 statementId={Id} operator={Op} before={@Before} after=({Base},{Meal},{Oth},{Tax},{Ded},total={Total})",
            id, operatorUserId, before,
            statement.BaseSalary, statement.MealAllowance, statement.OtherAllowance,
            statement.SocialTax, statement.OtherDeduction, statement.TotalAmount);

        return ApiResult<object?>.Ok(new
        {
            totalAmount = statement.TotalAmount,
            pieceworkAmount = piecework,
            baseSalary = statement.BaseSalary,
            mealAllowance = statement.MealAllowance,
            otherAllowance = statement.OtherAllowance,
            socialTax = statement.SocialTax,
            otherDeduction = statement.OtherDeduction
        });
    }

    /// <summary>草稿按当前工价原地重算计件明细，保留手工项（docs/133）。</summary>
    public async Task<ApiResult<object?>> RecalcDraftAsync(long id, long factoryId, long operatorUserId)
    {
        var statement = await _db.SalaryStatements
            .FirstOrDefaultAsync(s => s.Id == id && s.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(RecalcDraftAsync), "工资单不存在");
        if (statement.Status != 0)
            throw ThrowHelper.Biz(nameof(RecalcDraftAsync), "仅草稿可重算；已确认请先撤回");

        var items = await _db.SalaryStatementItems.Where(i => i.StatementId == id).ToListAsync();
        var reportIds = items.Select(i => i.ReportId).Distinct().ToList();
        var reports = await _db.Reports
            .Where(r => reportIds.Contains(r.Id) && r.FactoryId == factoryId)
            .ToDictionaryAsync(r => r.Id);

        foreach (var item in items)
        {
            if (!reports.ContainsKey(item.ReportId))
                throw ThrowHelper.Biz(nameof(RecalcDraftAsync),
                    $"明细关联报工不存在 reportId={item.ReportId} statementId={id}");
        }

        var rules = await _db.PriceRules.AsNoTracking()
            .Where(r => r.FactoryId == factoryId).ToListAsync();

        var unmatched = 0;
        decimal pieceworkSum = 0;
        foreach (var item in items)
        {
            var report = reports[item.ReportId];
            var rule = PriceRuleMatcher.Match(
                rules, factoryId, report.ProductId, report.OperationId,
                report.DepartmentId, report.UserId, report.ReportTime);
            var calc = PriceRuleMatcher.Calculate(rule, report.GoodQty, report.DefectQty, report.DurationMinutes);
            if (!calc.Matched) unmatched++;

            item.RuleId = calc.RuleId;
            item.CalcQty = calc.CalcQty;
            item.UnitPrice = calc.UnitPrice;
            item.Amount = calc.Amount;
            item.DeductAmount = calc.DeductAmount;

            report.UnitPrice = calc.Matched ? calc.UnitPrice : null;
            report.WageAmount = calc.Matched ? calc.WageAmount : null;
            pieceworkSum += calc.WageAmount;
        }

        var piecework = Round2(pieceworkSum);
        var beforeTotal = statement.TotalAmount;
        statement.TotalAmount = CalcTotalAmount(
            piecework,
            statement.BaseSalary, statement.MealAllowance, statement.OtherAllowance,
            statement.SocialTax, statement.OtherDeduction);

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "草稿工资单已重算 statementId={Id} operator={Op} unmatched={Unmatched} piecework={Piece} totalBefore={Before} totalAfter={After}",
            id, operatorUserId, unmatched, piecework, beforeTotal, statement.TotalAmount);

        return ApiResult<object?>.Ok(new
        {
            unmatchedCount = unmatched,
            pieceworkAmount = piecework,
            totalAmount = statement.TotalAmount
        });
    }

    public async Task<ApiResult<object?>> ConfirmAsync(long id, long factoryId)
    {
        var statement = await _db.SalaryStatements
            .FirstOrDefaultAsync(s => s.Id == id && s.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(ConfirmAsync), "工资单不存在");
        if (statement.Status != 0)
            throw ThrowHelper.Biz(nameof(ConfirmAsync), "仅草稿可确认");

        var unmatched = await _db.SalaryStatementItems.AsNoTracking()
            .AnyAsync(i => i.StatementId == id && i.RuleId == null);
        if (unmatched)
            throw ThrowHelper.Biz(nameof(ConfirmAsync), "存在未匹配工价的明细，请先补齐工价后再确认");

        var reportIds = await _db.SalaryStatementItems.AsNoTracking()
            .Where(i => i.StatementId == id).Select(i => i.ReportId).ToListAsync();

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // UPDLOCK 冻结报工行（docs/21）；id 来自库内主键，逐条参数化
            foreach (var rid in reportIds)
            {
                await _db.Database.ExecuteSqlRawAsync(
                    "SELECT id FROM prod_report WITH (UPDLOCK, ROWLOCK) WHERE id = {0}", rid);
            }

            var reports = await _db.Reports.Where(r => reportIds.Contains(r.Id)).ToListAsync();
            foreach (var r in reports)
                r.SettledFlag = true;

            statement.Status = 1;
            statement.ConfirmedAt = DateTime.Now;
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return ApiResult<object?>.OkMsg();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// 撤回已确认工资单：删明细/删单、回滚 settled_flag，便于重新 generate（docs/67）。
    /// </summary>
    public async Task<ApiResult<object?>> RevokeAsync(long id, long factoryId, long operatorUserId)
    {
        var statement = await _db.SalaryStatements
            .FirstOrDefaultAsync(s => s.Id == id && s.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(RevokeAsync), "工资单不存在");

        if (statement.Status == 2)
            throw ThrowHelper.Biz(nameof(RevokeAsync), "已发放工资单不可撤回");
        if (statement.Status != 1)
            throw ThrowHelper.Biz(nameof(RevokeAsync), "仅已确认工资单可撤回");

        var items = await _db.SalaryStatementItems.Where(i => i.StatementId == id).ToListAsync();
        var reportIds = items.Select(i => i.ReportId).ToList();
        var auditTotal = statement.TotalAmount;
        var auditUserId = statement.UserId;
        var auditPeriodType = statement.PeriodType;
        var auditPeriodValue = statement.PeriodValue;

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            foreach (var rid in reportIds)
            {
                await _db.Database.ExecuteSqlRawAsync(
                    "SELECT id FROM prod_report WITH (UPDLOCK, ROWLOCK) WHERE id = {0}", rid);
            }

            var reports = await _db.Reports.Where(r => reportIds.Contains(r.Id)).ToListAsync();
            foreach (var r in reports)
                r.SettledFlag = false;

            _db.SalaryStatementItems.RemoveRange(items);
            _db.SalaryStatements.Remove(statement);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        _logger.LogInformation(
            "工资单已撤回 statementId={Id} operator={Op} totalAmount={Total} reportCount={Cnt} userId={UserId} period={PeriodType}/{PeriodValue}",
            id, operatorUserId, auditTotal, reportIds.Count, auditUserId, auditPeriodType, auditPeriodValue);

        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> DeleteDraftAsync(long id, long factoryId)
    {
        var statement = await _db.SalaryStatements
            .FirstOrDefaultAsync(s => s.Id == id && s.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(DeleteDraftAsync), "工资单不存在");
        if (statement.Status != 0)
            throw ThrowHelper.Biz(nameof(DeleteDraftAsync), "仅草稿可删除");

        var items = await _db.SalaryStatementItems.Where(i => i.StatementId == id).ToListAsync();
        _db.SalaryStatementItems.RemoveRange(items);
        _db.SalaryStatements.Remove(statement);
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<MyWageDto>> MineAsync(MyWageQueryDto query, long factoryId, long userId)
    {
        var period = (query.Period ?? "month").Trim().ToLowerInvariant();
        if (period is not ("day" or "week" or "month"))
            throw ThrowHelper.Biz(nameof(MineAsync), "周期须为 day / week / month");
        var (start, end) = ResolveMineRange(period, query.Date);

        var (colorFieldId, specFieldId, skuEnabled) = await ResolveSkuFieldIdsAsync(factoryId);

        var reportRows = await (
            from r in _db.Reports.AsNoTracking()
            where r.FactoryId == factoryId && r.UserId == userId
                  && r.ReportTime >= start && r.ReportTime < end
            join o in _db.WorkOrders.AsNoTracking() on r.OrderId equals o.Id
            join p in _db.Products.AsNoTracking() on o.ProductId equals p.Id
            join op in _db.Operations.AsNoTracking() on r.OperationId equals op.Id
            orderby r.ReportTime descending
            select new
            {
                r.Id,
                OrderId = o.Id,
                o.OrderNo,
                ProductCode = p.Code,
                ProductName = p.Name,
                OperationName = op.Name,
                r.GoodQty,
                r.DefectQty,
                r.UnitPrice,
                r.WageAmount,
                r.ReviewStatus,
                r.SettledFlag,
                r.ReportTime
            }).ToListAsync();

        var reportIds = reportRows.Select(x => x.Id).ToList();
        var settledMap = reportIds.Count == 0
            ? new Dictionary<long, (decimal UnitPrice, decimal Net)>()
            : (await (
                from i in _db.SalaryStatementItems.AsNoTracking()
                where reportIds.Contains(i.ReportId)
                select new { i.ReportId, i.UnitPrice, Net = i.Amount - (i.DeductAmount ?? 0) }
            ).ToListAsync()).ToDictionary(
                x => x.ReportId,
                x => (UnitPrice: x.UnitPrice, Net: Round2(x.Net)));

        var orderIds = reportRows.Select(x => x.OrderId).Distinct().ToList();
        var valueMap = await LoadSkuValuesAsync(orderIds, colorFieldId, specFieldId);

        string Lookup(long orderId, long? fieldId)
        {
            if (!fieldId.HasValue) return "";
            return valueMap.TryGetValue((orderId, fieldId.Value), out var v) ? v : "";
        }

        var items = new List<MyWageItemDto>();
        decimal total = 0;
        var totalGood = 0;
        foreach (var x in reportRows)
        {
            var counted = x.ReviewStatus == 1;
            decimal? unit = null;
            decimal? amount = null;
            var settled = x.SettledFlag || settledMap.ContainsKey(x.Id);
            if (settled && settledMap.TryGetValue(x.Id, out var solid))
            {
                unit = solid.UnitPrice;
                amount = solid.Net;
            }
            else if (x.ReviewStatus == 1)
            {
                unit = x.UnitPrice;
                amount = x.WageAmount;
            }
            else
            {
                // 待复核/退回：可展示快照，但不计入
                unit = x.UnitPrice;
                amount = x.WageAmount;
            }

            if (counted)
            {
                total += amount ?? 0;
                totalGood += x.GoodQty;
            }

            items.Add(new MyWageItemDto
            {
                ReportId = x.Id,
                OrderNo = x.OrderNo,
                ProductCode = x.ProductCode,
                ProductName = x.ProductName,
                OperationName = x.OperationName,
                Color = Lookup(x.OrderId, colorFieldId),
                Spec = Lookup(x.OrderId, specFieldId),
                GoodQty = x.GoodQty,
                DefectQty = x.DefectQty,
                UnitPrice = unit,
                Amount = amount,
                ReviewStatus = x.ReviewStatus,
                Settled = settled,
                CountedInTotal = counted,
                ReportTime = x.ReportTime
            });
        }

        total = Round2(total);
        _logger.LogInformation(
            "MyWage factory={FactoryId} user={UserId} period={Period} start={Start} end={End} rows={Rows} total={Total}",
            factoryId, userId, period, start, end, items.Count, total);

        return ApiResult<MyWageDto>.Ok(new MyWageDto
        {
            Period = period,
            Start = start,
            End = end,
            SkuEnabled = skuEnabled,
            TotalAmount = total,
            TotalGoodQty = totalGood,
            Items = items
        });
    }

    public async Task<ApiResult<SalarySkuSummaryDto>> SkuSummaryAsync(SalarySkuSummaryQueryDto query, long factoryId)
    {
        var result = await BuildSkuSummaryAsync(query, factoryId);
        _logger.LogInformation(
            "SalarySkuSummary factory={FactoryId} periodType={PeriodType} periodValue={PeriodValue} enabled={Enabled} rows={Rows}",
            factoryId, query.PeriodType, query.PeriodValue, result.Enabled, result.Items.Count);
        return ApiResult<SalarySkuSummaryDto>.Ok(result);
    }

    public async Task<(byte[] Content, string FileName)> ExportSkuSummaryCsvAsync(SalarySkuSummaryQueryDto query, long factoryId)
    {
        var data = await BuildSkuSummaryAsync(query, factoryId);
        var sb = new StringBuilder();
        sb.AppendLine("产品编号,产品名称,颜色,规格,数量,金额,报工次数");
        foreach (var r in data.Items)
        {
            sb.AppendLine($"{EscapeCsv(r.ProductCode)},{EscapeCsv(r.ProductName)},{EscapeCsv(r.Color)},{EscapeCsv(r.Spec)},{r.Qty},{r.Amount},{r.ReportCount}");
        }
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        var pv = (query.PeriodValue ?? "").Trim();
        var file = $"salary_sku_{pv}.csv";
        return (bytes, file);
    }

    private async Task<SalarySkuSummaryDto> BuildSkuSummaryAsync(SalarySkuSummaryQueryDto query, long factoryId)
    {
        if (query.PeriodType is not (1 or 2))
            throw ThrowHelper.Biz(nameof(BuildSkuSummaryAsync), "周期类型须为 1月 / 2周");
        var periodValue = (query.PeriodValue ?? "").Trim();
        if (string.IsNullOrEmpty(periodValue))
            throw ThrowHelper.Biz(nameof(BuildSkuSummaryAsync), "周期值不能为空");
        // 校验周期格式（与 generate 一致）
        _ = ParsePeriod(query.PeriodType, periodValue, nameof(BuildSkuSummaryAsync));

        var productCode = string.IsNullOrWhiteSpace(query.ProductCode) ? null : query.ProductCode.Trim();
        var colorFilter = string.IsNullOrWhiteSpace(query.Color) ? null : query.Color.Trim();
        var specFilter = string.IsNullOrWhiteSpace(query.Spec) ? null : query.Spec.Trim();

        var (colorFieldId, specFieldId, skuEnabled) = await ResolveSkuFieldIdsAsync(factoryId);
        if (!skuEnabled)
            return new SalarySkuSummaryDto { Enabled = false };

        var rows = await (
            from s in _db.SalaryStatements.AsNoTracking()
            where s.FactoryId == factoryId && s.PeriodType == query.PeriodType && s.PeriodValue == periodValue
            join i in _db.SalaryStatementItems.AsNoTracking() on s.Id equals i.StatementId
            join r in _db.Reports.AsNoTracking() on i.ReportId equals r.Id
            join o in _db.WorkOrders.AsNoTracking() on r.OrderId equals o.Id
            join p in _db.Products.AsNoTracking() on o.ProductId equals p.Id
            select new
            {
                OrderId = o.Id,
                ProductCode = p.Code,
                ProductName = p.Name,
                Qty = i.CalcQty,
                Amount = i.Amount - (i.DeductAmount ?? 0)
            }).ToListAsync();

        var orderIds = rows.Select(x => x.OrderId).Distinct().ToList();
        var valueMap = await LoadSkuValuesAsync(orderIds, colorFieldId, specFieldId);

        string Lookup(long orderId, long? fieldId)
        {
            if (!fieldId.HasValue) return "";
            return valueMap.TryGetValue((orderId, fieldId.Value), out var v) ? v : "";
        }

        var enriched = rows.Select(x => new
        {
            x.ProductCode,
            x.ProductName,
            Color = Lookup(x.OrderId, colorFieldId),
            Spec = Lookup(x.OrderId, specFieldId),
            x.Qty,
            x.Amount
        }).AsEnumerable();

        if (productCode != null)
            enriched = enriched.Where(x => string.Equals(x.ProductCode, productCode, StringComparison.OrdinalIgnoreCase));
        if (colorFilter != null)
            enriched = enriched.Where(x => string.Equals(x.Color, colorFilter, StringComparison.Ordinal));
        if (specFilter != null)
            enriched = enriched.Where(x => string.Equals(x.Spec, specFilter, StringComparison.Ordinal));

        var items = enriched
            .GroupBy(x => new { x.ProductCode, x.ProductName, x.Color, x.Spec })
            .Select(g => new SalarySkuSummaryItemDto
            {
                ProductCode = g.Key.ProductCode,
                ProductName = g.Key.ProductName,
                Color = g.Key.Color,
                Spec = g.Key.Spec,
                Qty = g.Sum(x => x.Qty),
                Amount = Round2(g.Sum(x => x.Amount)),
                ReportCount = g.Count()
            })
            .OrderBy(x => x.ProductCode)
            .ThenBy(x => x.Color)
            .ThenBy(x => x.Spec)
            .ToList();

        return new SalarySkuSummaryDto { Enabled = true, Items = items };
    }

    private async Task<(long? ColorId, long? SpecId, bool Enabled)> ResolveSkuFieldIdsAsync(long factoryId)
    {
        var keyFields = await _db.CustomFields.AsNoTracking()
            .Where(f => f.FactoryId == factoryId
                        && f.Target == "work_order"
                        && f.FieldKey != null
                        && (f.FieldKey == "color" || f.FieldKey == "spec"))
            .Select(f => new { f.Id, f.FieldKey })
            .ToListAsync();
        var colorId = keyFields.FirstOrDefault(f => f.FieldKey == "color")?.Id;
        var specId = keyFields.FirstOrDefault(f => f.FieldKey == "spec")?.Id;
        return (colorId, specId, colorId != null || specId != null);
    }

    private async Task<Dictionary<(long OrderId, long FieldId), string>> LoadSkuValuesAsync(
        List<long> orderIds, long? colorFieldId, long? specFieldId)
    {
        var valueFieldIds = new List<long>();
        if (colorFieldId.HasValue) valueFieldIds.Add(colorFieldId.Value);
        if (specFieldId.HasValue) valueFieldIds.Add(specFieldId.Value);
        if (orderIds.Count == 0 || valueFieldIds.Count == 0)
            return new Dictionary<(long, long), string>();

        var vals = await _db.CustomFieldValues.AsNoTracking()
            .Where(v => valueFieldIds.Contains(v.FieldId) && orderIds.Contains(v.TargetId))
            .Select(v => new { v.FieldId, v.TargetId, v.Value })
            .ToListAsync();
        return vals.ToDictionary(v => (v.TargetId, v.FieldId), v => v.Value ?? "");
    }

    private static (DateTime Start, DateTime End) ResolveMineRange(string period, DateTime? date)
    {
        var anchor = (date ?? DateTime.Today).Date;
        switch (period)
        {
            case "day":
                return (anchor, anchor.AddDays(1));
            case "week":
                {
                    var week = ISOWeek.GetWeekOfYear(anchor);
                    var year = ISOWeek.GetYear(anchor);
                    var start = ISOWeek.ToDateTime(year, week, DayOfWeek.Monday);
                    return (start, start.AddDays(7));
                }
            default:
                {
                    var start = new DateTime(anchor.Year, anchor.Month, 1);
                    return (start, start.AddMonths(1));
                }
        }
    }

    public async Task<(byte[] Content, string FileName)> ExportCsvAsync(byte periodType, string periodValue, long factoryId)
    {
        periodValue = (periodValue ?? "").Trim();
        if (string.IsNullOrEmpty(periodValue))
            throw ThrowHelper.Biz(nameof(ExportCsvAsync), "周期值不能为空");

        var rows = await (
            from s in _db.SalaryStatements.AsNoTracking()
            where s.FactoryId == factoryId && s.PeriodType == periodType && s.PeriodValue == periodValue
            join u in _db.Users.AsNoTracking() on s.UserId equals u.Id
            orderby u.Name
            select new
            {
                u.Name,
                s.Id,
                s.PeriodValue,
                s.BaseSalary,
                s.MealAllowance,
                s.OtherAllowance,
                s.SocialTax,
                s.OtherDeduction,
                s.TotalAmount,
                s.Status
            }
        ).ToListAsync();

        var ids = rows.Select(r => r.Id).ToList();
        var pieceworkMap = await _db.SalaryStatementItems.AsNoTracking()
            .Where(i => ids.Contains(i.StatementId))
            .GroupBy(i => i.StatementId)
            .Select(g => new
            {
                StatementId = g.Key,
                Piecework = g.Sum(i => i.Amount - (i.DeductAmount ?? 0))
            })
            .ToDictionaryAsync(x => x.StatementId, x => Round2(x.Piecework));

        var sb = new StringBuilder();
        sb.AppendLine("姓名,周期,底薪,餐补,其他补贴,社保个税,其他扣款,计件合计,应发合计,状态");
        foreach (var r in rows)
        {
            var st = r.Status == 1 ? "已确认" : "草稿";
            var piece = pieceworkMap.GetValueOrDefault(r.Id, 0m);
            sb.AppendLine($"{EscapeCsv(r.Name)},{r.PeriodValue},{r.BaseSalary},{r.MealAllowance},{r.OtherAllowance},{r.SocialTax},{r.OtherDeduction},{piece},{r.TotalAmount},{st}");
        }
        // UTF-8 BOM，Excel 友好
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        var file = $"salary_{periodValue}.csv";
        return (bytes, file);
    }

    private async Task<decimal> SumPieceworkAsync(long statementId)
    {
        var sum = await _db.SalaryStatementItems.AsNoTracking()
            .Where(i => i.StatementId == statementId)
            .SumAsync(i => (decimal?)(i.Amount - (i.DeductAmount ?? 0))) ?? 0m;
        return Round2(sum);
    }

    internal static decimal CalcTotalAmount(
        decimal piecework,
        decimal baseSalary, decimal mealAllowance, decimal otherAllowance,
        decimal socialTax, decimal otherDeduction)
    {
        return Round2(piecework + baseSalary + mealAllowance + otherAllowance - socialTax - otherDeduction);
    }

    private static decimal Round2(decimal v) =>
        Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private static string EscapeCsv(string s) =>
        s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;

    /// <summary>解析周期为 [start, end)。</summary>
    public static (DateTime Start, DateTime End) ParsePeriod(byte periodType, string periodValue, string loc)
    {
        if (periodType == 1)
        {
            if (!DateTime.TryParseExact(periodValue, "yyyy-MM", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var monthStart))
                throw ThrowHelper.Biz(loc, "月周期格式须为 yyyy-MM");
            return (monthStart, monthStart.AddMonths(1));
        }

        // ISO 周：yyyy-Www
        if (periodValue.Length < 8 || periodValue[4] != '-' || periodValue[5] != 'W'
            || !int.TryParse(periodValue.AsSpan(0, 4), out var year)
            || !int.TryParse(periodValue.AsSpan(6), out var week))
            throw ThrowHelper.Biz(loc, "周周期格式须为 yyyy-Www");
        var start = ISOWeek.ToDateTime(year, week, DayOfWeek.Monday);
        return (start, start.AddDays(7));
    }
}
