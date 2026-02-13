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

        // Account management
        admin.MapGet("/accounts", ListAccounts);
        admin.MapGet("/accounts/{accountId}/status", GetAccountStatus);

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
        catch (TimeoutException ex)
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
