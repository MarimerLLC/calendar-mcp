using CalendarMcp.Core.Models;

namespace CalendarMcp.Core.Services;

/// <summary>
/// Service for authenticating M365 accounts
/// </summary>
public interface IM365AuthenticationService
{
    /// <summary>
    /// Authenticate interactively (for CLI setup)
    /// </summary>
    /// <param name="tenantId">Azure AD tenant ID</param>
    /// <param name="clientId">Application client ID</param>
    /// <param name="scopes">Required scopes</param>
    /// <param name="accountId">Unique account identifier for token cache</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Access token</returns>
    Task<string> AuthenticateInteractiveAsync(
        string tenantId,
        string clientId,
        string[] scopes,
        string accountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get token silently from cache (for MCP server runtime)
    /// </summary>
    /// <param name="tenantId">Azure AD tenant ID</param>
    /// <param name="clientId">Application client ID</param>
    /// <param name="scopes">Required scopes</param>
    /// <param name="accountId">Unique account identifier for token cache</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Access token or null if not available</returns>
    Task<string?> GetTokenSilentlyAsync(
        string tenantId,
        string clientId,
        string[] scopes,
        string accountId,
        CancellationToken cancellationToken = default);
}
