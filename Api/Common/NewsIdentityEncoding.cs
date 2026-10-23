using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ContextService.Common;

// JSON.stringify([string|null, ...]) из прежнего Kafka consumer определяет уже сохранённые IDs.
// Обычный System.Text.Json экранирует Unicode/HTML иначе и изменил бы идентичность публикаций.
public static class NewsIdentityEncoding
{
    public static string Hash(params string?[] values)
    {
        var json = new StringBuilder("[");
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0)
            {
                json.Append(',');
            }
            if (values[i] is not { } value)
            {
                json.Append("null");
                continue;
            }
            json.Append('"');
            for (var j = 0; j < value.Length; j++)
            {
                var character = value[j];
                switch (character)
                {
                    case '"':
                        json.Append("\\\"");
                        break;
                    case '\\':
                        json.Append("\\\\");
                        break;
                    case '\b':
                        json.Append("\\b");
                        break;
                    case '\f':
                        json.Append("\\f");
                        break;
                    case '\n':
                        json.Append("\\n");
                        break;
                    case '\r':
                        json.Append("\\r");
                        break;
                    case '\t':
                        json.Append("\\t");
                        break;
                    default:
                        if (character < 0x20
                            || char.IsSurrogate(character)
                            && !(char.IsHighSurrogate(character) && j + 1 < value.Length && char.IsLowSurrogate(value[j + 1])))
                        {
                            json.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                            break;
                        }
                        json.Append(character);
                        if (char.IsHighSurrogate(character))
                        {
                            json.Append(value[++j]);
                        }
                        break;
                }
            }
            json.Append('"');
        }
        json.Append(']');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json.ToString())));
    }

    public static string Trim(string value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && IsWhiteSpace(value[start]))
        {
            start++;
        }
        while (end > start && IsWhiteSpace(value[end - 1]))
        {
            end--;
        }
        return value[start..end];
    }

    private static bool IsWhiteSpace(char value)
    {
        return value is '\t' or '\v' or '\f' or '\n' or '\r' or ' ' or '\u00a0' or '\u1680' or
            >= '\u2000' and <= '\u200a' or '\u2028' or '\u2029' or '\u202f' or '\u205f' or '\u3000' or '\ufeff';
    }
}
