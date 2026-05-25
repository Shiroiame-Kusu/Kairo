namespace Kairo.Core.Models;

public sealed class FrpApiResult<T>
{
    public bool Success { get; init; }
    public int Code { get; init; }
    public string Message { get; init; } = string.Empty;
    public T? Data { get; init; }

    public static FrpApiResult<T> Ok(T? data, int code = 200, string message = "") => new()
    {
        Success = true,
        Code = code,
        Message = message,
        Data = data
    };

    public static FrpApiResult<T> Fail(int code, string message) => new()
    {
        Success = false,
        Code = code,
        Message = string.IsNullOrWhiteSpace(message) ? "未知错误" : message
    };
}
