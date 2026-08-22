using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CalendarMcp.Core.Tools;

/// <summary>
/// Opaque calendar-event reference minted by <c>get_calendar_events</c> and
/// consumed only by <c>get_calendar_event_details</c> (MCP-05, D-20). Encodes
/// the account id alongside the provider's own event id so the detail action
/// never asks the caller for an <c>accountId</c> -- the account is resolved
/// server-side from the reference itself, and a missing or malformed
/// reference is rejected outright rather than silently resolved against a
/// default account.
/// </summary>
/// <remarks>
/// The encoding is an implementation detail private to this fork: callers
/// (Aura included) must treat the string as opaque and pass it back
/// byte-for-byte, exactly the discipline the design doc's <c>eventId</c>
/// contract requires. There is no numeric or precision surface in this path
/// -- it is a string round-trip, nothing more.
/// </remarks>
internal static class EventRef
{
    private sealed class Payload
    {
        [JsonPropertyName("a")]
        public string AccountId { get; set; } = "";

        [JsonPropertyName("e")]
        public string EventId { get; set; } = "";
    }

    /// <summary>
    /// Encodes an account id and the provider's own event id into a single
    /// opaque reference string.
    /// </summary>
    public static string Encode(string accountId, string eventId)
    {
        var json = JsonSerializer.Serialize(new Payload { AccountId = accountId, EventId = eventId });
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// Attempts to decode a reference previously produced by <see cref="Encode"/>.
    /// Returns <see langword="false"/> -- never throws -- for a missing,
    /// malformed, or foreign-looking reference, so the caller can reject it
    /// as a plain validation failure instead of resolving against a default
    /// account.
    /// </summary>
    public static bool TryDecode(string? reference, out string accountId, out string eventId)
    {
        accountId = "";
        eventId = "";

        if (string.IsNullOrEmpty(reference))
            return false;

        try
        {
            var bytes = Convert.FromBase64String(reference);
            var payload = JsonSerializer.Deserialize<Payload>(bytes);
            if (payload is null || string.IsNullOrEmpty(payload.AccountId) || string.IsNullOrEmpty(payload.EventId))
                return false;

            accountId = payload.AccountId;
            eventId = payload.EventId;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or DecoderFallbackException)
        {
            return false;
        }
    }
}
