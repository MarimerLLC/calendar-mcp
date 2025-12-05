using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Abstractions;

namespace CalendarMcp.Spikes.M365DirectAccess;

/// <summary>
/// Simple authentication provider that uses a bearer token
/// </summary>
public class BearerTokenAuthenticationProvider : IAuthenticationProvider
{
    private readonly string _accessToken;

    public BearerTokenAuthenticationProvider(string accessToken)
    {
        _accessToken = accessToken;
    }

    public Task AuthenticateRequestAsync(RequestInformation request, Dictionary<string, object>? additionalAuthenticationContext = null, CancellationToken cancellationToken = default)
    {
        request.Headers.Add("Authorization", $"Bearer {_accessToken}");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Service for interacting with Microsoft Graph Calendar API
/// </summary>
public class GraphCalendarService
{
    private readonly ILogger<GraphCalendarService> _logger;
    private readonly GraphServiceClient _graphClient;
    private readonly string _tenantName;

    public GraphCalendarService(
        ILogger<GraphCalendarService> logger,
        string accessToken,
        string tenantName)
    {
        _logger = logger;
        _tenantName = tenantName;
        
        var authProvider = new BearerTokenAuthenticationProvider(accessToken);
        _graphClient = new GraphServiceClient(authProvider);
    }

    /// <summary>
    /// List all calendars for the authenticated user
    /// </summary>
    public async Task<List<Calendar>> ListCalendarsAsync()
    {
        try
        {
            _logger.LogInformation("Fetching calendars for {TenantName}...", _tenantName);
            
            var calendars = await _graphClient.Me.Calendars.GetAsync();
            var result = calendars?.Value ?? new List<Calendar>();
            
            _logger.LogInformation("✓ Found {Count} calendar(s) for {TenantName}", result.Count, _tenantName);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch calendars for {TenantName}: {Message}", _tenantName, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// List events from the primary calendar
    /// </summary>
    public async Task<List<Event>> ListEventsAsync(int maxResults = 10)
    {
        try
        {
            _logger.LogInformation("Fetching up to {MaxResults} events for {TenantName}...", maxResults, _tenantName);

            var events = await _graphClient.Me.Calendar.Events.GetAsync(config =>
            {
                config.QueryParameters.Top = maxResults;
                config.QueryParameters.Orderby = new[] { "start/dateTime" };
            });
            
            var result = events?.Value ?? new List<Event>();
            
            _logger.LogInformation("✓ Found {Count} event(s) for {TenantName}", result.Count, _tenantName);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch events for {TenantName}: {Message}", _tenantName, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Get events within a specific time range
    /// </summary>
    public async Task<List<Event>> GetEventsInRangeAsync(DateTime startTime, DateTime endTime)
    {
        try
        {
            _logger.LogInformation("Fetching events from {Start} to {End} for {TenantName}...", 
                startTime, endTime, _tenantName);

            var events = await _graphClient.Me.Calendar.CalendarView.GetAsync(config =>
            {
                config.QueryParameters.StartDateTime = startTime.ToString("yyyy-MM-ddTHH:mm:ss");
                config.QueryParameters.EndDateTime = endTime.ToString("yyyy-MM-ddTHH:mm:ss");
            });

            var result = events?.Value ?? new List<Event>();
            
            _logger.LogInformation("✓ Found {Count} event(s) in range for {TenantName}", result.Count, _tenantName);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch events in range for {TenantName}: {Message}", _tenantName, ex.Message);
            throw;
        }
    }
}

/// <summary>
/// Service for interacting with Microsoft Graph Mail API
/// </summary>
public class GraphMailService
{
    private readonly ILogger<GraphMailService> _logger;
    private readonly GraphServiceClient _graphClient;
    private readonly string _tenantName;

    public GraphMailService(
        ILogger<GraphMailService> logger,
        string accessToken,
        string tenantName)
    {
        _logger = logger;
        _tenantName = tenantName;
        
        var authProvider = new BearerTokenAuthenticationProvider(accessToken);
        _graphClient = new GraphServiceClient(authProvider);
    }

    /// <summary>
    /// List messages from the inbox
    /// </summary>
    public async Task<List<Message>> ListMessagesAsync(int maxResults = 10)
    {
        try
        {
            _logger.LogInformation("Fetching up to {MaxResults} messages for {TenantName}...", maxResults, _tenantName);
            
            var messages = await _graphClient.Me.MailFolders["inbox"].Messages.GetAsync(config =>
            {
                config.QueryParameters.Top = maxResults;
                config.QueryParameters.Orderby = new[] { "receivedDateTime desc" };
            });

            var result = messages?.Value ?? new List<Message>();
            
            _logger.LogInformation("✓ Found {Count} message(s) for {TenantName}", result.Count, _tenantName);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch messages for {TenantName}: {Message}", _tenantName, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Get unread message count
    /// </summary>
    public async Task<int> GetUnreadCountAsync()
    {
        try
        {
            _logger.LogInformation("Fetching unread count for {TenantName}...", _tenantName);
            
            var mailFolder = await _graphClient.Me.MailFolders["inbox"].GetAsync();
            var count = mailFolder?.UnreadItemCount ?? 0;
            
            _logger.LogInformation("✓ Unread count for {TenantName}: {Count}", _tenantName, count);
            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch unread count for {TenantName}: {Message}", _tenantName, ex.Message);
            throw;
        }
    }
}
