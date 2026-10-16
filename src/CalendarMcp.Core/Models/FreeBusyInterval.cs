namespace CalendarMcp.Core.Models;

/// <summary>A busy interval returned by an Exchange free/busy lookup.</summary>
public sealed class FreeBusyInterval
{
    public required string Email { get; init; }
    public required DateTimeOffset Start { get; init; }
    public required DateTimeOffset End { get; init; }
    public string Status { get; init; } = "busy";
    public string? Subject { get; init; }
}
