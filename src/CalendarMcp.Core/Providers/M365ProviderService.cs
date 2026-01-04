using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;

namespace CalendarMcp.Core.Providers;

/// <summary>
/// Microsoft 365 provider service with MSAL authentication integration
/// </summary>
public class M365ProviderService : IM365ProviderService
{
    private readonly ILogger<M365ProviderService> _logger;
    private readonly IM365AuthenticationService _authService;
    private readonly IAccountRegistry _accountRegistry;

    public M365ProviderService(
        ILogger<M365ProviderService> logger,
        IM365AuthenticationService authService,
        IAccountRegistry accountRegistry)
    {
        _logger = logger;
        _authService = authService;
        _accountRegistry = accountRegistry;
    }

    /// <summary>
    /// Get access token for an account
    /// </summary>
    private async Task<string?> GetAccessTokenAsync(string accountId, CancellationToken cancellationToken)
    {
        var account = await _accountRegistry.GetAccountAsync(accountId);
        if (account == null)
        {
            _logger.LogError("Account {AccountId} not found in registry", accountId);
            return null;
        }

        if (!account.ProviderConfig.TryGetValue("tenantId", out var tenantId) ||
            !account.ProviderConfig.TryGetValue("clientId", out var clientId))
        {
            _logger.LogError("Account {AccountId} missing tenantId or clientId in configuration", accountId);
            return null;
        }

        var scopes = new[] { "Mail.Read", "Mail.Send", "Calendars.ReadWrite" };

        var token = await _authService.GetTokenSilentlyAsync(
            tenantId,
            clientId,
            scopes,
            accountId,
            cancellationToken);

        if (token == null)
        {
            _logger.LogWarning("No cached token available for account {AccountId}. Run CLI to authenticate.", accountId);
        }

        return token;
    }

    public async Task<IEnumerable<EmailMessage>> GetEmailsAsync(
        string accountId, 
        int count = 20, 
        bool unreadOnly = false, 
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(accountId, cancellationToken);
        if (token == null)
        {
            return Enumerable.Empty<EmailMessage>();
        }

        _logger.LogWarning("M365ProviderService.GetEmailsAsync not yet fully implemented for account {AccountId}", accountId);
        // TODO: Use Microsoft Graph SDK to fetch emails
        return Enumerable.Empty<EmailMessage>();
    }

    public async Task<IEnumerable<EmailMessage>> SearchEmailsAsync(
        string accountId, 
        string query, 
        int count = 20, 
        DateTime? fromDate = null, 
        DateTime? toDate = null, 
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(accountId, cancellationToken);
        if (token == null)
        {
            return Enumerable.Empty<EmailMessage>();
        }

        _logger.LogWarning("M365ProviderService.SearchEmailsAsync not yet fully implemented for account {AccountId}", accountId);
        // TODO: Use Microsoft Graph SDK to search emails
        return Enumerable.Empty<EmailMessage>();
    }

    public async Task<EmailMessage?> GetEmailDetailsAsync(
        string accountId, 
        string emailId, 
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(accountId, cancellationToken);
        if (token == null)
        {
            return null;
        }

        _logger.LogWarning("M365ProviderService.GetEmailDetailsAsync not yet fully implemented for account {AccountId}", accountId);
        // TODO: Use Microsoft Graph SDK to fetch email details
        return null;
    }

    public async Task<string> SendEmailAsync(
        string accountId, 
        string to, 
        string subject, 
        string body, 
        string bodyFormat = "html", 
        List<string>? cc = null, 
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(accountId, cancellationToken);
        if (token == null)
        {
            throw new InvalidOperationException($"Cannot send email: No authentication token for account {accountId}");
        }

        _logger.LogWarning("M365ProviderService.SendEmailAsync not yet fully implemented for account {AccountId}", accountId);
        // TODO: Use Microsoft Graph SDK to send email
        return "stub-message-id";
    }

    public async Task<IEnumerable<CalendarInfo>> ListCalendarsAsync(
        string accountId, 
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(accountId, cancellationToken);
        if (token == null)
        {
            return Enumerable.Empty<CalendarInfo>();
        }

        _logger.LogWarning("M365ProviderService.ListCalendarsAsync not yet fully implemented for account {AccountId}", accountId);
        // TODO: Use Microsoft Graph SDK to list calendars
        return Enumerable.Empty<CalendarInfo>();
    }

    public async Task<IEnumerable<CalendarEvent>> GetCalendarEventsAsync(
        string accountId, 
        string? calendarId = null, 
        DateTime? startDate = null, 
        DateTime? endDate = null, 
        int count = 50, 
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(accountId, cancellationToken);
        if (token == null)
        {
            return Enumerable.Empty<CalendarEvent>();
        }

        _logger.LogWarning("M365ProviderService.GetCalendarEventsAsync not yet fully implemented for account {AccountId}", accountId);
        // TODO: Use Microsoft Graph SDK to fetch calendar events
        return Enumerable.Empty<CalendarEvent>();
    }

    public async Task<string> CreateEventAsync(
        string accountId, 
        string? calendarId, 
        string subject, 
        DateTime start, 
        DateTime end, 
        string? location = null, 
        List<string>? attendees = null, 
        string? body = null, 
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(accountId, cancellationToken);
        if (token == null)
        {
            throw new InvalidOperationException($"Cannot create event: No authentication token for account {accountId}");
        }

        _logger.LogWarning("M365ProviderService.CreateEventAsync not yet fully implemented for account {AccountId}", accountId);
        // TODO: Use Microsoft Graph SDK to create event
        return "stub-event-id";
    }

    public async Task UpdateEventAsync(
        string accountId, 
        string calendarId, 
        string eventId, 
        string? subject = null, 
        DateTime? start = null, 
        DateTime? end = null, 
        string? location = null, 
        List<string>? attendees = null, 
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(accountId, cancellationToken);
        if (token == null)
        {
            throw new InvalidOperationException($"Cannot update event: No authentication token for account {accountId}");
        }

        _logger.LogWarning("M365ProviderService.UpdateEventAsync not yet fully implemented for account {AccountId}", accountId);
        // TODO: Use Microsoft Graph SDK to update event
    }

    public async Task DeleteEventAsync(
        string accountId, 
        string calendarId, 
        string eventId, 
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(accountId, cancellationToken);
        if (token == null)
        {
            throw new InvalidOperationException($"Cannot delete event: No authentication token for account {accountId}");
        }

        _logger.LogWarning("M365ProviderService.DeleteEventAsync not yet fully implemented for account {AccountId}", accountId);
        // TODO: Use Microsoft Graph SDK to delete event
    }
}
