namespace Adjutant.Core.Models;

/// <summary>
/// Represents an available time slot across calendars
/// </summary>
public class TimeSlot
{
    /// <summary>
    /// Slot start time
    /// </summary>
    public required DateTime Start { get; init; }
    
    /// <summary>
    /// Slot end time
    /// </summary>
    public required DateTime End { get; init; }
    
    /// <summary>
    /// Whether all requested accounts are free during this slot
    /// </summary>
    public bool AllAccountsFree { get; init; }
    
    /// <summary>
    /// List of account IDs that have conflicts during this slot
    /// </summary>
    public List<string> BusyAccounts { get; init; } = new();
}
