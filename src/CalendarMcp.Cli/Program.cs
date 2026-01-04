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

// Add authentication service
services.AddSingleton<IM365AuthenticationService, M365AuthenticationService>();

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

    config.AddCommand<ListAccountsCommand>("list-accounts")
        .WithDescription("List all configured accounts")
        .WithExample(new[] { "list-accounts" })
        .WithExample(new[] { "list-accounts", "--config", "/path/to/appsettings.json" });

    config.AddCommand<TestAccountCommand>("test-account")
        .WithDescription("Test account authentication")
        .WithExample(new[] { "test-account", "xebia-work" })
        .WithExample(new[] { "test-account", "xebia-work", "--config", "/path/to/appsettings.json" });
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
