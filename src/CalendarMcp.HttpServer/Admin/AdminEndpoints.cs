using CalendarMcp.Auth;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;

namespace CalendarMcp.HttpServer.Admin;

/// <summary>
/// Maps admin API endpoints for account management and device code authentication.
/// </summary>
public static class AdminEndpoints
{
    public static WebApplication MapAdminEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/admin");

        // Account management (read)
        admin.MapGet("/accounts", ListAccounts);
        admin.MapGet("/accounts/{accountId}/status", GetAccountStatus);

        // Account CRUD
        admin.MapPost("/accounts", CreateAccount);
        admin.MapPut("/accounts/{accountId}", UpdateAccount);
        admin.MapDelete("/accounts/{accountId}", DeleteAccount);
        admin.MapPost("/accounts/{accountId}/logout", LogoutAccount);

        // Device code authentication
        admin.MapPost("/auth/{accountId}/start", StartDeviceCodeAuth);
        admin.MapGet("/auth/{accountId}/status", GetAuthStatus);
        admin.MapPost("/auth/{accountId}/cancel", CancelAuth);

        return app;
    }

    /// <summary>
    /// List all configured accounts with their basic info (no secrets).
    /// </summary>
    private static async Task<IResult> ListAccounts(IAccountRegistry accountRegistry)
    {
        var accounts = await accountRegistry.GetAllAccountsAsync();
        var response = accounts.Select(a => new
        {
            id = a.Id,
            displayName = a.DisplayName,
            provider = a.Provider,
            domains = a.Domains,
            enabled = a.Enabled,
            priority = a.Priority
        });

        return Results.Ok(new { accounts = response });
    }

    /// <summary>
    /// Get authentication status for a specific account.
    /// </summary>
    private static async Task<IResult> GetAccountStatus(
        string accountId,
        IAccountRegistry accountRegistry,
        DeviceCodeAuthManager authManager)
    {
        var account = await accountRegistry.GetAccountAsync(accountId);
        if (account == null)
        {
            return Results.NotFound(new { error = $"Account '{accountId}' not found." });
        }

        var flowStatus = authManager.GetFlowStatus(accountId);

        return Results.Ok(new
        {
            accountId = account.Id,
            displayName = account.DisplayName,
            provider = account.Provider,
            enabled = account.Enabled,
            authFlow = flowStatus.Status != "not_found" ? flowStatus : null
        });
    }

    /// <summary>
    /// Create a new account in the config file.
    /// </summary>
    private static async Task<IResult> CreateAccount(
        CreateAccountRequest request,
        IAccountConfigurationService configService)
    {
        // Validate ID
        var (idValid, idError) = AccountValidation.ValidateAccountId(request.Id);
        if (!idValid)
            return Results.BadRequest(new { error = idError });

        // Validate provider
        var (provValid, provError) = AccountValidation.ValidateProvider(request.Provider);
        if (!provValid)
            return Results.BadRequest(new { error = provError });

        // Validate provider config
        var (cfgValid, cfgError) = AccountValidation.ValidateProviderConfig(request.Provider, request.ProviderConfig);
        if (!cfgValid)
            return Results.BadRequest(new { error = cfgError });

        var account = new AccountInfo
        {
            Id = request.Id,
            DisplayName = request.DisplayName,
            Provider = request.Provider,
            Domains = request.Domains,
            Enabled = request.Enabled,
            Priority = request.Priority,
            ProviderConfig = request.ProviderConfig
        };

        try
        {
            await configService.AddAccountAsync(account);
            return Results.Created($"/admin/accounts/{account.Id}/status", new
            {
                id = account.Id,
                displayName = account.DisplayName,
                provider = account.Provider,
                domains = account.Domains,
                enabled = account.Enabled,
                priority = account.Priority
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
        {
            return Results.Conflict(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Update an existing account in the config file.
    /// </summary>
    private static async Task<IResult> UpdateAccount(
        string accountId,
        UpdateAccountRequest request,
        IAccountConfigurationService configService)
    {
        // Look up existing account to get its provider (provider is immutable)
        var existing = await configService.GetAccountFromConfigAsync(accountId);
        if (existing is null)
            return Results.NotFound(new { error = $"Account '{accountId}' not found." });

        // Validate provider config against the existing provider
        var (cfgValid, cfgError) = AccountValidation.ValidateProviderConfig(existing.Provider, request.ProviderConfig);
        if (!cfgValid)
            return Results.BadRequest(new { error = cfgError });

        var updated = new AccountInfo
        {
            Id = accountId,
            DisplayName = request.DisplayName,
            Provider = existing.Provider, // immutable
            Domains = request.Domains,
            Enabled = request.Enabled,
            Priority = request.Priority,
            ProviderConfig = request.ProviderConfig
        };

        try
        {
            await configService.UpdateAccountAsync(updated);
            return Results.Ok(new
            {
                id = updated.Id,
                displayName = updated.DisplayName,
                provider = updated.Provider,
                domains = updated.Domains,
                enabled = updated.Enabled,
                priority = updated.Priority
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return Results.NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Remove an account from the config file. Optionally clear credentials.
    /// </summary>
    private static async Task<IResult> DeleteAccount(
        string accountId,
        IAccountConfigurationService configService,
        bool logout = false)
    {
        try
        {
            await configService.RemoveAccountAsync(accountId, clearCredentials: logout);
            return Results.NoContent();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return Results.NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Clear cached credentials for an account without removing it from config.
    /// </summary>
    private static async Task<IResult> LogoutAccount(
        string accountId,
        IAccountConfigurationService configService)
    {
        var account = await configService.GetAccountFromConfigAsync(accountId);
        if (account is null)
            return Results.NotFound(new { error = $"Account '{accountId}' not found." });

        await configService.ClearCredentialsAsync(accountId, account.Provider);
        return Results.Ok(new { message = $"Credentials cleared for account '{accountId}'." });
    }

    /// <summary>
    /// Start a device code authentication flow for the specified account.
    /// Returns the device code and verification URL for the user to complete authentication.
    /// </summary>
    private static async Task<IResult> StartDeviceCodeAuth(
        string accountId,
        DeviceCodeAuthManager authManager,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await authManager.StartDeviceCodeFlowAsync(accountId, cancellationToken);
            return Results.Ok(response);
        }
        catch (ArgumentException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (NotSupportedException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (TimeoutException)
        {
            return Results.StatusCode(504);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message,
                title: "Failed to start device code flow",
                statusCode: 500);
        }
    }

    /// <summary>
    /// Get the status of a pending device code authentication flow.
    /// </summary>
    private static IResult GetAuthStatus(string accountId, DeviceCodeAuthManager authManager)
    {
        var status = authManager.GetFlowStatus(accountId);
        return Results.Ok(status);
    }

    /// <summary>
    /// Cancel a pending device code authentication flow.
    /// </summary>
    private static IResult CancelAuth(string accountId, DeviceCodeAuthManager authManager)
    {
        var cancelled = authManager.CancelFlow(accountId);
        if (cancelled)
        {
            return Results.Ok(new { message = $"Authentication flow for '{accountId}' has been cancelled." });
        }
        return Results.NotFound(new { error = $"No pending authentication flow found for '{accountId}'." });
    }
}
