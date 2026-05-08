using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// Fetches the bytes of one inbound email attachment. Defaults to "stash"
/// mode: the server downloads from the upstream provider, drops the bytes
/// into the same store used by the upload endpoint, and returns an
/// attachmentId the agent can pass to <c>send_email</c>. The bytes never
/// transit the LLM in that mode.
/// </summary>
[McpServerToolType]
public sealed class GetEmailAttachmentTool(
    IAccountRegistry accountRegistry,
    IProviderServiceFactory providerFactory,
    IAttachmentStore attachmentStore,
    ILogger<GetEmailAttachmentTool> logger)
{
    // Hard cap on inline mode — anything larger forces the agent to use
    // stash, which doesn't bloat the JSON tool result.
    private const long InlineSizeLimitBytes = 1L * 1024 * 1024;

    [McpServerTool, Description(
        "Fetch the bytes of one attachment on a received email. Required: accountId, emailId, attachmentId — get them from get_email_details. Modes: 'stash' (default) puts the bytes into the server's attachment store and returns an attachmentId you immediately pass to send_email; bytes never round-trip through the agent. 'inline' returns base64Content directly, capped at 1 MB; use when the agent itself needs to read the content (e.g., to extract text from a PDF). The stash attachmentId is consumed by send_email and is also readable via HTTP GET /attachments/{id} on the HTTP server (returns raw bytes; useful when the file exceeds the 1 MB inline cap and the agent has HTTP access). There is no MCP tool for downloading the stashed bytes — use the HTTP endpoint or just pass the ID to send_email.")]
    public async Task<string> GetEmailAttachment(
        [Description("Required. Account that owns the email.")] string accountId,
        [Description("Required. Email ID — pass as parameter name 'emailId' (NOT 'messageId'). Use the value from the 'id' field on get_email_details / get_emails / search_emails.")] string emailId,
        [Description("Required. Provider-side attachment ID, from the attachments[] array on get_email_details (e.g. 'part-0' for Gmail/IMAP, an opaque string for Microsoft Graph).")] string attachmentId,
        [Description("'stash' (default) returns an attachmentId for use in send_email. 'inline' returns base64Content directly; capped at 1 MB.")] string mode = "stash")
    {
        ToolGuard.RequireNonEmpty(accountId, nameof(accountId));
        ToolGuard.RequireNonEmpty(emailId, nameof(emailId));
        ToolGuard.RequireNonEmpty(attachmentId, nameof(attachmentId));

        var normalizedMode = mode?.Trim().ToLowerInvariant() ?? "stash";
        if (normalizedMode is not ("stash" or "inline"))
            throw new McpException($"mode '{mode}' is invalid; use 'stash' or 'inline'.");

        var account = await ToolGuard.RequireAccountAsync(accountRegistry, accountId);

        try
        {
            var provider = providerFactory.GetProvider(account.Provider);
            var content = await provider.GetEmailAttachmentContentAsync(
                accountId, emailId, attachmentId, CancellationToken.None);

            if (content == null)
                throw new McpException($"Attachment '{attachmentId}' on email '{emailId}' was not found or could not be fetched.");

            if (normalizedMode == "inline")
            {
                if (content.Bytes.LongLength > InlineSizeLimitBytes)
                    throw new McpException(
                        $"Attachment is {content.Bytes.LongLength:N0} bytes; inline mode is capped at {InlineSizeLimitBytes:N0} bytes. Re-call with mode='stash' and pass the returned attachmentId to send_email, or extract the bytes by other means.");
                return JsonSerializer.Serialize(new
                {
                    name = content.Name,
                    contentType = content.ContentType,
                    size = content.Bytes.LongLength,
                    base64Content = Convert.ToBase64String(content.Bytes),
                });
            }

            // stash mode
            var stored = attachmentStore.Put(content.Name, content.ContentType, content.Bytes);
            if (stored == null)
                throw new McpException(
                    $"Could not stash the attachment ({content.Bytes.LongLength:N0} bytes); the attachment is over the per-attachment cap or the store is full. Try inline mode if the file is small.");

            logger.LogInformation(
                "Stashed inbound attachment {AttachmentId} from {EmailId} as {StoreId} ({Size} bytes)",
                attachmentId, emailId, stored.Id, stored.Bytes.Length);

            return JsonSerializer.Serialize(new
            {
                attachmentId = stored.Id,
                name = stored.Name,
                contentType = stored.ContentType,
                size = stored.Bytes.LongLength,
                expiresAt = stored.ExpiresAt,
            });
        }
        catch (Exception ex) when (ex is not McpException)
        {
            logger.LogError(ex, "Error in get_email_attachment tool");
            throw new McpException("Failed to fetch attachment.", ex);
        }
    }
}
