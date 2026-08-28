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
    (McpApiKey Key, string Secret) Create(string label);

    /// <summary>
    /// Marks a key revoked. Returns false when the id is unknown or already revoked.
    /// </summary>
    bool Revoke(string id);

    /// <summary>
    /// Returns the key matching <paramref name="presentedSecret"/>, or null when the
    /// secret is absent, malformed, unknown, or revoked.
    /// </summary>
    McpApiKey? Validate(string? presentedSecret);

    /// <summary>
    /// True when at least one credential can authenticate — a persisted active key or the
    /// environment bootstrap key. Used at startup to decide whether to mint a first key.
    /// </summary>
    bool HasUsableKey { get; }
}
