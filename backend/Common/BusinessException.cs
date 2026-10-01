namespace ahu.MicrosoftMes.Common;

/// <summary>
/// 业务异常。业务校验失败时抛出，由全局异常中间件统一转成 ApiResult{code,msg}。
/// 【业务背景】Service 里业务校验不通过，不直接返回，而是 throw 此异常，
///             这样 Controller 保持干净、错误处理统一。
/// </summary>
public class BusinessException : Exception
{
    /// <summary>业务错误码（默认 1，可按模块自定义）</summary>
    public int Code { get; }

    public BusinessException(string message, int code = 1) : base(message)
    {
        Code = code;
    }
}
