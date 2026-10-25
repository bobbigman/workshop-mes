using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;
using Microsoft.EntityFrameworkCore;

namespace ahu.MicrosoftMes.Services;

/// <summary>价目内容不可变；有效分段闭合/替代时由调价批次保存原始时间线。</summary>
public class PriceTimelineService(AppDbContext db)
{
    public static DateTime Today => DateTime.SpecifyKind(DateTime.UtcNow.AddHours(8).Date, DateTimeKind.Unspecified);
    public static DateTime Day(DateTime value) => value.Kind == DateTimeKind.Utc ? value.AddHours(8).Date : value.Date;
    public static T Clone<T>(T value) => OperationPricingService.Read<T>(OperationPricingService.Write(value));
    public static void Sign(ProcessingQuote quote)
    {
        quote.QuoteToken = "";
        quote.QuoteToken = MaintenanceSqlService.Hash(OperationPricingService.Write(quote));
    }

    public async Task EnsureBaselineAsync(OperationPriceList table)
    {
        if (await db.OperationPriceRevisions.AnyAsync(x => x.FactoryId == table.FactoryId && x.PriceListId == table.Id)) return;
        // 当前版本最早只能从可核实的最后修改时间起解释价格，不能补造此前版本。
        var knownFrom = table.UpdatedAt >= new DateTime(2000,1,1) && table.UpdatedAt <= DateTime.Now
            ? Day(table.UpdatedAt) : Today;
        if (table.ActivatedAt.HasValue && Day(table.ActivatedAt.Value) > knownFrom) knownFrom = Day(table.ActivatedAt.Value);
        var from = table.EffectiveFrom.Date > knownFrom ? table.EffectiveFrom.Date : knownFrom;
        if (table.EffectiveTo.HasValue && table.EffectiveTo.Value.Date < from) return;
        await RecordAsync(table, from, 0);
    }

    public async Task RecordAsync(OperationPriceList table, DateTime from, long actor,
        Dictionary<long, SysCustomField>? fields = null)
    {
        var old = await db.OperationPriceRevisions.Where(x => x.FactoryId == table.FactoryId && x.PriceListId == table.Id && !x.Superseded).ToListAsync();
        OperationPriceListService.Require(!old.Any(x => x.EffectiveFrom > Today && x.EffectiveFrom >= from), "已有未来调价安排，请先核对，不可覆盖");
        foreach (var item in old.Where(x => x.EffectiveTo == null || x.EffectiveTo >= from))
        {
            if (item.EffectiveFrom >= from) item.Superseded = true;
            else item.EffectiveTo = from.AddDays(-1);
        }
        fields ??= await new OperationAttributeService(db).FieldsAsync(table.FactoryId, table.OperationId);
        var snapshot = Clone(table);
        snapshot.EffectiveFrom = from.Date;
        if (snapshot.EffectiveTo.HasValue && snapshot.EffectiveTo.Value < from)
        {
            // 已过期价表仍可停用/维护；关闭旧段即可，不制造起止倒置的新段。
            await db.SaveChangesAsync();
            return;
        }
        db.OperationPriceRevisions.Add(new()
        {
            FactoryId = table.FactoryId, PriceListId = table.Id, OperationId = table.OperationId, ProductId = table.ProductId,
            EffectiveFrom = from.Date, EffectiveTo = table.EffectiveTo, SnapshotJson = OperationPricingService.Write(snapshot),
            FieldsJson = OperationPricingService.Write(fields), CreatedBy = actor, CreatedAt = DateTime.Now
        });
        await db.SaveChangesAsync();
    }

    public sealed record Selection(OperationPriceList Table, Dictionary<long,SysCustomField> Fields, long? Revision);
    public async Task<ProcessingQuote> QuoteAsync(long factoryId, long productId, long operationId, long reportUnitId,
        Dictionary<long,string?> values, DateTime date, OperationPriceList? proposed = null,
        Dictionary<long,SysCustomField>? proposedFields = null)
        => Calculate(await SelectAsync(factoryId,productId,operationId,date,proposed,proposedFields),productId,reportUnitId,values,date);

    public async Task<Selection> SelectAsync(long factoryId, long productId, long operationId, DateTime date, OperationPriceList? proposed = null,
        Dictionary<long,SysCustomField>? proposedFields = null)
    {
        var day = Day(date);
        var revisions = await db.OperationPriceRevisions.AsNoTracking().Where(x => x.FactoryId == factoryId && x.OperationId == operationId
            && !x.Superseded && x.EffectiveFrom <= day && (x.EffectiveTo == null || x.EffectiveTo >= day)).ToListAsync();
        var candidates = new List<(OperationPriceList Table, Dictionary<long,SysCustomField> Fields, long? Revision)>();
        foreach (var r in revisions)
        {
            if (proposed?.Id == r.PriceListId) continue;
            var t = OperationPricingService.Read<OperationPriceList>(r.SnapshotJson);
            t.EffectiveFrom = r.EffectiveFrom; t.EffectiveTo = r.EffectiveTo;
            if (t.Status == 1) candidates.Add((t, OperationPricingService.Read<Dictionary<long,SysCustomField>>(r.FieldsJson), r.Id));
        }
        // 未初始化的旧表仅可临时解释当天及以后；不把当前价冒充过去的真实价格。
        var roots = await db.OperationPriceLists.AsNoTracking().Where(x => x.FactoryId == factoryId && x.OperationId == operationId).ToListAsync();
        var recorded = await db.OperationPriceRevisions.AsNoTracking().Where(x => x.FactoryId == factoryId).Select(x => x.PriceListId).Distinct().ToListAsync();
        foreach (var root in roots.Where(x => !recorded.Contains(x.Id) && x.Id != proposed?.Id && day >= Today && x.Status == 1
            && x.EffectiveFrom <= day && (x.EffectiveTo == null || x.EffectiveTo >= day)))
            candidates.Add((root, await new OperationAttributeService(db).FieldsAsync(factoryId, operationId), null));
        if (proposed != null) candidates.Add((proposed, proposedFields!, null));
        var selected = OperationPriceListService.SelectTable(candidates.Select(x => x.Table), productId, operationId);
        var candidate = candidates.Single(x => ReferenceEquals(x.Table, selected));
        return new(candidate.Table,candidate.Fields,candidate.Revision);
    }

    public static ProcessingQuote Calculate(Selection candidate,long productId,long reportUnitId,Dictionary<long,string?> values,DateTime date)
    {
        var quote = OperationPriceListService.Calculate(candidate.Table, productId, reportUnitId, values, candidate.Fields, Day(date));
        quote.PriceRevisionId = candidate.Revision;
        quote.FieldDefinitions = Clone(candidate.Fields);
        Sign(quote);
        return quote;
    }

    public static async Task InitializeAsync(AppDbContext db)
    {
        var tables = await db.OperationPriceLists.ToListAsync();
        foreach (var table in tables)
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            await OperationPriceListService.LockFactoryAsync(db, table.FactoryId);
            await new PriceTimelineService(db).EnsureBaselineAsync(table);
            await tx.CommitAsync();
        }
    }
}
