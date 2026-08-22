using System.Text.Json;
using CalendarMcp.Core.Models;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

namespace CalendarMcp.Core.Tools;

public sealed partial class CalendarActionTool
{
    // Total decoded attachment payload limit per message. Keeps the JSON
    // tool call within agent limits and matches typical provider caps (~25 MB).
    private const long MaxTotalAttachmentBytes = 25L * 1024 * 1024;

    /// <summary>get_emails -- unchanged from the raw GetEmailsTool.</summary>
    private async Task<string> GetEmailsAction(string? accountId, int? count, bool? unreadOnly)
    {
        var resolvedCount = count ?? 20;
        var resolvedUnreadOnly = unreadOnly ?? false;
        _logger.LogInformation("Getting emails: accountId={AccountId}, count={Count}, unreadOnly={UnreadOnly}",
            accountId, resolvedCount, resolvedUnreadOnly);

        List<AccountInfo> validAccounts;
        if (string.IsNullOrEmpty(accountId))
        {
            validAccounts = (await _accountRegistry.GetAllAccountsAsync()).ToList();
            if (validAccounts.Count == 0)
                throw new McpException("No accounts found");
        }
        else
        {
            validAccounts = new List<AccountInfo> { await ToolGuard.RequireAccountAsync(_accountRegistry, accountId) };
        }

        try
        {
            var tasks = validAccounts.Select(async account =>
            {
                try
                {
                    var provider = _providerFactory.GetProvider(account!.Provider);
                    var emails = await provider.GetEmailsAsync(account.Id, resolvedCount, resolvedUnreadOnly, CancellationToken.None);
                    return emails;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting emails from account {AccountId}", account!.Id);
                    return Enumerable.Empty<EmailMessage>();
                }
            });

            var results = await Task.WhenAll(tasks);
            var allEmails = results.SelectMany(e => e).OrderByDescending(e => e.ReceivedDateTime).ToList();

            var response = new
            {
                emails = allEmails.Select(e => new
                {
                    id = e.Id,
                    accountId = e.AccountId,
                    subject = e.Subject,
                    from = e.From,
                    receivedDateTime = e.ReceivedDateTime,
                    isRead = e.IsRead,
                    hasAttachments = e.HasAttachments
                })
            };

            _logger.LogInformation("Retrieved {Count} emails from {AccountCount} accounts",
                allEmails.Count, validAccounts.Count);

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex) when (ex is not McpException)
        {
            _logger.LogError(ex, "Error in get_emails action");
            throw new McpException("Failed to get emails.", ex);
        }
    }

    /// <summary>get_email_details -- unchanged from the raw GetEmailDetailsTool.</summary>
    private async Task<string> GetEmailDetailsAction(string? accountId, string? emailId)
    {
        _logger.LogInformation("Getting email details: accountId={AccountId}, emailId={EmailId}",
            accountId, emailId);

        ToolGuard.RequireNonEmpty(accountId, nameof(accountId));
        ToolGuard.RequireNonEmpty(emailId, nameof(emailId));
        var account = await ToolGuard.RequireAccountAsync(_accountRegistry, accountId!);

        try
        {
            var provider = _providerFactory.GetProvider(account.Provider);
            var email = await provider.GetEmailDetailsAsync(accountId!, emailId!, CancellationToken.None);

            if (email == null)
                throw new McpException($"Email '{emailId}' not found in account '{accountId}'");

            var response = new
            {
                id = email.Id,
                accountId = email.AccountId,
                subject = email.Subject,
                from = email.From,
                fromName = email.FromName,
                to = email.To,
                cc = email.Cc,
                body = email.Body,
                bodyFormat = email.BodyFormat,
                receivedDateTime = email.ReceivedDateTime,
                isRead = email.IsRead,
                hasAttachments = email.HasAttachments,
                attachments = email.Attachments.Select(a => new
                {
                    name = a.Name,
                    size = a.Size,
                    contentType = a.ContentType,
                    attachmentId = a.AttachmentId,
                })
            };

            _logger.LogInformation("Retrieved email details for {EmailId} from account {AccountId}",
                emailId, accountId);

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex) when (ex is not McpException)
        {
            _logger.LogError(ex, "Error in get_email_details action");
            throw new McpException("Failed to get email details.", ex);
        }
    }

    /// <summary>search_emails -- unchanged from the raw SearchEmailsTool.</summary>
    private async Task<string> SearchEmailsAction(string? query, string? accountId, int? count, DateTime? fromDate, DateTime? toDate)
    {
        var resolvedCount = count ?? 20;
        _logger.LogInformation("Searching emails: query={Query}, accountId={AccountId}, count={Count}",
            query, accountId, resolvedCount);

        if (string.IsNullOrWhiteSpace(query))
            throw new McpException("query is required");

        List<AccountInfo> validAccounts;
        if (string.IsNullOrEmpty(accountId))
        {
            validAccounts = (await _accountRegistry.GetAllAccountsAsync()).ToList();
            if (validAccounts.Count == 0)
                throw new McpException("No accounts found");
        }
        else
        {
            validAccounts = new List<AccountInfo> { await ToolGuard.RequireAccountAsync(_accountRegistry, accountId) };
        }

        try
        {
            var tasks = validAccounts.Select(async account =>
            {
                try
                {
                    var provider = _providerFactory.GetProvider(account!.Provider);
                    var emails = await provider.SearchEmailsAsync(
                        account.Id, query, resolvedCount, fromDate, toDate, CancellationToken.None);
                    return emails;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error searching emails in account {AccountId}", account!.Id);
                    return Enumerable.Empty<EmailMessage>();
                }
            });

            var results = await Task.WhenAll(tasks);
            var allEmails = results.SelectMany(e => e).OrderByDescending(e => e.ReceivedDateTime).ToList();

            var response = new
            {
                emails = allEmails.Select(e => new
                {
                    id = e.Id,
                    accountId = e.AccountId,
                    subject = e.Subject,
                    from = e.From,
                    receivedDateTime = e.ReceivedDateTime,
                    isRead = e.IsRead,
                    hasAttachments = e.HasAttachments
                })
            };

            _logger.LogInformation("Found {Count} emails matching '{Query}' from {AccountCount} accounts",
                allEmails.Count, query, validAccounts.Count);

            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex) when (ex is not McpException)
        {
            _logger.LogError(ex, "Error in search_emails action");
            throw new McpException("Failed to search emails.", ex);
        }
    }

    /// <summary>send_email -- unchanged from the raw SendEmailTool.</summary>
    private async Task<string> SendEmailAction(
        List<string>? to,
        string? subject,
        string? body,
        string? accountId,
        string? bodyFormat,
        List<string>? cc,
        List<OutboundEmailAttachment>? attachments,
        string? textBody,
        string? htmlBody)
    {
        var resolvedBodyFormat = bodyFormat ?? "html";
        var resolvedBody = StripCdataWrapper(body ?? "") ?? "";
        var resolvedTextBody = StripCdataWrapper(textBody);
        var resolvedHtmlBody = StripCdataWrapper(htmlBody);

        if (string.IsNullOrEmpty(subject))
            throw new McpException("subject is required.");

        if (resolvedBodyFormat.Equals("multipart", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(resolvedTextBody) || string.IsNullOrEmpty(resolvedHtmlBody))
                throw new McpException("Both 'textBody' and 'htmlBody' are required when bodyFormat is 'multipart'.");
        }

        if (to is null || to.Count == 0)
            throw new McpException("At least one recipient address is required in the 'to' field.");

        List<OutboundEmailAttachment>? resolvedAttachments = null;
        if (attachments is { Count: > 0 })
        {
            var shapeError = ValidateAttachmentShapes(attachments);
            if (shapeError != null)
                throw new McpException(shapeError);

            var (resolved, consumeError) = ResolveAttachments(attachments);
            if (consumeError != null)
                throw new McpException(consumeError);
            resolvedAttachments = resolved;
        }

        var toJoined = string.Join(", ", to);

        _logger.LogInformation("Sending email: to={To}, subject={Subject}, accountId={AccountId}",
            toJoined, subject, accountId);

        AccountInfo account;
        if (!string.IsNullOrEmpty(accountId))
        {
            account = await ToolGuard.RequireAccountAsync(_accountRegistry, accountId);
        }
        else
        {
            AccountInfo? selected = null;

            var recipientDomain = to[0].Split('@').LastOrDefault();
            if (!string.IsNullOrEmpty(recipientDomain))
            {
                var matchingAccounts = _accountRegistry.GetAccountsByDomain(recipientDomain).ToList();

                if (matchingAccounts.Count == 1)
                {
                    selected = matchingAccounts[0];
                    _logger.LogInformation("Smart routing selected account {AccountId} based on domain {Domain}",
                        selected.Id, recipientDomain);
                }
                else if (matchingAccounts.Count > 1)
                {
                    selected = matchingAccounts.First();
                    _logger.LogInformation("Smart routing selected account {AccountId} from {Count} matches",
                        selected.Id, matchingAccounts.Count);
                }
            }

            if (selected == null)
            {
                var allAccounts = await _accountRegistry.GetAllAccountsAsync();
                selected = allAccounts.FirstOrDefault();
            }

            if (selected == null)
                throw new McpException("No enabled account available to send email");

            account = selected;
        }

        try
        {
            var provider = _providerFactory.GetProvider(account.Provider);
            var messageId = await provider.SendEmailAsync(
                account.Id, toJoined, subject, resolvedBody, resolvedBodyFormat, cc, resolvedAttachments,
                resolvedTextBody, resolvedHtmlBody, CancellationToken.None);

            var result = new
            {
                success = true,
                messageId,
                accountUsed = account.Id
            };

            _logger.LogInformation("Sent email from account {AccountId} to {To}", account.Id, toJoined);

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex) when (ex is not McpException)
        {
            _logger.LogError(ex, "Error in send_email action");
            throw new McpException("Failed to send email.", ex);
        }
    }

    /// <summary>
    /// First pass: structural validation only. Does NOT touch the attachment
    /// store. Returns an error string for the caller, or null if all items are
    /// well-formed.
    /// </summary>
    private static string? ValidateAttachmentShapes(List<OutboundEmailAttachment> attachments)
    {
        long inlineEstimatedBytes = 0;
        for (var i = 0; i < attachments.Count; i++)
        {
            var att = attachments[i];
            var hasInline = !string.IsNullOrEmpty(att.Base64Content);
            var hasId = !string.IsNullOrEmpty(att.AttachmentId);

            if (hasInline && hasId)
            {
                return $"attachments[{i}]: set either 'base64Content' or 'attachmentId', not both.";
            }
            if (!hasInline && !hasId)
            {
                return $"attachments[{i}]: set either 'base64Content' (inline bytes) or 'attachmentId' (from POST /attachments).";
            }
            if (hasInline && string.IsNullOrWhiteSpace(att.Name))
            {
                return $"attachments[{i}].name is required when using base64Content.";
            }
            if (hasInline)
            {
                var len = att.Base64Content!.Length;
                inlineEstimatedBytes += (long)(len / 4) * 3;
                if (inlineEstimatedBytes > MaxTotalAttachmentBytes)
                {
                    return $"Total inline attachment size exceeds {MaxTotalAttachmentBytes:N0} bytes.";
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Second pass: resolves any <see cref="OutboundEmailAttachment.AttachmentId"/>
    /// entries to inline bytes by consuming them from the store. Single-use:
    /// successfully consumed IDs are removed, so an error after this point
    /// requires the agent to re-upload.
    /// </summary>
    private (List<OutboundEmailAttachment>? resolved, string? error) ResolveAttachments(
        List<OutboundEmailAttachment> attachments)
    {
        var result = new List<OutboundEmailAttachment>(attachments.Count);
        long totalBytes = 0;

        for (var i = 0; i < attachments.Count; i++)
        {
            var att = attachments[i];

            if (!string.IsNullOrEmpty(att.AttachmentId))
            {
                var stored = _attachmentStore.TryConsume(att.AttachmentId);
                if (stored == null)
                {
                    return (null, $"attachments[{i}]: attachmentId '{att.AttachmentId}' is unknown or expired. Re-upload via POST /attachments.");
                }
                totalBytes += stored.Bytes.Length;
                if (totalBytes > MaxTotalAttachmentBytes)
                {
                    return (null, $"Total attachment size exceeds {MaxTotalAttachmentBytes:N0} bytes.");
                }
                result.Add(new OutboundEmailAttachment
                {
                    Name = string.IsNullOrWhiteSpace(att.Name) ? stored.Name : att.Name,
                    ContentType = att.ContentType ?? stored.ContentType,
                    Base64Content = Convert.ToBase64String(stored.Bytes),
                });
            }
            else
            {
                result.Add(att);
            }
        }

        return (result, null);
    }

    /// <summary>
    /// Strips CDATA wrappers from content if present. LLMs sometimes wrap
    /// content in XML CDATA sections which are not valid HTML.
    /// </summary>
    private static string? StripCdataWrapper(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        var trimmed = content.Trim();

        if (trimmed.StartsWith("<![CDATA[", StringComparison.OrdinalIgnoreCase) &&
            trimmed.EndsWith("]]>", StringComparison.Ordinal))
        {
            return trimmed[9..^3];
        }

        return content;
    }
}
