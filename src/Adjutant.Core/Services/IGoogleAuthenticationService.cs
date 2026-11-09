namespace Adjutant.Core.Services;

/// <summary>
/// Service for authenticating Google Workspace/Gmail accounts
/// </summary>
public interface IGoogleAuthenticationService
{
    /// <summary>
    /// Authenticate interactively (for CLI setup)
    /// </summary>
    /// <param name="clientId">Google OAuth client ID</param>
    /// <param name="clientSecret">Google OAuth client secret</param>
    /// <param name="scopes">Required scopes</param>
    /// <param name="accountId">Unique account identifier for token cache</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if authentication was successful</returns>
    Task<bool> AuthenticateInteractiveAsync(
        string clientId,
        string clientSecret,
        string[] scopes,
        string accountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get credential silently from cache (for MCP server runtime)
    /// </summary>
    /// <param name="clientId">Google OAuth client ID</param>
    /// <param name="clientSecret">Google OAuth client secret</param>
    /// <param name="scopes">Required scopes</param>
    /// <param name="accountId">Unique account identifier for token cache</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if valid credential exists, false if interactive authentication required</returns>
    Task<bool> HasValidCredentialAsync(
        string clientId,
        string clientSecret,
        string[] scopes,
        string accountId,
        CancellationToken cancellationToken = default);
}
