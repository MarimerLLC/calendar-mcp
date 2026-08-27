using CalendarMcp.Core.Models;

namespace CalendarMcp.HttpServer.Admin;

/// <summary>
/// Request body for POST /admin/accounts.
/// </summary>
public record CreateAccountRequest
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Provider { get; init; }
    public List<string> Domains { get; init; } = [];
    public bool Enabled { get; init; } = true;
    public int Priority { get; init; } = 0;

    /// <summary>
    /// Optional per-capability grants. Omit the block, or any flag within it, to grant
    /// everything the provider supports.
    /// </summary>
    public AccountPermissions? Permissions { get; init; }

    public Dictionary<string, string> ProviderConfig { get; init; } = [];
}

/// <summary>
/// Request body for PUT /admin/accounts/{accountId}.
/// Id comes from route; Provider is immutable.
/// </summary>
public record UpdateAccountRequest
{
    public required string DisplayName { get; init; }
    public List<string> Domains { get; init; } = [];
    public bool Enabled { get; init; } = true;
    public int Priority { get; init; } = 0;

    /// <summary>
    /// Optional per-capability grants. Omit the block, or any flag within it, to grant
    /// everything the provider supports.
    /// </summary>
    public AccountPermissions? Permissions { get; init; }

    public Dictionary<string, string> ProviderConfig { get; init; } = [];
}
