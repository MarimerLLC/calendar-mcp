using System.Text.Json.Serialization;

namespace CalendarMcp.Core.Security;

/// <summary>
/// A credential that authorizes an MCP client to reach the HTTP server's MCP and
/// attachment endpoints.
///
/// Only the SHA-256 <see cref="Hash"/> of the secret is persisted — the secret itself is
/// shown once at creation and is unrecoverable afterwards. That means a leaked
/// <c>mcp-keys.json</c> does not hand an attacker working credentials.
/// </summary>
public sealed record McpApiKey
{
    /// <summary>Stable identifier used to revoke the key. Not a secret.</summary>
    public required string Id { get; init; }

    /// <summary>Operator-supplied description, e.g. "Claude Desktop - laptop".</summary>
    public required string Label { get; init; }

    /// <summary>Base64 SHA-256 of the UTF-8 secret, including its <c>cmcp_</c> prefix.</summary>
    public required string Hash { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }

    /// <summary>
    /// Last time this key authenticated a request. Persisted lazily (see
    /// <see cref="FileMcpKeyStore"/>) so a busy server doesn't rewrite the file per request,
    /// which means the stored value can lag real usage by a few minutes.
    /// </summary>
    public DateTimeOffset? LastUsedUtc { get; init; }

    /// <summary>Set when the key is revoked. Revoked keys are retained for audit, never deleted.</summary>
    public DateTimeOffset? RevokedUtc { get; init; }

    /// <summary>
    /// Derived from <see cref="RevokedUtc"/>. Kept out of the JSON so the stored file has one
    /// source of truth and cannot be edited into a contradictory state.
    /// </summary>
    [JsonIgnore]
    public bool IsActive => RevokedUtc is null;
}
