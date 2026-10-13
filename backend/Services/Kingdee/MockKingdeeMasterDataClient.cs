namespace ahu.MicrosoftMes.Services.Kingdee;

/// <summary>演示/开发用样例主数据（docs/72）。对齐厨具五金厂「打蛋器」演示话术。</summary>
public class MockKingdeeMasterDataClient : IKingdeeMasterDataClient
{
    public string ModeName => "Mock";

    private static readonly Dictionary<string, KingdeeMaterialDto> Materials = new(StringComparer.OrdinalIgnoreCase)
    {
        // 主演示：与本厂产品 DDQ-001「打蛋器」同编号，方便讲「金蝶物料=车间产品」
        ["DDQ-001"] = new() { Code = "DDQ-001", Name = "打蛋器", CostPrice = 18.6m },
        ["DEMO-MAT-001"] = new() { Code = "DEMO-MAT-001", Name = "演示轴类零件", CostPrice = 12.5m },
        ["DEMO-MAT-002"] = new() { Code = "DEMO-MAT-002", Name = "演示法兰盘", CostPrice = 28m },
    };

    private static readonly Dictionary<string, KingdeeBomCostDto> Boms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DDQ-001"] = new()
        {
            ParentCode = "DDQ-001",
            Lines =
            [
                new() { MaterialCode = "DDQ-HANDLE", MaterialName = "手柄组件", Qty = 1m, UnitCost = 6.5m },
                new() { MaterialCode = "DDQ-WIRE", MaterialName = "搅拌丝", Qty = 2m, UnitCost = 3.8m },
                new() { MaterialCode = "DDQ-GEAR", MaterialName = "齿轮盒", Qty = 1m, UnitCost = 4.5m },
            ]
        },
        ["DEMO-MAT-001"] = new()
        {
            ParentCode = "DEMO-MAT-001",
            Lines =
            [
                new() { MaterialCode = "RAW-STEEL-45", MaterialName = "45#圆钢", Qty = 1.2m, UnitCost = 8m },
                new() { MaterialCode = "STD-BOLT-M8", MaterialName = "M8螺栓", Qty = 4m, UnitCost = 0.35m },
            ]
        },
        ["DEMO-MAT-002"] = new()
        {
            ParentCode = "DEMO-MAT-002",
            Lines =
            [
                new() { MaterialCode = "RAW-STEEL-Q235", MaterialName = "Q235板材", Qty = 2.5m, UnitCost = 6.5m },
                new() { MaterialCode = "STD-PIN", MaterialName = "定位销", Qty = 2m, UnitCost = 1.2m },
            ]
        },
    };

    private static readonly Dictionary<string, List<KingdeeRoutingStepDto>> Routings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DDQ-001"] =
        [
            new() { Seq = 10, OperationName = "下料" },
            new() { Seq = 20, OperationName = "冲压" },
            new() { Seq = 30, OperationName = "组装" },
            new() { Seq = 40, OperationName = "检验" },
            new() { Seq = 50, OperationName = "包装" },
        ],
        ["DEMO-MAT-001"] =
        [
            new() { Seq = 10, OperationName = "下料" },
            new() { Seq = 20, OperationName = "车削" },
            new() { Seq = 30, OperationName = "检验" },
        ],
        ["DEMO-MAT-002"] =
        [
            new() { Seq = 10, OperationName = "下料" },
            new() { Seq = 20, OperationName = "铣削" },
            new() { Seq = 30, OperationName = "钻孔" },
            new() { Seq = 40, OperationName = "检验" },
        ],
    };

    public Task<KingdeeMaterialDto?> GetMaterialAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return Task.FromResult<KingdeeMaterialDto?>(null);
        Materials.TryGetValue(code.Trim(), out var m);
        return Task.FromResult(m);
    }

    public Task<KingdeeBomCostDto?> GetBomMaterialCostAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return Task.FromResult<KingdeeBomCostDto?>(null);
        if (!Boms.TryGetValue(code.Trim(), out var bom))
            return Task.FromResult<KingdeeBomCostDto?>(null);
        bom.TotalCost = bom.Lines.Sum(x => x.LineCost);
        return Task.FromResult<KingdeeBomCostDto?>(bom);
    }

    public Task<List<KingdeeRoutingStepDto>> GetRoutingAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return Task.FromResult(new List<KingdeeRoutingStepDto>());
        if (Routings.TryGetValue(code.Trim(), out var steps))
            return Task.FromResult(steps.ToList());
        return Task.FromResult(new List<KingdeeRoutingStepDto>());
    }
}
