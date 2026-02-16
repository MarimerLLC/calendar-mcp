using CalendarMcp.Core.Models;

namespace CalendarMcp.Tests.Helpers;

internal static class TestData
{
    public static AccountInfo CreateAccount(
        string id = "test-account",
        string displayName = "Test Account",
        string provider = "microsoft365",
        bool enabled = true,
        List<string>? domains = null,
        Dictionary<string, string>? providerConfig = null)
    {
        return new AccountInfo
        {
            Id = id,
            DisplayName = displayName,
            Provider = provider,
            Enabled = enabled,
            Domains = domains ?? ["example.com"],
            ProviderConfig = providerConfig ?? new Dictionary<string, string>
            {
                ["TenantId"] = "test-tenant",
                ["ClientId"] = "test-client"
            }
        };
    }

    public static EmailMessage CreateEmail(
        string id = "email-1",
        string accountId = "test-account",
        string subject = "Test Subject",
        string from = "sender@example.com",
        bool isRead = false)
    {
        return new EmailMessage
        {
            Id = id,
            AccountId = accountId,
            Subject = subject,
            From = from,
            FromName = "Test Sender",
            To = ["recipient@example.com"],
            Body = "Test body content",
            ReceivedDateTime = DateTime.UtcNow,
            IsRead = isRead,
            HasAttachments = false
        };
    }

    public static CalendarEvent CreateEvent(
        string id = "event-1",
        string accountId = "test-account",
        string calendarId = "calendar-1",
        string subject = "Test Event",
        DateTime? start = null,
        DateTime? end = null)
    {
        return new CalendarEvent
        {
            Id = id,
            AccountId = accountId,
            CalendarId = calendarId,
            Subject = subject,
            Start = start ?? DateTime.UtcNow.AddHours(1),
            End = end ?? DateTime.UtcNow.AddHours(2),
            Location = "Test Room",
            Organizer = "organizer@example.com"
        };
    }

    public static CalendarInfo CreateCalendar(
        string id = "calendar-1",
        string accountId = "test-account",
        string name = "Test Calendar")
    {
        return new CalendarInfo
        {
            Id = id,
            AccountId = accountId,
            Name = name,
            CanEdit = true,
            IsDefault = true
        };
    }
}
