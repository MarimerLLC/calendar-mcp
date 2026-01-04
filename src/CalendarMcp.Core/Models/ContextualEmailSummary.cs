namespace CalendarMcp.Core.Models;

/// <summary>
/// Represents a contextual summary of emails grouped by topic and analyzed for persona context
/// </summary>
public class ContextualEmailSummary
{
    /// <summary>
    /// Total number of emails analyzed
    /// </summary>
    public int TotalEmails { get; init; }
    
    /// <summary>
    /// Number of accounts searched
    /// </summary>
    public int AccountsSearched { get; init; }
    
    /// <summary>
    /// Search keywords/topics used (if any)
    /// </summary>
    public List<string> SearchKeywords { get; init; } = new();
    
    /// <summary>
    /// Emails grouped by detected topic/cluster
    /// </summary>
    public List<EmailTopicCluster> TopicClusters { get; init; } = new();
    
    /// <summary>
    /// Detected account mismatches where emails were sent to "wrong" account
    /// </summary>
    public List<AccountMismatch> AccountMismatches { get; init; } = new();
    
    /// <summary>
    /// Summary of which personas are being addressed by senders
    /// </summary>
    public List<PersonaContext> PersonaContexts { get; init; } = new();
}

/// <summary>
/// A cluster of emails grouped by topic/subject similarity
/// </summary>
public class EmailTopicCluster
{
    /// <summary>
    /// Detected topic name/label for this cluster
    /// </summary>
    public required string Topic { get; init; }
    
    /// <summary>
    /// Keywords that define this cluster
    /// </summary>
    public List<string> Keywords { get; init; } = new();
    
    /// <summary>
    /// Number of emails in this cluster
    /// </summary>
    public int EmailCount { get; init; }
    
    /// <summary>
    /// Number of unread emails in this cluster
    /// </summary>
    public int UnreadCount { get; init; }
    
    /// <summary>
    /// Accounts that have emails in this cluster
    /// </summary>
    public List<string> AccountIds { get; init; } = new();
    
    /// <summary>
    /// Earliest email date in this cluster
    /// </summary>
    public DateTime? EarliestDate { get; init; }
    
    /// <summary>
    /// Most recent email date in this cluster
    /// </summary>
    public DateTime? LatestDate { get; init; }
    
    /// <summary>
    /// Sample emails from this cluster (limited for response size)
    /// </summary>
    public List<EmailSummaryItem> SampleEmails { get; init; } = new();
    
    /// <summary>
    /// Unique senders in this cluster
    /// </summary>
    public List<string> UniqueSenders { get; init; } = new();
}

/// <summary>
/// Lightweight email summary for clustering results
/// </summary>
public class EmailSummaryItem
{
    public required string Id { get; init; }
    public required string AccountId { get; init; }
    public string Subject { get; init; } = string.Empty;
    public string From { get; init; } = string.Empty;
    public string FromName { get; init; } = string.Empty;
    public DateTime ReceivedDateTime { get; init; }
    public bool IsRead { get; init; }
    public bool HasAttachments { get; init; }
    
    /// <summary>
    /// Short preview of email body (first ~100 chars)
    /// </summary>
    public string BodyPreview { get; init; } = string.Empty;
}

/// <summary>
/// Detected account mismatch where someone emailed the "wrong" account
/// </summary>
public class AccountMismatch
{
    /// <summary>
    /// The email that was potentially misrouted
    /// </summary>
    public required EmailSummaryItem Email { get; init; }
    
    /// <summary>
    /// Account the email was received on
    /// </summary>
    public required string ReceivedOnAccount { get; init; }
    
    /// <summary>
    /// Account that would be more appropriate based on sender domain/context
    /// </summary>
    public required string ExpectedAccount { get; init; }
    
    /// <summary>
    /// Reason for detecting the mismatch
    /// </summary>
    public required string Reason { get; init; }
    
    /// <summary>
    /// Confidence level of the mismatch detection (0-1)
    /// </summary>
    public double Confidence { get; init; }
}

/// <summary>
/// Context about which persona/role people are addressing when emailing
/// </summary>
public class PersonaContext
{
    /// <summary>
    /// Account ID representing this persona
    /// </summary>
    public required string AccountId { get; init; }
    
    /// <summary>
    /// Display name for this persona/account
    /// </summary>
    public required string PersonaName { get; init; }
    
    /// <summary>
    /// Domains associated with this persona
    /// </summary>
    public List<string> Domains { get; init; } = new();
    
    /// <summary>
    /// Number of emails received by this persona
    /// </summary>
    public int EmailCount { get; init; }
    
    /// <summary>
    /// Number of unread emails for this persona
    /// </summary>
    public int UnreadCount { get; init; }
    
    /// <summary>
    /// Primary topics/contexts people contact this persona about
    /// </summary>
    public List<string> PrimaryTopics { get; init; } = new();
    
    /// <summary>
    /// Common sender domains for this persona (helps identify work vs personal context)
    /// </summary>
    public List<SenderDomainSummary> TopSenderDomains { get; init; } = new();
}

/// <summary>
/// Summary of emails from a particular sender domain
/// </summary>
public class SenderDomainSummary
{
    /// <summary>
    /// The sender domain (e.g., "company.com")
    /// </summary>
    public required string Domain { get; init; }
    
    /// <summary>
    /// Number of emails from this domain
    /// </summary>
    public int EmailCount { get; init; }
    
    /// <summary>
    /// Whether this domain matches the account's configured domains
    /// </summary>
    public bool IsInternalDomain { get; init; }
}
