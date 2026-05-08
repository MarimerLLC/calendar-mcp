using System.ComponentModel;
using System.Text.Json;
using CalendarMcp.Core.Models;
using CalendarMcp.Core.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// MCP tool for deleting multiple emails in a single batch operation
/// </summary>
[McpServerToolType]
public sealed class BulkDeleteEmailsTool(
    IAccountRegistry accountRegistry,
    IProviderServiceFactory providerFactory,
    ILogger<BulkDeleteEmailsTool> logger)
{
    private static readonly SemaphoreSlim Throttle = new(10);
    private const int MaxBatchSize = 50;

    [McpServerTool, Description("Delete multiple emails in a single batch operation. More efficient than calling delete_email repeatedly.")]
    public async Task<string> BulkDeleteEmails(
        [Description("Array of emails to delete, each with 'accountId' and 'emailId'. Maximum 50 items. Obtain values from get_emails or search_emails.")] BulkEmailItem[] items)
    {
        logger.LogInformation("Bulk deleting emails");

        if (items == null || items.Length == 0)
            throw new McpException("items array must not be empty");

        if (items.Length > MaxBatchSize)
            throw new McpException($"Batch size {items.Length} exceeds maximum of {MaxBatchSize}");

        // Validate all items have required fields
        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.AccountId) || string.IsNullOrEmpty(item.EmailId))
                throw new McpException("Each item must have 'accountId' and 'emailId' fields");
        }

        try
        {
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
                    await provider.DeleteEmailAsync(item.AccountId, item.EmailId, CancellationToken.None);
                    return new BulkResultItem(item.EmailId, item.AccountId, true, null);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error deleting email {EmailId} in account {AccountId}", item.EmailId, item.AccountId);
                    return new BulkResultItem(item.EmailId, item.AccountId, false, "Failed to delete email.");
                }
                finally
                {
                    Throttle.Release();
                }
            }));

            var succeeded = results.Count(r => r.Success);
            var failed = results.Count(r => !r.Success);

            logger.LogInformation("Bulk delete complete: {Succeeded} succeeded, {Failed} failed out of {Total}",
                succeeded, failed, results.Length);

            return JsonSerializer.Serialize(new
            {
                totalRequested = results.Length,
                succeeded,
                failed,
                results = results.Select(r => r.Success
                    ? new { r.EmailId, r.AccountId, r.Success, error = (string?)null }
                    : new { r.EmailId, r.AccountId, r.Success, error = r.Error })
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex) when (ex is not McpException)
        {
            logger.LogError(ex, "Error in bulk_delete_emails tool");
            throw new McpException("Failed to bulk delete emails.", ex);
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
