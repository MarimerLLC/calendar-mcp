using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using Microsoft.Extensions.Logging;

namespace CalendarMcp.Spikes.M365DirectAccess;

/// <summary>
/// Handles authentication for multiple M365 tenants using MSAL
/// </summary>
public class MultiTenantAuthenticator
{
    private readonly ILogger<MultiTenantAuthenticator> _logger;
    private readonly GraphConfig _graphConfig;
    private readonly Dictionary<string, IPublicClientApplication> _apps = new();
    private readonly Dictionary<string, TenantConfig> _tenants = new();

    private MultiTenantAuthenticator(
        ILogger<MultiTenantAuthenticator> logger,
        GraphConfig graphConfig,
        Dictionary<string, TenantConfig> tenants)
    {
        _logger = logger;
        _graphConfig = graphConfig;
        _tenants = tenants;
    }

    /// <summary>
    /// Create and initialize a new MultiTenantAuthenticator
    /// </summary>
    public static async Task<MultiTenantAuthenticator> CreateAsync(
        ILogger<MultiTenantAuthenticator> logger,
        GraphConfig graphConfig,
        Dictionary<string, TenantConfig> tenants)
    {
        var authenticator = new MultiTenantAuthenticator(logger, graphConfig, tenants);
        await authenticator.InitializeAsync();
        return authenticator;
    }

    private async Task InitializeAsync()
    {
        // Initialize MSAL apps for each tenant
        foreach (var kvp in _tenants.Where(t => t.Value.Enabled))
        {
            var tenantId = kvp.Key;
            var tenant = kvp.Value;
            
            // Use a unique cache file name to avoid conflicts with other spikes
            var cacheFileName = $"msal_cache_m365directaccess_{tenantId}.bin";
            var cacheFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CalendarMcp",
                cacheFileName
            );
            
            var app = PublicClientApplicationBuilder
                .Create(tenant.ClientId)
                .WithAuthority($"https://login.microsoftonline.com/{tenant.TenantId}")
                .WithRedirectUri("http://localhost")
                .Build();
            
            // Set up token cache persistence
            var storageProperties = new StorageCreationPropertiesBuilder(cacheFileName, 
                Path.GetDirectoryName(cacheFilePath))
                .Build();
            var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
            cacheHelper.RegisterCache(app.UserTokenCache);
                
            _apps[tenantId] = app;
            _logger.LogInformation("Initialized MSAL app for tenant: {TenantName}", tenant.Name);
        }
    }

    /// <summary>
    /// Authenticate to a specific tenant and get an access token
    /// </summary>
    public async Task<string> AuthenticateAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        if (!_apps.ContainsKey(tenantId))
        {
            throw new InvalidOperationException($"Tenant {tenantId} not configured");
        }

        var app = _apps[tenantId];
        var tenant = _tenants[tenantId];

        try
        {
            // Try to get token silently from cache first
            var accounts = await app.GetAccountsAsync();
            if (accounts.Any())
            {
                try
                {
                    _logger.LogInformation("Attempting silent authentication for {TenantName}...", tenant.Name);
                    var result = await app.AcquireTokenSilent(_graphConfig.Scopes, accounts.First())
                        .ExecuteAsync(cancellationToken);
                    _logger.LogInformation("✓ Silent authentication successful for {TenantName}", tenant.Name);
                    return result.AccessToken;
                }
                catch (MsalUiRequiredException)
                {
                    _logger.LogInformation("Silent authentication failed, falling back to interactive...");
                    // Silent acquisition failed, fall through to interactive
                }
            }

            // Perform interactive authentication
            _logger.LogInformation("Starting interactive authentication for {TenantName}...", tenant.Name);
            _logger.LogInformation("A browser window will open for you to sign in.");
            
            var authResult = await app.AcquireTokenInteractive(_graphConfig.Scopes)
                .WithPrompt(Prompt.SelectAccount)
                .ExecuteAsync(cancellationToken);

            _logger.LogInformation("✓ Interactive authentication successful for {TenantName}", tenant.Name);
            return authResult.AccessToken;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Authentication cancelled for {TenantName}", tenant.Name);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication failed for {TenantName}: {Message}", tenant.Name, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Get all configured tenant IDs
    /// </summary>
    public IEnumerable<string> GetTenantIds()
    {
        return _apps.Keys;
    }

    /// <summary>
    /// Get tenant configuration
    /// </summary>
    public TenantConfig GetTenantConfig(string tenantId)
    {
        return _tenants[tenantId];
    }
}
