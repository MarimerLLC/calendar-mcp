using System.Text.Json.Serialization;

namespace Adjutant.Core.Models;

/// <summary>
/// Represents a single email item in bulk operations, identifying the account and email message.
/// </summary>
public sealed class BulkEmailItem
{
    [JsonPropertyName("accountId")]
    public string AccountId { get; set; } = "";

    [JsonPropertyName("emailId")]
    public string EmailId { get; set; } = "";
}
