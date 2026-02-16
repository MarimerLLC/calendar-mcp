# Unsubscribe Tool - Implementation Examples

**Related Documents**:
- Research: `/changelogs/2026-02-16-unsubscribe-research.md`
- Summary: `/changelogs/UNSUBSCRIBE_SUMMARY.md`
- Architecture: `/changelogs/UNSUBSCRIBE_ARCHITECTURE.md`

---

## Code Examples

This document provides concrete code examples for implementing the unsubscribe functionality.

### 1. Header Parsing Examples

**Input**: Raw List-Unsubscribe header
**Output**: Structured UnsubscribeInfo

```csharp
// Example headers from real emails
var headers = new[]
{
    // GitHub (RFC 8058)
    new { 
        ListUnsubscribe = "<https://github.com/notifications/unsubscribe/xyz123>",
        ListUnsubscribePost = "List-Unsubscribe=One-Click"
    },
    
    // Mailchimp (RFC 2369 + RFC 8058)
    new {
        ListUnsubscribe = "<mailto:unsubscribe@domain.com?subject=unsubscribe>, <https://domain.us1.list-manage.com/unsubscribe?u=xxx&id=yyy>",
        ListUnsubscribePost = "List-Unsubscribe=One-Click"
    },
    
    // Google Groups (RFC 2369 mailto only)
    new {
        ListUnsubscribe = "<mailto:group+unsubscribe@googlegroups.com>",
        ListUnsubscribePost = (string?)null
    },
    
    // Newsletter (RFC 2369 HTTPS only)
    new {
        ListUnsubscribe = "<https://example.com/preferences?email=user@example.com>",
        ListUnsubscribePost = (string?)null
    }
};

// Parse each
foreach (var header in headers)
{
    var info = UnsubscribeHeaderParser.ParseHeaders(
        header.ListUnsubscribe, 
        header.ListUnsubscribePost
    );
    
    Console.WriteLine($"Supports One-Click: {info.SupportsOneClick}");
    Console.WriteLine($"HTTPS URL: {info.HttpsUrl}");
    Console.WriteLine($"mailto URL: {info.MailtoUrl}");
}
```

### 2. UnsubscribeHeaderParser Implementation

```csharp
public static class UnsubscribeHeaderParser
{
    private static readonly Regex UrlRegex = new Regex(@"<([^>]+)>", RegexOptions.Compiled);
    
    public static UnsubscribeInfo ParseHeaders(
        string? listUnsubscribe, 
        string? listUnsubscribePost)
    {
        if (string.IsNullOrWhiteSpace(listUnsubscribe))
        {
            return new UnsubscribeInfo();
        }
        
        // Parse URLs from angle brackets: <url1>, <url2>, ...
        var urls = UrlRegex.Matches(listUnsubscribe)
            .Select(m => m.Groups[1].Value.Trim())
            .ToList();
        
        // Separate mailto: and https: URLs
        var httpsUrl = urls.FirstOrDefault(u => 
            u.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            u.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
        
        var mailtoUrl = urls.FirstOrDefault(u => 
            u.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase));
        
        // Check for RFC 8058 one-click support
        var supportsOneClick = !string.IsNullOrWhiteSpace(listUnsubscribePost) && 
                               !string.IsNullOrWhiteSpace(httpsUrl);
        
        return new UnsubscribeInfo
        {
            SupportsOneClick = supportsOneClick,
            HttpsUrl = httpsUrl,
            MailtoUrl = mailtoUrl,
            ListUnsubscribeHeader = listUnsubscribe,
            ListUnsubscribePostHeader = listUnsubscribePost
        };
    }
}
```

### 3. UnsubscribeExecutor Implementation

```csharp
public class UnsubscribeExecutor
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<UnsubscribeExecutor> _logger;
    
    public UnsubscribeExecutor(
        IHttpClientFactory httpClientFactory,
        ILogger<UnsubscribeExecutor> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }
    
    /// <summary>
    /// Execute RFC 8058 one-click unsubscribe
    /// </summary>
    public async Task<UnsubscribeResult> ExecuteOneClickAsync(
        string httpsUrl,
        CancellationToken cancellationToken = default)
    {
        // Validate URL
        if (!Uri.TryCreate(httpsUrl, UriKind.Absolute, out var uri))
        {
            return new UnsubscribeResult
            {
                Success = false,
                Method = "one-click",
                ErrorDetails = "Invalid URL format"
            };
        }
        
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return new UnsubscribeResult
            {
                Success = false,
                Method = "one-click",
                ErrorDetails = "RFC 8058 requires HTTPS"
            };
        }
        
        try
        {
            // Create HTTP client
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            
            // Prepare POST request per RFC 8058
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("List-Unsubscribe", "One-Click")
            });
            
            _logger.LogInformation("Executing one-click unsubscribe to {Url}", httpsUrl);
            
            // Send POST request
            var response = await client.PostAsync(uri, content, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("One-click unsubscribe successful: {StatusCode}", response.StatusCode);
                return new UnsubscribeResult
                {
                    Success = true,
                    Method = "one-click",
                    Message = $"Successfully unsubscribed (HTTP {(int)response.StatusCode})"
                };
            }
            else
            {
                _logger.LogWarning("One-click unsubscribe failed: {StatusCode}", response.StatusCode);
                return new UnsubscribeResult
                {
                    Success = false,
                    Method = "one-click",
                    ErrorDetails = $"Server returned HTTP {(int)response.StatusCode}"
                };
            }
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("One-click unsubscribe timed out for {Url}", httpsUrl);
            return new UnsubscribeResult
            {
                Success = false,
                Method = "one-click",
                ErrorDetails = "Request timed out (10 seconds)"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing one-click unsubscribe to {Url}", httpsUrl);
            return new UnsubscribeResult
            {
                Success = false,
                Method = "one-click",
                ErrorDetails = ex.Message
            };
        }
    }
    
    /// <summary>
    /// Parse mailto: URL into components
    /// </summary>
    public (string recipient, string? subject, string? body) ParseMailtoUrl(string mailtoUrl)
    {
        // mailto:recipient?subject=xxx&body=yyy
        if (!mailtoUrl.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Not a mailto URL", nameof(mailtoUrl));
        }
        
        var withoutProtocol = mailtoUrl.Substring(7); // Remove "mailto:"
        var parts = withoutProtocol.Split('?');
        var recipient = parts[0];
        
        string? subject = null;
        string? body = null;
        
        if (parts.Length > 1)
        {
            var queryParams = parts[1].Split('&');
            foreach (var param in queryParams)
            {
                var keyValue = param.Split('=');
                if (keyValue.Length == 2)
                {
                    var key = Uri.UnescapeDataString(keyValue[0]);
                    var value = Uri.UnescapeDataString(keyValue[1]);
                    
                    if (key.Equals("subject", StringComparison.OrdinalIgnoreCase))
                    {
                        subject = value;
                    }
                    else if (key.Equals("body", StringComparison.OrdinalIgnoreCase))
                    {
                        body = value;
                    }
                }
            }
        }
        
        return (recipient, subject, body);
    }
}
```

### 4. Provider Implementation - Microsoft Graph

```csharp
// M365ProviderService.cs
public async Task<EmailMessage?> GetEmailWithHeadersAsync(
    string accountId, 
    string emailId, 
    CancellationToken cancellationToken = default)
{
    var token = await GetAccessTokenAsync(accountId, cancellationToken);
    if (token == null)
    {
        return null;
    }

    try
    {
        var authProvider = new BearerTokenAuthenticationProvider(token);
        var graphClient = new GraphServiceClient(authProvider);

        // Get message with headers
        var message = await graphClient.Me.Messages[emailId].GetAsync(config =>
        {
            config.QueryParameters.Select = new[] { 
                "id", 
                "subject", 
                "from", 
                "toRecipients", 
                "ccRecipients",
                "receivedDateTime", 
                "isRead", 
                "hasAttachments", 
                "body",
                "internetMessageHeaders"  // KEY: Get headers
            };
        }, cancellationToken);

        if (message == null)
        {
            return null;
        }

        // Extract unsubscribe headers
        var listUnsubscribe = message.InternetMessageHeaders
            ?.FirstOrDefault(h => h.Name.Equals("List-Unsubscribe", StringComparison.OrdinalIgnoreCase))
            ?.Value;

        var listUnsubscribePost = message.InternetMessageHeaders
            ?.FirstOrDefault(h => h.Name.Equals("List-Unsubscribe-Post", StringComparison.OrdinalIgnoreCase))
            ?.Value;

        // Parse unsubscribe info
        var unsubscribeInfo = UnsubscribeHeaderParser.ParseHeaders(listUnsubscribe, listUnsubscribePost);

        // Build EmailMessage with unsubscribe info
        return new EmailMessage
        {
            Id = message.Id ?? string.Empty,
            AccountId = accountId,
            Subject = message.Subject ?? string.Empty,
            From = message.From?.EmailAddress?.Address ?? string.Empty,
            FromName = message.From?.EmailAddress?.Name ?? string.Empty,
            To = message.ToRecipients?.Select(r => r.EmailAddress?.Address ?? string.Empty).ToList() ?? [],
            Cc = message.CcRecipients?.Select(r => r.EmailAddress?.Address ?? string.Empty).ToList() ?? [],
            Body = message.Body?.Content ?? string.Empty,
            BodyFormat = message.Body?.ContentType?.ToString()?.ToLowerInvariant() ?? "text",
            ReceivedDateTime = message.ReceivedDateTime?.DateTime ?? DateTime.MinValue,
            IsRead = message.IsRead ?? false,
            HasAttachments = message.HasAttachments ?? false,
            UnsubscribeInfo = unsubscribeInfo  // NEW
        };
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error fetching email with headers from M365 account {AccountId}", accountId);
        return null;
    }
}
```

### 5. Provider Implementation - Gmail

```csharp
// GoogleProviderService.cs
public async Task<EmailMessage?> GetEmailWithHeadersAsync(
    string accountId, 
    string emailId, 
    CancellationToken cancellationToken = default)
{
    var credential = await GetCredentialAsync(accountId, cancellationToken);
    if (credential == null)
    {
        return null;
    }

    try
    {
        var service = CreateGmailService(credential);
        
        // Get message with specific headers
        var request = service.Users.Messages.Get("me", emailId);
        request.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
        request.MetadataHeaders = new Repeatable<string>(new[] { 
            "List-Unsubscribe", 
            "List-Unsubscribe-Post",
            "From",
            "To",
            "Cc",
            "Subject",
            "Date"
        });

        var message = await request.ExecuteAsync(cancellationToken);

        // Extract headers
        var headers = message.Payload?.Headers ?? new List<MessagePartHeader>();
        
        var listUnsubscribe = headers
            .FirstOrDefault(h => h.Name.Equals("List-Unsubscribe", StringComparison.OrdinalIgnoreCase))
            ?.Value;
        
        var listUnsubscribePost = headers
            .FirstOrDefault(h => h.Name.Equals("List-Unsubscribe-Post", StringComparison.OrdinalIgnoreCase))
            ?.Value;

        // Parse unsubscribe info
        var unsubscribeInfo = UnsubscribeHeaderParser.ParseHeaders(listUnsubscribe, listUnsubscribePost);

        // Build EmailMessage with unsubscribe info
        return new EmailMessage
        {
            Id = message.Id,
            AccountId = accountId,
            Subject = headers.FirstOrDefault(h => h.Name == "Subject")?.Value ?? string.Empty,
            From = headers.FirstOrDefault(h => h.Name == "From")?.Value ?? string.Empty,
            FromName = ExtractNameFromEmailHeader(headers.FirstOrDefault(h => h.Name == "From")?.Value),
            To = ParseEmailList(headers.FirstOrDefault(h => h.Name == "To")?.Value),
            Cc = ParseEmailList(headers.FirstOrDefault(h => h.Name == "Cc")?.Value),
            Body = string.Empty, // Not included in metadata format
            BodyFormat = "text",
            ReceivedDateTime = ParseGmailDate(headers.FirstOrDefault(h => h.Name == "Date")?.Value),
            IsRead = !message.LabelIds?.Contains("UNREAD") ?? true,
            HasAttachments = message.Payload?.Parts?.Any(p => !string.IsNullOrEmpty(p.Filename)) ?? false,
            UnsubscribeInfo = unsubscribeInfo  // NEW
        };
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error fetching email with headers from Google account {AccountId}", accountId);
        return null;
    }
}
```

### 6. MCP Tool - Get Unsubscribe Info

```csharp
[McpServerToolType]
public sealed class GetUnsubscribeInfoTool
{
    private readonly IAccountRegistry _accountRegistry;
    private readonly IProviderServiceFactory _providerFactory;
    private readonly ILogger<GetUnsubscribeInfoTool> _logger;
    
    public GetUnsubscribeInfoTool(
        IAccountRegistry accountRegistry,
        IProviderServiceFactory providerFactory,
        ILogger<GetUnsubscribeInfoTool> logger)
    {
        _accountRegistry = accountRegistry;
        _providerFactory = providerFactory;
        _logger = logger;
    }

    [McpServerTool(Name = "get_unsubscribe_info"),
     Description("Get unsubscribe information for an email without executing unsubscribe. Returns available methods (one-click, HTTPS, mailto) from List-Unsubscribe headers. Use this to inspect what unsubscribe options are available before taking action.")]
    public async Task<string> GetUnsubscribeInfo(
        [Description("Account ID containing the email")] string accountId,
        [Description("Email ID to check for unsubscribe options")] string emailId)
    {
        _logger.LogInformation("Getting unsubscribe info for email {EmailId} in account {AccountId}", 
            emailId, accountId);

        try
        {
            // Validate account
            var account = await _accountRegistry.GetAccountAsync(accountId);
            if (account == null)
            {
                return JsonSerializer.Serialize(new
                {
                    error = "Account not found",
                    accountId
                }, JsonOptions);
            }

            // Get provider
            var provider = _providerFactory.GetProviderService(account.Provider);

            // Get email with headers
            var email = await provider.GetEmailWithHeadersAsync(accountId, emailId);
            
            if (email == null)
            {
                return JsonSerializer.Serialize(new
                {
                    error = "Email not found",
                    emailId,
                    accountId
                }, JsonOptions);
            }

            // Check if unsubscribe info is available
            if (email.UnsubscribeInfo == null || 
                (string.IsNullOrEmpty(email.UnsubscribeInfo.HttpsUrl) && 
                 string.IsNullOrEmpty(email.UnsubscribeInfo.MailtoUrl)))
            {
                return JsonSerializer.Serialize(new
                {
                    hasUnsubscribe = false,
                    message = "This email does not contain standard List-Unsubscribe headers",
                    emailId,
                    accountId,
                    subject = email.Subject,
                    from = email.From
                }, JsonOptions);
            }

            // Return unsubscribe info
            return JsonSerializer.Serialize(new
            {
                hasUnsubscribe = true,
                emailId,
                accountId,
                subject = email.Subject,
                from = email.From,
                unsubscribeInfo = new
                {
                    supportsOneClick = email.UnsubscribeInfo.SupportsOneClick,
                    httpsUrl = email.UnsubscribeInfo.HttpsUrl,
                    mailtoUrl = email.UnsubscribeInfo.MailtoUrl,
                    recommendedMethod = email.UnsubscribeInfo.SupportsOneClick ? "one-click" :
                                       !string.IsNullOrEmpty(email.UnsubscribeInfo.HttpsUrl) ? "https" :
                                       !string.IsNullOrEmpty(email.UnsubscribeInfo.MailtoUrl) ? "mailto" : "none"
                }
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting unsubscribe info for email {EmailId}", emailId);
            return JsonSerializer.Serialize(new
            {
                error = "Failed to get unsubscribe info",
                message = ex.Message
            }, JsonOptions);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
```

### 7. MCP Tool - Unsubscribe From Email

```csharp
[McpServerToolType]
public sealed class UnsubscribeFromEmailTool
{
    private readonly IAccountRegistry _accountRegistry;
    private readonly IProviderServiceFactory _providerFactory;
    private readonly UnsubscribeExecutor _executor;
    private readonly ILogger<UnsubscribeFromEmailTool> _logger;
    
    public UnsubscribeFromEmailTool(
        IAccountRegistry accountRegistry,
        IProviderServiceFactory providerFactory,
        UnsubscribeExecutor executor,
        ILogger<UnsubscribeFromEmailTool> logger)
    {
        _accountRegistry = accountRegistry;
        _providerFactory = providerFactory;
        _executor = executor;
        _logger = logger;
    }

    [McpServerTool(Name = "unsubscribe_from_email"),
     Description("Unsubscribe from an email mailing list using standard methods (RFC 2369/8058). Supports one-click unsubscribe (RFC 8058), HTTPS links, and mailto methods. In 'auto' mode, tries one-click first (if available), then falls back to returning HTTPS/mailto URLs for manual action.")]
    public async Task<string> UnsubscribeFromEmail(
        [Description("Account ID containing the email")] string accountId,
        [Description("Email ID to unsubscribe from")] string emailId,
        [Description("Unsubscribe method: 'auto' (default, tries one-click then falls back), 'one-click' (RFC 8058 only), 'https' (return URL), 'mailto' (send email). Default: 'auto'")] 
        string method = "auto",
        [Description("For 'https' method, whether to return URL for manual action instead of attempting automatic action. Default: false")] 
        bool manualConfirmation = false)
    {
        _logger.LogInformation(
            "Unsubscribing from email {EmailId} in account {AccountId} using method {Method}", 
            emailId, accountId, method);

        try
        {
            // Validate account
            var account = await _accountRegistry.GetAccountAsync(accountId);
            if (account == null)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "Account not found",
                    accountId
                }, JsonOptions);
            }

            // Get provider
            var provider = _providerFactory.GetProviderService(account.Provider);

            // Get email with headers
            var email = await provider.GetEmailWithHeadersAsync(accountId, emailId);
            
            if (email == null)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "Email not found",
                    emailId,
                    accountId
                }, JsonOptions);
            }

            // Check if unsubscribe info is available
            if (email.UnsubscribeInfo == null || 
                (string.IsNullOrEmpty(email.UnsubscribeInfo.HttpsUrl) && 
                 string.IsNullOrEmpty(email.UnsubscribeInfo.MailtoUrl)))
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "No unsubscribe information found",
                    message = "This email does not contain standard List-Unsubscribe headers",
                    suggestion = "You may need to manually find unsubscribe link in email body"
                }, JsonOptions);
            }

            // Execute based on method
            UnsubscribeResult result;
            
            switch (method.ToLowerInvariant())
            {
                case "auto":
                    result = await ExecuteAutoAsync(email.UnsubscribeInfo, provider, accountId);
                    break;
                    
                case "one-click":
                    if (!email.UnsubscribeInfo.SupportsOneClick || 
                        string.IsNullOrEmpty(email.UnsubscribeInfo.HttpsUrl))
                    {
                        result = new UnsubscribeResult
                        {
                            Success = false,
                            Method = "one-click",
                            ErrorDetails = "Email does not support one-click unsubscribe (RFC 8058)"
                        };
                    }
                    else
                    {
                        result = await _executor.ExecuteOneClickAsync(email.UnsubscribeInfo.HttpsUrl);
                    }
                    break;
                    
                case "https":
                    if (string.IsNullOrEmpty(email.UnsubscribeInfo.HttpsUrl))
                    {
                        result = new UnsubscribeResult
                        {
                            Success = false,
                            Method = "https",
                            ErrorDetails = "No HTTPS unsubscribe URL found"
                        };
                    }
                    else
                    {
                        result = new UnsubscribeResult
                        {
                            Success = true,
                            Method = "https",
                            Message = $"Unsubscribe URL: {email.UnsubscribeInfo.HttpsUrl}. Please visit this URL to complete unsubscribe."
                        };
                    }
                    break;
                    
                case "mailto":
                    if (string.IsNullOrEmpty(email.UnsubscribeInfo.MailtoUrl))
                    {
                        result = new UnsubscribeResult
                        {
                            Success = false,
                            Method = "mailto",
                            ErrorDetails = "No mailto unsubscribe address found"
                        };
                    }
                    else
                    {
                        result = await ExecuteMailtoAsync(
                            email.UnsubscribeInfo.MailtoUrl, 
                            provider, 
                            accountId);
                    }
                    break;
                    
                default:
                    result = new UnsubscribeResult
                    {
                        Success = false,
                        Method = method,
                        ErrorDetails = $"Unknown method: {method}. Use 'auto', 'one-click', 'https', or 'mailto'"
                    };
                    break;
            }

            return JsonSerializer.Serialize(new
            {
                success = result.Success,
                method = result.Method,
                message = result.Message,
                errorDetails = result.ErrorDetails,
                emailInfo = new
                {
                    emailId,
                    accountId,
                    subject = email.Subject,
                    from = email.From
                }
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unsubscribing from email {EmailId}", emailId);
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Failed to unsubscribe",
                message = ex.Message
            }, JsonOptions);
        }
    }

    private async Task<UnsubscribeResult> ExecuteAutoAsync(
        UnsubscribeInfo info, 
        IProviderService provider, 
        string accountId)
    {
        // Try one-click first (best method)
        if (info.SupportsOneClick && !string.IsNullOrEmpty(info.HttpsUrl))
        {
            var result = await _executor.ExecuteOneClickAsync(info.HttpsUrl);
            if (result.Success)
            {
                return result;
            }
            
            _logger.LogWarning("One-click failed, falling back: {Error}", result.ErrorDetails);
        }

        // Fallback to HTTPS manual
        if (!string.IsNullOrEmpty(info.HttpsUrl))
        {
            return new UnsubscribeResult
            {
                Success = true,
                Method = "https",
                Message = $"One-click not available. Please visit: {info.HttpsUrl}"
            };
        }

        // Fallback to mailto
        if (!string.IsNullOrEmpty(info.MailtoUrl))
        {
            return await ExecuteMailtoAsync(info.MailtoUrl, provider, accountId);
        }

        return new UnsubscribeResult
        {
            Success = false,
            Method = "auto",
            ErrorDetails = "No viable unsubscribe method found"
        };
    }

    private async Task<UnsubscribeResult> ExecuteMailtoAsync(
        string mailtoUrl, 
        IProviderService provider, 
        string accountId)
    {
        try
        {
            var (recipient, subject, body) = _executor.ParseMailtoUrl(mailtoUrl);
            
            // Send unsubscribe email using provider
            await provider.SendEmailAsync(
                accountId,
                new[] { recipient },
                subject ?? "Unsubscribe",
                body ?? "Please unsubscribe me from this mailing list.",
                null);

            return new UnsubscribeResult
            {
                Success = true,
                Method = "mailto",
                Message = $"Unsubscribe email sent to {recipient}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending mailto unsubscribe");
            return new UnsubscribeResult
            {
                Success = false,
                Method = "mailto",
                ErrorDetails = ex.Message
            };
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
```

---

## Usage Examples (AI Assistant Perspective)

### Example 1: Check if email can be unsubscribed

**User**: "Can I unsubscribe from this GitHub notification?"

**AI Assistant**:
```
get_unsubscribe_info(
    accountId: "work-account",
    emailId: "msg-12345"
)

Response:
{
  "hasUnsubscribe": true,
  "subject": "New issue opened in repository",
  "from": "notifications@github.com",
  "unsubscribeInfo": {
    "supportsOneClick": true,
    "httpsUrl": "https://github.com/notifications/unsubscribe/xyz123",
    "mailtoUrl": null,
    "recommendedMethod": "one-click"
  }
}
```

### Example 2: Auto-unsubscribe (one-click)

**User**: "Unsubscribe me from this newsletter"

**AI Assistant**:
```
unsubscribe_from_email(
    accountId: "personal-account",
    emailId: "msg-67890",
    method: "auto"
)

Response:
{
  "success": true,
  "method": "one-click",
  "message": "Successfully unsubscribed (HTTP 200)",
  "emailInfo": {
    "subject": "Weekly Newsletter - January 2026",
    "from": "newsletter@example.com"
  }
}
```

### Example 3: Fallback to manual (HTTPS)

**User**: "Unsubscribe from this marketing email"

**AI Assistant**:
```
unsubscribe_from_email(
    accountId: "work-account",
    emailId: "msg-11111",
    method: "auto"
)

Response:
{
  "success": true,
  "method": "https",
  "message": "One-click not available. Please visit: https://example.com/preferences?email=user@example.com",
  "emailInfo": {
    "subject": "Special Offer Inside!",
    "from": "marketing@company.com"
  }
}
```

**AI Assistant to User**: "I found the unsubscribe link. Please visit this URL to complete the unsubscribe: https://example.com/preferences?email=user@example.com"

### Example 4: No unsubscribe available

**User**: "Unsubscribe from this email"

**AI Assistant**:
```
unsubscribe_from_email(
    accountId: "work-account",
    emailId: "msg-22222"
)

Response:
{
  "success": false,
  "error": "No unsubscribe information found",
  "message": "This email does not contain standard List-Unsubscribe headers",
  "suggestion": "You may need to manually find unsubscribe link in email body"
}
```

**AI Assistant to User**: "This email doesn't have a standard unsubscribe mechanism. Would you like me to get the full email content so you can manually look for an unsubscribe link?"

---

## Testing Checklist

- [ ] Unit test: Parse GitHub one-click header
- [ ] Unit test: Parse Mailchimp dual header (mailto + HTTPS)
- [ ] Unit test: Parse Google Groups mailto header
- [ ] Unit test: Handle malformed headers
- [ ] Unit test: Handle missing headers
- [ ] Integration test: M365 header retrieval
- [ ] Integration test: Gmail header retrieval
- [ ] Integration test: One-click POST execution (mock server)
- [ ] Integration test: mailto parsing
- [ ] End-to-end: Real GitHub unsubscribe
- [ ] End-to-end: Real Mailchimp unsubscribe
- [ ] End-to-end: Verify no more emails after unsubscribe
- [ ] Security test: HTTPS-only enforcement
- [ ] Security test: Timeout handling
- [ ] Security test: Invalid URL rejection

---

**Related Documents**:
- Full research: `/changelogs/2026-02-16-unsubscribe-research.md`
- Executive summary: `/changelogs/UNSUBSCRIBE_SUMMARY.md`
- Architecture: `/changelogs/UNSUBSCRIBE_ARCHITECTURE.md`
