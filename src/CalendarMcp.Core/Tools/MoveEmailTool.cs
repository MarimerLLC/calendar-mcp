using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// MCP tool for moving emails to different folders or applying labels
/// </summary>
[McpServerToolType]
public sealed class MoveEmailTool(
    IAccountRegistry accountRegistry,
    IProviderServiceFactory providerFactory,
    ILogger<MoveEmailTool> logger)
{
    [McpServerTool, Description("Move or archive an email to a different folder (Microsoft) or apply/remove labels (Google)")]
    public async Task<string> MoveEmail(
        [Description("Account ID that owns the email. Obtain from the accountId field returned by get_emails or search_emails.")] string accountId,
        [Description("Email message ID to move. Obtain from the id field returned by get_emails or search_emails.")] string emailId,
        [Description("Destination: 'archive', 'inbox', 'trash', 'spam', 'drafts' (Microsoft, IMAP), 'sentitems' (Microsoft, IMAP), or a custom folder ID (Microsoft), label ID (Google) or folder name (IMAP). Aliases: 'deleteditems'='trash', 'junkemail'='spam'.")] string destination)
    {
        logger.LogInformation("Moving email: accountId={AccountId}, emailId={EmailId}, destination={Destination}",
            accountId, emailId, destination);

        ToolGuard.RequireNonEmpty(accountId, nameof(accountId));
        ToolGuard.RequireNonEmpty(emailId, nameof(emailId));
        ToolGuard.RequireNonEmpty(destination, nameof(destination));
        var account = await ToolGuard.RequireAccountAsync(
            accountRegistry, accountId, AccountPermission.EmailRead);

        try
        {
            var provider = providerFactory.GetProvider(account.Provider);
            var newEmailId = await provider.MoveEmailAsync(accountId, emailId, destination, CancellationToken.None);

            var response = new
            {
                success = true,
                emailId = emailId,
                // Microsoft and IMAP assign a new ID in the destination folder; use this one
                // for follow-up calls. Null when the provider can't report it.
                newEmailId = newEmailId,
                accountId = accountId,
                destination = destination,
                message = $"Email '{emailId}' moved to '{destination}' in account '{accountId}'"
            };

            logger.LogInformation("Moved email {EmailId} to folder '{Destination}' in account {AccountId}",
                emailId, destination, accountId);

            return JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (Exception ex) when (ex is not McpException)
        {
            logger.LogError(ex, "Error in move_email tool");
            throw ToolGuard.Failure("move email", ex);
        }
    }
}
