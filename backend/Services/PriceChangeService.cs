using System.Security.Cryptography;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ahu.MicrosoftMes.Services;

public record PriceChangeRow(string RowId, decimal Amount, decimal DeductPrice);
public class PriceChangeInput
{
    public Guid RequestId { get; set; }
    public string ExpectedVersion { get; set; } = "";
    public DateTime EffectiveFrom { get; set; }
    public bool RepriceCurrentMonth { get; set; }
    public string Reason { get; set; } = "";
    public List<PriceChangeRow> Rows { get; set; } = new();
}
public record PriceChangeCommit(Guid RequestId, string PreviewToken, string PlanHash);
public record PriceChangeDraft(long FactoryId, long ActorId, long TableId, PriceChangeInput Input, string PlanHash, DateTime ExpiresAt);
public sealed class PriceChangeDraftStore(IMemoryCache cache)
{
    public string Add(PriceChangeDraft value)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        cache.Set("price-change:" + token, value, TimeSpan.FromMinutes(5));
        return token;
    }
    public PriceChangeDraft Get(string token) => token is { Length: 64 }
        && cache.TryGetValue<PriceChangeDraft>("price-change:" + token, out var draft) && draft != null && draft.ExpiresAt > DateTime.UtcNow
        ? draft : throw ThrowHelper.BizUser("调价预览结果已过期失效。请重新执行预览操作，获取最新调价方案。");
}

public class PriceChangeService(AppDbContext db, PriceChangeDraftStore drafts)
{
    private static T Copy<T>(T x) => PriceTimelineService.Clone(x);
    private static string Json<T>(T x) => OperationPricingService.Write(x);
    private static void Require(bool ok, string text) => OperationPriceListService.Require(ok, text);
    private static string Structure(OperationPriceList table)
    {
        var details = OperationPriceListService.Details(table);
        return Json(new { table.ProductId,table.OperationId,table.PricingType,table.PriceUnit,table.ReportUnitId,unitsPerPriceUnit=table.UnitsPerPriceUnit.ToString("G29",System.Globalization.CultureInfo.InvariantCulture),
            details.RequiredFieldIds,details.Groups, rows=details.Rows.Select(r=>new {r.RowId,r.Kind,r.Conditions,r.GroupKey,r.IncludedRowIds}) });
    }
    private sealed record Change(ProdReport OldReport, ProdReport NewReport, SalaryStatementItem? OldItem, SalaryStatementItem? NewItem);
    private sealed class Plan
    {
        public OperationPriceList Before { get; set; } = null!;
        public OperationPriceList After { get; set; } = null!;
        public Dictionary<long,SysCustomField> Fields { get; set; } = new();
        public List<OperationPriceRevision> Revisions { get; set; } = new();
        public List<Change> Changes { get; set; } = new();
        public List<SalaryStatement> OldStatements { get; set; } = new();
        public List<SalaryStatement> NewStatements { get; set; } = new();
        public List<SalaryStatementItem> StatementItems { get; set; } = new();
        public List<ProdReport> SettlementReports { get; set; } = new();
        public List<string> Blockers { get; set; } = new();
        public string Hash { get; set; } = "";
    }

    private async Task AuthorizeAsync(long factoryId, long actor)
    {
        Require(await db.Users.AsNoTracking().AnyAsync(u => u.Id == actor && u.FactoryId == factoryId && u.Role == 1 && u.Status == 1), "只有本厂的有效管理员账号才能调整工价。请确认当前登录账号具有管理员权限且归属本厂。");
    }

    public async Task<object> PreviewAsync(long id, PriceChangeInput input, long factoryId, long actor)
    {
        await AuthorizeAsync(factoryId, actor);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await OperationPriceListService.LockFactoryAsync(db, factoryId);
        input = Copy(input);
        var plan = await BuildAsync(id, input, factoryId);
        var expiresAt = DateTime.UtcNow.AddMinutes(5);
        var token = plan.Blockers.Count == 0 ? drafts.Add(new(factoryId, actor, id, input, plan.Hash, expiresAt)) : null;
        await tx.CommitAsync();
        return new
        {
            previewToken = token, expiresAt, planHash = plan.Hash, input.RequestId, input.EffectiveFrom,
            endDate = input.RepriceCurrentMonth ? new DateTime(PriceTimelineService.Today.Year, PriceTimelineService.Today.Month, 1).AddMonths(1).AddDays(-1) : (DateTime?)null,
            reportCount = plan.Changes.Count, employeeCount = plan.Changes.Select(x => x.OldReport.UserId).Distinct().Count(),
            statementCount = plan.OldStatements.Count, revokeStatementIds = plan.OldStatements.Where(x => x.Status == 1).Select(x => x.Id),
            reportOldAmount = plan.Changes.Sum(x => x.OldReport.WageAmount ?? 0), reportNewAmount = plan.Changes.Sum(x => x.NewReport.WageAmount ?? 0),
            oldAmount = plan.Changes.Sum(x => x.OldItem == null ? 0 : x.OldItem.Amount - (x.OldItem.DeductAmount ?? 0)),
            newAmount = plan.Changes.Sum(x => x.NewItem == null ? 0 : x.NewItem.Amount - (x.NewItem.DeductAmount ?? 0)),
            rows = plan.Changes.Select(x => new { reportId = x.OldReport.Id, x.OldReport.UserId, x.OldReport.ReportTime,
                statementId = x.OldItem?.StatementId, oldReportAmount = x.OldReport.WageAmount, newReportAmount = x.NewReport.WageAmount,
                oldAmount = x.OldItem == null ? (decimal?)null : x.OldItem.Amount - (x.OldItem.DeductAmount ?? 0),
                newAmount = x.NewItem == null ? (decimal?)null : x.NewItem.Amount - (x.NewItem.DeductAmount ?? 0) }), blockers = plan.Blockers
        };
    }

    private async Task<Plan> BuildAsync(long id, PriceChangeInput input, long factoryId)
    {
        Require(input.RequestId != Guid.Empty && !string.IsNullOrWhiteSpace(input.ExpectedVersion), "调价请求缺少必要的标识或价表版本信息。请刷新页面重新发起调价流程，确保信息完整。");
        Require(input.Rows is { Count: > 0 and <= 1000 } && input.Rows.All(x => x != null), "还没有勾选要调整的价格行。请在调价列表中至少勾选一行后再提交。");
        Require(input.Rows.Select(x => x.RowId).Distinct().Count() == input.Rows.Count, "调价请求中有重复的行标识。请刷新页面重新选择调价行，避免重复提交。");
        Require((input.Reason?.Length ?? 0) <= 500, "调价原因说明不能超过500字。请把原因精简到500字以内再提交。");
        input.Reason = input.Reason?.Trim() ?? "";
        var from = input.EffectiveFrom.Date;
        var today = PriceTimelineService.Today;
        Require(from >= new DateTime(2000,1,1), "生效日期未选或格式不正确。请在调价表单中选择一个有效的生效日期。");
        Require(!input.RepriceCurrentMonth || (from.Year == today.Year && from.Month == today.Month && from <= today), "本月追溯生效日必须在本月1日到今天之间。请把生效日期调整到该合法范围内。");
        Require(input.RepriceCurrentMonth || from >= today, "选择了过去日期作为生效日，需要勾选「本月追溯重算」。请确认是否需要追溯重算，勾选后重新提交。");
        Require(!input.RepriceCurrentMonth || input.Reason.Length > 0, "追溯重算会影响历史工资，必须填写调价原因。请在原因栏写明追溯调整的理由后再提交。");
        var old = await db.OperationPriceLists.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.FactoryId == factoryId) ?? throw ThrowHelper.BizUser("指定的价目表不存在或不属于本厂。请到「工序价目表」列表确认该表是否存在及归属。");
        Require(old.Version == input.ExpectedVersion && old.Status == 1, "价目表在你操作期间被修改过或已停用。请刷新页面重新加载最新状态后再预览调价。");
        Require(OperationPriceListService.Details(old).SchemaVersion == 2, "这张旧版价目表需要先完成格式转换才能调价。请先执行旧表转换，完成后再发起调价。");
        Require(old.EffectiveTo == null || old.EffectiveTo >= from, "生效日期晚于失效日期，前后矛盾。请把生效日期改成早于失效日期。");
        var revisions = await db.OperationPriceRevisions.AsNoTracking().Where(x => x.FactoryId == factoryId).OrderBy(x => x.Id).ToListAsync();
        Require(!revisions.Any(x => x.PriceListId == id && !x.Superseded && x.EffectiveFrom > today), "这张价目表已有一个未来的调价安排在排队，不能叠加新的。请先查看现有调价计划，确认后再决定取消或等待。");
        Require(!input.RepriceCurrentMonth || !revisions.Any(x => x.PriceListId == id && !x.Superseded
            && (x.EffectiveTo == null || x.EffectiveTo >= from) && Structure(OperationPricingService.Read<OperationPriceList>(x.SnapshotJson)) != Structure(old)),
            "追溯区间内发生了条件、单位或产品范围的结构性变化，不能跨变化点追溯。请选择结构调整完成之后的日期作为生效日。");
        var snapshot = revisions.LastOrDefault(x => x.PriceListId == id && !x.Superseded);
        var fields = snapshot == null ? await new OperationAttributeService(db).FieldsAsync(factoryId, old.OperationId)
            : OperationPricingService.Read<Dictionary<long,SysCustomField>>(snapshot.FieldsJson);
        var next = Copy(old);
        var details = OperationPriceListService.Details(next);
        foreach (var patch in input.Rows)
        {
            var row = details.Rows.SingleOrDefault(x => x.RowId == patch.RowId) ?? throw ThrowHelper.BizUser("调价行在当前价目表中已不存在。请刷新页面重新加载最新价格行列表后再操作。");
            Require(patch.Amount is >= 0 and <= 1000000 && patch.DeductPrice is >= 0 and <= 1000000
                && decimal.Round(patch.Amount,4) == patch.Amount && decimal.Round(patch.DeductPrice,4) == patch.DeductPrice, "调价金额必须在0到1000000之间，最多4位小数。请检查输入金额是否符合范围和小数位数要求。");
            row.Amount = patch.Amount; row.DeductPrice = patch.DeductPrice;
        }
        Require(Json(details) != Json(OperationPriceListService.Details(old)), "新旧价格完全相同，没有实际变化。请确认是否选错了调价行，或把目标金额改成与原值不同。");
        OperationPriceListService.Validate(details, fields, true, next.PricingType);
        next.Details = Json(details); next.EffectiveFrom = from;
        next.Version = MaintenanceSqlService.Hash(Json(input))[..32];
        var plan = new Plan { Before = old, After = next, Fields = fields, Revisions = revisions };
        var roots = await db.OperationPriceLists.AsNoTracking().Where(x => x.FactoryId == factoryId).OrderBy(x => x.Id).ToListAsync();
        Require(!roots.Any(x => x.Id != id && x.OperationId == old.OperationId && x.ProductId == old.ProductId && x.Status == 1
            && (x.EffectiveTo == null || x.EffectiveTo >= from) && (next.EffectiveTo == null || x.EffectiveFrom <= next.EffectiveTo)), "同一产品范围内已有另一张价目表有效期与本次调价重叠，无法安排。请先处理重叠价目表（停用或调整日期），再重新提交调价。");
        var reports = input.RepriceCurrentMonth ? await db.Reports.AsNoTracking().Where(x => x.FactoryId == factoryId && x.OperationId == old.OperationId
            && x.ReportTime >= from && x.ReportTime < new DateTime(today.Year,today.Month,1).AddMonths(1)).OrderBy(x => x.Id).ToListAsync() : new List<ProdReport>();
        var ids = reports.Select(x => x.Id).ToList();
        var items = await db.SalaryStatementItems.AsNoTracking().Where(x => ids.Contains(x.ReportId)).OrderBy(x => x.Id).ToListAsync();
        var statementIds = items.Select(x => x.StatementId).Distinct().ToList();
        var statements = await db.SalaryStatements.AsNoTracking().Where(x => statementIds.Contains(x.Id)).OrderBy(x => x.Id).ToListAsync();
        var allItems = await db.SalaryStatementItems.AsNoTracking().Where(x => statementIds.Contains(x.StatementId)).OrderBy(x => x.Id).ToListAsync();
        plan.StatementItems = allItems;
        foreach (var report in reports)
        {
            if (old.ProductId.HasValue && report.ProductId.HasValue && report.ProductId != old.ProductId) continue;
            try
            {
                Require(report.PriceSnapshot != null, $"请检查这条报工的原始工资记录是否完整：报工{report.Id}");
                var prior = OperationPricingService.Read<ReportPriceSnapshot>(report.PriceSnapshot!);
                _ = ReportPriceSnapshot.Calculate(report, 1m);
                var basis = prior.Processing!;
                var selected = await new PriceTimelineService(db).SelectAsync(factoryId,basis.ProductId,basis.OperationId,report.ReportTime,next,fields);
                if (selected.Table.Id != id)
                {
                    Require(prior.PriceListId != id, $"报工{report.Id}的原价表与当前产品范围对不上，请核查。");
                    continue;
                }
                var q = PriceTimelineService.Calculate(selected,basis.ProductId,basis.ReportUnitId,basis.AttributeEvidence!.Attributes,report.ReportTime);
                Require(q.PriceType == prior.PriceType && q.ReportUnitId == basis.ReportUnitId && q.UnitsPerPriceUnit == basis.UnitsPerPriceUnit
                    && q.PriceUnit == basis.PriceUnit, $"报工{report.Id}的计价类型或单位折算已变化，不能只调数字。");
                var linked = items.Where(x => x.ReportId == report.Id).ToList();
                Require(linked.Count <= 1, $"报工{report.Id}已被多张工资单引用，需先处理冲突。");
                var item = linked.SingleOrDefault();
                if (item != null)
                {
                    var statement = statements.SingleOrDefault(x => x.Id == item.StatementId);
                    Require(statement != null && statement.FactoryId == factoryId && statement.UserId == report.UserId, $"报工{report.Id}的工资归属有矛盾，请检查。");
                    Require(statement!.Status is 0 or 1, $"工资单{statement.Id}已发放或当前状态不能修改。");
                    Require(report.ReviewStatus == 1 && item.PriceListId == prior.PriceListId && item.PriceType == prior.PriceType, $"报工{report.Id}的复核状态或工资来源有冲突，请检查。");
                    Require(item.WageCoeff > 0, $"报工{report.Id}工资明细中的人员倍数无效，请检查。");
                    var expected = ReportPriceSnapshot.Calculate(report,item.WageCoeff);
                    Require(item.Amount == expected.Amount && (item.DeductAmount ?? 0) == expected.DeductAmount && item.UnitPrice == expected.UnitPrice
                        && item.CalcQty == expected.CalcQty && item.DurationMinutes == report.DurationMinutes && item.WageCoeff == expected.WageCoeff
                        && report.SettledFlag == (statement.Status == 1), $"报工{report.Id}的工资明细与原始记录对不上，请检查。");
                }
                else Require(!report.SettledFlag, $"报工{report.Id}已结算但缺少可靠的工资明细，需补全后再操作。");
                var coeff = prior.AppliedWageCoeff;
                if (coeff == null && item != null)
                {
                    // 工资明细是可靠系数来源，但须核对它能解释原报工显示金额。
                    var recorded = ReportPriceSnapshot.Calculate(report,item.WageCoeff);
                    if (recorded.WageAmount == report.WageAmount) coeff = item.WageCoeff;
                }
                Require(coeff is > 0, $"报工{report.Id}缺少可靠的历史人员倍数，请核查，不能直接套用当前倍数。");
                Require(ReportPriceSnapshot.Calculate(report,coeff!.Value).WageAmount == report.WageAmount, $"报工{report.Id}原金额与历史人员倍数对不上，请检查。");
                var updated = Copy(report);
                updated.PriceSnapshot = Json(new ReportPriceSnapshot(q.AttributeEvidence!.PriceListId,q.PriceType,q.UnitPrice,q.DeductPrice)
                    { Processing = q, PriceRevisionId = q.PriceRevisionId, AppliedWageCoeff = coeff, CoefficientHistory = prior.CoefficientHistory });
                var reportCalc = ReportPriceSnapshot.Calculate(updated,coeff!.Value);
                updated.UnitPrice = reportCalc.UnitPrice; updated.WageAmount = reportCalc.WageAmount;
                SalaryStatementItem? newItem = null;
                if (item != null)
                {
                    newItem = Copy(item);
                    var result = ReportPriceSnapshot.Calculate(updated,item.WageCoeff);
                    newItem.PriceListId = result.PriceListId; newItem.UnitPrice = result.UnitPrice;
                    newItem.Amount = result.Amount; newItem.DeductAmount = result.DeductAmount;
                }
                plan.Changes.Add(new(report,updated,item,newItem));
            }
            catch (BusinessException ex) { plan.Blockers.Add($"报工{report.Id}：{ex.Message}"); }
        }
        var affected = plan.Changes.Where(x => x.OldItem != null).Select(x => x.OldItem!.StatementId).Distinct().ToList();
        foreach (var statement in statements.Where(x => affected.Contains(x.Id)))
        {
            if (statement.TotalAmount != decimal.Round(allItems.Where(x => x.StatementId == statement.Id).Sum(x => x.Amount - (x.DeductAmount ?? 0)),2,MidpointRounding.AwayFromZero))
                plan.Blockers.Add($"工资单{statement.Id}原合计与明细不一致，请先核查");
            var nextStatement = Copy(statement);
            nextStatement.Status = 0; nextStatement.ConfirmedAt = null;
            var amended = plan.Changes.Where(x => x.NewItem?.StatementId == statement.Id).ToDictionary(x => x.NewItem!.Id,x => x.NewItem!);
            nextStatement.TotalAmount = decimal.Round(allItems.Where(x => x.StatementId == statement.Id).Sum(x =>
                (amended.GetValueOrDefault(x.Id) ?? x).Amount - ((amended.GetValueOrDefault(x.Id) ?? x).DeductAmount ?? 0)),2,MidpointRounding.AwayFromZero);
            plan.OldStatements.Add(statement); plan.NewStatements.Add(nextStatement);
        }
        var revokeIds = plan.OldStatements.Where(x => x.Status == 1).Select(x => x.Id).ToList();
        var settlingIds = allItems.Where(x => revokeIds.Contains(x.StatementId)).Select(x => x.ReportId).Distinct().ToList();
        plan.SettlementReports = await db.Reports.AsNoTracking().Where(x => settlingIds.Contains(x.Id)).OrderBy(x => x.Id).ToListAsync();
        foreach (var item in allItems.Where(x => revokeIds.Contains(x.StatementId)))
        {
            var report = plan.SettlementReports.SingleOrDefault(x => x.Id == item.ReportId);
            var statement = plan.OldStatements.Single(x => x.Id == item.StatementId);
            if (report == null || report.FactoryId != factoryId || report.UserId != statement.UserId)
                plan.Blockers.Add($"工资单{statement.Id}有缺失或归属冲突的报工{item.ReportId}，不能撤回确认");
        }
        plan.Hash = MaintenanceSqlService.Hash(Json(new { today, input, old, next, fields, roots, revisions, reports, items, statements, allItems,
            plan.Changes, plan.NewStatements, plan.SettlementReports, plan.Blockers }));
        return plan;
    }

    public async Task<object> CommitAsync(long id, PriceChangeCommit input, long factoryId, long actor)
    {
        await AuthorizeAsync(factoryId,actor);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await OperationPriceListService.LockFactoryAsync(db,factoryId);
        var existing = await db.OperationPriceChanges.AsNoTracking().SingleOrDefaultAsync(x => x.FactoryId == factoryId && x.RequestId == input.RequestId);
        if (existing != null)
        {
            Require(existing.ChangedBy == actor && existing.PriceListId == id && existing.PlanHash == input.PlanHash, "提交调价时发现请求内容或操作者已发生变化。请刷新页面重新预览并提交。");
            await tx.CommitAsync();
            return new { batchId = existing.Id, idempotent = true, result = OperationPricingService.Read<object>(existing.AfterJson) };
        }
        var draft = drafts.Get(input.PreviewToken);
        Require(draft.FactoryId == factoryId && draft.ActorId == actor && draft.TableId == id && draft.Input.RequestId == input.RequestId
            && draft.PlanHash == input.PlanHash, "提交的预览凭据与本次调价操作不匹配。请重新预览后再提交确认，确保凭据一致。");
        var plan = await BuildAsync(id,draft.Input,factoryId);
        Require(plan.Hash == draft.PlanHash && plan.Blockers.Count == 0, "价格或报工范围在你预览之后发生了变化，请重新预览确认最新数据后再提交。");
        var table = await db.OperationPriceLists.SingleAsync(x => x.Id == id && x.FactoryId == factoryId);
        var timeline = new PriceTimelineService(db);
        await timeline.EnsureBaselineAsync(table);
        table.Details = plan.After.Details; table.EffectiveFrom = plan.After.EffectiveFrom; table.Version = plan.After.Version; table.UpdatedAt = DateTime.Now;
        await timeline.RecordAsync(table,table.EffectiveFrom,actor,plan.Fields);
        var revision = await db.OperationPriceRevisions.Where(x => x.FactoryId == factoryId && x.PriceListId == id && !x.Superseded).OrderByDescending(x => x.Id).FirstAsync();
        foreach (var change in plan.Changes)
        {
            var after = OperationPricingService.Read<ReportPriceSnapshot>(change.NewReport.PriceSnapshot!);
            after.Processing!.PriceRevisionId = revision.Id; PriceTimelineService.Sign(after.Processing);
            after = after with { PriceRevisionId = revision.Id };
            change.NewReport.PriceSnapshot = Json(after);
            var report = await db.Reports.SingleAsync(x => x.Id == change.OldReport.Id && x.FactoryId == factoryId);
            report.PriceSnapshot = change.NewReport.PriceSnapshot; report.UnitPrice = change.NewReport.UnitPrice; report.WageAmount = change.NewReport.WageAmount;
            if (change.NewItem != null)
            {
                var item = await db.SalaryStatementItems.SingleAsync(x => x.Id == change.NewItem.Id);
                item.PriceListId = change.NewItem.PriceListId; item.UnitPrice = change.NewItem.UnitPrice;
                item.Amount = change.NewItem.Amount; item.DeductAmount = change.NewItem.DeductAmount;
            }
        }
        foreach (var after in plan.NewStatements)
        {
            var statement = await db.SalaryStatements.SingleAsync(x => x.Id == after.Id && x.FactoryId == factoryId);
            if (statement.Status == 1)
            {
                var allReportIds = plan.StatementItems.Where(x => x.StatementId == statement.Id).Select(x => x.ReportId).ToList();
                foreach (var r in await db.Reports.Where(x => x.FactoryId == factoryId && allReportIds.Contains(x.Id)).ToListAsync()) r.SettledFlag = false;
            }
            statement.Status = 0; statement.ConfirmedAt = null; statement.TotalAmount = after.TotalAmount;
        }
        foreach (var change in plan.Changes)
            change.NewReport.SettledFlag = (await db.Reports.FindAsync(change.NewReport.Id))!.SettledFlag;
        var result = new { table = Copy(table), reportCount = plan.Changes.Count, statementCount = plan.NewStatements.Count,
            revokeStatementIds = plan.OldStatements.Where(x => x.Status == 1).Select(x => x.Id).ToList(),
            oldAmount = plan.Changes.Sum(x => x.OldItem == null ? 0 : x.OldItem.Amount - (x.OldItem.DeductAmount ?? 0)),
            newAmount = plan.Changes.Sum(x => x.NewItem == null ? 0 : x.NewItem.Amount - (x.NewItem.DeductAmount ?? 0)),
            reports = plan.Changes.Select(x => x.NewReport).Concat(plan.SettlementReports.Where(x => !plan.Changes.Any(c => c.NewReport.Id == x.Id))
                .Select(x => { var copy = Copy(x); copy.SettledFlag = false; return copy; })).ToList(), statements = plan.NewStatements, items = plan.Changes.Select(x => x.NewItem).Where(x => x != null).ToList() };
        var batch = new OperationPriceChange { FactoryId = factoryId, PriceListId = id, RequestId = input.RequestId, ChangedBy = actor,
            Reason = draft.Input.Reason, EffectiveFrom = draft.Input.EffectiveFrom.Date, RepriceCurrentMonth = draft.Input.RepriceCurrentMonth,
            RequestHash = MaintenanceSqlService.Hash(Json(draft.Input)), PlanHash = plan.Hash, CreatedAt = DateTime.Now,
            BeforeJson = Json(new { table = plan.Before, revisions = plan.Revisions, reports = plan.Changes.Select(x => x.OldReport).Concat(plan.SettlementReports.Where(x => !plan.Changes.Any(c => c.OldReport.Id == x.Id))), items = plan.StatementItems, statements = plan.OldStatements }), AfterJson = Json(result) };
        db.OperationPriceChanges.Add(batch);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new { batchId = batch.Id, idempotent = false, result };
    }

    public async Task<OperationPriceChange> GetAsync(long id, long factoryId, long actor)
    {
        await AuthorizeAsync(factoryId,actor);
        return await db.OperationPriceChanges.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.FactoryId == factoryId)
            ?? throw ThrowHelper.BizUser("指定的调价记录不存在或不属于本厂。请到调价记录列表确认该记录是否存在及归属。");
    }
}
