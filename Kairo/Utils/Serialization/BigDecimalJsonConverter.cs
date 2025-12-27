using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExtendedNumerics;

namespace Kairo.Utils.Serialization;

internal sealed class BigDecimalJsonConverter : JsonConverter<BigDecimal>
{
    /// <summary>
    /// 静态构造函数：安全初始化 BigDecimal 库
    /// BigDecimal 在某些区域设置下会因为 NativeDigits 不兼容而抛出 TypeInitializationException
    /// </summary>
    static BigDecimalJsonConverter()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUICulture = CultureInfo.CurrentUICulture;
        
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            
            // 触发 BigDecimal 静态构造函数
            _ = BigDecimal.Zero;
        }
        catch
        {
            // 忽略初始化错误
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUICulture;
        }
    }
    
    public override BigDecimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return default;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            return string.IsNullOrWhiteSpace(text) ? default : BigDecimal.Parse(text);
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var raw = document.RootElement.GetRawText();
        return string.IsNullOrWhiteSpace(raw) ? default : BigDecimal.Parse(raw);
    }

    public override void Write(Utf8JsonWriter writer, BigDecimal value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
