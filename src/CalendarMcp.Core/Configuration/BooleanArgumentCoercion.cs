using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Configuration;

/// <summary>
/// Accepts the strings "true" and "false" for boolean tool parameters. Some clients and models
/// quote booleans, and System.Text.Json refuses to bind a JSON string to a bool, so the call
/// would fail before the tool runs. A call-tool filter rewrites those arguments to JSON booleans,
/// guided by each tool's input schema, so every tool gets the same leniency and the advertised
/// schemas still say "boolean".
/// </summary>
public static class BooleanArgumentCoercion
{
    private static readonly JsonElement True = JsonSerializer.SerializeToElement(true);
    private static readonly JsonElement False = JsonSerializer.SerializeToElement(false);

    /// <summary>
    /// Registers the call-tool filter. Shared by the stdio and HTTP server entry points.
    /// </summary>
    public static IMcpServerBuilder WithBooleanStringArguments(this IMcpServerBuilder builder) =>
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
                        ?.CreateLogger(typeof(BooleanArgumentCoercion).FullName!)
                        .LogInformation("Read string arguments {Names} as booleans for {Tool}",
                            string.Join(", ", names), request.Name);
                }
            }
            return next(context, cancellationToken);
        };

    /// <summary>
    /// Returns the arguments with every "true"/"false" string (trimmed, any case) replaced by a
    /// JSON boolean where the schema types that property as boolean. Other strings are left
    /// alone so binding still reports them. <paramref name="coercedNames"/> lists what changed.
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
                || !properties.TryGetProperty(name, out var property)
                || !IsBooleanOnly(property))
                continue;

            var text = value.GetString()?.Trim();
            JsonElement replacement;
            if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
                replacement = True;
            else if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
                replacement = False;
            else
                continue;

            result ??= new Dictionary<string, JsonElement>(arguments);
            result[name] = replacement;
            coercedNames.Add(name);
        }

        return result ?? arguments;
    }

    /// <summary>
    /// True when the property's "type" is "boolean", or a list of "boolean" and "null" (a
    /// nullable bool). A type that also allows "string" takes the string as is.
    /// </summary>
    private static bool IsBooleanOnly(JsonElement property)
    {
        if (property.ValueKind != JsonValueKind.Object || !property.TryGetProperty("type", out var type))
            return false;

        if (type.ValueKind == JsonValueKind.String)
            return type.ValueEquals("boolean");

        if (type.ValueKind != JsonValueKind.Array)
            return false;

        var hasBoolean = false;
        foreach (var entry in type.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String)
                return false;
            if (entry.ValueEquals("boolean"))
                hasBoolean = true;
            else if (!entry.ValueEquals("null"))
                return false;
        }
        return hasBoolean;
    }
}
