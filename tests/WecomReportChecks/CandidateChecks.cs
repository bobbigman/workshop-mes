using System.Reflection;
using System.Text.Json;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Mcp;
using ahu.MicrosoftMes.Models;
using ahu.MicrosoftMes.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

public static class CandidateChecks
{
    public static async Task Run(Action<bool, string> check, Func<Func<Task>, string, Task> reject,
        Func<Func<Task<string>>, string, Task> rejectResult)
    {
        var assignedRows = new List<AssignTaskDto>();
        var assignFail = false;
        var assigns = DispatchProxy.Create<IAssignService, Stub>();
        ((Stub)(object)assigns).Call = (_, _) => Task.FromResult(assignFail
            ? ApiResult<List<AssignTaskDto>>.Fail("派工查询失败")
            : ApiResult<List<AssignTaskDto>>.Ok(assignedRows));
        var blocked = new HashSet<long>();
        var technicalFailure = false;
        var reports = DispatchProxy.Create<IReportService, Stub>();
        ((Stub)(object)reports).Call = (method, args) =>
        {
            if (method.Name != "ValidateSubmitAsync") throw new InvalidOperationException("候选查询不应写入");
            if (technicalFailure) throw new InvalidOperationException("模拟SQL失败");
            if (blocked.Contains(((ReportDto)args![0]!).OperationId)) throw ThrowHelper.BizUser("前道不足或无报工权限");
            return Task.CompletedTask;
        };
        var source = new TaskSource();
        for (var i = 1; i <= 8; i++) source.Rows.Add(new ReportTaskCandidate
        {
            TaskId = i, OrderId = 100 + i, OperationId = 200 + i, OrderNo = $"GD{i}",
            ProductName = "雨伞" + i, OperationName = "包装", PlanQty = 100, DoneQty = 10
        });
        assignedRows.Add(new AssignTaskDto { TaskId = 2, PlanQty = 100, OrderStatus = 1, AssignSource = "team" });
        var service = new WecomReportTaskService(assigns, source, reports);
        var preferred = await service.QueryAsync(1, 7, 2, null, 1);
        check(preferred.Source == "assigned" && preferred.Items.Single().TaskId == 2 && preferred.Items[0].AssignSource == "team",
            "班组派工优先，未派工候选不混入");
        var end = await service.QueryAsync(1, 7, 2, null, 2);
        check(end.Source == "assigned" && end.Items.Count == 0 && !end.HasMore, "派工末页不回退为全厂任务");
        blocked.Add(202);
        var fallback = await service.QueryAsync(1, 7, 2, null, 1);
        check(fallback.Source == "available" && fallback.Items.Count == 5 && fallback.Items.All(x => x.OperationId != 202)
              && fallback.ExcludedReasons.Count > 0, "派工不可预览时回退可用工序并记录排除原因");
        blocked.Clear(); assignedRows.Clear();
        var first = await service.QueryAsync(1, 7, 2, null, 1);
        var next = await service.QueryAsync(1, 7, 2, null, 2);
        check(first.Items.Count == 5 && first.HasMore && next.Items.Count == 3 && !next.HasMore &&
              !first.Items.Select(x => x.OrderId).Intersect(next.Items.Select(x => x.OrderId)).Any(), "每页5项，换批不重复，hasMore准确");
        var keyword = await service.QueryAsync(1, 7, 2, "雨伞8", 1);
        check(keyword.Items.Single().OrderNo == "GD8", "按产品缩小候选");
        var absent = await service.QueryAsync(1, 7, 2, "不存在", 1);
        check(absent.Items.Count == 0 && absent.Hint.Contains("暂无"), "无候选有明确提示");
        await reject(async () => await service.QueryAsync(1, 7, 2, null, 0), "非法页码拒绝");
        assignFail = true;
        await reject(async () => await service.QueryAsync(1, 7, 2, null, 1), "派工错误不降级为空列表");
        assignFail = false; technicalFailure = true;
        var bubbled = false;
        try { await service.QueryAsync(1, 7, 2, null, 1); }
        catch (InvalidOperationException ex) when (ex.Message == "模拟SQL失败") { bubbled = true; }
        check(bubbled, "技术异常向上抛，不当业务过滤吞掉");
        technicalFailure = false;

        // SQL Server 提供程序仅生成 SQL，不打开连接，不接触真实库。
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=127.0.0.1;Database=NotUsed;Integrated Security=true;TrustServerCertificate=true").Options);
        var sqlSource = new ReportTaskCandidateSource(db);
        foreach (var ids in new long[]?[] { null, new long[] { 2 } })
        {
            var sql = sqlSource.BuildQuery(1, new long[] { 201, 202 }, "雨伞", ids)
                .OrderBy(x => x.DueDate == null).ThenBy(x => x.DueDate).ThenBy(x => x.OrderId)
                .ThenBy(x => x.Seq).ThenBy(x => x.OperationId).ThenBy(x => x.TaskId).Skip(0).Take(50).ToQueryString();
            check(sql.Contains("UNION ALL") && sql.Contains("review_status") && sql.Contains("factory_id") && sql.Contains("OFFSET"),
                ids == null ? "未派工/路线回退查询能翻译为SQL Server分页SQL" : "派工优先查询能翻译为SQL Server分页SQL");
        }

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mcp:WecomReport:Enabled"] = "true",
            [$"Mcp:WecomReport:VerifiedUsers:1:{McpWxBindService.HashSession("worker-a")}"] = "7",
            [$"Mcp:WecomReport:VerifiedUsers:1:{McpWxBindService.HashSession("worker-b")}"] = "8"
        }).Build();
        var ctx = new McpRequestContext { CurrentFactoryId = 1 };
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        http.HttpContext.Request.Headers["X-Mcp-Session-Id"] = "worker-a";
        var actor = new SysUser { Id = 7, FactoryId = 1, Status = 1, Role = 2 };
        var bind = DispatchProxy.Create<IMcpWxBindService, Stub>();
        ((Stub)(object)bind).Call = (_, _) => Task.FromResult<SysUser?>(actor);
        var fake = new FakeWecom();
        var tools = new WecomReportMcpTools(fake, ctx, bind, http, config, cache, service);
        string ListToken(string json) => JsonDocument.Parse(json).RootElement.GetProperty("data").GetProperty("candidateListToken").GetString()!;
        var token = ListToken(await tools.QueryTasks());
        await tools.Preview(goodQty: 50, candidateListToken: token, candidateNumber: 1);
        check(fake.LastPreview?.OrderNo == "GD1" && fake.LastPreview.OperationName == "包装" && fake.LastPreview.GoodQty == 50 &&
              fake.LastPreview.ExpectedOrderId == 101 && fake.LastPreview.ExpectedOperationId == 201 && fake.Submitted == 0,
              "第一项50件后端准确映射，携带对象ID，不入库");
        await rejectResult(() => tools.Preview(goodQty: 50, candidateListToken: token, candidateNumber: 6), "越界序号拒绝");
        await rejectResult(() => tools.Preview(goodQty: 50, candidateListToken: token, candidateNumber: 1, orderNo: "GD8"), "序号与手填工单冲突拒绝");
        await rejectResult(() => tools.Preview(candidateListToken: token, candidateNumber: 1), "不将剩余数自动当完成数");
        var secondToken = ListToken(await tools.QueryTasks(page: 2));
        await rejectResult(() => tools.Preview(goodQty: 50, candidateListToken: token, candidateNumber: 1), "换批后旧列表失效");
        await tools.Preview(goodQty: 20, candidateListToken: secondToken, candidateNumber: 1);
        check(fake.LastPreview?.OrderNo == "GD6", "第二页第一项正确映射，不套用上一页");
        var confirm = JsonDocument.Parse(await tools.Preview(goodQty: 20, candidateListToken: secondToken, candidateNumber: 1))
            .RootElement.GetProperty("data").GetProperty("confirmToken").GetString();
        await tools.QueryTasks();
        await rejectResult(() => tools.Confirm(confirm), "重查候选同时作废旧待确认预览");
        actor.Id = 8; http.HttpContext.Request.Headers["X-Mcp-Session-Id"] = "worker-b";
        await rejectResult(() => tools.Preview(goodQty: 50, candidateListToken: secondToken, candidateNumber: 1), "其他员工不能使用本人候选");
        actor.Id = 7; http.HttpContext.Request.Headers["X-Mcp-Session-Id"] = "worker-a";
        var expireToken = ListToken(await tools.QueryTasks());
        cache.Remove("mcp-report-list:" + expireToken);
        await rejectResult(() => tools.Preview(goodQty: 50, candidateListToken: expireToken, candidateNumber: 1), "列表缓存丢失或过期拒绝");
        await tools.Preview("GD8", "包装", 12);
        check(fake.LastPreview?.OrderNo == "GD8" && fake.LastPreview.ExpectedOrderId == null, "原单号工序预览调用兼容");
    }

    private sealed class TaskSource : IReportTaskCandidateSource
    {
        public List<ReportTaskCandidate> Rows { get; } = new();
        public Task<List<ReportTaskCandidate>> ReadAsync(long factoryId, long userId, byte role,
            string keyword, long[]? assignedTaskIds, int skip, int take)
            => Task.FromResult(Rows.Where(x => assignedTaskIds == null || assignedTaskIds.Contains(x.TaskId))
                .Where(x => keyword == "" || x.ProductName.Contains(keyword) || x.OrderNo.Contains(keyword))
                .OrderBy(x => x.OrderId).Skip(skip).Take(take).ToList());
    }
}
