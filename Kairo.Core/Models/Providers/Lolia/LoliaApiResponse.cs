namespace Kairo.Core.Models;

public sealed class LoliaApiResponse<T>
{
    public int Code { get; init; }
    public string Msg { get; init; } = string.Empty;
    public T? Data { get; init; }
}
