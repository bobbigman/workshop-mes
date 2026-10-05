using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
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
    /// <summary>docs/136：手工列已移除；旧端仍传字段则忽略不报错。</summary>
    Task<ApiResult<object?>> UpdateComponentsAsync(long id, SalaryComponentsDto dto, long factoryId, long operatorUserId);
    Task<ApiResult<object?>> ConfirmAsync(long id, long factoryId);
    /// <summary>草稿按当前工价原地重算计件明细（docs/133/136）。</summary>
    Task<ApiResult<object?>> RecalcDraftAsync(long id, long factoryId, long operatorUserId);
    /// <summary>软撤回已确认工资单：回草稿、回滚 settled_flag，保留单与明细（docs/136；docs/67 删单语义废弃）。</summary>
    Task<ApiResult<object?>> RevokeAsync(long id, long factoryId, long operatorUserId);
    /// <summary>批量重算：草稿直接 recalc；已确认先软撤回再 recalc（docs/134/136）。</summary>
    Task<ApiResult<object?>> BatchRecalcAsync(long[] ids, long factoryId, long operatorUserId, byte operatorRole);
    Task<ApiResult<object?>> DeleteDraftAsync(long id, long factoryId);
    Task<(byte[] Content, string FileName)> ExportCsvAsync(byte periodType, string periodValue, long factoryId);
    /// <summary>工资明细多维度查询（docs/139）：按工序/时间筛选，报表层直显。</summary>
    Task<ApiResult<PageResult<SalaryItemRowDto>>> QueryItemsAsync(SalaryItemsQueryDto query, long factoryId);
    /// <summary>工资明细导出 CSV（docs/139）。</summary>
    Task<(byte[] Content, string FileName)> ExportItemsCsvAsync(SalaryItemsQueryDto query, long factoryId);
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
    public byte Status { get; set; }
    public List<SalaryItemDto> Items { get; set; } = new();
}

/// <summary>兼容旧端：字段可仍传入，服务端忽略（docs/136）。</summary>
public class SalaryComponentsDto
{
    public decimal BaseSalary { get; set; }
    public decimal MealAllowance { get; set; }
    public decimal OtherAllowance { get; set; }
    public decimal SocialTax { get; set; }
    public decimal OtherDeduction { get; set; }
}

public class SalaryBatchRecalcResultItem
{
    public long Id { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }
}

public class SalaryItemDto
{
    public long Id { get; set; }
    public long ReportId { get; set; }
    public string OrderNo { get; set; } = "";
    public string OperationName { get; set; } = "";
    /// <summary>工单颜色（docs/140）。</summary>
    public string Color { get; set; } = "";
    /// <summary>工单规格尺码（docs/140）。</summary>
    public string Spec { get; set; } = "";
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

/// <summary>工资明细多维度查询入参（docs/139）。</summary>
public class SalaryItemsQueryDto
{
    public byte PeriodType { get; set; } = 1;
    public string PeriodValue { get; set; } = "";
    public long? OperationId { get; set; }
    /// <summary>报工时间起（含），可选；不传则当前周期全部。</summary>
    public DateTime? From { get; set; }
    /// <summary>报工时间止（不含），可选。</summary>
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 200;
}

/// <summary>报表层工资明细行（docs/139；docs/140 追加 color/spec）。</summary>
public class SalaryItemRowDto
{
    public long ItemId { get; set; }
    public long StatementId { get; set; }
    public long ReportId { get; set; }
    public long UserId { get; set; }
    public string UserName { get; set; } = "";
    public string OrderNo { get; set; } = "";
    public long OrderId { get; set; }
    public long OperationId { get; set; }
    public string OperationName { get; set; } = "";
    public int GoodQty { get; set; }
    public int DefectQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DeductAmount { get; set; }
    /// <summary>小计 = amount（计件/计时毛额）。</summary>
    public decimal Amount { get; set; }
    /// <summary>应发 = amount − deduct_amount。</summary>
    public decimal NetAmount { get; set; }
    public DateTime ReportTime { get; set; }
    /// <summary>工单颜色（docs/140）。</summary>
    public string Color { get; set; } = "";
    /// <summary>工单规格尺码（docs/140）。</summary>
    public string Spec { get; set; } = "";
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
    private readonly ILogger<SalaryService> _logger;

    public SalaryService(AppDbContext db, ILogger<SalaryService> logger)
    {
        _db = db;
        _logger = logger;
    }

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
                // 应发合计 = Σ(amount − deduct_amount)（docs/136）
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
        var rawItems = await (
            from i in _db.SalaryStatementItems.AsNoTracking()
            where i.StatementId == id
            join r in _db.Reports.AsNoTracking() on i.ReportId equals r.Id
            join o in _db.WorkOrders.AsNoTracking() on r.OrderId equals o.Id
            join op in _db.Operations.AsNoTracking() on r.OperationId equals op.Id
            orderby r.ReportTime
            select new
            {
                i.Id,
                i.ReportId,
                OrderId = o.Id,
                OrderNo = o.OrderNo,
                OperationName = op.Name,
                r.GoodQty,
                r.DefectQty,
                r.DurationMinutes,
                i.RuleId,
                i.CalcQty,
                i.UnitPrice,
                i.Amount,
                DeductAmount = i.DeductAmount ?? 0,
                r.ReportTime
            }).ToListAsync();

        var (colorFieldId, specFieldId, _) = await ResolveSkuFieldIdsAsync(factoryId);
        var orderIds = rawItems.Select(x => x.OrderId).Distinct().ToList();
        var valueMap = await LoadSkuValuesAsync(orderIds, colorFieldId, specFieldId);
        string Lookup(long orderId, long? fieldId) =>
            fieldId.HasValue && valueMap.TryGetValue((orderId, fieldId.Value), out var v) ? v : "";

        var items = rawItems.Select(x => new SalaryItemDto
        {
            Id = x.Id,
            ReportId = x.ReportId,
            OrderNo = x.OrderNo,
            OperationName = x.OperationName,
            Color = Lookup(x.OrderId, colorFieldId),
            Spec = Lookup(x.OrderId, specFieldId),
            GoodQty = x.GoodQty,
            DefectQty = x.DefectQty,
            DurationMinutes = x.DurationMinutes,
            RuleId = x.RuleId,
            CalcQty = x.CalcQty,
            UnitPrice = x.UnitPrice,
            Amount = x.Amount,
            DeductAmount = x.DeductAmount,
            NetAmount = x.Amount - x.DeductAmount,
            Matched = x.RuleId != null,
            ReportTime = x.ReportTime
        }).ToList();

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
            Status = s.Status,
            Items = items
        });
    }

    public async Task<ApiResult<object?>> UpdateComponentsAsync(long id, SalaryComponentsDto dto, long factoryId, long operatorUserId)
    {
        // docs/136：手工列已移除；旧端仍传字段则忽略，不报错
        _ = dto;
        var statement = await _db.SalaryStatements
            .FirstOrDefaultAsync(s => s.Id == id && s.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(UpdateComponentsAsync), "工资单不存在");

        if (statement.Status != 0)
        {
            _logger.LogWarning(
                "工资单 components 被拒（非草稿） statementId={Id} status={Status} operator={Op}",
                id, statement.Status, operatorUserId);
            throw ThrowHelper.BizUser("已确认工资单不能修改");
        }

        var piecework = await SumPieceworkAsync(id);
        statement.TotalAmount = piecework;
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "工资单 components 已忽略手工字段（docs/136） statementId={Id} operator={Op} total={Total}",
            id, operatorUserId, statement.TotalAmount);

        return ApiResult<object?>.Ok(new
        {
            totalAmount = statement.TotalAmount,
            pieceworkAmount = piecework
        });
    }

    /// <summary>草稿按当前工价原地重算计件明细（docs/133/136）。</summary>
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
        statement.TotalAmount = CalcTotalAmount(piecework);

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
    /// 软撤回已确认工资单：status→草稿、settled_flag→0、保留单与明细（docs/136）。
    /// docs/67 删单语义废弃；历史已被旧 revoke 删掉的单不回溯。
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

        var reportIds = await _db.SalaryStatementItems.AsNoTracking()
            .Where(i => i.StatementId == id)
            .Select(i => i.ReportId)
            .ToListAsync();
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

            statement.Status = 0;
            statement.ConfirmedAt = null;
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        _logger.LogInformation(
            "工资单已软撤回 statementId={Id} operator={Op} totalAmount={Total} reportCount={Cnt} userId={UserId} period={PeriodType}/{PeriodValue}",
            id, operatorUserId, auditTotal, reportIds.Count, auditUserId, auditPeriodType, auditPeriodValue);

        return ApiResult<object?>.OkMsg();
    }

    /// <summary>
    /// 批量重算（docs/134/136）：草稿→recalc；已确认→软撤回+recalc（仅管理员）；失败不停批。
    /// </summary>
    public async Task<ApiResult<object?>> BatchRecalcAsync(
        long[] ids, long factoryId, long operatorUserId, byte operatorRole)
    {
        if (ids == null || ids.Length == 0)
            throw ThrowHelper.Biz(nameof(BatchRecalcAsync), "请至少选择一张工资单");

        var results = new List<SalaryBatchRecalcResultItem>();
        var isAdmin = operatorRole == 1;

        foreach (var id in ids.Distinct())
        {
            try
            {
                var statement = await _db.SalaryStatements.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == id && s.FactoryId == factoryId);
                if (statement == null)
                {
                    results.Add(new SalaryBatchRecalcResultItem
                    {
                        Id = id,
                        Success = false,
                        Message = "工资单不存在或不属于本厂"
                    });
                    continue;
                }

                if (statement.Status == 1)
                {
                    if (!isAdmin)
                    {
                        results.Add(new SalaryBatchRecalcResultItem
                        {
                            Id = id,
                            Success = false,
                            Message = "仅管理员可对已确认工资单批量重算"
                        });
                        continue;
                    }

                    await RevokeAsync(id, factoryId, operatorUserId);
                    await RecalcDraftAsync(id, factoryId, operatorUserId);
                    results.Add(new SalaryBatchRecalcResultItem
                    {
                        Id = id,
                        Success = true,
                        Message = "已撤回并重算"
                    });
                }
                else if (statement.Status == 0)
                {
                    await RecalcDraftAsync(id, factoryId, operatorUserId);
                    results.Add(new SalaryBatchRecalcResultItem
                    {
                        Id = id,
                        Success = true,
                        Message = "已重算"
                    });
                }
                else
                {
                    results.Add(new SalaryBatchRecalcResultItem
                    {
                        Id = id,
                        Success = false,
                        Message = "已发放工资单不可重算"
                    });
                }
            }
            catch (Exception ex)
            {
                var msg = ex is BusinessException be ? be.Message : ex.Message;
                _logger.LogWarning(ex,
                    "批量重算单张失败 statementId={Id} operator={Op}", id, operatorUserId);
                results.Add(new SalaryBatchRecalcResultItem
                {
                    Id = id,
                    Success = false,
                    Message = msg
                });
            }
        }

        var ok = results.Count(r => r.Success);
        var fail = results.Count - ok;
        _logger.LogInformation(
            "批量重算完成 operator={Op} total={Total} ok={Ok} fail={Fail}",
            operatorUserId, results.Count, ok, fail);

        return ApiResult<object?>.Ok(new { list = results, ok, fail });
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
        sb.AppendLine("姓名,周期,计件合计,应发合计,状态");
        foreach (var r in rows)
        {
            var st = r.Status == 1 ? "已确认" : "草稿";
            var piece = pieceworkMap.GetValueOrDefault(r.Id, 0m);
            sb.AppendLine($"{EscapeCsv(r.Name)},{r.PeriodValue},{piece},{r.TotalAmount},{st}");
        }
        // UTF-8 BOM，Excel 友好
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        var file = $"salary_{periodValue}.csv";
        return (bytes, file);
    }

    /// <summary>工资明细多维度查询（docs/139）。</summary>
    public async Task<ApiResult<PageResult<SalaryItemRowDto>>> QueryItemsAsync(
        SalaryItemsQueryDto query, long factoryId)
    {
        var list = await LoadItemRowsAsync(query, factoryId, applyPaging: true);
        var total = await CountItemRowsAsync(query, factoryId);
        return ApiResult<PageResult<SalaryItemRowDto>>.Ok(new PageResult<SalaryItemRowDto>
        {
            List = list,
            Total = total
        });
    }

    /// <summary>工资明细导出 CSV（docs/139；docs/140 含颜色/规格）。</summary>
    public async Task<(byte[] Content, string FileName)> ExportItemsCsvAsync(
        SalaryItemsQueryDto query, long factoryId)
    {
        var rows = await LoadItemRowsAsync(query, factoryId, applyPaging: false);
        var sb = new StringBuilder();
        sb.AppendLine("人员,工单,工序,颜色,规格尺码,良品,不良,单价,不良扣款,小计,应发,报工时间");
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(',',
                EscapeCsv(r.UserName),
                EscapeCsv(r.OrderNo),
                EscapeCsv(r.OperationName),
                EscapeCsv(r.Color),
                EscapeCsv(r.Spec),
                r.GoodQty,
                r.DefectQty,
                r.UnitPrice,
                r.DeductAmount,
                r.Amount,
                r.NetAmount,
                r.ReportTime.ToString("yyyy-MM-dd HH:mm:ss")));
        }
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        var period = (query.PeriodValue ?? "").Trim();
        var file = $"salary_items_{period}.csv";
        return (bytes, file);
    }

    private async Task<int> CountItemRowsAsync(SalaryItemsQueryDto query, long factoryId)
    {
        var q = BuildItemRowsQuery(query, factoryId);
        return await q.CountAsync();
    }

    private async Task<List<SalaryItemRowDto>> LoadItemRowsAsync(
        SalaryItemsQueryDto query, long factoryId, bool applyPaging)
    {
        var q = BuildItemRowsQuery(query, factoryId);
        q = q.OrderBy(x => x.ReportTime).ThenBy(x => x.ItemId);

        if (applyPaging)
        {
            var page = query.Page < 1 ? 1 : query.Page;
            var size = query.PageSize < 1 ? 200 : Math.Min(query.PageSize, 500);
            q = q.Skip((page - 1) * size).Take(size);
        }

        var rows = await q.ToListAsync();

        // docs/140：复用色码 Lookup（无 color/spec 字段时为空串）
        var (colorFieldId, specFieldId, _) = await ResolveSkuFieldIdsAsync(factoryId);
        if (rows.Count == 0 || (colorFieldId == null && specFieldId == null))
            return rows;

        var orderIds = rows.Select(r => r.OrderId).Distinct().ToList();
        var valueMap = await LoadSkuValuesAsync(orderIds, colorFieldId, specFieldId);

        string Lookup(long orderId, long? fieldId) =>
            fieldId.HasValue && valueMap.TryGetValue((orderId, fieldId.Value), out var v) ? v : "";

        foreach (var r in rows)
        {
            r.Color = Lookup(r.OrderId, colorFieldId);
            r.Spec = Lookup(r.OrderId, specFieldId);
        }
        return rows;
    }

    private IQueryable<SalaryItemRowDto> BuildItemRowsQuery(SalaryItemsQueryDto query, long factoryId)
    {
        var periodValue = (query.PeriodValue ?? "").Trim();
        if (query.PeriodType is not (1 or 2))
            throw ThrowHelper.Biz(nameof(QueryItemsAsync), "周期类型须为 1月 / 2周");
        if (string.IsNullOrEmpty(periodValue))
            throw ThrowHelper.Biz(nameof(QueryItemsAsync), "周期值不能为空");
        _ = ParsePeriod(query.PeriodType, periodValue, nameof(QueryItemsAsync));

        DateTime? from = query.From;
        DateTime? to = query.To;
        if (from.HasValue && to.HasValue && from.Value >= to.Value)
            throw ThrowHelper.Biz(nameof(QueryItemsAsync), "时间范围起须早于止");

        var q =
            from i in _db.SalaryStatementItems.AsNoTracking()
            join s in _db.SalaryStatements.AsNoTracking() on i.StatementId equals s.Id
            where s.FactoryId == factoryId
                  && s.PeriodType == query.PeriodType
                  && s.PeriodValue == periodValue
            join r in _db.Reports.AsNoTracking() on i.ReportId equals r.Id
            join u in _db.Users.AsNoTracking() on s.UserId equals u.Id
            join o in _db.WorkOrders.AsNoTracking() on r.OrderId equals o.Id
            join op in _db.Operations.AsNoTracking() on r.OperationId equals op.Id
            select new { i, s, r, u, o, op };

        if (query.OperationId.HasValue)
            q = q.Where(x => x.r.OperationId == query.OperationId.Value);
        if (from.HasValue)
            q = q.Where(x => x.r.ReportTime >= from.Value);
        if (to.HasValue)
            q = q.Where(x => x.r.ReportTime < to.Value);

        return q.Select(x => new SalaryItemRowDto
        {
            ItemId = x.i.Id,
            StatementId = x.s.Id,
            ReportId = x.i.ReportId,
            UserId = x.s.UserId,
            UserName = x.u.Name,
            OrderNo = x.o.OrderNo,
            OrderId = x.o.Id,
            OperationId = x.op.Id,
            OperationName = x.op.Name,
            GoodQty = x.r.GoodQty,
            DefectQty = x.r.DefectQty,
            UnitPrice = x.i.UnitPrice,
            DeductAmount = x.i.DeductAmount ?? 0,
            Amount = x.i.Amount,
            NetAmount = x.i.Amount - (x.i.DeductAmount ?? 0),
            ReportTime = x.r.ReportTime,
            Color = "",
            Spec = ""
        });
    }

    private async Task<decimal> SumPieceworkAsync(long statementId)
    {
        var sum = await _db.SalaryStatementItems.AsNoTracking()
            .Where(i => i.StatementId == statementId)
            .SumAsync(i => (decimal?)(i.Amount - (i.DeductAmount ?? 0))) ?? 0m;
        return Round2(sum);
    }

    /// <summary>应发合计 = Σ(amount − deduct_amount)（docs/136）。</summary>
    internal static decimal CalcTotalAmount(decimal piecework) => Round2(piecework);

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
