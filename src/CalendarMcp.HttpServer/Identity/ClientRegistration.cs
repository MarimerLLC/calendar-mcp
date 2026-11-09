using System.Globalization;
using System.Text;
using System.Text.Json;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace CalendarMcp.HttpServer.Identity;

/// <summary>
/// Rules shared by the two ways an MCP client registers with the authorization server: a client ID
/// metadata document (the client_id is an HTTPS URL) or dynamic client registration (RFC 7591, kept
/// for older clients). Either way the client is public (PKCE, no secret) and every sign-in shows the
/// consent page. Ported from MarimerLLC/scheduler.
/// </summary>
internal static class ClientRegistration
{
    /// <summary>Application property recording how a client registered: <see cref="MetadataDocument"/> or <see cref="Dynamic"/>.</summary>
    public const string RegistrationProperty = "registration";
    public const string MetadataDocument = "metadata_document";
    public const string Dynamic = "dynamic";

    /// <summary>Application property: when a metadata document client must be fetched again (ISO 8601).</summary>
    public const string RefreshAfterProperty = "refresh_after";

    public const int MaxRedirectUris = 10;
    public const int MaxUriLength = 2000;
    public const int MaxNameLength = 100;

    /// <summary>
    /// Every redirect URI must be absolute, without a fragment, and either HTTPS or HTTP on a loopback
    /// host (MCP authorization, "Communication Security"). Returns the parsed URIs, or the reason
    /// they're refused.
    /// </summary>
    public static (IReadOnlyList<Uri> Uris, string? Error) ParseRedirectUris(IReadOnlyList<string?>? values)
    {
        if (values is null || values.Count == 0)
            return ([], "redirect_uris must list at least one redirect URI.");
        if (values.Count > MaxRedirectUris)
            return ([], $"redirect_uris may list at most {MaxRedirectUris} URIs.");

        var uris = new List<Uri>();
        foreach (var value in values)
        {
            if (string.IsNullOrEmpty(value) || value.Length > MaxUriLength || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
                return ([], $"'{Truncate(value)}' isn't an absolute URI.");
            if (!string.IsNullOrEmpty(uri.Fragment))
                return ([], $"'{Truncate(value)}' has a fragment; redirect URIs can't.");
            var secure = uri.Scheme == Uri.UriSchemeHttps;
            var loopback = uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
            if (!secure && !loopback)
                return ([], $"'{Truncate(value)}' must use HTTPS, or HTTP on localhost.");
            uris.Add(uri);
        }
        return (uris, null);
    }

    /// <summary>
    /// A client-supplied name, safe to show on the consent page: control characters removed,
    /// whitespace collapsed, length limited. It is still whatever the client claims to be.
    /// </summary>
    public static string? CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var builder = new StringBuilder(name.Length);
        foreach (var c in name.Trim())
        {
            if (char.IsControl(c) || char.IsWhiteSpace(c))
            {
                if (builder.Length > 0 && builder[^1] != ' ')
                    builder.Append(' ');
            }
            else
            {
                builder.Append(c);
            }
        }
        var cleaned = builder.ToString().Trim();
        if (cleaned.Length == 0)
            return null;
        return cleaned.Length <= MaxNameLength ? cleaned : cleaned[..MaxNameLength];
    }

    /// <summary>The OpenIddict application for an MCP client: public, PKCE required, explicit consent.</summary>
    public static OpenIddictApplicationDescriptor Descriptor(string clientId, string displayName, IReadOnlyList<Uri> redirectUris, string registration)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientType = ClientTypes.Public,
            // Native: a port-less loopback redirect URI then matches any port (RFC 8252, section 7.3).
            ApplicationType = redirectUris.Any(u => u.IsLoopback) ? ApplicationTypes.Native : ApplicationTypes.Web,
            ConsentType = ConsentTypes.Explicit,
            DisplayName = displayName,
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.Revocation,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Prefixes.Scope + McpResource.Scope,
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };
        foreach (var uri in redirectUris)
            descriptor.RedirectUris.Add(uri);
        descriptor.Properties[RegistrationProperty] = JsonSerializer.SerializeToElement(registration);
        return descriptor;
    }

    /// <summary>A timestamp as stored in an application property.</summary>
    public static JsonElement Timestamp(DateTimeOffset value) =>
        JsonSerializer.SerializeToElement(value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));

    /// <summary>Reads a timestamp written by <see cref="Timestamp"/>, or null.</summary>
    public static DateTimeOffset? ParseTimestamp(JsonElement value) =>
        value.ValueKind == JsonValueKind.String &&
        DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static string Truncate(string? value) =>
        value is null ? "" : value.Length <= 100 ? value : value[..100] + "…";
}
