using CalendarMcp.Core.Configuration;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Options;

namespace CalendarMcp.HttpServer.Identity;

/// <summary>
/// The MCP endpoint as an OAuth protected resource. MCP is mapped at the root, so the canonical
/// resource URI is the server's origin (<c>https://host/</c>), and that origin is also the
/// authorization server's issuer.
///
/// <c>CalendarMcp:ExternalBaseUrl</c> wins when it is set, as it already does for the admin
/// sign-in redirect URIs; otherwise the origin comes from the request (scheme, host and path base
/// after forwarded headers), the way OpenIddict infers its issuer, so the two always agree.
/// </summary>
internal static class McpResource
{
    /// <summary>The one scope this server grants; it covers every tool.</summary>
    public const string Scope = "adjutant";

    /// <summary>RFC 9728 protected resource metadata for the root resource.</summary>
    public const string ResourceMetadataPath = "/.well-known/oauth-protected-resource";

    /// <summary>The canonical MCP server URI, with a trailing slash (what URL parsers produce for an origin).</summary>
    public static string Canonical(HttpRequest request) =>
        Canonical(request, request.HttpContext.RequestServices
            .GetRequiredService<IOptionsMonitor<CalendarMcpConfiguration>>().CurrentValue.ExternalBaseUrl);

    /// <summary>The canonical MCP server URI for a request, given the configured external base URL.</summary>
    public static string Canonical(HttpRequest request, string? externalBaseUrl) =>
        ConfiguredOrigin(externalBaseUrl)
        ?? UriHelper.BuildAbsolute(request.Scheme, request.Host, request.PathBase, "/");

    /// <summary>The URL of the protected resource metadata document.</summary>
    public static string ResourceMetadataUrl(HttpRequest request) =>
        Canonical(request).TrimEnd('/') + ResourceMetadataPath;

    /// <summary>
    /// True when <paramref name="resource"/> names this server: the canonical URI with or without
    /// its trailing slash, since clients differ on that for a server at the root.
    /// </summary>
    public static bool IsThisResource(string? resource, string canonical) =>
        resource is not null &&
        (string.Equals(resource, canonical, StringComparison.Ordinal) ||
         string.Equals(resource, canonical.TrimEnd('/'), StringComparison.Ordinal));

    /// <summary>
    /// The origin declared by <c>ExternalBaseUrl</c>, normalized to end with a slash, or null when it
    /// is unset or not an absolute URL.
    /// </summary>
    public static string? ConfiguredOrigin(string? externalBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(externalBaseUrl) ||
            !Uri.TryCreate(externalBaseUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/";
    }
}
