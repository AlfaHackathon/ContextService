using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContextService.Common;

// Отсутствие optional-поля допустимо, явный JSON null сохраняет прежнюю ошибку схемы события.
public sealed class OptionalStringConverter : JsonConverter<string>
{
    public override bool HandleNull => true;

    public override string Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Expected a string when the optional field is supplied");
        }
        return reader.GetString()!;
    }

    public override void Write(
        Utf8JsonWriter writer,
        string value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}
