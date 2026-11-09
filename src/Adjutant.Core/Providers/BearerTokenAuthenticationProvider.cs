using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;

namespace Adjutant.Core.Providers;

/// <summary>
/// Simple authentication provider that uses a bearer token for Microsoft Graph API calls
/// </summary>
public class BearerTokenAuthenticationProvider : IAuthenticationProvider
{
    private readonly string _accessToken;

    public BearerTokenAuthenticationProvider(string accessToken)
    {
        _accessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
    }

    public Task AuthenticateRequestAsync(
        RequestInformation request, 
        Dictionary<string, object>? additionalAuthenticationContext = null, 
        CancellationToken cancellationToken = default)
    {
        request.Headers.Add("Authorization", $"Bearer {_accessToken}");
        return Task.CompletedTask;
    }
}
