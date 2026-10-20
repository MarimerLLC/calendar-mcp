using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Configuration;

/// <summary>
/// Accepts quoted booleans and numbers for boolean and numeric tool parameters. Some clients and
/// models send "true" or "50" as strings, and System.Text.Json refuses to bind a JSON string to a
/// bool or int, so the call would fail before the tool runs. A call-tool filter rewrites those
/// arguments to JSON booleans and numbers, guided by each tool's input schema, so every tool gets
/// the same leniency and the advertised schemas keep their types.
/// </summary>
public static class StringArgumentCoercion
{
    private static readonly JsonElement True = JsonSerializer.SerializeToElement(true);
    private static readonly JsonElement False = JsonSerializer.SerializeToElement(false);

    /// <summary>
    /// Registers the call-tool filter. Shared by the stdio and HTTP server entry points.
    /// </summary>
    public static IMcpServerBuilder WithStringScalarArguments(this IMcpServerBuilder builder) =>
        builder.WithRequestFilters(filters => filters.AddCallToolFilter(Filter));

    internal static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        (context, cancellationToken) =>
        {
            if (context.MatchedPrimitive is McpServerTool tool
                && context.Params is { Arguments.Count: > 0 } request)
            {
                var coerced = Coerce(tool.ProtocolTool.InputSchema, request.Arguments, out var names);
                if (names.Count > 0)
                {
                    request.Arguments = coerced;
                    context.Services?.GetService<ILoggerFactory>()
                        ?.CreateLogger(typeof(StringArgumentCoercion).FullName!)
                        .LogInformation("Read string arguments {Names} as booleans or numbers for {Tool}",
                            string.Join(", ", names), request.Name);
                }
            }
            return next(context, cancellationToken);
        };

    /// <summary>
    /// Returns the arguments with string values rewritten where the schema types that property
    /// as a boolean or a number: "true"/"false" (any case) become JSON booleans, and a JSON number
    /// literal such as "50" becomes that number. Surrounding spaces are ignored. Other strings are
    /// left alone so binding still reports them, and a number the parameter can't hold (a fraction
    /// for an integer, or one out of range) is left to binding to reject.
    /// <paramref name="coercedNames"/> lists what changed.
    /// </summary>
    internal static IDictionary<string, JsonElement> Coerce(
        JsonElement inputSchema, IDictionary<string, JsonElement> arguments, out List<string> coercedNames)
    {
        coercedNames = [];
        if (inputSchema.ValueKind != JsonValueKind.Object
            || !inputSchema.TryGetProperty("properties", out var properties)
            || properties.ValueKind != JsonValueKind.Object)
            return arguments;

        Dictionary<string, JsonElement>? result = null;
        foreach (var (name, value) in arguments)
        {
            if (value.ValueKind != JsonValueKind.String
                || !properties.TryGetProperty(name, out var property))
                continue;

            var text = value.GetString()?.Trim() ?? string.Empty;
            JsonElement? replacement = GetScalarType(property) switch
            {
                "boolean" => ParseBoolean(text),
                "integer" or "number" => ParseNumber(text),
                _ => null
            };
            if (replacement is null)
                continue;

            result ??= new Dictionary<string, JsonElement>(arguments);
            result[name] = replacement.Value;
            coercedNames.Add(name);
        }

        return result ?? arguments;
    }

    private static JsonElement? ParseBoolean(string text) =>
        string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ? True
        : string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) ? False
        : null;

    /// <summary>
    /// Parses the text as a JSON number literal, keeping its exact digits so binding applies the
    /// parameter's own range and integer checks.
    /// </summary>
    private static JsonElement? ParseNumber(string text)
    {
        if (text.Length == 0)
            return null;
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Number
                ? document.RootElement.Clone()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The property's scalar type when its "type" is "boolean", "integer" or "number", alone or
    /// with "null" (a nullable parameter). Null for anything else, including a type that also
    /// allows "string", which takes the string as is.
    /// </summary>
    private static string? GetScalarType(JsonElement property)
    {
        if (property.ValueKind != JsonValueKind.Object || !property.TryGetProperty("type", out var type))
            return null;

        string? scalar = null;
        if (type.ValueKind == JsonValueKind.String)
        {
            scalar = type.GetString();
        }
        else if (type.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in type.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String)
                    return null;
                if (entry.ValueEquals("null"))
                    continue;
                if (scalar != null)
                    return null;
                scalar = entry.GetString();
            }
        }

        return scalar is "boolean" or "integer" or "number" ? scalar : null;
    }
}
