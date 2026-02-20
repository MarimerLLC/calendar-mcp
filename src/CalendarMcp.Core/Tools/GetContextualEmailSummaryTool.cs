using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// MCP tool for getting contextual, clustered email summaries across all accounts
/// with persona detection and account mismatch analysis
/// </summary>
[McpServerToolType]
public sealed partial class GetContextualEmailSummaryTool(
    IAccountRegistry accountRegistry,
    IProviderServiceFactory providerFactory,
    ILogger<GetContextualEmailSummaryTool> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    
    // Common topic keywords for clustering
    private static readonly Dictionary<string, string[]> TopicKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Meeting/Calendar"] = ["meeting", "calendar", "schedule", "invite", "call", "zoom", "teams", "agenda", "sync"],
        ["Project Updates"] = ["update", "status", "progress", "milestone", "sprint", "release", "deployment", "project"],
        ["Action Required"] = ["action", "required", "urgent", "asap", "deadline", "due", "reminder", "follow-up", "followup"],
        ["Financial"] = ["invoice", "payment", "expense", "budget", "cost", "price", "quote", "proposal", "contract"],
        ["HR/Admin"] = ["hr", "vacation", "leave", "pto", "benefits", "payroll", "timesheet", "policy"],
        ["Support/Issues"] = ["issue", "bug", "problem", "error", "help", "support", "ticket", "incident"],
        ["Newsletters/Marketing"] = ["newsletter", "subscribe", "unsubscribe", "promotion", "offer", "marketing"],
        ["Social/Personal"] = ["birthday", "congratulations", "welcome", "farewell", "party", "lunch", "coffee"]
    };

    [McpServerTool(Name = "get_contextual_email_summary"), 
     Description("Get a contextual, topic-grouped summary of emails across all accounts with persona detection and account mismatch analysis. Returns clustered results showing which topics span which accounts, identifies emails that may have been sent to the wrong account, and provides context about which 'persona' senders are addressing.")]
    public async Task<string> GetContextualEmailSummary(
        [Description("Topic keywords to focus on, comma-separated (e.g. 'invoice, budget'). If omitted, analyzes all recent emails across built-in topic categories.")] string? topics = null,
        [Description("Number of emails to fetch per account for analysis (default 50)")] int countPerAccount = 50,
        [Description("If true, only analyze unread emails")] bool unreadOnly = false,
        [Description("If true, include a 150-character body preview for each sample email in the results")] bool includeBodyPreview = false,
        [Description("Maximum number of sample emails to include per topic cluster (default 5)")] int maxSamplesPerCluster = 5)
    {
        logger.LogInformation(
            "Getting contextual email summary: topics={Topics}, countPerAccount={Count}, unreadOnly={UnreadOnly}",
            topics, countPerAccount, unreadOnly);

        try
        {
            // Get all enabled accounts
            var accounts = (await accountRegistry.GetAllAccountsAsync()).ToList();
            
            if (accounts.Count == 0)
            {
                return JsonSerializer.Serialize(new { error = "No accounts configured" }, JsonOptions);
            }

            // Parse topic keywords
            var searchKeywords = ParseTopics(topics);
            
            // Fetch emails from all accounts in parallel
            var allEmails = await FetchEmailsFromAllAccountsAsync(accounts, countPerAccount, unreadOnly, searchKeywords);
            
            if (allEmails.Count == 0)
            {
                return JsonSerializer.Serialize(new
                {
                    message = "No emails found matching criteria",
                    searchKeywords,
                    accountsSearched = accounts.Count
                }, JsonOptions);
            }

            // Build the contextual summary
            var summary = BuildContextualSummary(
                allEmails, 
                accounts, 
                searchKeywords, 
                includeBodyPreview, 
                maxSamplesPerCluster);

            logger.LogInformation(
                "Built contextual summary: {TotalEmails} emails, {ClusterCount} clusters, {MismatchCount} mismatches, {PersonaCount} personas",
                summary.TotalEmails, summary.TopicClusters.Count, summary.AccountMismatches.Count, summary.PersonaContexts.Count);

            return JsonSerializer.Serialize(summary, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in get_contextual_email_summary tool");
            return JsonSerializer.Serialize(new
            {
                error = "Failed to get contextual email summary",
                message = ex.Message
            }, JsonOptions);
        }
    }

    private static List<string> ParseTopics(string? topics)
    {
        if (string.IsNullOrWhiteSpace(topics))
            return new List<string>();
        
        return topics
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();
    }

    private async Task<List<EmailMessage>> FetchEmailsFromAllAccountsAsync(
        List<AccountInfo> accounts,
        int countPerAccount,
        bool unreadOnly,
        List<string> searchKeywords)
    {
        var tasks = accounts.Select(async account =>
        {
            try
            {
                var provider = providerFactory.GetProvider(account.Provider);
                
                // If we have search keywords, use search; otherwise get recent emails
                if (searchKeywords.Count > 0)
                {
                    var query = string.Join(" OR ", searchKeywords);
                    return await provider.SearchEmailsAsync(
                        account.Id, query, countPerAccount, null, null, CancellationToken.None);
                }
                else
                {
                    return await provider.GetEmailsAsync(
                        account.Id, countPerAccount, unreadOnly, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error fetching emails from account {AccountId}", account.Id);
                return Enumerable.Empty<EmailMessage>();
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.SelectMany(e => e).ToList();
    }

    private ContextualEmailSummary BuildContextualSummary(
        List<EmailMessage> emails,
        List<AccountInfo> accounts,
        List<string> searchKeywords,
        bool includeBodyPreview,
        int maxSamplesPerCluster)
    {
        // Cluster emails by topic
        var topicClusters = ClusterEmailsByTopic(emails, searchKeywords, includeBodyPreview, maxSamplesPerCluster);
        
        // Detect account mismatches
        var mismatches = DetectAccountMismatches(emails, accounts, includeBodyPreview);
        
        // Build persona contexts
        var personaContexts = BuildPersonaContexts(emails, accounts, topicClusters);

        return new ContextualEmailSummary
        {
            TotalEmails = emails.Count,
            AccountsSearched = accounts.Count,
            SearchKeywords = searchKeywords,
            TopicClusters = topicClusters,
            AccountMismatches = mismatches,
            PersonaContexts = personaContexts
        };
    }

    private List<EmailTopicCluster> ClusterEmailsByTopic(
        List<EmailMessage> emails,
        List<string> searchKeywords,
        bool includeBodyPreview,
        int maxSamplesPerCluster)
    {
        var clusters = new Dictionary<string, List<EmailMessage>>(StringComparer.OrdinalIgnoreCase);
        var emailsAssigned = new HashSet<string>();

        // First, try to match against known topic patterns
        foreach (var (topicName, keywords) in TopicKeywords)
        {
            var matchingEmails = emails
                .Where(e => !emailsAssigned.Contains(e.Id))
                .Where(e => MatchesKeywords(e, keywords))
                .ToList();

            if (matchingEmails.Count > 0)
            {
                clusters[topicName] = matchingEmails;
                foreach (var email in matchingEmails)
                    emailsAssigned.Add(email.Id);
            }
        }

        // If user provided custom search keywords, create clusters for those
        foreach (var keyword in searchKeywords)
        {
            var matchingEmails = emails
                .Where(e => !emailsAssigned.Contains(e.Id))
                .Where(e => MatchesKeywords(e, new[] { keyword }))
                .ToList();

            if (matchingEmails.Count > 0)
            {
                var topicName = $"Custom: {keyword}";
                clusters[topicName] = matchingEmails;
                foreach (var email in matchingEmails)
                    emailsAssigned.Add(email.Id);
            }
        }

        // Put remaining emails in "Other" cluster
        var unassignedEmails = emails.Where(e => !emailsAssigned.Contains(e.Id)).ToList();
        if (unassignedEmails.Count > 0)
        {
            clusters["Other/General"] = unassignedEmails;
        }

        // Convert to EmailTopicCluster objects
        return clusters
            .Where(kvp => kvp.Value.Count > 0)
            .Select(kvp => BuildCluster(kvp.Key, kvp.Value, includeBodyPreview, maxSamplesPerCluster))
            .OrderByDescending(c => c.UnreadCount)
            .ThenByDescending(c => c.EmailCount)
            .ToList();
    }

    private static bool MatchesKeywords(EmailMessage email, string[] keywords)
    {
        var searchText = $"{email.Subject} {email.Body}".ToLowerInvariant();
        return keywords.Any(k => searchText.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private static EmailTopicCluster BuildCluster(
        string topicName,
        List<EmailMessage> emails,
        bool includeBodyPreview,
        int maxSamples)
    {
        var sortedEmails = emails.OrderByDescending(e => e.ReceivedDateTime).ToList();
        
        // Extract keywords that appear frequently in this cluster
        var extractedKeywords = ExtractFrequentKeywords(emails)
            .Take(5)
            .ToList();

        return new EmailTopicCluster
        {
            Topic = topicName,
            Keywords = extractedKeywords,
            EmailCount = emails.Count,
            UnreadCount = emails.Count(e => !e.IsRead),
            AccountIds = emails.Select(e => e.AccountId).Distinct().ToList(),
            EarliestDate = sortedEmails.LastOrDefault()?.ReceivedDateTime,
            LatestDate = sortedEmails.FirstOrDefault()?.ReceivedDateTime,
            UniqueSenders = emails.Select(e => e.From).Distinct().Take(10).ToList(),
            SampleEmails = sortedEmails
                .Take(maxSamples)
                .Select(e => ToSummaryItem(e, includeBodyPreview))
                .ToList()
        };
    }

    private static List<string> ExtractFrequentKeywords(List<EmailMessage> emails)
    {
        // Simple keyword extraction from subjects
        var wordCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with",
            "is", "are", "was", "were", "be", "been", "being", "have", "has", "had", "do", "does",
            "did", "will", "would", "could", "should", "may", "might", "can", "this", "that",
            "these", "those", "i", "you", "he", "she", "it", "we", "they", "my", "your", "his",
            "her", "its", "our", "their", "re", "fw", "fwd"
        };

        foreach (var email in emails)
        {
            var words = WordSplitRegex().Split(email.Subject.ToLowerInvariant())
                .Where(w => w.Length > 2 && !stopWords.Contains(w));
            
            foreach (var word in words)
            {
                wordCounts.TryGetValue(word, out var count);
                wordCounts[word] = count + 1;
            }
        }

        return wordCounts
            .Where(kvp => kvp.Value >= 2) // Must appear at least twice
            .OrderByDescending(kvp => kvp.Value)
            .Take(10)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    private List<AccountMismatch> DetectAccountMismatches(
        List<EmailMessage> emails,
        List<AccountInfo> accounts,
        bool includeBodyPreview)
    {
        var mismatches = new List<AccountMismatch>();
        var accountLookup = accounts.ToDictionary(a => a.Id);
        
        // Build domain-to-account mapping
        var domainToAccounts = new Dictionary<string, List<AccountInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in accounts)
        {
            foreach (var domain in account.Domains)
            {
                if (!domainToAccounts.TryGetValue(domain, out var list))
                {
                    list = new List<AccountInfo>();
                    domainToAccounts[domain] = list;
                }
                list.Add(account);
            }
        }

        foreach (var email in emails)
        {
            if (!accountLookup.TryGetValue(email.AccountId, out var receivedAccount))
                continue;

            // Check if sender domain suggests a different account
            var senderDomain = ExtractDomain(email.From);
            if (string.IsNullOrEmpty(senderDomain))
                continue;

            // Case 1: Sender domain matches a different account's domain
            if (domainToAccounts.TryGetValue(senderDomain, out var matchingAccounts))
            {
                var expectedAccount = matchingAccounts.FirstOrDefault(a => a.Id != email.AccountId);
                if (expectedAccount != null)
                {
                    // Internal email sent to wrong account
                    mismatches.Add(new AccountMismatch
                    {
                        Email = ToSummaryItem(email, includeBodyPreview),
                        ReceivedOnAccount = email.AccountId,
                        ExpectedAccount = expectedAccount.Id,
                        Reason = $"Sender from {senderDomain} typically communicates via {expectedAccount.DisplayName}",
                        Confidence = 0.8
                    });
                }
            }

            // Case 2: Email mentions a different account's domain in subject/body
            foreach (var account in accounts)
            {
                if (account.Id == email.AccountId) continue;
                
                foreach (var domain in account.Domains)
                {
                    if (email.Subject.Contains(domain, StringComparison.OrdinalIgnoreCase) ||
                        email.Body.Contains($"@{domain}", StringComparison.OrdinalIgnoreCase))
                    {
                        mismatches.Add(new AccountMismatch
                        {
                            Email = ToSummaryItem(email, includeBodyPreview),
                            ReceivedOnAccount = email.AccountId,
                            ExpectedAccount = account.Id,
                            Reason = $"Email mentions {domain} which is associated with {account.DisplayName}",
                            Confidence = 0.5
                        });
                        break;
                    }
                }
            }
        }

        return mismatches
            .OrderByDescending(m => m.Confidence)
            .ThenByDescending(m => m.Email.ReceivedDateTime)
            .Take(20) // Limit to top 20 mismatches
            .ToList();
    }

    private static List<PersonaContext> BuildPersonaContexts(
        List<EmailMessage> emails,
        List<AccountInfo> accounts,
        List<EmailTopicCluster> clusters)
    {
        return accounts.Select(account =>
        {
            var accountEmails = emails.Where(e => e.AccountId == account.Id).ToList();
            
            // Find which topics this persona receives emails about
            var topicsForAccount = clusters
                .Where(c => c.AccountIds.Contains(account.Id))
                .Select(c => c.Topic)
                .Take(5)
                .ToList();

            // Analyze sender domains
            var senderDomains = accountEmails
                .Select(e => ExtractDomain(e.From))
                .Where(d => !string.IsNullOrEmpty(d))
                .GroupBy(d => d!)
                .Select(g => new SenderDomainSummary
                {
                    Domain = g.Key,
                    EmailCount = g.Count(),
                    IsInternalDomain = account.Domains.Contains(g.Key, StringComparer.OrdinalIgnoreCase)
                })
                .OrderByDescending(s => s.EmailCount)
                .Take(5)
                .ToList();

            return new PersonaContext
            {
                AccountId = account.Id,
                PersonaName = account.DisplayName,
                Domains = account.Domains,
                EmailCount = accountEmails.Count,
                UnreadCount = accountEmails.Count(e => !e.IsRead),
                PrimaryTopics = topicsForAccount,
                TopSenderDomains = senderDomains
            };
        })
        .Where(p => p.EmailCount > 0)
        .OrderByDescending(p => p.UnreadCount)
        .ThenByDescending(p => p.EmailCount)
        .ToList();
    }

    private static string? ExtractDomain(string email)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex < 0 || atIndex >= email.Length - 1)
            return null;
        
        var domain = email[(atIndex + 1)..].Trim().TrimEnd('>').ToLowerInvariant();
        return string.IsNullOrEmpty(domain) ? null : domain;
    }

    private static EmailSummaryItem ToSummaryItem(EmailMessage email, bool includeBodyPreview)
    {
        var bodyPreview = string.Empty;
        if (includeBodyPreview && !string.IsNullOrEmpty(email.Body))
        {
            // Strip HTML if needed and take first ~150 chars
            var plainText = StripHtml(email.Body);
            bodyPreview = plainText.Length > 150 
                ? plainText[..150].Trim() + "..." 
                : plainText.Trim();
        }

        return new EmailSummaryItem
        {
            Id = email.Id,
            AccountId = email.AccountId,
            Subject = email.Subject,
            From = email.From,
            FromName = email.FromName,
            ReceivedDateTime = email.ReceivedDateTime,
            IsRead = email.IsRead,
            HasAttachments = email.HasAttachments,
            BodyPreview = bodyPreview
        };
    }

    private static string StripHtml(string html)
    {
        // Basic HTML stripping
        var text = HtmlTagRegex().Replace(html, " ");
        text = HtmlEntityRegex().Replace(text, " ");
        text = MultiSpaceRegex().Replace(text, " ");
        return text.Trim();
    }

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();
    
    [GeneratedRegex(@"&\w+;")]
    private static partial Regex HtmlEntityRegex();
    
    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpaceRegex();
    
    [GeneratedRegex(@"\W+")]
    private static partial Regex WordSplitRegex();
}
