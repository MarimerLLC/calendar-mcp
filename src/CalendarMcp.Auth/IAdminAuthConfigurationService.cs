namespace CalendarMcp.Auth;

/// <summary>
/// Reads and writes the admin console allow-list in appsettings.json.
/// </summary>
public interface IAdminAuthConfigurationService
{
    /// <summary>
    /// Adds an address to the allow-list. Returns false when it was already present.
    /// </summary>
    Task<bool> AddAllowedEmailAsync(string email, CancellationToken ct = default);

    /// <summary>
    /// Removes an address from the allow-list. Returns false when it was not present.
    /// </summary>
    Task<bool> RemoveAllowedEmailAsync(string email, CancellationToken ct = default);

    /// <summary>Reads the allow-list straight from disk.</summary>
    Task<IReadOnlyList<string>> GetAllowedEmailsAsync(CancellationToken ct = default);
}
