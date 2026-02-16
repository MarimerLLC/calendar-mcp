namespace CalendarMcp.Core.Models;

/// <summary>
/// Result of an unsubscribe operation
/// </summary>
public class UnsubscribeResult
{
    public bool Success { get; init; }

    /// <summary>
    /// Method used: "one-click", "https", "mailto"
    /// </summary>
    public string Method { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string? ErrorDetails { get; init; }
}
