using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Adjutant.HttpServer.Identity;

/// <summary>An MCP client the owner approved that can still reach the server.</summary>
/// <param name="ApplicationId">OpenIddict's id for the client; what <see cref="ConnectedApps.DisconnectAsync"/> takes.</param>
/// <param name="Name">The client's display name, as it registered.</param>
/// <param name="ClientId">The OAuth client_id: a metadata document URL, or a <c>dcr_</c> id.</param>
/// <param name="Publisher">For a metadata document client, the host that vouches for the name; null when the name is self-asserted.</param>
/// <param name="RedirectHosts">Hosts the client's registered redirect URIs point at.</param>
/// <param name="ApprovedBy">Who approved it on the consent page (the owner's address).</param>
/// <param name="ConnectedUtc">When it was first approved, of the approvals still in force.</param>
/// <param name="LastActiveUtc">When it last got a token (sign-in or refresh), so up to an access-token lifetime behind real use.</param>
internal sealed record ConnectedApp(
    string ApplicationId,
    string Name,
    string ClientId,
    string? Publisher,
    IReadOnlyList<string> RedirectHosts,
    IReadOnlyList<string> ApprovedBy,
    DateTimeOffset? ConnectedUtc,
    DateTimeOffset? LastActiveUtc);

/// <summary>
/// The MCP Access page's view of OAuth clients: which ones can still reach the server, and
/// Disconnect. A singleton that opens a scope per call, because a Blazor Server scope lasts as long
/// as the browser circuit and a DbContext shouldn't.
/// </summary>
internal sealed class ConnectedApps(IServiceScopeFactory scopes, TimeProvider time, ILogger<ConnectedApps> logger)
{
    /// <summary>
    /// Clients with an approval that hasn't been revoked and still holds an unexpired access or
    /// refresh token, so one that stopped refreshing drops off the list once its refresh token runs
    /// out. Several sign-ins of the same client are one entry.
    /// </summary>
    public async Task<IReadOnlyList<ConnectedApp>> ListAsync(CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OAuthDbContext>();

        // Access and refresh tokens only: an authorization code alone is a sign-in that was never
        // finished, not a connected app.
        var rows = await db.Set<OpenIddictEntityFrameworkCoreAuthorization>()
            .Where(a => a.Status == Statuses.Valid && a.Application != null &&
                        a.Tokens.Any(t => (t.Type == TokenTypeIdentifiers.AccessToken || t.Type == TokenTypeIdentifiers.RefreshToken) &&
                                          t.Status == Statuses.Valid && (t.ExpirationDate == null || t.ExpirationDate > now)))
            .Select(a => new
            {
                ApplicationId = a.Application!.Id!,
                a.Application.ClientId,
                a.Application.DisplayName,
                a.Application.Properties,
                a.Application.RedirectUris,
                a.Subject,
                a.CreationDate,
                LastToken = a.Tokens.Max(t => t.CreationDate),
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.ApplicationId)
            .Select(g =>
            {
                var first = g.First();
                return new ConnectedApp(
                    g.Key,
                    string.IsNullOrWhiteSpace(first.DisplayName) ? "An unnamed app" : first.DisplayName,
                    first.ClientId ?? "",
                    Publisher(first.ClientId, first.Properties),
                    RedirectHosts(first.RedirectUris),
                    g.Select(r => r.Subject).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList(),
                    Utc(g.Min(r => r.CreationDate)),
                    Utc(g.Max(r => r.LastToken)));
            })
            .OrderByDescending(a => a.LastActiveUtc)
            .ToList();
    }

    /// <summary>
    /// Revokes every approval the client holds and every token issued to it. Its access token stops
    /// working on the next request (validation checks the token and its approval) and its refresh token is refused.
    /// The client stays registered, so it can connect again through the consent page.
    /// Returns false when there is no such client.
    /// </summary>
    public async Task<bool> DisconnectAsync(string applicationId, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var application = await applications.FindByIdAsync(applicationId, cancellationToken);
        if (application is null)
            return false;

        // Validation checks both the token's own entry and its approval (McpOAuthSetup), so revoking
        // either is enough on its own; both are revoked so the database says what happened.
        await scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>()
            .RevokeByApplicationIdAsync(applicationId, cancellationToken);
        await scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>()
            .RevokeByApplicationIdAsync(applicationId, cancellationToken);

        logger.LogInformation(
            "Disconnected MCP client {ClientId} ({Name}): its approvals and tokens are revoked",
            await applications.GetClientIdAsync(application, cancellationToken),
            await applications.GetDisplayNameAsync(application, cancellationToken));
        return true;
    }

    /// <summary>The metadata document's host, which vouches for the client's name; null for dynamic registration.</summary>
    private static string? Publisher(string? clientId, string? properties)
    {
        if (string.IsNullOrEmpty(properties))
            return null;
        using var json = JsonDocument.Parse(properties);
        var isDocument = json.RootElement.TryGetProperty(ClientRegistration.RegistrationProperty, out var registration)
            && registration.ValueKind == JsonValueKind.String
            && registration.GetString() == ClientRegistration.MetadataDocument;
        return isDocument && Uri.TryCreate(clientId, UriKind.Absolute, out var uri) ? uri.Host : null;
    }

    private static List<string> RedirectHosts(string? redirectUris)
    {
        if (string.IsNullOrEmpty(redirectUris))
            return [];
        var uris = JsonSerializer.Deserialize<string[]>(redirectUris) ?? [];
        return uris
            .Select(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) ? uri : null)
            .OfType<Uri>()
            .Select(u => u.IsDefaultPort ? u.Host : $"{u.Host}:{u.Port}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static DateTimeOffset? Utc(DateTime? value) =>
        value is { } date ? new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)) : null;
}
