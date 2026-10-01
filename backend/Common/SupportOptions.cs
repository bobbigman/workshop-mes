namespace ahu.MicrosoftMes.Common;

/// <summary>售后提示用语（docs/111）；不写死联系电话/微信，仅可选提供商称呼。</summary>
public class SupportOptions
{
    public const string SectionName = "Support";

    /// <summary>空则前端提示「您的软件提供商」；非空则替换为该名称。</summary>
    public string ProviderName { get; set; } = "";
}
