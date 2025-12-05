using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Util.Store;

namespace GoogleWorkspaceSpike;

public class GoogleConfig
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string[] Scopes { get; set; } = Array.Empty<string>();
}

public class GoogleAuthenticator
{
    private readonly GoogleConfig _config;
    private readonly ILogger<GoogleAuthenticator> _logger;
    private UserCredential? _credential;

    public GoogleAuthenticator(GoogleConfig config, ILogger<GoogleAuthenticator> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<UserCredential> GetCredentialAsync()
    {
        if (_credential != null)
        {
            _logger.LogInformation("Using existing Google credential");
            return _credential;
        }

        _logger.LogInformation("Initializing Google OAuth authentication...");
        _logger.LogInformation("User: {UserEmail}", _config.UserEmail);
        _logger.LogInformation("Scopes: {Scopes}", string.Join(", ", _config.Scopes));

        var secrets = new ClientSecrets
        {
            ClientId = _config.ClientId,
            ClientSecret = _config.ClientSecret
        };

        // Use FileDataStore to persist tokens locally
        var credPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".credentials",
            "google-workspace-spike"
        );

        _logger.LogInformation("Token cache path: {CredPath}", credPath);

        _credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets,
            _config.Scopes,
            _config.UserEmail,
            CancellationToken.None,
            new FileDataStore(credPath, true)
        );

        _logger.LogInformation("✓ Google authentication successful");
        return _credential;
    }

    public BaseClientService.Initializer GetServiceInitializer()
    {
        if (_credential == null)
        {
            throw new InvalidOperationException("Must call GetCredentialAsync first");
        }

        return new BaseClientService.Initializer
        {
            HttpClientInitializer = _credential,
            ApplicationName = "Google Workspace Spike"
        };
    }
}
