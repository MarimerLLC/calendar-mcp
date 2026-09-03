using CalendarMcp.Core.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace CalendarMcp.Core.Configuration;

/// <summary>
/// Registers ASP.NET DataProtection with a keystore persisted under the
/// Adjutant data directory. Each host (HttpServer, StdioServer, Cli)
/// calls this so any host can encrypt or decrypt values produced by another,
/// as long as they share the same data directory (which is the contract
/// established by <see cref="ConfigurationPaths.GetDataDirectory"/>).
/// </summary>
public static class DataProtectionExtensions
{
    /// <summary>Application name passed to DataProtection. Constant across hosts.</summary>
    public const string ApplicationName = "CalendarMcp";

    public static IServiceCollection AddCalendarMcpDataProtection(this IServiceCollection services)
    {
        var keysDir = ConfigurationPaths.GetDataProtectionKeysDirectory();
        Directory.CreateDirectory(keysDir);

        services
            .AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(keysDir));

        services.AddSingleton<PasswordProtector>();

        return services;
    }
}
