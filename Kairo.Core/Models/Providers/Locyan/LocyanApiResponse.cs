namespace Kairo.Core.Models;

public sealed class LocyanApiResponse<T>
{
    public int Status { get; init; }
    public string Message { get; init; } = string.Empty;
    public T? Data { get; init; }
}
