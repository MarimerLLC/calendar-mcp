using Microsoft.Extensions.Logging;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using System.Text;

namespace GoogleWorkspaceSpike;

public class GmailServiceWrapper
{
    private readonly GoogleAuthenticator _authenticator;
    private readonly ILogger<GmailServiceWrapper> _logger;
    private GmailService? _service;

    public GmailServiceWrapper(GoogleAuthenticator authenticator, ILogger<GmailServiceWrapper> logger)
    {
        _authenticator = authenticator;
        _logger = logger;
    }

    private async Task<GmailService> GetServiceAsync()
    {
        if (_service != null) return _service;

        var initializer = _authenticator.GetServiceInitializer();
        _service = new GmailService(initializer);
        return _service;
    }

    public async Task<IEnumerable<Message>> GetUnreadMessagesAsync(int maxResults = 10)
    {
        _logger.LogInformation("Fetching unread messages (max: {MaxResults})...", maxResults);
        
        var service = await GetServiceAsync();
        var request = service.Users.Messages.List("me");
        request.Q = "is:unread";
        request.MaxResults = maxResults;

        var response = await request.ExecuteAsync();
        
        if (response.Messages == null || response.Messages.Count == 0)
        {
            _logger.LogInformation("No unread messages found");
            return Array.Empty<Message>();
        }

        _logger.LogInformation("Found {Count} unread messages", response.Messages.Count);

        // Fetch full message details
        var messages = new List<Message>();
        foreach (var msg in response.Messages)
        {
            var fullMessage = await service.Users.Messages.Get("me", msg.Id).ExecuteAsync();
            messages.Add(fullMessage);
        }

        return messages;
    }

    public async Task<Message> GetMessageAsync(string messageId)
    {
        _logger.LogInformation("Fetching message: {MessageId}", messageId);
        
        var service = await GetServiceAsync();
        var message = await service.Users.Messages.Get("me", messageId).ExecuteAsync();
        
        _logger.LogInformation("✓ Message retrieved");
        return message;
    }

    public async Task<IEnumerable<Message>> SearchMessagesAsync(string query, int maxResults = 10)
    {
        _logger.LogInformation("Searching messages: {Query} (max: {MaxResults})", query, maxResults);
        
        var service = await GetServiceAsync();
        var request = service.Users.Messages.List("me");
        request.Q = query;
        request.MaxResults = maxResults;

        var response = await request.ExecuteAsync();
        
        if (response.Messages == null || response.Messages.Count == 0)
        {
            _logger.LogInformation("No messages found");
            return Array.Empty<Message>();
        }

        _logger.LogInformation("Found {Count} messages", response.Messages.Count);

        // Fetch full message details
        var messages = new List<Message>();
        foreach (var msg in response.Messages)
        {
            var fullMessage = await service.Users.Messages.Get("me", msg.Id).ExecuteAsync();
            messages.Add(fullMessage);
        }

        return messages;
    }

    public async Task<string> SendMessageAsync(string to, string subject, string bodyText)
    {
        _logger.LogInformation("Sending message to: {To}, subject: {Subject}", to, subject);
        
        var service = await GetServiceAsync();

        // Create the email message in RFC 2822 format
        var message = new StringBuilder();
        message.AppendLine($"To: {to}");
        message.AppendLine($"Subject: {subject}");
        message.AppendLine("Content-Type: text/plain; charset=utf-8");
        message.AppendLine();
        message.AppendLine(bodyText);

        var rawMessage = Convert.ToBase64String(Encoding.UTF8.GetBytes(message.ToString()))
            .Replace('+', '-')
            .Replace('/', '_')
            .Replace("=", "");

        var gmailMessage = new Message
        {
            Raw = rawMessage
        };

        var result = await service.Users.Messages.Send(gmailMessage, "me").ExecuteAsync();
        
        _logger.LogInformation("✓ Message sent successfully. ID: {MessageId}", result.Id);
        return result.Id;
    }

    public string GetMessageSubject(Message message)
    {
        return message.Payload?.Headers?
            .FirstOrDefault(h => h.Name.Equals("Subject", StringComparison.OrdinalIgnoreCase))
            ?.Value ?? "(No Subject)";
    }

    public string GetMessageFrom(Message message)
    {
        return message.Payload?.Headers?
            .FirstOrDefault(h => h.Name.Equals("From", StringComparison.OrdinalIgnoreCase))
            ?.Value ?? "(Unknown)";
    }

    public string GetMessageDate(Message message)
    {
        return message.Payload?.Headers?
            .FirstOrDefault(h => h.Name.Equals("Date", StringComparison.OrdinalIgnoreCase))
            ?.Value ?? "(Unknown)";
    }

    public string GetMessageSnippet(Message message)
    {
        return message.Snippet ?? "(No preview)";
    }
}
