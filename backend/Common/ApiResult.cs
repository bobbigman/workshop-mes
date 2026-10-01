namespace ahu.MicrosoftMes.Common;

/// <summary>统一 API 返回格式：{ code, msg, data }</summary>
public class ApiResult<T>
{
    public int Code { get; set; }
    public string Msg { get; set; } = "ok";
    public T? Data { get; set; }

    public static ApiResult<T> Ok(T data) => new() { Code = 0, Data = data };
    public static ApiResult<T> Ok() => new() { Code = 0 };
    public static ApiResult<T> Fail(string msg, int code = 1) => new() { Code = code, Msg = msg };

    /// <summary>无 data 的成功返回（用于 Task&lt;ApiResult&lt;object?&gt;&gt;）</summary>
    public static ApiResult<T> OkMsg() => new() { Code = 0 };

    /// <summary>无 data 的失败返回</summary>
    public static ApiResult<T> FailMsg(string msg, int code = 1) => new() { Code = code, Msg = msg };
}

public class PageResult<T>
{
    public List<T> List { get; set; } = new();
    public int Total { get; set; }
}
