namespace ahu.MicrosoftMes.Common;

/// <summary>部署形态识别（docs/88、docs/90）。私有买断版置 Private=true。</summary>
public class DeploymentOptions
{
    public const string SectionName = "Deployment";

    /// <summary>true=私有买断版：存储放行、体验清理整段跳过。</summary>
    public bool Private { get; set; }
}
