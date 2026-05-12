using System.ComponentModel;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Prompts;

/// <summary>
/// MCP prompt templates for email-related workflows
/// </summary>
[McpServerPromptType]
public sealed class EmailPrompts
{
    [McpServerPrompt(Name = "email_triage"), Description(
        "Triages the inbox by summarising unread emails and identifying messages that need action. " +
        "Use this to quickly process a busy inbox and decide what requires a response.")]
    public string EmailTriage(
        [Description("Optional topics or senders to prioritise (e.g. 'invoices, support@example.com'). Leave empty to triage all unread.")] string? focusTopics = null)
    {
        var focusHint = string.IsNullOrWhiteSpace(focusTopics)
            ? "Triage all unread messages."
            : $"Give extra priority to messages related to: {focusTopics}.";

        return $"""
            Please triage my inbox. Follow these steps:

            1. Call list_accounts to discover all configured accounts.
            2. Call get_emails with unreadOnly=true for each account (or omit accountId to query all at once).
            3. For messages that look important, call get_email_details to read the full body.

            {focusHint}

            Present results as a prioritised triage:

            ## Requires Action
            List emails that need a reply or action today, with sender, subject, and a one-line summary of what's needed.

            ## FYI / Low Priority
            List emails that are informational but don't need a reply.

            ## Can Probably Ignore
            List newsletters, notifications, or bulk mail that can be archived or deleted.

            After presenting the triage, ask which emails I'd like to act on.
            """;
    }

    [McpServerPrompt(Name = "draft_reply"), Description(
        "Drafts a reply to a specific email. Provide the email ID and account ID (from get_emails) " +
        "and optionally a tone for the reply.")]
    public string DraftReply(
        [Description("Email ID to reply to. Obtain from get_emails.")] string emailId,
        [Description("Account ID the email belongs to. Obtain from list_accounts.")] string accountId,
        [Description("Tone for the reply: 'professional', 'friendly', or 'brief'. Defaults to 'professional'.")] string tone = "professional")
    {
        return $"""
            Please draft a reply to an email. Follow these steps:

            1. Call get_email_details with emailId="{emailId}" and accountId="{accountId}" to read the full message.
            2. Draft a {tone} reply that addresses the key points in the email.
            3. Present the draft to me for review before sending.
            4. Ask if I'd like to adjust anything, then call send_email if I approve.

            Reply tone: {tone}
            - professional: formal, courteous, clear
            - friendly: warm, conversational, approachable
            - brief: concise, to-the-point, no filler
            """;
    }

    [McpServerPrompt(Name = "forward_with_attachments"), Description(
        "Forwards a received email — including its attachments — to one or more recipients. " +
        "Handles the two-step stash flow so attachment bytes never round-trip through the agent.")]
    public string ForwardWithAttachments(
        [Description("Email ID of the message to forward. Obtain from get_emails or search_emails.")] string emailId,
        [Description("Account ID the email belongs to. Obtain from list_accounts.")] string accountId,
        [Description("Comma-separated list of recipient email addresses to forward to.")] string forwardTo,
        [Description("Optional note to prepend to the forwarded body. Defaults to a short 'FYI — forwarding this' line.")] string? note = null)
    {
        var noteLine = string.IsNullOrWhiteSpace(note)
            ? "FYI — forwarding this."
            : note;

        return $$"""
            Please forward an email with its attachments. Follow these steps in order — the attachment flow is non-obvious, so do not skip steps:

            1. Call get_email_details with emailId="{{emailId}}" and accountId="{{accountId}}".
               - Capture the subject, body, and attachments[] array.
               - Each attachment carries a provider-side attachmentId — this is NOT the ID you pass to send_email.

            2. For each attachment in the response:
               Call get_email_attachment with accountId="{{accountId}}", emailId="{{emailId}}", attachmentId=<provider-side ID>, mode="stash".
               - This returns a server-stash attachmentId (different namespace from the provider's).
               - Stash IDs are single-use; they're consumed by the send below.

            3. Call send_email:
               - accountId="{{accountId}}" (forward from the same account that received the original)
               - to=[<each address in "{{forwardTo}}">]
               - subject="Fwd: <original subject>"
               - body=<noteLine> + "\n\n---\n\n" + original body
               - attachments=[{ "attachmentId": <stash-id-1> }, { "attachmentId": <stash-id-2> }, ...]

            Note to include: {{noteLine}}

            4. Confirm the forward was sent and report how many attachments were included.

            If any attachment exceeds provider limits (3 MB on M365/Outlook.com, 25 MB on Google), surface the error to the user — don't retry blindly.
            """;
    }

    [McpServerPrompt(Name = "bulk_unsubscribe"), Description(
        "Finds likely-marketing emails, unsubscribes from them using RFC-compliant List-Unsubscribe methods, " +
        "and optionally deletes the historical clutter afterwards.")]
    public string BulkUnsubscribe(
        [Description("Optional search query to bias which messages are considered (e.g. 'newsletter', 'marketing'). Defaults to 'unsubscribe' which matches most legitimate bulk mail.")] string? searchQuery = null,
        [Description("Account ID to limit the search to. Omit to scan all accounts.")] string? accountId = null,
        [Description("Set to true to also delete the matched messages after unsubscribing. Defaults to false — present the list to the user first.")] bool deleteAfter = false)
    {
        var query = string.IsNullOrWhiteSpace(searchQuery) ? "unsubscribe" : searchQuery;
        var accountHint = accountId != null
            ? $"Search only in account '{accountId}'."
            : "Search across all accounts.";
        var deleteHint = deleteAfter
            ? "After the user confirms, call bulk_delete_emails with the items you successfully unsubscribed from."
            : "Do NOT delete anything in this run — just unsubscribe. Tell the user they can run a follow-up to clean up history.";

        return $"""
            Please help me unsubscribe from marketing emails. Follow these steps:

            1. Call search_emails with query="{query}", count=50{(accountId != null ? $", accountId=\"{accountId}\"" : "")}. {accountHint}

            2. Present the candidate list to me (sender, subject, account) and ask which to skip, if any. Do not proceed silently — bulk unsubscribe is hard to reverse.

            3. For each remaining candidate:
               a. Call get_unsubscribe_info with its accountId and emailId to confirm a List-Unsubscribe method exists.
               b. If a method is available, call unsubscribe_from_email with method="auto" — this prefers one-click POST, then falls back to HTTPS URL, then mailto.
               c. If no method is available, skip that message and note it in the summary.

            4. Report back:
               - How many we tried to unsubscribe from.
               - How many succeeded (one-click vs URL returned vs mailto sent).
               - How many had no unsubscribe method.

            5. {deleteHint}
            """;
    }

    [McpServerPrompt(Name = "find_emails_about"), Description(
        "Searches emails for a topic and summarises the findings. " +
        "Useful for researching a thread or finding past correspondence on a subject.")]
    public string FindEmailsAbout(
        [Description("Topic, keyword, or phrase to search for (e.g. 'project alpha', 'invoice #1234').")] string topic,
        [Description("Account ID to search within. Omit to search all accounts.")] string? accountId = null)
    {
        var accountHint = accountId != null
            ? $"Search only in account '{accountId}'."
            : "Search across all accounts.";

        return $"""
            Please find and summarise emails about: "{topic}"

            Follow these steps:

            1. Call search_emails with query="{topic}"{(accountId != null ? $" and accountId=\"{accountId}\"" : "")}. {accountHint}
            2. For the top results, call get_email_details to read the full content.
            3. Summarise the findings:

            ## Emails Found About "{topic}"
            - How many results were found and across which accounts.
            - A brief summary of each relevant email (sender, date, key content).
            - Any patterns or trends across the emails (e.g. ongoing thread, recurring issue).
            - The most recent status or resolution, if applicable.
            """;
    }
}
