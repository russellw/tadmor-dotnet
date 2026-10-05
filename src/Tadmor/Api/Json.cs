using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tadmor.Api;

/// <summary>
/// The wire format of responses (spec/api.md §1.2): snake_case field names,
/// decimals as strings at the scale the database holds them (money "9.9900"),
/// dates as YYYY-MM-DD, and null for absent optional values.
/// </summary>
public static class Json
{
    public static void Configure(JsonSerializerOptions o)
    {
        o.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        o.DictionaryKeyPolicy = null;
        o.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        o.Converters.Add(new DecimalConverter());
        o.Converters.Add(new NullableDecimalConverter());
    }

    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(o);
        return o;
    }

    private sealed class DecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            decimal.Parse(reader.GetString()!, CultureInfo.InvariantCulture);

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }

    private sealed class NullableDecimalConverter : JsonConverter<decimal?>
    {
        public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Null ? null : decimal.Parse(reader.GetString()!, CultureInfo.InvariantCulture);

        public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
        {
            if (value is { } d)
            {
                writer.WriteStringValue(d.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }
}
