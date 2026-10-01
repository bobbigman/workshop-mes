namespace ahu.MicrosoftMes.Common;

/// <summary>
/// 统一抛错辅助 —— 不吞异常，抛出时带上可定位的上下文。
/// 完整敏感上下文由服务器日志采集；Biz / BizUser 的 Message 只放业务提示。
/// Sql / Api 的 Message 会经异常中间件回传到 PC/H5（密钥脱敏）。
/// </summary>
public static class ThrowHelper
{
    /// <summary>面向用户的业务提示（不含方法名等技术前缀）。</summary>
    public static BusinessException BizUser(string what)
    {
        return new BusinessException(what);
    }

    public static BusinessException Biz(string where, string what)
    {
        // 保留 where 参数兼容现有调用点，不将内部方法名拼入用户提示。
        return new BusinessException(what);
    }

    /// <summary>执行 SQL 失败 —— Message 带 SQL 原文；可选参数快照（已建议脱敏后传入）。</summary>
    public static Exception Sql(string sql, Exception ex, string? parametersSnapshot = null)
    {
        var msg = string.IsNullOrEmpty(parametersSnapshot)
            ? $"SQL执行失败。SQL原文: {sql}"
            : $"SQL执行失败。SQL原文: {sql}；参数: {parametersSnapshot}";
        return new Exception(msg, ex);
    }

    /// <summary>调用外部 API 失败 —— 网址 + 请求 + 返回（调用方应先脱敏）。</summary>
    public static Exception Api(string url, string requestBody, string responseBody, Exception? ex = null)
    {
        return new Exception(
            $"调用API失败。网址: {LogRedactor.RedactUrl(url)}；请求报文: {LogRedactor.RedactText(requestBody, 4096)}；返回信息: {LogRedactor.RedactText(responseBody, 4096)}",
            ex);
    }

    public static Exception General(string where, string what, Exception? ex = null)
    {
        return new Exception($"{where}：{what}", ex);
    }
}
