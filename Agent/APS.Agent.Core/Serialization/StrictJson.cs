using System.Text;
using System.Text.Json;

namespace APS.Agent.Core.Serialization;

public static class StrictJson
{
    public static JsonDocument Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        ValidateNoDuplicateKeys(json);
        var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
        });
        EnsureFiniteNumbers(doc.RootElement);
        return doc;
    }

    public static string CanonicalSerialize<T>(T value) =>
        JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            WriteIndented = false,
        });

    private static void ValidateNoDuplicateKeys(string json)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(json);
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
        });
        var objectKeys = new Stack<HashSet<string>?>();
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    objectKeys.Push(new HashSet<string>(StringComparer.Ordinal));
                    break;
                case JsonTokenType.EndObject:
                    if (objectKeys.Count > 0) objectKeys.Pop();
                    break;
                case JsonTokenType.StartArray:
                    objectKeys.Push(null);
                    break;
                case JsonTokenType.EndArray:
                    if (objectKeys.Count > 0) objectKeys.Pop();
                    break;
                case JsonTokenType.PropertyName:
                    string name = reader.GetString() ?? "";
                    if (objectKeys.Count > 0 && objectKeys.Peek() is { } keys && !keys.Add(name))
                        throw new JsonException($"Duplicate JSON key '{name}'.");
                    break;
            }
        }
    }

    private static void EnsureFiniteNumbers(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Number:
                if (!el.TryGetDouble(out double d) || !double.IsFinite(d))
                    throw new JsonException("Non-finite JSON number.");
                break;
            case JsonValueKind.Object:
                foreach (var prop in el.EnumerateObject())
                    EnsureFiniteNumbers(prop.Value);
                break;
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                    EnsureFiniteNumbers(item);
                break;
        }
    }
}
