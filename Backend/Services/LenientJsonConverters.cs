using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Printify.Backend.Services;

// Tumatanggap ng number/bool bilang string (para hindi mag-error kung iba ang uri na ipinadala ng frontend).
public class LenientStringConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.Number:
                return reader.TryGetInt64(out var whole)
                    ? whole.ToString(CultureInfo.InvariantCulture)
                    : reader.GetDecimal().ToString(CultureInfo.InvariantCulture);
            case JsonTokenType.True:
                return "true";
            case JsonTokenType.False:
                return "false";
            default:
                throw new JsonException("Expected a string.");
        }
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}

// Ang walang laman o hindi valid na numero ay gagawing 0 imbes na mag-error.
public class LenientDecimalConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.GetDecimal();
            case JsonTokenType.String:
                return decimal.TryParse(reader.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0m;
            case JsonTokenType.Null:
                return 0m;
            default:
                throw new JsonException("Expected a number.");
        }
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}
