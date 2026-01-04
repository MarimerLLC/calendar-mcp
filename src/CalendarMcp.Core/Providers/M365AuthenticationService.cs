using CalendarMcp.Core.Services;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using Microsoft.Extensions.Logging;

namespace CalendarMcp.Core.Providers;

/// <summary>
/// M365 authentication service using MSAL with per-account token caching
/// </summary>
public class M365AuthenticationService : IM365AuthenticationService
{
    private readonly ILogger<M365AuthenticationService> _logger;
    private readonly Dictionary<string, IPublicClientApplication> _apps = new();

    public M365AuthenticationService(ILogger<M365AuthenticationService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<string> AuthenticateInteractiveAsync(
        string tenantId,
        string clientId,
        string[] scopes,
        string accountId,
        CancellationToken cancellationToken = default)
    {
        var app = await GetOrCreateAppAsync(tenantId, clientId, accountId);

        try
        {
            _logger.LogInformation("Starting interactive authentication for account {AccountId}...", accountId);
            _logger.LogInformation("A browser window will open for you to sign in.");

            var authResult = await app.AcquireTokenInteractive(scopes)
                .WithPrompt(Prompt.SelectAccount)
                .ExecuteAsync(cancellationToken);

            _logger.LogInformation("✓ Interactive authentication successful for account {AccountId}", accountId);
            return authResult.AccessToken;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Authentication cancelled for account {AccountId}", accountId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication failed for account {AccountId}: {Message}", accountId, ex.Message);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<string?> GetTokenSilentlyAsync(
        string tenantId,
        string clientId,
        string[] scopes,
        string accountId,
        CancellationToken cancellationToken = default)
    {
        var app = await GetOrCreateAppAsync(tenantId, clientId, accountId);

        try
        {
            var accounts = await app.GetAccountsAsync();
            if (!accounts.Any())
            {
                _logger.LogWarning("No cached account found for {AccountId}. Interactive authentication required.", accountId);
                return null;
            }

            _logger.LogDebug("Attempting silent authentication for account {AccountId}...", accountId);
            var result = await app.AcquireTokenSilent(scopes, accounts.First())
                .ExecuteAsync(cancellationToken);

            _logger.LogDebug("✓ Silent authentication successful for account {AccountId}", accountId);
            return result.AccessToken;
        }
        catch (MsalUiRequiredException)
        {
            _logger.LogWarning("Silent authentication failed for account {AccountId}. Interactive authentication required.", accountId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during silent authentication for account {AccountId}: {Message}", accountId, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Get or create an IPublicClientApplication for a specific account
    /// </summary>
    private async Task<IPublicClientApplication> GetOrCreateAppAsync(
        string tenantId,
        string clientId,
        string accountId)
    {
        // Use account ID as cache key
        if (_apps.TryGetValue(accountId, out var existingApp))
        {
            return existingApp;
        }

        // Create cache file name unique to this account
        var cacheFileName = $"msal_cache_{accountId}.bin";
        var cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CalendarMcp"
        );

        // Ensure directory exists
        Directory.CreateDirectory(cacheDirectory);

        // Build MSAL application
        var app = PublicClientApplicationBuilder
            .Create(clientId)
            .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
            .WithRedirectUri("http://localhost")
            .Build();

        // Set up token cache persistence
        var storageProperties = new StorageCreationPropertiesBuilder(
            cacheFileName,
            cacheDirectory)
            .Build();

        var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
        cacheHelper.RegisterCache(app.UserTokenCache);

        _logger.LogDebug("Initialized MSAL app for account {AccountId} with cache file {CacheFile}",
            accountId, cacheFileName);

        _apps[accountId] = app;
        return app;
    }
}
