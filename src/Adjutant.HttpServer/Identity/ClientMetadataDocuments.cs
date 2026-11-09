using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenIddict.Abstractions;

namespace Adjutant.HttpServer.Identity;

/// <summary>
/// OAuth Client ID Metadata Documents (draft-ietf-oauth-client-id-metadata-document, the MCP default
/// for new clients): an MCP client's client_id is an HTTPS URL, and the document there lists its name
/// and redirect URIs. On first use, and again once the cached copy is stale, the document is fetched,
/// checked, and stored as an OpenIddict application, so OpenIddict's own redirect URI and PKCE checks
/// apply unchanged. Ported from MarimerLLC/scheduler.
/// </summary>
internal sealed partial class ClientMetadataDocuments(
    IHttpClientFactory httpClientFactory,
    IOpenIddictApplicationManager applications,
    TimeProvider time,
    ILogger<ClientMetadataDocuments> logger)
{
    public const string HttpClientName = "client-metadata-documents";
    public const int MaxDocumentBytes = 5 * 1024;
    public const int MaxClientIdLength = 2000;

    private static readonly TimeSpan DefaultCacheTime = TimeSpan.FromHours(1);
    private static readonly TimeSpan MinCacheTime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxCacheTime = TimeSpan.FromHours(24);

    /// <summary>Whether a client_id is meant as a metadata document URL (as opposed to a registered id).</summary>
    public static bool IsMetadataDocumentClientId(string? clientId) =>
        clientId is not null && clientId.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Makes sure an up-to-date application exists for a metadata document client_id. Returns null
    /// when it does, or the reason the client can't be used.
    /// </summary>
    public async Task<string?> EnsureRegisteredAsync(string clientId, CancellationToken cancellationToken)
    {
        if (ValidateClientIdUrl(clientId) is { } urlError)
            return urlError;

        var existing = await applications.FindByClientIdAsync(clientId, cancellationToken);
        var now = time.GetUtcNow();
        if (existing is not null && !await IsStaleAsync(existing, now, cancellationToken))
            return null;

        var (document, cacheTime, fetchError) = await FetchAsync(new Uri(clientId), cancellationToken);
        if (fetchError is not null)
        {
            LogRejected(logger, clientId, fetchError);
            return fetchError;
        }

        if (ValidateDocument(clientId, document!) is { } documentError)
        {
            LogRejected(logger, clientId, documentError);
            return documentError;
        }

        var (redirectUris, _) = ClientRegistration.ParseRedirectUris(document!.RedirectUris);
        var descriptor = ClientRegistration.Descriptor(
            clientId, ClientRegistration.CleanName(document.ClientName)!, redirectUris, ClientRegistration.MetadataDocument);
        descriptor.Properties[ClientRegistration.RefreshAfterProperty] = ClientRegistration.Timestamp(now + cacheTime);

        if (existing is null)
        {
            try
            {
                await applications.CreateAsync(descriptor, cancellationToken);
            }
            catch (Exception)
            {
                // A concurrent request for the same new client may have stored it first (the
                // client_id is unique); its copy is just as fresh. Anything else is a real failure.
                if (await applications.FindByClientIdAsync(clientId, cancellationToken) is null)
                    throw;
            }
        }
        else
        {
            await applications.UpdateAsync(existing, descriptor, cancellationToken);
        }
        LogRegistered(logger, clientId, descriptor.DisplayName!);
        return null;
    }

    /// <summary>
    /// The client_id URL itself: HTTPS, a path, no fragment, no credentials, no dot segments
    /// (draft-ietf-oauth-client-id-metadata-document, section 3).
    /// </summary>
    internal static string? ValidateClientIdUrl(string clientId)
    {
        if (clientId.Length > MaxClientIdLength || !Uri.TryCreate(clientId, UriKind.Absolute, out var uri))
            return "The client_id isn't a valid URL.";
        if (uri.Scheme != Uri.UriSchemeHttps)
            return "A URL client_id must use HTTPS.";
        if (uri.AbsolutePath is "" or "/")
            return "A URL client_id must have a path, e.g. https://example.com/client.json.";
        if (!string.IsNullOrEmpty(uri.UserInfo) || clientId.Contains('#'))
            return "A URL client_id can't contain credentials or a fragment.";
        // Uri normalizes dot segments away, so look at the original text.
        var path = clientId[(clientId.IndexOf("//", StringComparison.Ordinal) + 2)..];
        if (path.Split('?')[0].Split('/').Any(segment => segment is "." or ".."))
            return "A URL client_id can't contain . or .. path segments.";
        return null;
    }

    internal static string? ValidateDocument(string clientId, ClientMetadataDocument document)
    {
        if (!string.Equals(document.ClientId, clientId, StringComparison.Ordinal))
            return "The metadata document's client_id doesn't match its URL.";
        if (ClientRegistration.CleanName(document.ClientName) is null)
            return "The metadata document has no client_name.";
        if (document.ClientSecret is not null || document.ClientSecretExpiresAt is not null)
            return "A metadata document must not contain a client secret.";
        if (document.TokenEndpointAuthMethod is not (null or "none"))
            return $"Token endpoint authentication '{document.TokenEndpointAuthMethod}' isn't supported; only public clients (none) are.";
        if (document.GrantTypes is { } grants && !grants.Contains("authorization_code"))
            return "The client must use the authorization_code grant.";
        if (document.ResponseTypes is { } responses && !responses.Contains("code"))
            return "The client must use the code response type.";
        return ClientRegistration.ParseRedirectUris(document.RedirectUris).Error;
    }

    private async Task<bool> IsStaleAsync(object application, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var properties = await applications.GetPropertiesAsync(application, cancellationToken);
        if (!properties.TryGetValue(ClientRegistration.RegistrationProperty, out var registration)
            || registration.GetString() != ClientRegistration.MetadataDocument)
        {
            // Some other kind of client happens to have an https:// id; it isn't ours to refresh.
            return false;
        }
        return !properties.TryGetValue(ClientRegistration.RefreshAfterProperty, out var refreshAfter)
            || ClientRegistration.ParseTimestamp(refreshAfter) is not { } parsed
            || parsed <= now;
    }

    private async Task<(ClientMetadataDocument? Document, TimeSpan CacheTime, string? Error)> FetchAsync(Uri url, CancellationToken cancellationToken)
    {
        var http = httpClientFactory.CreateClient(HttpClientName);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK)
                return (null, default, $"Fetching the client's metadata document returned HTTP {(int)response.StatusCode}.");
            if (response.Content.Headers.ContentLength > MaxDocumentBytes)
                return (null, default, "The client's metadata document is too large.");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[MaxDocumentBytes + 1];
            var length = 0;
            int read;
            while (length < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken)) > 0)
                length += read;
            if (length > MaxDocumentBytes)
                return (null, default, "The client's metadata document is too large.");

            var document = JsonSerializer.Deserialize<ClientMetadataDocument>(buffer.AsSpan(0, length));
            if (document is null)
                return (null, default, "The client's metadata document is empty.");
            return (document, CacheTime(response.Headers.CacheControl), null);
        }
        catch (JsonException)
        {
            return (null, default, "The client's metadata document isn't valid JSON.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogFetchFailed(logger, url, ex);
            return (null, default, "The client's metadata document couldn't be fetched.");
        }
    }

    /// <summary>How long to trust a fetched document: its max-age, held between 5 minutes and 24 hours.</summary>
    private static TimeSpan CacheTime(CacheControlHeaderValue? cacheControl)
    {
        if (cacheControl is null)
            return DefaultCacheTime;
        if (cacheControl.NoStore || cacheControl.NoCache)
            return MinCacheTime;
        if (cacheControl.MaxAge is not { } maxAge)
            return DefaultCacheTime;
        return maxAge < MinCacheTime ? MinCacheTime : maxAge > MaxCacheTime ? MaxCacheTime : maxAge;
    }

    /// <summary>
    /// The HTTP handler for fetching metadata documents: no redirects, short timeouts, and
    /// connections only to public addresses, so a client_id can't make the server probe its own
    /// network (SSRF). The address check runs on the resolved IP at connect time, which also defeats
    /// DNS rebinding.
    /// </summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectCallback = async (context, cancellationToken) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
            var allowed = addresses.Where(IsPublicAddress).ToArray();
            if (allowed.Length == 0)
                throw new HttpRequestException($"{context.DnsEndPoint.Host} doesn't resolve to a public address.");

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    /// <summary>False for loopback, private, link-local, carrier-grade NAT (Tailscale), multicast and reserved ranges.</summary>
    internal static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] is 0 or 10 or 127 or >= 224
                || (b[0] == 100 && b[1] is >= 64 and <= 127)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 172 && b[1] is >= 16 and <= 31)
                || (b[0] == 192 && b[1] == 0 && b[2] is 0 or 2)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 198 && b[1] is 18 or 19)
                || (b[0] == 198 && b[1] == 51 && b[2] == 100)
                || (b[0] == 203 && b[1] == 0 && b[2] == 113));
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.IPv6None)
                || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
                return false;
            var b = address.GetAddressBytes();
            // fc00::/7 unique local; 2001:db8::/32 documentation.
            return (b[0] & 0xFE) != 0xFC && !(b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8);
        }

        return false;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Client metadata document {ClientId} accepted as '{ClientName}'")]
    private static partial void LogRegistered(ILogger logger, string clientId, string clientName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client metadata document {ClientId} rejected: {Reason}")]
    private static partial void LogRejected(ILogger logger, string clientId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client metadata document {Url} couldn't be fetched")]
    private static partial void LogFetchFailed(ILogger logger, Uri url, Exception exception);
}

/// <summary>The fields of a client ID metadata document (RFC 7591 client metadata) that this server reads.</summary>
internal sealed class ClientMetadataDocument
{
    [JsonPropertyName("client_id")] public string? ClientId { get; set; }
    [JsonPropertyName("client_name")] public string? ClientName { get; set; }
    [JsonPropertyName("redirect_uris")] public List<string?>? RedirectUris { get; set; }
    [JsonPropertyName("grant_types")] public List<string>? GrantTypes { get; set; }
    [JsonPropertyName("response_types")] public List<string>? ResponseTypes { get; set; }
    [JsonPropertyName("token_endpoint_auth_method")] public string? TokenEndpointAuthMethod { get; set; }
    [JsonPropertyName("client_secret")] public string? ClientSecret { get; set; }
    [JsonPropertyName("client_secret_expires_at")] public long? ClientSecretExpiresAt { get; set; }
}
