using CalendarMcp.Core.Models;
using Microsoft.Extensions.Logging;

namespace CalendarMcp.Core.Utilities;

/// <summary>
/// Executes unsubscribe actions: RFC 8058 one-click POST, or returns HTTPS/mailto info
/// </summary>
public sealed class UnsubscribeExecutor(
    IHttpClientFactory httpClientFactory,
    ILogger<UnsubscribeExecutor> logger)
{
    /// <summary>
    /// Execute one-click unsubscribe per RFC 8058: POST to HTTPS URL with List-Unsubscribe=One-Click body
    /// </summary>
    public async Task<UnsubscribeResult> ExecuteOneClickAsync(UnsubscribeInfo info, CancellationToken cancellationToken = default)
    {
        if (!info.SupportsOneClick || info.HttpsUrl == null)
        {
            return new UnsubscribeResult
            {
                Success = false,
                Method = "one-click",
                Message = "Email does not support one-click unsubscribe"
            };
        }

        // RFC 8058 requires HTTPS
        if (!info.HttpsUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return new UnsubscribeResult
            {
                Success = false,
                Method = "one-click",
                Message = "One-click unsubscribe requires HTTPS URL"
            };
        }

        try
        {
            var client = httpClientFactory.CreateClient("Unsubscribe");
            var content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("List-Unsubscribe", "One-Click")
            ]);

            logger.LogInformation("Executing one-click unsubscribe POST to {Url}", info.HttpsUrl);
            var response = await client.PostAsync(info.HttpsUrl, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("One-click unsubscribe succeeded for {Url}", info.HttpsUrl);
                return new UnsubscribeResult
                {
                    Success = true,
                    Method = "one-click",
                    Message = "Successfully unsubscribed via one-click (RFC 8058)"
                };
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("One-click unsubscribe returned {StatusCode} for {Url}: {Body}",
                response.StatusCode, info.HttpsUrl, responseBody);

            return new UnsubscribeResult
            {
                Success = false,
                Method = "one-click",
                Message = $"Unsubscribe request returned HTTP {(int)response.StatusCode}",
                ErrorDetails = responseBody
            };
        }
        catch (TaskCanceledException)
        {
            return new UnsubscribeResult
            {
                Success = false,
                Method = "one-click",
                Message = "Unsubscribe request timed out"
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing one-click unsubscribe to {Url}", info.HttpsUrl);
            return new UnsubscribeResult
            {
                Success = false,
                Method = "one-click",
                Message = "Failed to execute one-click unsubscribe",
                ErrorDetails = ex.Message
            };
        }
    }

    /// <summary>
    /// Parse mailto URL into components for sending via provider's SendEmailAsync
    /// </summary>
    public static (string to, string subject, string body)? ParseMailtoUrl(string mailtoUrl)
    {
        if (!mailtoUrl.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            return null;

        var rest = mailtoUrl[7..]; // strip "mailto:"
        var parts = rest.Split('?', 2);
        var to = Uri.UnescapeDataString(parts[0]);
        var subject = "Unsubscribe";
        var body = "Unsubscribe";

        if (parts.Length > 1)
        {
            var queryParams = parts[1].Split('&');
            foreach (var param in queryParams)
            {
                var kv = param.Split('=', 2);
                if (kv.Length != 2) continue;

                var key = Uri.UnescapeDataString(kv[0]);
                var value = Uri.UnescapeDataString(kv[1]);

                if (key.Equals("subject", StringComparison.OrdinalIgnoreCase))
                    subject = value;
                else if (key.Equals("body", StringComparison.OrdinalIgnoreCase))
                    body = value;
            }
        }

        return (to, subject, body);
    }
}
