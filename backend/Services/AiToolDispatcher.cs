using System.Text.Json;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ahu.MicrosoftMes.Services;

/// <summary>白名单工具调度。模型不能拓展工具或传入 factoryId/路径。</summary>
public class AiToolDispatcher
{
    private readonly AppDbContext _db;
    private readonly IWorkOrderService _workOrders;
    private readonly KnowledgeFileParser _parser;
    private readonly SupportDocumentService _supportDocs;
    private readonly IAiReportEvidenceService _reportEvidence;
    private readonly AiOptions _opt;
    private readonly ILogger<AiToolDispatcher> _logger;

    public AiToolDispatcher(
        AppDbContext db,
        IWorkOrderService workOrders,
        KnowledgeFileParser parser,
        SupportDocumentService supportDocs,
        IAiReportEvidenceService reportEvidence,
        IOptions<AiOptions> opt,
        ILogger<AiToolDispatcher> logger)
    {
        _db = db;
        _workOrders = workOrders;
        _parser = parser;
        _supportDocs = supportDocs;
        _reportEvidence = reportEvidence;
        _opt = opt.Value;
        _logger = logger;
    }

    public IReadOnlyList<DeepSeekToolDef> ToolsForMode(string mode)
    {
        if (mode == "progress")
        {
            return new[]
            {
                SearchWorkOrdersTool(),
                ProgressTool()
            };
        }

        if (mode == "support")
        {
            return new[]
            {
                SearchSupportDocsTool(),
                SearchWorkOrdersTool(),
                ProgressTool(),
                ReportEvidenceTool()
            };
        }

        return new[]
        {
            Tool("resolve_product", "按产品编号精确或名称模糊定位产品；多个同名返回候选，不自动取第一条", new
            {
                type = "object",
                properties = new
                {
                    productCode = new { type = "string" },
                    productName = new { type = "string" }
                }
            }),
            Tool("get_production_guide", "在已明确产品编号后读取工艺资料；可选工序须属于该产品路线", new
            {
                type = "object",
                properties = new
                {
                    productCode = new { type = "string", description = "必填，精确产品编号" },
                    operationId = new { type = "integer", description = "工序id，同名歧义时用" },
                    operationName = new { type = "string", description = "工序名称，可能歧义" }
                },
                required = new[] { "productCode" }
            })
        };
    }

    private static DeepSeekToolDef SearchWorkOrdersTool() =>
        Tool("search_work_orders", "按关键词/产品编号/状态/交期查询当前工厂工单，返回分页与总数。查「未完成」时只传 statuses=[0,1]，不要传交期。只有用户明确说今天/明天到期或逾期时才传 dueRelative。", new
        {
            type = "object",
            properties = new
            {
                keyword = new { type = "string", description = "工单号/产品名/产品编号关键词" },
                productCode = new { type = "string", description = "产品编号精确" },
                statuses = new { type = "array", items = new { type = "integer" }, description = "状态列表：0未开始1执行中2已结束3已取消；未完成用[0,1]" },
                dueFrom = new { type = "string", description = "交期起 yyyy-MM-dd 或 今天/明天" },
                dueToExclusive = new { type = "string", description = "交期止（不含）yyyy-MM-dd；与 dueFrom 组成半开区间" },
                dueRelative = new { type = "string", description = "相对交期：今天/明天/逾期；优先于 dueFrom/dueToExclusive" },
                page = new { type = "integer", description = "页码从1开始" },
                pageSize = new { type = "integer", description = "每页条数，默认20最大50" }
            }
        });

    private static DeepSeekToolDef ProgressTool() =>
        Tool("get_work_order_progress", "按工单ID或精确单号查进度", new
        {
            type = "object",
            properties = new
            {
                workOrderId = new { type = "integer" },
                orderNo = new { type = "string" }
            }
        });

    private static DeepSeekToolDef SearchSupportDocsTool() =>
        Tool("search_support_docs", "检索服务器上的操作说明/常见问题/数据口径文档；返回来源、章节与摘录。只传 query，不传任何路径。", new
        {
            type = "object",
            properties = new
            {
                query = new { type = "string", description = "用户问题，必填，最多 500 字" }
            },
            required = new[] { "query" }
        });

    private static DeepSeekToolDef ReportEvidenceTool() =>
        Tool("get_work_order_report_evidence", "查询某工单的报工证据：整单完成数、按工序的待复核/已通过/已退回良品不良品汇总、报工明细与分页。workOrderId 或精确 orderNo 至少一个；时间 reportFrom/reportToExclusive 为半开区间。", new
        {
            type = "object",
            properties = new
            {
                workOrderId = new { type = "integer", description = "工单ID" },
                orderNo = new { type = "string", description = "精确工单号" },
                operationId = new { type = "integer", description = "可选：仅看某工序" },
                reportFrom = new { type = "string", description = "报工时间起（含），yyyy-MM-dd 或 yyyy-MM-dd HH:mm" },
                reportToExclusive = new { type = "string", description = "报工时间止（不含），同上格式" },
                page = new { type = "integer", description = "页码从1开始，默认1" },
                pageSize = new { type = "integer", description = "每页条数，默认20最大50" }
            }
        });

    public async Task<string> DispatchAsync(
        string toolName,
        string argumentsJson,
        AiSessionState session,
        CancellationToken ct)
    {
        RejectForbiddenArgs(argumentsJson);
        EnsureToolAllowed(session.Mode, toolName);
        _logger.LogInformation("AI工具 {Tool} factory={FactoryId} user={UserId} argsLen={Len}",
            toolName, session.FactoryId, session.UserId, argumentsJson?.Length ?? 0);

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
        var root = doc.RootElement;

        return toolName switch
        {
            "search_work_orders" => await SearchWorkOrdersAsync(root, session, ct),
            "get_work_order_progress" => await GetProgressAsync(root, session, ct),
            "search_support_docs" => await SearchSupportDocsAsync(root, session, ct),
            "get_work_order_report_evidence" => await GetReportEvidenceAsync(root, session, ct),
            "resolve_product" => await ResolveProductAsync(root, session, ct),
            "get_production_guide" => await GetGuideAsync(root, session, ct),
            _ => throw ThrowHelper.BizUser("不支持的操作，请换个问法")
        };
    }

    private static readonly Dictionary<string, HashSet<string>> ModeTools = new(StringComparer.OrdinalIgnoreCase)
    {
        ["progress"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "search_work_orders", "get_work_order_progress" },
        ["guide"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "resolve_product", "get_production_guide" },
        ["support"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "search_support_docs", "search_work_orders", "get_work_order_progress", "get_work_order_report_evidence"
        }
    };

    private static void EnsureToolAllowed(string mode, string toolName)
    {
        if (!ModeTools.TryGetValue(mode, out var allowed) || !allowed.Contains(toolName))
            throw ThrowHelper.BizUser("该功能在当前模式下不可用");
    }

    private static void RejectForbiddenArgs(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        using var doc = JsonDocument.Parse(json);
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            var n = p.Name;
            if (n.Equals("factoryId", StringComparison.OrdinalIgnoreCase)
                || n.Equals("userId", StringComparison.OrdinalIgnoreCase)
                || n.Equals("role", StringComparison.OrdinalIgnoreCase)
                || n.Equals("sql", StringComparison.OrdinalIgnoreCase)
                || n.Contains("path", StringComparison.OrdinalIgnoreCase)
                || n.Contains("password", StringComparison.OrdinalIgnoreCase))
            {
                throw ThrowHelper.BizUser("包含不支持的内容，请调整后重试");
            }
        }
    }

    private async Task<string> SearchWorkOrdersAsync(JsonElement root, AiSessionState session, CancellationToken ct)
    {
        var filters = new AiWorkOrderFiltersDto
        {
            Keyword = GetString(root, "keyword"),
            ProductCode = GetString(root, "productCode")
        };

        if (root.TryGetProperty("statuses", out var st) && st.ValueKind == JsonValueKind.Array)
        {
            filters.Statuses = st.EnumerateArray().Select(x => (byte)x.GetInt32()).Distinct().ToList();
        }

        var tz = AiBusinessTime.Resolve(_opt.BusinessTimeZone);
        var dueRelative = GetString(root, "dueRelative");
        if (!string.IsNullOrWhiteSpace(dueRelative))
        {
            var (from, toEx, label) = AiBusinessTime.ResolveRelativeDueRange(dueRelative, tz);
            filters.DueFrom = from == DateTime.MinValue.AddDays(1) ? null : from.ToString("yyyy-MM-dd");
            filters.DueToExclusive = toEx.ToString("yyyy-MM-dd");
            if (dueRelative is "逾期" or "已逾期" or "overdue")
                filters.DueFrom = null;
            _ = label;
        }
        else
        {
            filters.DueFrom = NormalizeDate(GetString(root, "dueFrom"), tz);
            filters.DueToExclusive = NormalizeDate(GetString(root, "dueToExclusive"), tz);
        }

        var page = GetInt(root, "page", 1);
        var pageSize = GetInt(root, "pageSize", 20);
        var result = await QueryWorkOrdersForAiAsync(filters, page, pageSize, session.FactoryId, ct);
        session.LastFilters = filters;
        session.LastWorkOrders = result;
        return JsonSerializer.Serialize(result);
    }

    public async Task<AiWorkOrderPageDto> QueryWorkOrdersForAiAsync(
        AiWorkOrderFiltersDto filters,
        int page,
        int pageSize,
        long factoryId,
        CancellationToken ct)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 50) pageSize = 50;

        var q = from o in _db.WorkOrders.AsNoTracking()
                where o.FactoryId == factoryId
                join p in _db.Products.AsNoTracking() on o.ProductId equals p.Id
                select new { o, p.Code, p.Name };

        if (!string.IsNullOrWhiteSpace(filters.Keyword))
        {
            var kw = filters.Keyword.Trim();
            q = q.Where(x => x.o.OrderNo.Contains(kw) || x.Name.Contains(kw) || x.Code.Contains(kw));
        }
        if (!string.IsNullOrWhiteSpace(filters.ProductCode))
        {
            var code = filters.ProductCode.Trim();
            q = q.Where(x => x.Code == code);
        }
        if (filters.Statuses is { Count: > 0 })
            q = q.Where(x => filters.Statuses.Contains(x.o.Status));

        // 交期半开区间；空交期不参与有交期条件的结果
        DateTime? dueFrom = ParseDate(filters.DueFrom);
        DateTime? dueToEx = ParseDate(filters.DueToExclusive);
        if (dueFrom != null || dueToEx != null)
        {
            q = q.Where(x => x.o.DueDate != null);
            if (dueFrom != null)
                q = q.Where(x => x.o.DueDate >= dueFrom.Value);
            if (dueToEx != null)
                q = q.Where(x => x.o.DueDate < dueToEx.Value);
        }

        var total = await q.CountAsync(ct);
        // AI 工具内排序：交期升序、无交期置后、ID 稳定。不改 WorkOrderService 默认排序。
        var list = await q
            .OrderBy(x => x.o.DueDate == null)
            .ThenBy(x => x.o.DueDate)
            .ThenBy(x => x.o.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AiWorkOrderCardDto
            {
                Id = x.o.Id,
                OrderNo = x.o.OrderNo,
                ProductCode = x.Code,
                ProductName = x.Name,
                Qty = x.o.Qty,
                Status = x.o.Status,
                DueDate = x.o.DueDate
            })
            .ToListAsync(ct);

        // 复用现有进度口径
        if (list.Count > 0)
        {
            var ids = list.Select(x => x.Id).ToList();
            var detailTasks = ids.Select(id => _workOrders.GetAsync(id, factoryId));
            var details = await Task.WhenAll(detailTasks);
            var map = details.Where(d => d.Code == 0 && d.Data != null)
                .ToDictionary(d => d.Data!.Id, d => d.Data!);
            foreach (var item in list)
            {
                if (!map.TryGetValue(item.Id, out var d)) continue;
                item.DoneQty = d.Tasks.Count > 0 ? d.Tasks.Min(t => t.DoneQty) : 0;
                item.RemainQty = Math.Max(0, item.Qty - item.DoneQty);
            }
        }

        return new AiWorkOrderPageDto
        {
            List = list,
            Total = total,
            Page = page,
            PageSize = pageSize,
            Filters = filters
        };
    }

    private async Task<string> GetProgressAsync(JsonElement root, AiSessionState session, CancellationToken ct)
    {
        long? id = root.TryGetProperty("workOrderId", out var wid) && wid.ValueKind == JsonValueKind.Number
            ? wid.GetInt64() : null;
        var orderNo = GetString(root, "orderNo");

        ApiResult<WorkOrderDetailDto> res;
        if (id != null)
            res = await _workOrders.GetAsync(id.Value, session.FactoryId);
        else if (!string.IsNullOrWhiteSpace(orderNo))
            res = await _workOrders.GetByOrderNoAsync(orderNo.Trim(), session.FactoryId);
        else
            throw ThrowHelper.BizUser("请提供工单号");

        if (res.Code != 0 || res.Data == null)
            return JsonSerializer.Serialize(new { found = false, msg = res.Msg });

        var d = res.Data;
        var done = d.Tasks.Count > 0 ? d.Tasks.Min(t => t.DoneQty) : 0;
        return JsonSerializer.Serialize(new
        {
            found = true,
            id = d.Id,
            orderNo = d.OrderNo,
            productCode = d.ProductCode,
            productName = d.ProductName,
            qty = d.Qty,
            status = d.Status,
            dueDate = AiBusinessTime.FormatDate(d.DueDate),
            doneQty = done,
            remainQty = Math.Max(0, d.Qty - done),
            tasks = d.Tasks.Select(t => new
            {
                t.OperationId,
                t.OperationName,
                t.Seq,
                t.PlanQty,
                t.DoneQty,
                t.DefectQty
            })
        });
    }

    private async Task<string> SearchSupportDocsAsync(JsonElement root, AiSessionState session, CancellationToken ct)
    {
        var query = GetString(root, "query");
        if (string.IsNullOrWhiteSpace(query))
            throw ThrowHelper.BizUser("请输入要搜索的内容");
        if (query!.Trim().Length > 500)
            throw ThrowHelper.BizUser("搜索内容最多 500 字");

        var result = _supportDocs.Search(query.Trim());
        var sources = result.Sources.Select(s => new AiSourceDto
        {
            SourceId = s.SourceId,
            Type = "support_doc",
            Title = s.Title,
            Version = s.Version,
            Chapter = s.Chapter,
            UpdatedAt = s.UpdatedAt,
            Excerpt = s.Excerpt
        }).ToList();

        // 每轮重新取证：仅保留本轮检索到的文档来源（替换而非累加，避免旧轮来源冒充新版本）
        session.Sources = sources;

        return JsonSerializer.Serialize(new
        {
            state = result.State,
            message = result.Message,
            truncated = result.Truncated,
            totalMatched = result.TotalMatched,
            warnings = result.Warnings,
            sources
        });
    }

    private async Task<string> GetReportEvidenceAsync(JsonElement root, AiSessionState session, CancellationToken ct)
    {
        long? id = root.TryGetProperty("workOrderId", out var wid) && wid.ValueKind == JsonValueKind.Number
            ? wid.GetInt64() : null;
        var orderNo = GetString(root, "orderNo");
        long? opId = root.TryGetProperty("operationId", out var oid) && oid.ValueKind == JsonValueKind.Number
            ? oid.GetInt64() : null;
        var from = ParseReportDate(GetString(root, "reportFrom"));
        var to = ParseReportDate(GetString(root, "reportToExclusive"));
        var page = GetInt(root, "page", 1);
        var pageSize = GetInt(root, "pageSize", 20);

        var evidence = await _reportEvidence.GetEvidenceAsync(
            id, orderNo, opId, from, to, page, pageSize, session.FactoryId, ct);

        session.LastReportEvidence = evidence;
        foreach (var s in evidence.Sources)
            if (session.Sources.All(x => x.SourceId != s.SourceId))
                session.Sources.Add(s);

        return JsonSerializer.Serialize(evidence);
    }

    private static DateTime? ParseReportDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        if (DateTime.TryParse(t, out var dt))
            return dt;
        throw ThrowHelper.BizUser("报工时间格式不正确，请重试");
    }

    private async Task<string> ResolveProductAsync(JsonElement root, AiSessionState session, CancellationToken ct)
    {
        var code = GetString(root, "productCode");
        var name = GetString(root, "productName");
        if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(name))
            throw ThrowHelper.BizUser("请提供产品编号或产品名称");

        if (!string.IsNullOrWhiteSpace(code))
        {
            var one = await _db.Products.AsNoTracking()
                .FirstOrDefaultAsync(p => p.FactoryId == session.FactoryId && p.Code == code.Trim(), ct);
            if (one == null)
                return JsonSerializer.Serialize(new { found = false, candidates = Array.Empty<object>() });
            var c = new AiCandidateDto
            {
                Type = "product",
                ProductCode = one.Code,
                ProductName = one.Name
            };
            session.Candidates = new List<AiCandidateDto> { c };
            return JsonSerializer.Serialize(new { found = true, product = c });
        }

        var list = await _db.Products.AsNoTracking()
            .Where(p => p.FactoryId == session.FactoryId && p.Name.Contains(name!))
            .OrderBy(p => p.Code)
            .Take(21)
            .Select(p => new AiCandidateDto { Type = "product", ProductCode = p.Code, ProductName = p.Name })
            .ToListAsync(ct);

        var hasMore = list.Count > 20;
        if (hasMore) list = list.Take(20).ToList();
        session.Candidates = list;

        if (list.Count == 0)
            return JsonSerializer.Serialize(new { found = false, candidates = list, hasMore });
        if (list.Count == 1)
            return JsonSerializer.Serialize(new { found = true, product = list[0], needsClarification = false });

        return JsonSerializer.Serialize(new
        {
            found = true,
            needsClarification = true,
            message = "存在多个同名产品，请让用户按编号选择",
            candidates = list,
            hasMore
        });
    }

    private async Task<string> GetGuideAsync(JsonElement root, AiSessionState session, CancellationToken ct)
    {
        var productCode = GetString(root, "productCode");
        if (string.IsNullOrWhiteSpace(productCode))
            throw ThrowHelper.BizUser("请提供产品编号");

        var product = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.FactoryId == session.FactoryId && p.Code == productCode.Trim(), ct);
        if (product == null)
            return JsonSerializer.Serialize(new { found = false, msg = "未找到产品" });

        long? operationId = root.TryGetProperty("operationId", out var oid) && oid.ValueKind == JsonValueKind.Number
            ? oid.GetInt64() : null;
        var operationName = GetString(root, "operationName");

        var routeOpIds = new List<long>();
        var opNameById = new Dictionary<long, string>();
        var opCodeById = new Dictionary<long, string>();
        if (product.RoutingId != null)
        {
            var steps = await _db.RoutingSteps.AsNoTracking()
                .Where(s => s.RoutingId == product.RoutingId)
                .OrderBy(s => s.Seq)
                .ToListAsync(ct);
            var ops = await _db.Operations.AsNoTracking()
                .Where(o => steps.Select(s => s.OperationId).Contains(o.Id))
                .ToListAsync(ct);
            foreach (var s in steps)
            {
                var op = ops.FirstOrDefault(x => x.Id == s.OperationId);
                if (op == null) continue;
                routeOpIds.Add(op.Id);
                opNameById[op.Id] = op.Name;
                opCodeById[op.Id] = op.Code;
            }
        }

        if (operationId != null)
        {
            if (!routeOpIds.Contains(operationId.Value))
                return JsonSerializer.Serialize(new { found = false, msg = "指定工序不属于该产品工艺路线" });
        }
        else if (!string.IsNullOrWhiteSpace(operationName))
        {
            var matched = routeOpIds
                .Where(id => opNameById.GetValueOrDefault(id, "").Contains(operationName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matched.Count == 0)
                return JsonSerializer.Serialize(new { found = false, msg = $"产品路线中未找到工序：{operationName}" });
            if (matched.Count > 1)
            {
                var cands = matched.Select(id => new AiCandidateDto
                {
                    Type = "operation",
                    ProductCode = product.Code,
                    ProductName = product.Name,
                    OperationId = id,
                    OperationCode = opCodeById.GetValueOrDefault(id),
                    OperationName = opNameById.GetValueOrDefault(id)
                }).ToList();
                session.Candidates = cands;
                return JsonSerializer.Serialize(new
                {
                    needsClarification = true,
                    message = "路线内存在同名工序，请让用户按工序选择",
                    candidates = cands
                });
            }
            operationId = matched[0];
        }

        var files = new List<Models.BaseKnowledgeFile>();
        files.AddRange(await _db.KnowledgeFiles.AsNoTracking()
            .Where(f => f.FactoryId == session.FactoryId && f.RefType == "product" && f.RefId == product.Id)
            .ToListAsync(ct));

        if (operationId != null)
        {
            files.AddRange(await _db.KnowledgeFiles.AsNoTracking()
                .Where(f => f.FactoryId == session.FactoryId && f.RefType == "operation" && f.RefId == operationId)
                .ToListAsync(ct));
        }
        else if (routeOpIds.Count > 0)
        {
            files.AddRange(await _db.KnowledgeFiles.AsNoTracking()
                .Where(f => f.FactoryId == session.FactoryId && f.RefType == "operation" && routeOpIds.Contains(f.RefId))
                .ToListAsync(ct));
        }

        if (files.Count == 0)
            return JsonSerializer.Serialize(new { found = false, msg = "未找到对应工艺资料", productCode = product.Code });

        var sources = new List<AiSourceDto>();
        var contents = new List<object>();
        var warnings = new List<string>();
        var budget = _opt.MaxKnowledgeChars;
        var used = 0;
        var anyFail = false;

        foreach (var f in files)
        {
            try
            {
                EnsureSafeRelativePath(f.RelativePath);
                var text = _parser.Extract(f.RelativePath, f.FileType);
                if (used + text.Length > budget)
                {
                    warnings.Add($"资料正文超出限额（{budget}字），请缩小工序或问题；未截断冒充完整答案");
                    anyFail = true;
                    break;
                }
                used += text.Length;
                var sourceId = $"src_{sources.Count + 1}";
                var excerpt = text.Length <= 200 ? text : text[..200] + "...";
                var src = new AiSourceDto
                {
                    SourceId = sourceId,
                    Type = f.RefType,
                    Title = f.FileName,
                    Version = string.IsNullOrWhiteSpace(f.Version) ? null : f.Version,
                    Excerpt = excerpt,
                    ProductCode = product.Code,
                    OperationName = f.RefType == "operation" ? opNameById.GetValueOrDefault(f.RefId) : null
                };
                sources.Add(src);
                contents.Add(new
                {
                    sourceId,
                    fileName = f.FileName,
                    version = src.Version ?? "未标注版本",
                    content = text
                });
            }
            catch (Exception ex)
            {
                anyFail = true;
                warnings.Add($"文件「{f.FileName}」读取失败：{ex.Message}");
                _logger.LogWarning(ex, "AI工艺资料解析失败 file={File}", f.FileName);
            }
        }

        session.Sources = sources;

        if (anyFail && contents.Count == 0)
        {
            return JsonSerializer.Serialize(new
            {
                found = false,
                incomplete = true,
                msg = "资料读取失败，不能给出工艺结论",
                warnings
            });
        }

        if (anyFail)
        {
            return JsonSerializer.Serialize(new
            {
                found = true,
                incomplete = true,
                msg = "部分资料读取失败或超限，不能当作完整工艺结论",
                warnings,
                sources,
                files = contents
            });
        }

        return JsonSerializer.Serialize(new
        {
            found = true,
            incomplete = false,
            productCode = product.Code,
            productName = product.Name,
            sources,
            files = contents
        });
    }

    private static void EnsureSafeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw ThrowHelper.BizUser("资料路径无效");
        if (Path.IsPathRooted(relativePath))
            throw ThrowHelper.BizUser("资料路径无效");
        var norm = relativePath.Replace('\\', '/');
        if (norm.Split('/').Any(p => p == ".."))
            throw ThrowHelper.BizUser("资料路径无效");
    }

    private static DeepSeekToolDef Tool(string name, string desc, object parameters) => new()
    {
        Type = "function",
        Function = new DeepSeekFunctionDef
        {
            Name = name,
            Description = desc,
            Parameters = parameters
        }
    };

    private static string? GetString(JsonElement root, string name)
        => root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static int GetInt(JsonElement root, string name, int fallback)
        => root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number
            ? el.GetInt32()
            : fallback;

    private static string? NormalizeDate(string? input, TimeZoneInfo tz)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var s = input.Trim();
        if (s is "今天" or "today" or "明天" or "tomorrow" or "逾期" or "overdue")
        {
            var (from, toEx, _) = AiBusinessTime.ResolveRelativeDueRange(s, tz);
            // 单端字段场景：今天/明天返回起点；逾期不适用单字段
            if (s is "逾期" or "overdue") return null;
            return from.ToString("yyyy-MM-dd");
        }
        if (DateTime.TryParse(s, out var dt))
            return dt.Date.ToString("yyyy-MM-dd");
        throw ThrowHelper.BizUser("日期格式不正确，请重试");
    }

    private static DateTime? ParseDate(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : DateTime.Parse(s).Date;
}
