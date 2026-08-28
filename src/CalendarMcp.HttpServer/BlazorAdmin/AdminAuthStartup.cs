using CalendarMcp.Core.Configuration;
using CalendarMcp.Core.Security;
using Microsoft.Extensions.Options;

namespace CalendarMcp.HttpServer.BlazorAdmin;

/// <summary>
/// Startup-time reporting and claim-code issuance for admin console sign-in.
/// </summary>
public static class AdminAuthStartup
{
    /// <summary>
    /// Issues a claim code when no allow-list exists, and logs what the login page will offer,
    /// so an operator can tell from the startup log alone how to get in.
    /// </summary>
    public static void ConfigureAdminAuth(this WebApplication app)
    {
        var config = app.Services.GetRequiredService<IOptionsMonitor<AdminAuthConfiguration>>().CurrentValue;
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("CalendarMcp.AdminAuth");

        var providers = config.ConfiguredProviderSchemes().ToList();
        if (providers.Count > 0)
            logger.LogInformation("Admin console sign-in providers: {Providers}", string.Join(", ", providers));
        else
            logger.LogInformation("No admin console sign-in providers are configured.");

        if (config.IsTokenLoginAllowed())
        {
            logger.LogWarning(
                "Admin token login is enabled on the console login page. It is the break-glass path " +
                "and resolves off automatically once a sign-in provider is configured; set " +
                "AdminAuth:AllowTokenLogin explicitly to override.");
        }

        if (config.AllowedEmails.Count > 0)
        {
            logger.LogInformation(
                "Admin console allow-list has {Count} entr(ies).", config.AllowedEmails.Count);
            return;
        }

        // Issued whenever the allow-list is empty, even with no provider configured yet: a
        // provider can be added later without a restart, and the code must already exist when
        // that first sign-in arrives. On its own it grants nothing — it is only accepted
        // alongside an identity a provider has already verified.
        var claimCode = app.Services.GetRequiredService<IAdminClaimCodeService>();
        var code = claimCode.Issue();

        logger.LogWarning("========================================================================");
        logger.LogWarning("No admin console allow-list is configured yet.");
        logger.LogWarning("Sign in with a provider, then enter this code to claim the server:");
        logger.LogWarning("    Claim code: {Code}", code);
        logger.LogWarning("It is single-use, also written to {File} in the data directory,", AdminClaimCodeService.FileName);
        logger.LogWarning("and a new one is issued each time the server starts unclaimed.");
        logger.LogWarning("========================================================================");
    }
}
