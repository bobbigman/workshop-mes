namespace ahu.MicrosoftMes.Services.Kingdee;

public class KingdeeMaterialDto
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal CostPrice { get; set; }
}

public class KingdeeBomLineDto
{
    public string MaterialCode { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public decimal Qty { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LineCost => Math.Round(Qty * UnitCost, 4, MidpointRounding.AwayFromZero);
}

public class KingdeeBomCostDto
{
    public string ParentCode { get; set; } = "";
    public decimal TotalCost { get; set; }
    public List<KingdeeBomLineDto> Lines { get; set; } = new();
}

public class KingdeeRoutingStepDto
{
    public int Seq { get; set; }
    public string OperationName { get; set; } = "";
}

public interface IKingdeeMasterDataClient
{
    string ModeName { get; }
    Task<KingdeeMaterialDto?> GetMaterialAsync(string code, CancellationToken ct = default);
    Task<KingdeeBomCostDto?> GetBomMaterialCostAsync(string code, CancellationToken ct = default);
    Task<List<KingdeeRoutingStepDto>> GetRoutingAsync(string code, CancellationToken ct = default);
}
