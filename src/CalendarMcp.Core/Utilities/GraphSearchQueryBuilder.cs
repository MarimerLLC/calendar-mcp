namespace CalendarMcp.Core.Utilities;

/// <summary>
/// Builds the $search value for Microsoft Graph message queries
/// </summary>
public static class GraphSearchQueryBuilder
{
    /// <summary>
    /// Wraps the query in a single pair of double quotes for Graph's $search parameter.
    /// Surrounding quotes supplied by the caller are stripped so they don't nest,
    /// and any embedded quotes or backslashes are escaped.
    /// </summary>
    public static string Build(string query)
    {
        var term = query.Trim().Trim('"').Trim();
        var escaped = term.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"\"{escaped}\"";
    }
}
