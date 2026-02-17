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
        [Description("JSON array of objects with 'accountId' and 'emailId' fields. Example: [{\"accountId\":\"acct1\",\"emailId\":\"msg1\"}]. Maximum 50 items.")] string items,
        [Description("Destination folder or label for all emails: 'archive', 'inbox', 'trash', 'spam', 'drafts' (Microsoft only), 'sentitems' (Microsoft only), or custom label ID (Google only). Aliases: 'deleteditems'='trash', 'junkemail'='spam' (required)")] string destination)
    {
        logger.LogInformation("Bulk moving emails to {Destination}", destination);

        try
        {
            if (string.IsNullOrEmpty(destination))
            {
                return JsonSerializer.Serialize(new { error = "destination is required" });
            }

            BulkEmailItem[]? parsedItems;
            try
            {
                parsedItems = JsonSerializer.Deserialize<BulkEmailItem[]>(items);
            }
            catch (JsonException ex)
            {
                return JsonSerializer.Serialize(new { error = "Invalid items JSON format", message = ex.Message });
            }

            if (parsedItems == null || parsedItems.Length == 0)
            {
                return JsonSerializer.Serialize(new { error = "items array must not be empty" });
            }

            if (parsedItems.Length > MaxBatchSize)
            {
                return JsonSerializer.Serialize(new { error = $"Batch size {parsedItems.Length} exceeds maximum of {MaxBatchSize}" });
            }

            // Validate all items have required fields
            foreach (var item in parsedItems)
            {
                if (string.IsNullOrEmpty(item.AccountId) || string.IsNullOrEmpty(item.EmailId))
                {
                    return JsonSerializer.Serialize(new { error = "Each item must have 'accountId' and 'emailId' fields" });
                }
            }

            // Resolve accounts once per unique accountId
            var accounts = await ResolveAccountsAsync(parsedItems);

            var results = await Task.WhenAll(parsedItems.Select(async item =>
            {
                await Throttle.WaitAsync();
                try
                {
                    if (!accounts.TryGetValue(item.AccountId, out var account))
                    {
                        return new BulkResultItem(item.EmailId, item.AccountId, false, $"Account '{item.AccountId}' not found");
                    }

                    var provider = providerFactory.GetProvider(account.Provider);
                    await provider.MoveEmailAsync(item.AccountId, item.EmailId, destination, CancellationToken.None);
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

            logger.LogInformation("Bulk move to '{Destination}' complete: {Succeeded} succeeded, {Failed} failed out of {Total}",
                destination, succeeded, failed, results.Length);

            return JsonSerializer.Serialize(new
            {
                totalRequested = results.Length,
                succeeded,
                failed,
                destination,
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

    private async Task<Dictionary<string, AccountInfo>> ResolveAccountsAsync(BulkEmailItem[] items)
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

    private sealed record BulkResultItem(string EmailId, string AccountId, bool Success, string? Error);
}
