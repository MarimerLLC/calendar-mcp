using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace CalendarMcp.HttpServer.Security;

/// <summary>
/// Rate limits the endpoints worth guessing at: console sign-in and the MCP endpoint.
///
/// Implemented as a partitioned global limiter rather than per-endpoint policies because the
/// sign-in and claim pages are Razor components served by a single <c>MapRazorComponents</c>
/// endpoint — there is nothing to attach a per-page policy to. Partitioning by path gives the
/// pages and the minimal-API endpoints one consistent rule.
/// </summary>
public static class AdminRateLimiting
{
    /// <summary>Sign-in attempts allowed per client per window. Generous for a person, useless for a script.</summary>
    private const int SignInPermitLimit = 10;

    /// <summary>MCP calls allowed per credential per window. An assistant session is bursty but nowhere near this.</summary>
    private const int McpPermitLimit = 240;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddAdminRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);
                }

                context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("CalendarMcp.RateLimit")
                    .LogWarning("Rate limited {Method} {Path} from {RemoteIp}",
                        context.HttpContext.Request.Method,
                        context.HttpContext.Request.Path,
                        context.HttpContext.Connection.RemoteIpAddress);

                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { error = "Too many requests. Slow down and try again shortly." },
                    cancellationToken);
            };

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (IsSignInPath(context.Request.Path))
                {
                    return RateLimitPartition.GetFixedWindowLimiter(
                        $"signin:{ClientKey(context)}",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = SignInPermitLimit,
                            Window = Window,
                            QueueLimit = 0
                        });
                }

                if (IsMcpPath(context.Request.Path))
                {
                    return RateLimitPartition.GetFixedWindowLimiter(
                        $"mcp:{McpClientKey(context)}",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = McpPermitLimit,
                            Window = Window,
                            QueueLimit = 0
                        });
                }

                return RateLimitPartition.GetNoLimiter("unlimited");
            });
        });

        return services;
    }

    /// <summary>
    /// Pages and endpoints where a wrong answer is worth retrying: the console login, the claim
    /// page, and the endpoints that start or complete a provider sign-in.
    /// </summary>
    private static bool IsSignInPath(PathString path) =>
        path.StartsWithSegments("/admin/ui/login") ||
        path.StartsWithSegments("/admin/ui/claim") ||
        path.StartsWithSegments("/admin/auth");

    /// <summary>
    /// The MCP surface: the protocol endpoints at the root plus the attachment endpoints.
    /// </summary>
    private static bool IsMcpPath(PathString path) =>
        path == "/" ||
        path.StartsWithSegments("/sse") ||
        path.StartsWithSegments("/message") ||
        path.StartsWithSegments("/attachments");

    /// <summary>
    /// Partition key for sign-in attempts. The remote IP is what
    /// <c>UseForwardedHeaders</c> resolved it to — with the default <c>ForwardLimit</c> of 1
    /// that is the entry appended by the nearest proxy, not one a client can put there itself.
    /// </summary>
    private static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>
    /// Partition key for MCP calls: the presented API key when there is one, so a single noisy
    /// client cannot exhaust the budget of every other client behind the same address. Requests
    /// with no credential fall back to the address — they are about to be rejected anyway, and
    /// limiting them is the point.
    /// </summary>
    private static string McpClientKey(HttpContext context)
    {
        var presented = context.Request.Headers.Authorization.FirstOrDefault()
            ?? context.Request.Headers[McpApiKeyHandler.ApiKeyHeader].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(presented))
            return $"anon:{ClientKey(context)}";

        // Hashed so the credential is never used as a dictionary key or written to a log line.
        return "key:" + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(presented)))[..16];
    }
}
