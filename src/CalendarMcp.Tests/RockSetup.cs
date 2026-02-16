using CalendarMcp.Core.Services;
using Rocks;

[assembly: Rock(typeof(IAccountRegistry), BuildType.Create)]
[assembly: Rock(typeof(IProviderServiceFactory), BuildType.Create)]
[assembly: Rock(typeof(IProviderService), BuildType.Create)]
[assembly: Rock(typeof(IM365ProviderService), BuildType.Create)]
[assembly: Rock(typeof(IGoogleProviderService), BuildType.Create)]
[assembly: Rock(typeof(IOutlookComProviderService), BuildType.Create)]
[assembly: Rock(typeof(IIcsProviderService), BuildType.Create)]
[assembly: Rock(typeof(IJsonCalendarProviderService), BuildType.Create)]
