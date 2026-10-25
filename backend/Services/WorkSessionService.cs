using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public class WorkTimeInput
{
    public long? SessionId { get; set; }
    public int? DurationMinutes { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
}

public sealed record ResolvedWorkTime(ProdWorkSession? Session, int Minutes, DateTime ReportTime,
    DateTime? StartTime, DateTime? EndTime, byte PriceType);

/// <summary>开工生命周期及逐人计时校验。调用方负责将报工与结束动作放在同一事务。</summary>
public class WorkSessionService(AppDbContext db)
{
    public static int CalculateMinutes(DateTime start, DateTime end, DateTime now)
    {
        if (end <= start || end > now)
            throw ThrowHelper.BizUser("结束时间必须晚于开始时间且不能晚于当前时间");
        var value = Math.Ceiling((end - start).TotalMinutes);
        if (value > 1440) throw ThrowHelper.BizUser("单次报工时长不能超过24小时");
        return (int)value;
    }

    public async Task<ProdWorkSession> StartAsync(long orderId, long operationId, long userId)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId && x.Status == 1)
            ?? throw ThrowHelper.BizUser("当前人员不存在或已停用");
        await using var tx = await db.Database.BeginTransactionAsync();
        // 按人串行开工，避免不同工单同时抢开工；唯一索引作为第二道防线。
        if (db.Database.IsSqlServer())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM sys_user WITH (UPDLOCK, ROWLOCK) WHERE id={userId}");
        var order = await db.WorkOrders.FirstOrDefaultAsync(x => x.Id == orderId && x.FactoryId == user.FactoryId)
            ?? throw ThrowHelper.BizUser("工单不存在或无权访问");
        if (order.Status >= 2) throw ThrowHelper.BizUser("工单已结束或取消，不可开工");
        if (!await db.WorkOrderOperations.AnyAsync(x => x.WorkOrderId == orderId && x.OperationId == operationId))
            throw ThrowHelper.BizUser("该工序不属于此工单");
        if (!await ReportPermissionHelper.CanReportOperationAsync(db, userId, user.Role, operationId))
            throw ThrowHelper.BizUser("无报工权限：您不在该工序的报工权限部门内");
        var old = await db.WorkSessions.FirstOrDefaultAsync(x => x.FactoryId == user.FactoryId && x.UserId == userId && x.Status == 0);
        if (old?.WorkOrderId == orderId && old.OperationId == operationId)
        {
            await tx.CommitAsync();
            return old;
        }
        var now = DateTime.Now;
        if (old != null)
        {
            var count = await db.WorkSessions.Where(x => x.Id == old.Id && x.Status == 0 && x.ReportId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, (byte)2)
                    .SetProperty(x => x.Source, (byte)2).SetProperty(x => x.EndTime, now).SetProperty(x => x.EndedAt, now));
            if (count != 1) throw ThrowHelper.BizUser("开工状态已变化，请刷新后重试");
        }
        var session = new ProdWorkSession { FactoryId = user.FactoryId, UserId = userId,
            WorkOrderId = orderId, OperationId = operationId, StartTime = now, CreatedAt = now };
        db.WorkSessions.Add(session);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return session;
    }

    private async Task<SysUser> RequireAdminAsync(long userId)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId && x.Status == 1)
            ?? throw ThrowHelper.BizUser("当前人员不存在或已停用");
        if (user.Role != 1) throw ThrowHelper.BizUser("仅管理员可补报开工记录");
        return user;
    }

    public async Task<object> PendingAsync(long userId)
    {
        var actor = await RequireAdminAsync(userId);
        return await (from s in db.WorkSessions.AsNoTracking()
            where s.FactoryId == actor.FactoryId && s.Status == 2
            join u in db.Users on s.UserId equals u.Id
            join o in db.WorkOrders on s.WorkOrderId equals o.Id
            join p in db.Products on o.ProductId equals p.Id
            join op in db.Operations on s.OperationId equals op.Id
            orderby s.StartTime
            select new { s.Id, s.UserId, UserName = u.Name, s.WorkOrderId, o.OrderNo,
                ProductName = p.Name, s.OperationId, OperationName = op.Name, s.StartTime, s.EndTime })
            .ToListAsync();
    }

    public async Task<ApiResult<object?>> ReportPendingAsync(long id, ReportDto input, long userId, IReportService reports)
    {
        var actor = await RequireAdminAsync(userId);
        var session = await db.WorkSessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.FactoryId == actor.FactoryId)
            ?? throw ThrowHelper.BizUser("开工记录不存在");
        input.OrderId = session.WorkOrderId;
        input.OperationId = session.OperationId;
        input.SessionId = session.Id;
        input.FromPendingSession = true;
        input.Participants = null;
        input.ParticipantUserIds = null;
        input.Assignees = new() { new ReportAssigneeDto { UserId = session.UserId, GoodQty = input.GoodQty,
            DefectQty = input.DefectQty, DefectId = input.DefectId, SessionId = id,
            StartTime = input.StartTime ?? session.StartTime, EndTime = input.EndTime ?? session.EndTime } };
        // 共用原报工校验、请求去重、事务和工单进度计算。
        return await reports.SubmitAsync(input, userId);
    }

    public async Task<object> CurrentAsync(long userId, long orderId, long operationId)
    {
        var user = await db.Users.AsNoTracking().FirstAsync(x => x.Id == userId);
        var order = await db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == orderId && x.FactoryId == user.FactoryId)
            ?? throw ThrowHelper.BizUser("工单不存在或无权访问");
        var session = await db.WorkSessions.AsNoTracking().FirstOrDefaultAsync(x => x.FactoryId == user.FactoryId
            && x.UserId == userId && x.Status == 0 && x.WorkOrderId == orderId && x.OperationId == operationId);
        return new { session, priceType = await PriceTypeAsync(order, operationId, userId, DateTime.Now), serverTime = DateTime.Now };
    }

    public async Task<byte> PriceTypeAsync(ProdWorkOrder order, long operationId, long userId, DateTime at)
    {
        // 报工计价方式一律取本单任务锁价，不按当前价目表或人员猜。
        return (await new OperationPricingService(db).LockedAsync(order.Id, operationId, order.FactoryId, at)).PriceType;
    }

    public async Task<ResolvedWorkTime> ResolveAsync(ProdWorkOrder order, long operationId, long userId,
        long operatorId, WorkTimeInput input, DateTime now, bool allowPending = false)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId && x.FactoryId == order.FactoryId && x.Status == 1)
            ?? throw ThrowHelper.BizUser($"报工人员不存在、停用或不属于本厂：{userId}");
        if (!await ReportPermissionHelper.CanReportOperationAsync(db, userId, user.Role, operationId))
            throw ThrowHelper.BizUser($"无该工序报工权限：{user.Name}");
        var session = input.SessionId.HasValue
            ? await db.WorkSessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == input.SessionId.Value)
            : input.StartTime.HasValue || input.EndTime.HasValue ? null : await db.WorkSessions.AsNoTracking().FirstOrDefaultAsync(x => x.FactoryId == order.FactoryId && x.UserId == userId
                && x.WorkOrderId == order.Id && x.OperationId == operationId && x.Status == 0);
        if (session != null && (session.FactoryId != order.FactoryId || session.UserId != userId
            || session.WorkOrderId != order.Id || session.OperationId != operationId || session.ReportId != null
            || session.Status != (allowPending ? 2 : 0)))
            throw ThrowHelper.BizUser($"开工记录不匹配或已被报工：{user.Name}");
        if (input.SessionId.HasValue && session == null) throw ThrowHelper.BizUser("开工记录不存在");
        var op = await db.Users.AsNoTracking().FirstAsync(x => x.Id == operatorId && x.FactoryId == order.FactoryId);
        var hasTimes = input.StartTime.HasValue || input.EndTime.HasValue;
        if (hasTimes && (op.Role != 1 || !input.StartTime.HasValue || !input.EndTime.HasValue))
            throw ThrowHelper.BizUser("仅管理员可补填完整开始和结束时间");
        var start = hasTimes ? input.StartTime : session?.StartTime;
        var end = hasTimes ? input.EndTime : allowPending ? session?.EndTime : null;
        if (hasTimes) CalculateMinutes(start!.Value, end!.Value, now);
        var reportTime = end ?? now;
        var priceType = await PriceTypeAsync(order, operationId, userId, reportTime);
        if (priceType == 2 && session == null && !hasTimes)
            throw ThrowHelper.BizUser($"请先开工：{user.Name}");
        var minutes = hasTimes ? CalculateMinutes(start!.Value, end!.Value, now)
            : input.DurationMinutes ?? (session != null ? CalculateMinutes(session.StartTime, end ?? now, now) : 0);
        if (minutes < 0 || (priceType == 2 && minutes == 0)) throw ThrowHelper.BizUser($"计时报工时长必须大于0分钟：{user.Name}");
        if (minutes > 1440) throw ThrowHelper.BizUser("单次报工时长不能超过24小时");
        return new(session, minutes, reportTime, start, end, priceType);
    }

    public async Task CompleteAsync(ResolvedWorkTime time, ProdReport report, long operatorId)
    {
        if (time.Session != null)
        {
            var count = await db.WorkSessions.Where(x => x.Id == time.Session.Id && x.Status == time.Session.Status && x.ReportId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, (byte)1).SetProperty(x => x.ReportId, report.Id)
                    .SetProperty(x => x.EndTime, time.EndTime ?? report.ReportTime)
                    .SetProperty(x => x.EndedAt, DateTime.Now)
                    .SetProperty(x => x.OperatorUserId, time.Session.Status == 2 ? operatorId : time.Session.OperatorUserId)
                    .SetProperty(x => x.DurationMinutes, time.Minutes));
            if (count != 1) throw ThrowHelper.BizUser("该记录已被报工，请刷新后重试");
        }
        else if (time.StartTime.HasValue)
        {
            db.WorkSessions.Add(new ProdWorkSession { FactoryId = report.FactoryId, WorkOrderId = report.OrderId,
                OperationId = report.OperationId, UserId = report.UserId, StartTime = time.StartTime.Value,
                EndTime = time.EndTime, DurationMinutes = time.Minutes, Status = 1, Source = 1,
                OperatorUserId = operatorId, ReportId = report.Id, CreatedAt = DateTime.Now, EndedAt = DateTime.Now });
        }
    }
}
