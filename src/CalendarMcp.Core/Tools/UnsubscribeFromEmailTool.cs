using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Services;
using CalendarMcp.Core.Utilities;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// MCP tool for executing email list unsubscribe actions
/// </summary>
[McpServerToolType]
public sealed class UnsubscribeFromEmailTool(
    IAccountRegistry accountRegistry,
    IProviderServiceFactory providerFactory,
    UnsubscribeExecutor unsubscribeExecutor,
    ILogger<UnsubscribeFromEmailTool> logger)
{
    [McpServerTool, Description("Unsubscribe from an email mailing list using List-Unsubscribe headers (RFC 2369/8058). Supports one-click POST, returning HTTPS link, or sending mailto unsubscribe.")]
    public async Task<string> UnsubscribeFromEmail(
        [Description("Account ID that owns the email. Obtain from the accountId field returned by get_emails or search_emails.")] string accountId,
        [Description("Email message ID. Obtain from the id field returned by get_emails or search_emails.")] string emailId,
        [Description("Unsubscribe method: 'auto' (default — tries one-click POST, then returns HTTPS link, then sends mailto), 'one-click' (RFC 8058 POST), 'https' (returns URL for user to open), or 'mailto' (sends unsubscribe email)")] string method = "auto")
    {
        logger.LogInformation("Unsubscribe request: accountId={AccountId}, emailId={EmailId}, method={Method}",
            accountId, emailId, method);

        try
        {
            if (string.IsNullOrEmpty(accountId))
                return JsonSerializer.Serialize(new { error = "accountId is required" });

            if (string.IsNullOrEmpty(emailId))
                return JsonSerializer.Serialize(new { error = "emailId is required" });

            var account = await accountRegistry.GetAccountAsync(accountId);
            if (account == null)
                return JsonSerializer.Serialize(new { error = $"Account '{accountId}' not found" });

            var provider = providerFactory.GetProvider(account.Provider);
            var email = await provider.GetEmailDetailsAsync(accountId, emailId, CancellationToken.None);

            if (email == null)
                return JsonSerializer.Serialize(new { error = $"Email '{emailId}' not found in account '{accountId}'" });

            if (email.UnsubscribeInfo == null)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    message = "This email does not contain List-Unsubscribe headers"
                }, new JsonSerializerOptions { WriteIndented = true });
            }

            var info = email.UnsubscribeInfo;
            var result = method.ToLowerInvariant() switch
            {
                "one-click" => await TryOneClickAsync(info),
                "https" => TryHttps(info),
                "mailto" => await TryMailtoAsync(info, account, provider),
                "auto" => await TryAutoAsync(info, account, provider),
                _ => new Models.UnsubscribeResult
                {
                    Success = false,
                    Method = method,
                    Message = $"Unknown method '{method}'. Use 'auto', 'one-click', 'https', or 'mailto'."
                }
            };

            logger.LogInformation("Unsubscribe result for {EmailId}: success={Success}, method={Method}",
                emailId, result.Success, result.Method);

            return JsonSerializer.Serialize(new
            {
                result.Success,
                result.Method,
                result.Message,
                result.ErrorDetails
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in unsubscribe_from_email tool");
            return JsonSerializer.Serialize(new
            {
                error = "Failed to unsubscribe",
                message = ex.Message
            });
        }
    }

    private async Task<Models.UnsubscribeResult> TryAutoAsync(
        Models.UnsubscribeInfo info,
        Models.AccountInfo account,
        IProviderService provider)
    {
        // Try one-click first (best option)
        if (info.SupportsOneClick)
        {
            var result = await unsubscribeExecutor.ExecuteOneClickAsync(info);
            if (result.Success) return result;
        }

        // Fall back to HTTPS URL (return link for user)
        if (info.HttpsUrl != null)
            return TryHttps(info);

        // Fall back to mailto
        if (info.MailtoUrl != null)
            return await TryMailtoAsync(info, account, provider);

        return new Models.UnsubscribeResult
        {
            Success = false,
            Method = "auto",
            Message = "No unsubscribe method available"
        };
    }

    private async Task<Models.UnsubscribeResult> TryOneClickAsync(Models.UnsubscribeInfo info)
    {
        return await unsubscribeExecutor.ExecuteOneClickAsync(info);
    }

    private static Models.UnsubscribeResult TryHttps(Models.UnsubscribeInfo info)
    {
        if (info.HttpsUrl == null)
        {
            return new Models.UnsubscribeResult
            {
                Success = false,
                Method = "https",
                Message = "No HTTPS unsubscribe URL available"
            };
        }

        return new Models.UnsubscribeResult
        {
            Success = true,
            Method = "https",
            Message = $"Open this URL to unsubscribe: {info.HttpsUrl}"
        };
    }

    private async Task<Models.UnsubscribeResult> TryMailtoAsync(
        Models.UnsubscribeInfo info,
        Models.AccountInfo account,
        IProviderService provider)
    {
        if (info.MailtoUrl == null)
        {
            return new Models.UnsubscribeResult
            {
                Success = false,
                Method = "mailto",
                Message = "No mailto unsubscribe URL available"
            };
        }

        var parsed = UnsubscribeExecutor.ParseMailtoUrl(info.MailtoUrl);
        if (parsed == null)
        {
            return new Models.UnsubscribeResult
            {
                Success = false,
                Method = "mailto",
                Message = "Failed to parse mailto unsubscribe URL"
            };
        }

        try
        {
            var (to, subject, body) = parsed.Value;
            await provider.SendEmailAsync(account.Id, to, subject, body, "text", null, CancellationToken.None);

            return new Models.UnsubscribeResult
            {
                Success = true,
                Method = "mailto",
                Message = $"Sent unsubscribe email to {to}"
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending mailto unsubscribe for {MailtoUrl}", info.MailtoUrl);
            return new Models.UnsubscribeResult
            {
                Success = false,
                Method = "mailto",
                Message = "Failed to send unsubscribe email",
                ErrorDetails = ex.Message
            };
        }
    }
}
