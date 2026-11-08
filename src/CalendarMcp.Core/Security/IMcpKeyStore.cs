namespace CalendarMcp.Core.Security;

/// <summary>
/// Stores and validates the API keys that guard the MCP and attachment endpoints.
/// </summary>
public interface IMcpKeyStore
{
    /// <summary>
    /// All persisted keys, active and revoked, newest first. Never includes the
    /// environment bootstrap key, which is not persisted and cannot be revoked.
    /// </summary>
    IReadOnlyList<McpApiKey> List();

    /// <summary>
    /// Mints a new key and persists its hash. The returned secret is the only time the
    /// caller can see it — it is not recoverable from the store afterwards.
    /// </summary>
    /// <param name="label">What the key is for, so it can be told apart when revoking.</param>
    /// <param name="lifetime">How long the key works for, from now; null for a key that never expires.</param>
    (McpApiKey Key, string Secret) Create(string label, TimeSpan? lifetime = null);

    /// <summary>
    /// Marks a key revoked. Returns false when the id is unknown or already revoked.
    /// </summary>
    bool Revoke(string id);

    /// <summary>
    /// Returns the key matching <paramref name="presentedSecret"/>, or null when the
    /// secret is absent, malformed, unknown, revoked, or expired.
    /// </summary>
    McpApiKey? Validate(string? presentedSecret);

    /// <summary>
    /// True when at least one credential can authenticate — a persisted key that is neither
    /// revoked nor expired, or the environment bootstrap key. Used at startup to decide whether to mint a first key.
    /// </summary>
    bool HasUsableKey { get; }
}
