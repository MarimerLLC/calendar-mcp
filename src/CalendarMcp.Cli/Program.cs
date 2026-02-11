using CalendarMcp.Cli.Commands;
using CalendarMcp.Core.Providers;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

// Set up dependency injection
var services = new ServiceCollection();

// Add logging
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

// Add authentication services
services.AddSingleton<IM365AuthenticationService, M365AuthenticationService>();
services.AddSingleton<IGoogleAuthenticationService, GoogleAuthenticationService>();

// Create service provider
var registrar = new TypeRegistrar(services);
var app = new CommandApp(registrar);

app.Configure(config =>
{
    config.SetApplicationName("calendar-mcp-cli");

    config.AddCommand<AddM365AccountCommand>("add-m365-account")
        .WithDescription("Add a new Microsoft 365 account")
        .WithExample(new[] { "add-m365-account" })
        .WithExample(new[] { "add-m365-account", "--config", "/path/to/appsettings.json" });

    config.AddCommand<AddOutlookComAccountCommand>("add-outlook-account")
        .WithDescription("Add a new Outlook.com personal account (Outlook.com, Hotmail, Live)")
        .WithExample(new[] { "add-outlook-account" })
        .WithExample(new[] { "add-outlook-account", "--config", "/path/to/appsettings.json" });

    config.AddCommand<AddGoogleAccountCommand>("add-google-account")
        .WithDescription("Add a new Google account (Gmail, Google Workspace, or custom domain)")
        .WithExample(new[] { "add-google-account" })
        .WithExample(new[] { "add-google-account", "--config", "/path/to/appsettings.json" });

    config.AddCommand<AddIcsAccountCommand>("add-ics-account")
        .WithDescription("Add a new ICS calendar feed (read-only calendar via HTTP URL)")
        .WithExample(new[] { "add-ics-account" })
        .WithExample(new[] { "add-ics-account", "--config", "/path/to/appsettings.json" });

    config.AddCommand<AddJsonAccountCommand>("add-json-account")
        .WithDescription("Add a JSON calendar file (Power Automate export, local or OneDrive)")
        .WithExample(new[] { "add-json-account" })
        .WithExample(new[] { "add-json-account", "--config", "/path/to/appsettings.json" });

    config.AddCommand<ListAccountsCommand>("list-accounts")
        .WithDescription("List all configured accounts")
        .WithExample(new[] { "list-accounts" })
        .WithExample(new[] { "list-accounts", "--config", "/path/to/appsettings.json" });

    config.AddCommand<TestAccountCommand>("test-account")
        .WithDescription("Test account authentication")
        .WithExample(new[] { "test-account", "work-account" })
        .WithExample(new[] { "test-account", "work-account", "--config", "/path/to/appsettings.json" });

    config.AddCommand<ReauthenticateAccountCommand>("reauth")
        .WithDescription("Reauthenticate an existing account")
        .WithExample(new[] { "reauth", "work-account" })
        .WithExample(new[] { "reauth", "work-account", "--config", "/path/to/appsettings.json" });

    config.AddCommand<LogoutAccountCommand>("logout")
        .WithDescription("Logout an account by clearing cached credentials")
        .WithExample(new[] { "logout", "work-account" })
        .WithExample(new[] { "logout", "work-account", "--config", "/path/to/appsettings.json" });

    config.AddCommand<RemoveAccountCommand>("remove-account")
        .WithDescription("Remove an account from the configuration")
        .WithExample(new[] { "remove-account", "work-account" })
        .WithExample(new[] { "remove-account", "work-account", "--force" });
});

try
{
    return await app.RunAsync(args);
}
catch (Exception ex)
{
    AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
    return 1;
}

/// <summary>
/// Type registrar for Spectre.Console.Cli to integrate with Microsoft.Extensions.DependencyInjection
/// </summary>
sealed class TypeRegistrar : ITypeRegistrar
{
    private readonly IServiceCollection _services;

    public TypeRegistrar(IServiceCollection services)
    {
        _services = services;
    }

    public ITypeResolver Build()
    {
        return new TypeResolver(_services.BuildServiceProvider());
    }

    public void Register(Type service, Type implementation)
    {
        _services.AddSingleton(service, implementation);
    }

    public void RegisterInstance(Type service, object implementation)
    {
        _services.AddSingleton(service, implementation);
    }

    public void RegisterLazy(Type service, Func<object> factory)
    {
        _services.AddSingleton(service, _ => factory());
    }
}

/// <summary>
/// Type resolver for Spectre.Console.Cli to integrate with Microsoft.Extensions.DependencyInjection
/// </summary>
sealed class TypeResolver : ITypeResolver
{
    private readonly IServiceProvider _provider;

    public TypeResolver(IServiceProvider provider)
    {
        _provider = provider;
    }

    public object? Resolve(Type? type)
    {
        return type == null ? null : _provider.GetService(type);
    }
}
