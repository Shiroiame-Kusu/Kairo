using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExtendedNumerics;

namespace Kairo.Utils.Serialization;

internal sealed class BigDecimalJsonConverter : JsonConverter<BigDecimal>
{
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
