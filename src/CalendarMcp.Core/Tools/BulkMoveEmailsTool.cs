using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// MCP tool for moving multiple emails to a folder in a single batch operation
/// </summary>
[McpServerToolType]
public sealed class BulkMoveEmailsTool(
    IAccountRegistry accountRegistry,
    IProviderServiceFactory providerFactory,
    ILogger<BulkMoveEmailsTool> logger)
{
    private static readonly SemaphoreSlim Throttle = new(10);
    private const int MaxBatchSize = 50;

    [McpServerTool, Description("Move multiple emails to a folder or apply labels in a single batch operation. More efficient than calling move_email repeatedly.")]
    public async Task<string> BulkMoveEmails(
        [Description("JSON array of objects with 'accountId' and 'emailId' fields, e.g. [{\"accountId\":\"acc1\",\"emailId\":\"msg1\"},{\"accountId\":\"acc1\",\"emailId\":\"msg2\"}]. Maximum 50 items.")] string emails,
        [Description("Destination folder or label for all emails: 'archive', 'inbox', 'trash', 'spam', 'drafts' (Microsoft only), 'sentitems' (Microsoft only), or custom label ID (Google only). Aliases: 'deleteditems'='trash', 'junkemail'='spam' (required)")] string destinationFolder)
    {
        logger.LogInformation("Bulk moving emails to {DestinationFolder}", destinationFolder);

        try
        {
            if (string.IsNullOrEmpty(destinationFolder))
            {
                return JsonSerializer.Serialize(new { error = "destinationFolder is required" });
            }

            var items = ParseEmailItems(emails);
            if (items == null)
            {
                return JsonSerializer.Serialize(new { error = "Invalid JSON. Expected an array of {\"accountId\":\"...\",\"emailId\":\"...\"} objects." });
            }

            if (items.Count == 0)
            {
                return JsonSerializer.Serialize(new { error = "emails array must not be empty" });
            }

            if (items.Count > MaxBatchSize)
            {
                return JsonSerializer.Serialize(new { error = $"Batch size {items.Count} exceeds maximum of {MaxBatchSize}" });
            }

            // Resolve accounts once per unique accountId
            var accounts = await ResolveAccountsAsync(items);

            var results = await Task.WhenAll(items.Select(async item =>
            {
                await Throttle.WaitAsync();
                try
                {
                    if (!accounts.TryGetValue(item.AccountId, out var account))
                    {
                        return new BulkResultItem(item.EmailId, item.AccountId, false, $"Account '{item.AccountId}' not found");
                    }

                    var provider = providerFactory.GetProvider(account.Provider);
                    await provider.MoveEmailAsync(item.AccountId, item.EmailId, destinationFolder, CancellationToken.None);
                    return new BulkResultItem(item.EmailId, item.AccountId, true, null);
                }
                catch (Exception ex)
                {
                    return new BulkResultItem(item.EmailId, item.AccountId, false, ex.Message);
                }
                finally
                {
                    Throttle.Release();
                }
            }));

            var succeeded = results.Count(r => r.Success);
            var failed = results.Count(r => !r.Success);

            logger.LogInformation("Bulk move to '{DestinationFolder}' complete: {Succeeded} succeeded, {Failed} failed out of {Total}",
                destinationFolder, succeeded, failed, results.Length);

            return JsonSerializer.Serialize(new
            {
                totalRequested = results.Length,
                succeeded,
                failed,
                destinationFolder,
                results = results.Select(r => r.Success
                    ? new { r.EmailId, r.AccountId, r.Success, error = (string?)null }
                    : new { r.EmailId, r.AccountId, r.Success, error = r.Error })
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in bulk_move_emails tool");
            return JsonSerializer.Serialize(new { error = "Failed to bulk move emails", message = ex.Message });
        }
    }

    private static List<EmailItem>? ParseEmailItems(string json)
    {
        try
        {
            var items = JsonSerializer.Deserialize<List<EmailItem>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (items == null) return null;

            foreach (var item in items)
            {
                if (string.IsNullOrEmpty(item.AccountId) || string.IsNullOrEmpty(item.EmailId))
                    return null;
            }

            return items;
        }
        catch
        {
            return null;
        }
    }

    private async Task<Dictionary<string, AccountInfo>> ResolveAccountsAsync(List<EmailItem> items)
    {
        var result = new Dictionary<string, AccountInfo>();
        foreach (var accountId in items.Select(i => i.AccountId).Distinct())
        {
            var account = await accountRegistry.GetAccountAsync(accountId);
            if (account != null)
                result[accountId] = account;
        }
        return result;
    }

    private sealed record EmailItem(string AccountId, string EmailId)
    {
        public string AccountId { get; init; } = AccountId ?? "";
        public string EmailId { get; init; } = EmailId ?? "";
    }

    private sealed record BulkResultItem(string EmailId, string AccountId, bool Success, string? Error);
}
