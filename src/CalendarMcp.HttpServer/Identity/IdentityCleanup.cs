using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;

namespace CalendarMcp.HttpServer.Identity;

/// <summary>
/// Hourly housekeeping for OAuth data nobody will use again: tokens and authorizations that lapsed
/// over two weeks ago (OpenIddict's pruning), and dynamically registered clients that never got a
/// token. Anyone can register a client, so without this they would pile up. Ported from
/// MarimerLLC/scheduler.
/// </summary>
internal sealed class IdentityCleanup(
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<IdentityCleanup> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>A registered client with no token or authorization after this long is deleted.</summary>
    public static readonly TimeSpan UnusedClientLifetime = TimeSpan.FromDays(1);

    /// <summary>Lapsed tokens and authorizations are kept this long (OpenIddict's own default), for troubleshooting.</summary>
    private static readonly TimeSpan PruneAfter = TimeSpan.FromDays(14);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "OAuth cleanup failed; retrying next hour");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One pass. Returns the number of unused clients deleted.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await using var scope = scopes.CreateAsyncScope();

        var threshold = now - PruneAfter;
        var tokens = await scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>().PruneAsync(threshold, cancellationToken);
        var authorizations = await scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>().PruneAsync(threshold, cancellationToken);
        if (tokens + authorizations > 0)
            logger.LogInformation("Pruned {Tokens} OAuth tokens and {Authorizations} authorizations", tokens, authorizations);

        var db = scope.ServiceProvider.GetRequiredService<OAuthDbContext>();
        var clients = await DeleteUnusedClientsAsync(db, now - UnusedClientLifetime, cancellationToken);
        if (clients > 0)
            logger.LogInformation("Deleted {Count} dynamically registered MCP clients that were never used", clients);
        return clients;
    }

    /// <summary>
    /// Dynamically registered clients with no authorization and no token that registered before
    /// <paramref name="registeredBefore"/>. Metadata document clients aren't touched: they are
    /// fetched again whenever they are used.
    /// </summary>
    private static async Task<int> DeleteUnusedClientsAsync(OAuthDbContext db, DateTimeOffset registeredBefore, CancellationToken cancellationToken)
    {
        var applications = db.Set<OpenIddictEntityFrameworkCoreApplication>();
        var unused = await applications
            .Where(a => a.ClientId!.StartsWith(DynamicClientRegistration.ClientIdPrefix) && !a.Authorizations.Any() && !a.Tokens.Any())
            .Select(a => new { a.Id, a.Properties })
            .ToListAsync(cancellationToken);

        var ids = unused.Where(a => RegisteredAt(a.Properties) is not { } at || at < registeredBefore).Select(a => a.Id).ToList();
        if (ids.Count == 0)
            return 0;

        // Checked again in the delete itself, in case a client signed someone in since the query above.
        return await applications
            .Where(a => ids.Contains(a.Id) && !a.Authorizations.Any() && !a.Tokens.Any())
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static DateTimeOffset? RegisteredAt(string? properties)
    {
        if (string.IsNullOrEmpty(properties))
            return null;
        using var json = System.Text.Json.JsonDocument.Parse(properties);
        return json.RootElement.TryGetProperty(DynamicClientRegistration.RegisteredAtProperty, out var value)
            ? ClientRegistration.ParseTimestamp(value)
            : null;
    }
}
