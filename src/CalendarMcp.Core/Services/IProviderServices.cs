using CalendarMcp.Core.Models;

namespace CalendarMcp.Core.Services;

/// <summary>
/// Microsoft 365 provider service for organizational accounts
/// </summary>
public interface IM365ProviderService : IProviderService
{
}

/// <summary>
/// Google Workspace provider service
/// </summary>
public interface IGoogleProviderService : IProviderService
{
}

/// <summary>
/// Outlook.com provider service for personal Microsoft accounts
/// </summary>
public interface IOutlookComProviderService : IProviderService
{
}

/// <summary>
/// ICS feed provider service for read-only calendar access via HTTP ICS URLs
/// </summary>
public interface IIcsProviderService : IProviderService
{
}

/// <summary>
/// JSON calendar file provider service for read-only calendar access via exported JSON files
/// </summary>
public interface IJsonCalendarProviderService : IProviderService
{
}
