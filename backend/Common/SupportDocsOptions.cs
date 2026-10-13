namespace ahu.MicrosoftMes.Common;

/// <summary>
/// 智能客服知识目录配置（docs/25 §3）。默认关闭、目录为空，不影响原系统。
/// 只索引专用发布目录的 UTF-8 md/txt；数值为首版上限，可部署调小。
/// </summary>
public class SupportDocsOptions
{
    /// <summary>总开关。false 时客服检索返回「未启用」，不影响 progress/guide 与原业务。</summary>
    public bool Enabled { get; set; }

    /// <summary>客服发布目录（绝对路径，或相对内容根目录）。空 = 未配置。</summary>
    public string RootPath { get; set; } = "";

    /// <summary>最多索引文件数。</summary>
    public int MaxFiles { get; set; } = 100;

    /// <summary>单文件最大字节数。</summary>
    public int MaxFileBytes { get; set; } = 1048576;

    /// <summary>目录总量最大字节数。</summary>
    public int MaxTotalBytes { get; set; } = 10485760;

    /// <summary>每次检索最多返回片段数。</summary>
    public int MaxResults { get; set; } = 5;

    /// <summary>每次送入模型的证据正文预算（字符）。</summary>
    public int MaxEvidenceChars { get; set; } = 12000;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(RootPath);
}
