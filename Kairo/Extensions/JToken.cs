using Newtonsoft.Json;

namespace Kairo.Extensions
{
    internal static partial class JToken
    {
        public static string ToJson(this object obj, Formatting formatting)
            => obj != null ? JsonConvert.SerializeObject(obj, formatting) : string.Empty;

        public static string ToJson(this object obj)
            => obj?.ToJson(Formatting.None) ?? string.Empty;
    }
}
