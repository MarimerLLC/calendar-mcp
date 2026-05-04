using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
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
        "Fetch the bytes of one attachment on a received email. Use 'stash' mode (default) to put the bytes into the server's attachment store and get back an attachmentId you can immediately pass to send_email — bytes never round-trip through the agent. Use 'inline' mode only when the agent itself needs to read the content (e.g., to extract text from a PDF), and only for files under 1 MB. Required: accountId, emailId, attachmentId — get them from get_email_details.")]
    public async Task<string> GetEmailAttachment(
        [Description("Required. Account that owns the email.")] string accountId,
        [Description("Required. Email message ID, from get_email_details.")] string emailId,
        [Description("Required. Provider-side attachment ID, from the attachments[] array on get_email_details.")] string attachmentId,
        [Description("'stash' (default) returns an attachmentId for use in send_email. 'inline' returns base64Content directly; capped at 1 MB.")] string mode = "stash")
    {
        if (string.IsNullOrEmpty(accountId)) return Err("accountId is required");
        if (string.IsNullOrEmpty(emailId)) return Err("emailId is required");
        if (string.IsNullOrEmpty(attachmentId)) return Err("attachmentId is required");

        var normalizedMode = mode?.Trim().ToLowerInvariant() ?? "stash";
        if (normalizedMode is not ("stash" or "inline"))
        {
            return Err($"mode '{mode}' is invalid; use 'stash' or 'inline'.");
        }

        try
        {
            var account = await accountRegistry.GetAccountAsync(accountId);
            if (account == null) return Err($"Account '{accountId}' not found");

            var provider = providerFactory.GetProvider(account.Provider);
            var content = await provider.GetEmailAttachmentContentAsync(
                accountId, emailId, attachmentId, CancellationToken.None);

            if (content == null)
            {
                return Err($"Attachment '{attachmentId}' on email '{emailId}' was not found or could not be fetched.");
            }

            if (normalizedMode == "inline")
            {
                if (content.Bytes.LongLength > InlineSizeLimitBytes)
                {
                    return Err(
                        $"Attachment is {content.Bytes.LongLength:N0} bytes; inline mode is capped at {InlineSizeLimitBytes:N0} bytes. Re-call with mode='stash' and pass the returned attachmentId to send_email, or extract the bytes by other means.");
                }
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
            {
                return Err(
                    $"Could not stash the attachment ({content.Bytes.LongLength:N0} bytes); the attachment is over the per-attachment cap or the store is full. Try inline mode if the file is small.");
            }

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
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in get_email_attachment tool");
            return Err("Failed to fetch attachment", ex.Message);
        }
    }

    private static string Err(string error, string? detail = null)
        => detail == null
            ? JsonSerializer.Serialize(new { error })
            : JsonSerializer.Serialize(new { error, detail });
}
