namespace CalendarMcp.Auth;

/// <summary>
/// Reads and writes the admin console's own settings in appsettings.json: who may sign in, and
/// which identity providers are configured.
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

    /// <summary>
    /// Writes an identity provider's registration.
    ///
    /// A null or empty <paramref name="clientSecret"/> leaves any stored secret in place, so a
    /// settings form can round-trip without the secret ever being sent back to the browser.
    /// The value is stored exactly as given — callers are expected to encrypt it first.
    /// </summary>
    Task SetProviderAsync(
        string scheme,
        string authority,
        string clientId,
        string? clientSecret,
        string? displayName = null,
        CancellationToken ct = default);

    /// <summary>Removes a provider registration. Returns false when it was not configured.</summary>
    Task<bool> RemoveProviderAsync(string scheme, CancellationToken ct = default);

    /// <summary>
    /// Sets the tri-state token-login override. Null restores the automatic behaviour, where it
    /// is enabled only while no provider is configured.
    /// </summary>
    Task SetAllowTokenLoginAsync(bool? allow, CancellationToken ct = default);
}
