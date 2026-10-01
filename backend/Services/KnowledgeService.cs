using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public interface IKnowledgeService
{
    /// <summary>按产品（顺工艺路线）捞知识库文件并翻译成文字返回。只读。</summary>
    Task<KnowledgeGuideResult> QueryProductionGuideAsync(
        string? productCode, string? productName, string? operationName, long factoryId);
}

public class KnowledgeGuideResult
{
    public ProductBrief Product { get; set; } = new();
    public List<string> Routing { get; set; } = new();
    public List<KnowledgeFileContent> Files { get; set; } = new();
}

public class ProductBrief
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>单个文件的解析结果：只含文字内容，绝不含磁盘路径（docs/48 §6.2）。</summary>
public class KnowledgeFileContent
{
    public string FileName { get; set; } = "";
    public string? Version { get; set; }
    public string? RefName { get; set; }   // 挂产品级为空；挂工序为工序名
    public string Content { get; set; } = "";
}

public class KnowledgeService : IKnowledgeService
{
    private readonly AppDbContext _db;
    private readonly KnowledgeFileParser _parser;

    public KnowledgeService(AppDbContext db, KnowledgeFileParser parser)
    {
        _db = db;
        _parser = parser;
    }

    public async Task<KnowledgeGuideResult> QueryProductionGuideAsync(
        string? productCode, string? productName, string? operationName, long factoryId)
    {
        productCode = productCode?.Trim();
        productName = productName?.Trim();
        operationName = operationName?.Trim();

        if (string.IsNullOrWhiteSpace(productCode) && string.IsNullOrWhiteSpace(productName))
            throw ThrowHelper.Biz(nameof(QueryProductionGuideAsync), "请提供产品编号或产品名称");

        // 定位产品：编号精确、名称模糊
        BaseProduct? product;
        if (!string.IsNullOrWhiteSpace(productCode))
            product = await _db.Products.AsNoTracking()
                .FirstOrDefaultAsync(p => p.FactoryId == factoryId && p.Code == productCode);
        else
            product = await _db.Products.AsNoTracking()
                .FirstOrDefaultAsync(p => p.FactoryId == factoryId && p.Name.Contains(productName!));

        if (product == null)
            throw ThrowHelper.Biz(nameof(QueryProductionGuideAsync), $"未找到产品（编号/名称：{productCode ?? productName}）");

        // 顺工艺路线：routing_id -> base_routing_step -> base_operation
        var routingNames = new List<string>();
        var operationIds = new List<long>();
        var opNameById = new Dictionary<long, string>();
        if (product.RoutingId != null)
        {
            var steps = await _db.RoutingSteps.AsNoTracking()
                .Where(s => s.RoutingId == product.RoutingId)
                .OrderBy(s => s.Seq).ToListAsync();
            if (steps.Count > 0)
            {
                var ops = await _db.Operations.AsNoTracking()
                    .Where(o => steps.Select(s => s.OperationId).Contains(o.Id))
                    .ToDictionaryAsync(o => o.Id, o => o.Name);
                foreach (var s in steps)
                {
                    var name = ops.GetValueOrDefault(s.OperationId, $"工序{s.OperationId}");
                    routingNames.Add(name);
                    operationIds.Add(s.OperationId);
                    opNameById[s.OperationId] = name;
                }
            }
        }

        // 指定工序过滤（可选）
        long? filterOperationId = null;
        if (!string.IsNullOrWhiteSpace(operationName))
        {
            var op = await _db.Operations.AsNoTracking()
                .FirstOrDefaultAsync(o => o.FactoryId == factoryId && o.Name.Contains(operationName));
            if (op == null)
                throw ThrowHelper.Biz(nameof(QueryProductionGuideAsync), $"未找到工序：{operationName}");
            filterOperationId = op.Id;
            opNameById[op.Id] = op.Name;
        }

        // 捞文件：产品级 + 工序级（一个文件挂多处 = 多行记录，天然会全捞到）
        var files = new List<BaseKnowledgeFile>();
        files.AddRange(await _db.KnowledgeFiles.AsNoTracking()
            .Where(f => f.FactoryId == factoryId && f.RefType == "product" && f.RefId == product.Id)
            .ToListAsync());

        if (filterOperationId != null)
        {
            files.AddRange(await _db.KnowledgeFiles.AsNoTracking()
                .Where(f => f.FactoryId == factoryId && f.RefType == "operation" && f.RefId == filterOperationId)
                .ToListAsync());
        }
        else if (operationIds.Count > 0)
        {
            files.AddRange(await _db.KnowledgeFiles.AsNoTracking()
                .Where(f => f.FactoryId == factoryId && f.RefType == "operation" && operationIds.Contains(f.RefId))
                .ToListAsync());
        }

        // 组装 + 翻译：只吐文字内容，绝不吐 relative_path
        var result = new KnowledgeGuideResult
        {
            Product = new ProductBrief { Code = product.Code, Name = product.Name },
            Routing = routingNames
        };
        foreach (var f in files)
        {
            // 图片无法翻译成文字，跳过（供手机端预览，不参与 AI 文字问答）
            if (IsImageType(f.FileType)) continue;
            var content = _parser.Extract(f.RelativePath, f.FileType);
            result.Files.Add(new KnowledgeFileContent
            {
                FileName = f.FileName,
                Version = f.Version,
                RefName = f.RefType == "operation" ? opNameById.GetValueOrDefault(f.RefId) : null,
                Content = content
            });
        }
        return result;
    }

    private static bool IsImageType(string fileType) =>
        fileType is "jpg" or "jpeg" or "png" or "webp";
}
