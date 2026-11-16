using System.Text.Json;
using System.Text.Json.Nodes;

namespace APS.Agent.Core.Schemas;

public sealed record ToolDeclaration(
    string Name,
    string Description,
    JsonElement Parameters,
    string ToolClass = "read");

public static class ToolSchemaValidator
{
    private static readonly string[] RefKeys = ["$ref", "$dynamicRef", "$recursiveRef"];

    public static void ValidateToolSchemas(IEnumerable<ToolDeclaration> tools)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            if (string.IsNullOrWhiteSpace(tool.Name) || !seen.Add(tool.Name))
                throw new InvalidOperationException($"Duplicate or empty tool name '{tool.Name}'.");
            InspectSchemaNode(tool.Parameters, tool.Name);
        }
    }

    private static void InspectSchemaNode(JsonElement el, string toolName)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in el.EnumerateObject())
                {
                    if (RefKeys.Contains(prop.Name, StringComparer.Ordinal))
                    {
                        if (prop.Value.ValueKind != JsonValueKind.String
                            || !(prop.Value.GetString() ?? "").StartsWith('#'))
                        {
                            throw new InvalidOperationException($"Tool '{toolName}' contains external {prop.Name}.");
                        }
                    }
                    else if (prop.Name == "$id")
                    {
                        if (prop.Value.ValueKind != JsonValueKind.String
                            || !(prop.Value.GetString() ?? "").StartsWith('#'))
                        {
                            throw new InvalidOperationException($"Tool '{toolName}' contains external $id.");
                        }
                    }
                    InspectSchemaNode(prop.Value, toolName);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                    InspectSchemaNode(item, toolName);
                break;
        }
    }

    public static JsonElement ValidateArguments(ToolDeclaration tool, JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
            throw new ArgumentException($"Arguments for '{tool.Name}' must be a JSON object.");

        var required = new HashSet<string>(StringComparer.Ordinal);
        if (tool.Parameters.ValueKind == JsonValueKind.Object
            && tool.Parameters.TryGetProperty("required", out var reqEl)
            && reqEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in reqEl.EnumerateArray())
                if (r.ValueKind == JsonValueKind.String && r.GetString() is { } s)
                    required.Add(s);
        }

        JsonElement props = default;
        bool hasProps = tool.Parameters.ValueKind == JsonValueKind.Object
            && tool.Parameters.TryGetProperty("properties", out props)
            && props.ValueKind == JsonValueKind.Object;

        var cleaned = new JsonObject();
        foreach (var prop in arguments.EnumerateObject())
        {
            bool isOptional = !required.Contains(prop.Name);
            if (isOptional && (prop.Value.ValueKind == JsonValueKind.Null
                || (prop.Value.ValueKind == JsonValueKind.String && prop.Value.GetString() == "null")))
            {
                continue;
            }
            cleaned[prop.Name] = JsonNode.Parse(prop.Value.GetRawText());
        }

        foreach (string req in required)
        {
            if (!cleaned.ContainsKey(req))
                throw new ArgumentException($"Tool '{tool.Name}' missing required argument '{req}'.");
        }

        if (hasProps)
        {
            foreach (var kv in cleaned)
            {
                if (!props.TryGetProperty(kv.Key, out var propSchema)) continue;
                var resolved = ResolveLocalRef(tool.Parameters, propSchema);
                if (resolved.ValueKind == JsonValueKind.Object
                    && resolved.TryGetProperty("type", out var typeEl)
                    && typeEl.ValueKind == JsonValueKind.String
                    && kv.Value is not null)
                {
                    ValidateJsonType(tool.Name, kv.Key, typeEl.GetString()!, kv.Value);
                }
            }
        }

        using var doc = JsonDocument.Parse(cleaned.ToJsonString());
        return doc.RootElement.Clone();
    }

    private static JsonElement ResolveLocalRef(JsonElement root, JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object
            || !schema.TryGetProperty("$ref", out var refEl)
            || refEl.ValueKind != JsonValueKind.String)
            return schema;

        string? pointer = refEl.GetString();
        if (pointer is null || !pointer.StartsWith("#/", StringComparison.Ordinal))
            return schema;

        JsonElement current = root;
        foreach (string rawSeg in pointer[2..].Split('/'))
        {
            string seg = rawSeg.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(seg, out current))
                return schema;
        }
        return current;
    }

    private static void ValidateJsonType(string toolName, string field, string expectedType, JsonNode node)
    {
        var kind = node.GetValueKind();
        bool valid = expectedType switch
        {
            "string" => kind == JsonValueKind.String,
            "boolean" => kind is JsonValueKind.True or JsonValueKind.False,
            "integer" => kind == JsonValueKind.Number && node.AsValue().TryGetValue<long>(out _),
            "number" => kind == JsonValueKind.Number,
            "array" => kind == JsonValueKind.Array,
            "object" => kind == JsonValueKind.Object,
            _ => true,
        };
        if (!valid)
            throw new ArgumentException($"Tool '{toolName}' field '{field}' expected {expectedType} but got {kind}.");
    }
}
