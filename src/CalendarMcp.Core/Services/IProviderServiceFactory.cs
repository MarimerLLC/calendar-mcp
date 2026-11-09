namespace CalendarMcp.Core.Services;

/// <summary>
/// Factory for resolving provider services based on account type
/// </summary>
public interface IProviderServiceFactory
{
    /// <summary>
    /// Gets the appropriate provider service for the given account type
    /// </summary>
    /// <param name="accountType">Account type: "microsoft365", "google", "outlook.com", "imap", "dav", "ics", "json", …</param>
    /// <returns>Provider service instance</returns>
    IProviderService GetProvider(string accountType);
}
