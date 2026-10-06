namespace Kairo.Core.Logging;

/// <summary>
/// 日志脱敏：把文本中出现的令牌等敏感值替换为只保留首尾少量字符的形式
/// </summary>
public static class SecretMasker
{
    public static string Mask(string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return string.Empty;
        return secret.Length <= 8 ? new string('*', secret.Length) : $"{secret[..3]}***{secret[^3..]}";
    }

    public static string Redact(string? text, params string?[] secrets)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        foreach (var secret in secrets)
        {
            // 过短的值替换后反而会破坏正常文本，令牌不会这么短
            if (string.IsNullOrEmpty(secret) || secret.Length < 4) continue;
            text = text.Replace(secret, Mask(secret), StringComparison.Ordinal);
        }
        return text;
    }
}
