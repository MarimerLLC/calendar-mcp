using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;

namespace CalendarMcp.Core.Providers;

/// <summary>
/// Factory for resolving provider services based on account type
/// </summary>
public class ProviderServiceFactory : IProviderServiceFactory
{
    private readonly IM365ProviderService _m365Provider;
    private readonly IGoogleProviderService _googleProvider;
    private readonly IOutlookComProviderService _outlookProvider;
    private readonly IIcsProviderService _icsProvider;
    private readonly IJsonCalendarProviderService _jsonProvider;
    private readonly ILogger<ProviderServiceFactory> _logger;

    public ProviderServiceFactory(
        IM365ProviderService m365Provider,
        IGoogleProviderService googleProvider,
        IOutlookComProviderService outlookProvider,
        IIcsProviderService icsProvider,
        IJsonCalendarProviderService jsonProvider,
        ILogger<ProviderServiceFactory> logger)
    {
        _m365Provider = m365Provider;
        _googleProvider = googleProvider;
        _outlookProvider = outlookProvider;
        _icsProvider = icsProvider;
        _jsonProvider = jsonProvider;
        _logger = logger;
    }

    public IProviderService GetProvider(string accountType)
    {
        var provider = accountType.ToLowerInvariant() switch
        {
            "microsoft365" or "m365" => (IProviderService)_m365Provider,
            "google" or "gmail" or "google workspace" => _googleProvider,
            "outlook.com" or "outlook" or "hotmail" => _outlookProvider,
            "ics" or "icalendar" => _icsProvider,
            "json" or "json-calendar" => _jsonProvider,
            _ => throw new ArgumentException($"Unknown account type: {accountType}", nameof(accountType))
        };

        _logger.LogDebug("Resolved provider for account type {AccountType}", accountType);
        return provider;
    }
}
