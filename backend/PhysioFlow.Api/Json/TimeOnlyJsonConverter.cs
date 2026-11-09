using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhysioFlow.Api.Json;

/// <summary>Zapisuje godziny jako HH:mm i przyjmuje też HH:mm:ss — godziny pracy kalendarza w Europe/Warsaw.</summary>
public sealed class TimeOnlyJsonConverter : JsonConverter<TimeOnly>
{
    private static readonly string[] Formats = ["HH:mm", "HH:mm:ss"];

    public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new JsonException("Godzina nie może być pusta.");

        if (TimeOnly.TryParseExact(value, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            return time;

        throw new JsonException("Godzina musi mieć format HH:mm.");
    }

    public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("HH:mm", CultureInfo.InvariantCulture));
}

public sealed class NullableTimeOnlyJsonConverter : JsonConverter<TimeOnly?>
{
    private readonly TimeOnlyJsonConverter _inner = new();

    public override TimeOnly? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        return _inner.Read(ref reader, typeof(TimeOnly), options);
    }

    public override void Write(Utf8JsonWriter writer, TimeOnly? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            _inner.Write(writer, value.Value, options);
    }
}
