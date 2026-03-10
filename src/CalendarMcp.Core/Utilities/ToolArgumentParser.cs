using System.Text;
using System.Text.Json;

namespace CalendarMcp.Core.Utilities;

/// <summary>
/// Helpers for parsing tool arguments that may arrive as JS-style (single-quoted) JSON
/// rather than strict JSON, which some LLMs produce when they echo schema examples.
/// </summary>
public static class ToolArgumentParser
{
    /// <summary>
    /// Deserializes a JSON string, falling back to lenient single-quoted JS normalization
    /// if strict parsing fails.
    /// </summary>
    public static T? ParseArguments<T>(string json, JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            return default;

        // Strict parse first
        try
        {
            return JsonSerializer.Deserialize<T>(json, options);
        }
        catch (JsonException) { }

        // Lenient fallback: normalize single-quoted / unquoted-key JS syntax
        var normalized = NormalizeSingleQuotedJson(json);
        return JsonSerializer.Deserialize<T>(normalized, options);
    }

    /// <summary>
    /// Converts a JS-style single-quoted or bare-key JSON string to strict JSON.
    /// <list type="bullet">
    ///   <item>Single-quoted strings: <c>'value'</c> → <c>"value"</c></item>
    ///   <item>Unquoted object keys: <c>{key: 'v'}</c> → <c>{"key": "v"}</c></item>
    ///   <item>Double-quotes inside single-quoted strings are escaped.</item>
    /// </list>
    /// Returns the input unchanged if it is already valid JSON.
    /// </summary>
    public static string NormalizeSingleQuotedJson(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        // Fast path: already valid
        try
        {
            JsonDocument.Parse(input);
            return input;
        }
        catch (JsonException) { }

        var sb = new StringBuilder(input.Length + 16);
        int i = 0;
        bool inDouble = false;
        bool inSingle = false;
        bool escaped = false;

        while (i < input.Length)
        {
            char c = input[i];

            // Handle escape sequences inside strings
            if (escaped)
            {
                sb.Append(c);
                escaped = false;
                i++;
                continue;
            }

            if (c == '\\' && (inDouble || inSingle))
            {
                sb.Append(c);
                escaped = true;
                i++;
                continue;
            }

            // Track double-quoted string context
            if (c == '"' && !inSingle)
            {
                inDouble = !inDouble;
                sb.Append(c);
                i++;
                continue;
            }

            // Single-quote transitions: open → emit ", close → emit "
            if (c == '\'' && !inDouble)
            {
                inSingle = !inSingle;
                sb.Append('"');
                i++;
                continue;
            }

            // Inside a single-quoted string: escape any bare double-quotes
            if (inSingle && c == '"')
            {
                sb.Append('\\');
                sb.Append('"');
                i++;
                continue;
            }

            // Outside strings: quote bare object/array keys after { or ,
            if (!inDouble && !inSingle && (c == '{' || c == ','))
            {
                sb.Append(c);
                i++;

                // Consume and emit whitespace
                while (i < input.Length && char.IsWhiteSpace(input[i]))
                {
                    sb.Append(input[i]);
                    i++;
                }

                // If the next char starts a bare identifier (not a quote or structural char),
                // wrap it in double quotes.
                if (i < input.Length)
                {
                    char next = input[i];
                    if (next != '"' && next != '\'' && next != '}' && next != ']'
                        && (char.IsLetter(next) || next == '_'))
                    {
                        sb.Append('"');
                        while (i < input.Length && (char.IsLetterOrDigit(input[i]) || input[i] == '_'))
                        {
                            sb.Append(input[i]);
                            i++;
                        }
                        sb.Append('"');
                    }
                }

                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }
}
