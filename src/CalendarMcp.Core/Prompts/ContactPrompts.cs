using System.ComponentModel;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Prompts;

/// <summary>
/// MCP prompt templates for contact-related workflows
/// </summary>
[McpServerPromptType]
public sealed class ContactPrompts
{
    [McpServerPrompt(Name = "contact_summary"), Description(
        "Builds a comprehensive profile for a contact by searching across all accounts. " +
        "Provide a name or email address to look up.")]
    public string ContactSummary(
        [Description("Name or email address of the contact to look up (e.g. 'Jane Smith' or 'jane@example.com').")] string nameOrEmail)
    {
        return $"""
            Please build a profile for this contact: "{nameOrEmail}"

            Follow these steps:

            1. Call list_accounts to discover all configured accounts.
            2. Call search_contacts with query="{nameOrEmail}" across all accounts.
            3. For each matching contact found, call get_contact_details to retrieve full information.
            4. Call search_emails with query="{nameOrEmail}" to find recent correspondence.

            Present a comprehensive contact profile:

            ## Contact: {nameOrEmail}

            ### Details
            Full name, email addresses, phone numbers, job title, company, and any other available fields.
            Note which account(s) the contact is from.

            ### Recent Correspondence
            Summarise the last few emails exchanged, including dates and topics.

            ### Notes
            Any other relevant context from the contact record.
            """;
    }
}
