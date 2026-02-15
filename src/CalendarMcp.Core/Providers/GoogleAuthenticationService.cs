using CalendarMcp.Core.Services;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Logging;

namespace CalendarMcp.Core.Providers;

/// <summary>
/// Google authentication service using OAuth 2.0 with per-account token caching
/// </summary>
public class GoogleAuthenticationService : IGoogleAuthenticationService
{
    private readonly ILogger<GoogleAuthenticationService> _logger;

    public GoogleAuthenticationService(ILogger<GoogleAuthenticationService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<bool> AuthenticateInteractiveAsync(
        string clientId,
        string clientSecret,
        string[] scopes,
        string accountId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting interactive Google authentication for account {AccountId}...", accountId);
            _logger.LogInformation("A browser window will open for you to sign in.");

            var secrets = new ClientSecrets
            {
                ClientId = clientId,
                ClientSecret = clientSecret
            };

            var credPath = GetCredentialPath(accountId);
            _logger.LogDebug("Token cache path: {CredPath}", credPath);

            // Use "user" as the user identifier since we're isolating by accountId directory
            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets,
                scopes,
                "user",
                cancellationToken,
                new FileDataStore(credPath, true)
            );

            _logger.LogInformation("✓ Interactive Google authentication successful for account {AccountId}", accountId);
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Google authentication cancelled for account {AccountId}", accountId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google authentication failed for account {AccountId}: {Message}", accountId, ex.Message);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> HasValidCredentialAsync(
        string clientId,
        string clientSecret,
        string[] scopes,
        string accountId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var credPath = GetCredentialPath(accountId);
            
            // Check if token file exists
            var tokenFile = Path.Combine(credPath, "Google.Apis.Auth.OAuth2.Responses.TokenResponse-user");
            if (!File.Exists(tokenFile))
            {
                _logger.LogDebug("No cached credential found for Google account {AccountId}", accountId);
                return false;
            }

            var secrets = new ClientSecrets
            {
                ClientId = clientId,
                ClientSecret = clientSecret
            };

            _logger.LogDebug("Checking cached credential for Google account {AccountId}...", accountId);

            // Try to load existing credential - this will refresh if needed
            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets,
                scopes,
                "user",
                cancellationToken,
                new FileDataStore(credPath, true)
            );

            // Check if the token is valid (not expired or can be refreshed)
            if (credential.Token.IsStale)
            {
                // Try to refresh
                var refreshed = await credential.RefreshTokenAsync(cancellationToken);
                if (!refreshed)
                {
                    _logger.LogWarning("Failed to refresh Google token for account {AccountId}", accountId);
                    return false;
                }
            }

            _logger.LogDebug("✓ Valid Google credential found for account {AccountId}", accountId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Error checking Google credential for account {AccountId}: {Message}", accountId, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Get the credential storage path for a specific account
    /// </summary>
    private static string GetCredentialPath(string accountId)
    {
        return Configuration.ConfigurationPaths.GetGoogleCredentialsDirectory(accountId);
    }
}
