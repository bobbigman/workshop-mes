namespace ahu.MicrosoftMes.Services.Kingdee;

/// <summary>金蝶主数据适配配置（docs/72）。凭证只放服务端 appsettings，禁止进前端。</summary>
public class KingdeeOptions
{
    public const string SectionName = "Kingdee";

    /// <summary>Mock | K3Cloud</summary>
    public string Mode { get; set; } = "Mock";

    /// <summary>无计时工价时的默认人工费率（元/小时）</summary>
    public decimal DefaultLaborRatePerHour { get; set; } = 50m;

    /// <summary>建议报价默认毛利加点（%）</summary>
    public decimal DefaultMarkupPct { get; set; } = 15m;

    public string BaseUrl { get; set; } = "";
    public string AcctId { get; set; } = "";
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
    public int Lcid { get; set; } = 2052;
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>物料 FormId，标准多为 BD_MATERIAL</summary>
    public string MaterialFormId { get; set; } = "BD_MATERIAL";

    /// <summary>BOM FormId，标准多为 ENG_BOM；现场可改</summary>
    public string BomFormId { get; set; } = "ENG_BOM";

    /// <summary>工艺路线 FormId，标准多为 ENG_ROUTE；现场可改</summary>
    public string RoutingFormId { get; set; } = "ENG_ROUTE";
}
