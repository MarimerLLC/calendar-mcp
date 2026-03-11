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
