using Adjutant.Core.Models;

namespace Adjutant.Core.Services;

/// <summary>
/// Service for authenticating M365 accounts
/// </summary>
public interface IM365AuthenticationService
{
    /// <summary>
    /// Authenticate interactively via browser popup (for M365 organizational accounts)
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
    /// Authenticate using Device Code Flow (for Outlook.com/MSA personal accounts)
    /// </summary>
    /// <param name="tenantId">Azure AD tenant ID (typically "consumers" for MSA)</param>
    /// <param name="clientId">Application client ID</param>
    /// <param name="scopes">Required scopes</param>
    /// <param name="accountId">Unique account identifier for token cache</param>
    /// <param name="deviceCodeCallback">Callback to display the device code to user</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Access token</returns>
    Task<string> AuthenticateWithDeviceCodeAsync(
        string tenantId,
        string clientId,
        string[] scopes,
        string accountId,
        Func<string, Task> deviceCodeCallback,
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
