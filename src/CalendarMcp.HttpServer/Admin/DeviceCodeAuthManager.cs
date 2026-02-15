using System.Collections.Concurrent;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;

namespace CalendarMcp.HttpServer.Admin;

/// <summary>
/// Manages device code authentication flows for headless/containerized environments.
/// Tracks pending authentication requests and their status.
/// </summary>
public class DeviceCodeAuthManager
{
    private readonly IM365AuthenticationService _m365Auth;
    private readonly IGoogleAuthenticationService _googleAuth;
    private readonly IAccountRegistry _accountRegistry;
    private readonly ILogger<DeviceCodeAuthManager> _logger;
    private readonly ConcurrentDictionary<string, AuthFlowState> _pendingFlows = new();

    public DeviceCodeAuthManager(
        IM365AuthenticationService m365Auth,
        IGoogleAuthenticationService googleAuth,
        IAccountRegistry accountRegistry,
        ILogger<DeviceCodeAuthManager> logger)
    {
        _m365Auth = m365Auth;
        _googleAuth = googleAuth;
        _accountRegistry = accountRegistry;
        _logger = logger;
    }

    /// <summary>
    /// Start a device code authentication flow for the specified account.
    /// </summary>
    public async Task<DeviceCodeResponse> StartDeviceCodeFlowAsync(string accountId, CancellationToken cancellationToken)
    {
        var account = await _accountRegistry.GetAccountAsync(accountId);
        if (account == null)
        {
            throw new ArgumentException($"Account '{accountId}' not found.");
        }

        // Cancel any existing flow for this account
        if (_pendingFlows.TryRemove(accountId, out var existingFlow))
        {
            existingFlow.CancellationSource.Cancel();
            existingFlow.CancellationSource.Dispose();
        }

        var provider = account.Provider.ToLowerInvariant();
        return provider switch
        {
            "microsoft365" or "m365" or "outlook.com" or "outlook" or "hotmail"
                => await StartM365DeviceCodeFlowAsync(account, cancellationToken),
            "google" or "gmail" or "google workspace"
                => throw new NotSupportedException(
                    "Google device code flow requires a TV/limited-input client type in Google Cloud Console. " +
                    "Use the CLI tool for interactive Google authentication instead."),
            _ => throw new NotSupportedException($"Device code flow is not supported for provider '{account.Provider}'.")
        };
    }

    /// <summary>
    /// Get the current status of a device code authentication flow.
    /// </summary>
    public AuthFlowStatus GetFlowStatus(string accountId)
    {
        if (_pendingFlows.TryGetValue(accountId, out var flow))
        {
            return new AuthFlowStatus
            {
                AccountId = accountId,
                Status = flow.Status,
                Message = flow.Message,
                StartedAt = flow.StartedAt,
                CompletedAt = flow.CompletedAt,
                UserCode = flow.UserCode,
                VerificationUrl = flow.VerificationUrl
            };
        }

        return new AuthFlowStatus
        {
            AccountId = accountId,
            Status = "not_found",
            Message = "No authentication flow found for this account."
        };
    }

    /// <summary>
    /// Cancel a pending device code authentication flow.
    /// </summary>
    public bool CancelFlow(string accountId)
    {
        if (_pendingFlows.TryRemove(accountId, out var flow))
        {
            flow.CancellationSource.Cancel();
            flow.CancellationSource.Dispose();
            _logger.LogInformation("Cancelled authentication flow for account {AccountId}", accountId);
            return true;
        }
        return false;
    }

    private async Task<DeviceCodeResponse> StartM365DeviceCodeFlowAsync(AccountInfo account, CancellationToken cancellationToken)
    {
        var tenantId = account.ProviderConfig.GetValueOrDefault("TenantId")
            ?? account.ProviderConfig.GetValueOrDefault("tenantId", "common");
        var clientId = account.ProviderConfig.GetValueOrDefault("ClientId")
            ?? account.ProviderConfig.GetValueOrDefault("clientId")
            ?? throw new InvalidOperationException($"Account '{account.Id}' is missing ClientId in ProviderConfig.");

        // Build explicit scopes - .default doesn't work for personal/consumer accounts
        var scopeList = new List<string>
        {
            "Mail.Read",
            "Mail.Send",
            "Calendars.ReadWrite",
            "offline_access"
        };

        // Check if any JSON calendar accounts reference this account for OneDrive access
        var allAccounts = await _accountRegistry.GetAllAccountsAsync();
        var needsFilesRead = allAccounts.Any(a =>
        {
            if (a.Provider is not ("json" or "json-calendar"))
                return false;
            if (!a.ProviderConfig.TryGetValue("authAccountId", out var authId) &&
                !a.ProviderConfig.TryGetValue("AuthAccountId", out authId))
                return false;
            return authId == account.Id;
        });

        if (needsFilesRead)
        {
            scopeList.Add("Files.Read");
            _logger.LogInformation("Including Files.Read scope for account {AccountId} (needed by JSON calendar accounts)", account.Id);
        }

        var scopes = scopeList.ToArray();

        var flowCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var flowState = new AuthFlowState
        {
            AccountId = account.Id,
            Status = "pending",
            Message = "Waiting for device code...",
            StartedAt = DateTimeOffset.UtcNow,
            CancellationSource = flowCts
        };

        _pendingFlows[account.Id] = flowState;

        var deviceCodeTcs = new TaskCompletionSource<DeviceCodeResponse>();

        // Start the device code flow in the background
        _ = Task.Run(async () =>
        {
            try
            {
                var token = await _m365Auth.AuthenticateWithDeviceCodeAsync(
                    tenantId,
                    clientId,
                    scopes,
                    account.Id,
                    async message =>
                    {
                        // Parse the device code message to extract user code and URL
                        flowState.Message = message;
                        flowState.Status = "awaiting_user";

                        // Extract user code and verification URL from MSAL message
                        // Message format: "To sign in, use a web browser to open the page
                        // https://microsoft.com/devicelogin and enter the code XXXXXXX to authenticate."
                        var urlMatch = System.Text.RegularExpressions.Regex.Match(message, @"(https?://\S+)");
                        var codeMatch = System.Text.RegularExpressions.Regex.Match(message, @"code\s+(\S+)\s+to");

                        if (urlMatch.Success) flowState.VerificationUrl = urlMatch.Groups[1].Value;
                        if (codeMatch.Success) flowState.UserCode = codeMatch.Groups[1].Value;

                        _logger.LogInformation(
                            "Device code flow for account {AccountId}: {Message}",
                            account.Id, message);

                        // Signal that we have the device code info
                        deviceCodeTcs.TrySetResult(new DeviceCodeResponse
                        {
                            AccountId = account.Id,
                            UserCode = flowState.UserCode ?? "(see message)",
                            VerificationUrl = flowState.VerificationUrl ?? "https://microsoft.com/devicelogin",
                            Message = message,
                            ExpiresIn = 900 // 15 minutes default
                        });
                    },
                    flowCts.Token);

                flowState.Status = "completed";
                flowState.Message = "Authentication successful.";
                flowState.CompletedAt = DateTimeOffset.UtcNow;
                _logger.LogInformation("Device code authentication completed for account {AccountId}", account.Id);
            }
            catch (OperationCanceledException)
            {
                flowState.Status = "cancelled";
                flowState.Message = "Authentication flow was cancelled.";
                flowState.CompletedAt = DateTimeOffset.UtcNow;
                deviceCodeTcs.TrySetCanceled();
            }
            catch (Exception ex)
            {
                flowState.Status = "failed";
                flowState.Message = $"Authentication failed: {ex.Message}";
                flowState.CompletedAt = DateTimeOffset.UtcNow;
                _logger.LogError(ex, "Device code authentication failed for account {AccountId}", account.Id);
                deviceCodeTcs.TrySetException(ex);
            }
        }, cancellationToken);

        // Wait for the device code info to be available (or timeout after 30 seconds)
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            return await deviceCodeTcs.Task.WaitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out waiting for device code from identity provider.");
        }
    }
}

/// <summary>
/// Tracks the state of an in-progress device code authentication flow.
/// </summary>
public class AuthFlowState
{
    public required string AccountId { get; set; }
    public string Status { get; set; } = "pending";
    public string Message { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? UserCode { get; set; }
    public string? VerificationUrl { get; set; }
    public required CancellationTokenSource CancellationSource { get; set; }
}

/// <summary>
/// Response returned when starting a device code flow.
/// </summary>
public class DeviceCodeResponse
{
    public required string AccountId { get; set; }
    public required string UserCode { get; set; }
    public required string VerificationUrl { get; set; }
    public required string Message { get; set; }
    public int ExpiresIn { get; set; }
}

/// <summary>
/// Status of a device code authentication flow.
/// </summary>
public class AuthFlowStatus
{
    public required string AccountId { get; set; }
    public required string Status { get; set; }
    public required string Message { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? UserCode { get; set; }
    public string? VerificationUrl { get; set; }
}
