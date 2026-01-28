using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// MCP tool for sending emails
/// </summary>
[McpServerToolType]
public sealed class SendEmailTool(
    IAccountRegistry accountRegistry,
    IProviderServiceFactory providerFactory,
    ILogger<SendEmailTool> logger)
{
    [McpServerTool, Description("Send email from specific account (requires explicit account selection or smart routing)")]
    public async Task<string> SendEmail(
        [Description("Recipient email address")] string to,
        [Description("Email subject")] string subject,
        [Description("Email body content")] string body,
        [Description("Specific account ID, or omit for smart routing")] string? accountId = null,
        [Description("Body format: 'html' or 'text'")] string bodyFormat = "html",
        [Description("CC recipients")] List<string>? cc = null)
    {
        // Strip CDATA wrappers if present (LLMs sometimes wrap content in XML CDATA)
        body = StripCdataWrapper(body);
        
        logger.LogInformation("Sending email: to={To}, subject={Subject}, accountId={AccountId}",
            to, subject, accountId);

        try
        {
            // Determine which account to use
            Models.AccountInfo? account = null;

            if (!string.IsNullOrEmpty(accountId))
            {
                // Explicit account specified
                account = await accountRegistry.GetAccountAsync(accountId);
                if (account == null)
                {
                    return JsonSerializer.Serialize(new
                    {
                        error = $"Account '{accountId}' not found"
                    });
                }
            }
            else
            {
                // Smart routing: extract domain from recipient
                var recipientDomain = to.Split('@').LastOrDefault();
                if (!string.IsNullOrEmpty(recipientDomain))
                {
                    var matchingAccounts = accountRegistry.GetAccountsByDomain(recipientDomain).ToList();

                    if (matchingAccounts.Count == 1)
                    {
                        account = matchingAccounts[0];
                        logger.LogInformation("Smart routing selected account {AccountId} based on domain {Domain}",
                            account.Id, recipientDomain);
                    }
                    else if (matchingAccounts.Count > 1)
                    {
                        // Multiple matches, use first one (could enhance with priority logic)
                        account = matchingAccounts.First();
                        logger.LogInformation("Smart routing selected account {AccountId} from {Count} matches",
                            account.Id, matchingAccounts.Count);
                    }
                }

                // If still no account, use default (first enabled)
                if (account == null)
                {
                    var allAccounts = await accountRegistry.GetAllAccountsAsync();
                    account = allAccounts.FirstOrDefault();
                }

                if (account == null)
                {
                    return JsonSerializer.Serialize(new
                    {
                        error = "No enabled account available to send email"
                    });
                }
            }

            // Send email
            var provider = providerFactory.GetProvider(account.Provider);
            var messageId = await provider.SendEmailAsync(
                account.Id, to, subject, body, bodyFormat, cc, CancellationToken.None);

            var result = new
            {
                success = true,
                messageId = messageId,
                accountUsed = account.Id
            };

            logger.LogInformation("Sent email from account {AccountId} to {To}", account.Id, to);

            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in send_email tool");
            return JsonSerializer.Serialize(new
            {
                error = "Failed to send email",
                message = ex.Message
            });
        }
    }

    /// <summary>
    /// Strips CDATA wrappers from content if present.
    /// LLMs sometimes wrap HTML content in XML CDATA sections which are not valid HTML.
    /// </summary>
    private static string StripCdataWrapper(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        var trimmed = content.Trim();
        
        // Check for CDATA wrapper: <![CDATA[...]]>
        if (trimmed.StartsWith("<![CDATA[", StringComparison.OrdinalIgnoreCase) &&
            trimmed.EndsWith("]]>", StringComparison.Ordinal))
        {
            return trimmed[9..^3]; // Remove "<![CDATA[" (9 chars) and "]]>" (3 chars)
        }

        return content;
    }
}
