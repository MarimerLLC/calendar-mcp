using CalendarMcp.Core.Models;

namespace CalendarMcp.Auth;

/// <summary>
/// Provides CRUD operations on the account configuration file (appsettings.json).
/// This service reads/writes the config file directly — it does NOT use the in-memory
/// IOptionsMonitor pipeline, so changes are visible immediately on the next read.
/// </summary>
public interface IAccountConfigurationService
{
    /// <summary>
    /// Reads all accounts from the config file on disk (fresh read every call).
    /// </summary>
    Task<IReadOnlyList<AccountInfo>> GetAllAccountsFromConfigAsync(CancellationToken ct = default);

    /// <summary>
    /// Reads a single account by ID from the config file on disk.
    /// Returns null if not found.
    /// </summary>
    Task<AccountInfo?> GetAccountFromConfigAsync(string accountId, CancellationToken ct = default);

    /// <summary>
    /// Adds a new account to the config file. Throws if an account with the same ID already exists.
    /// </summary>
    Task AddAccountAsync(AccountInfo account, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing account in the config file. Throws if not found.
    /// </summary>
    Task UpdateAccountAsync(AccountInfo account, CancellationToken ct = default);

    /// <summary>
    /// Removes an account from the config file. Optionally clears cached credentials.
    /// Throws if not found.
    /// </summary>
    Task RemoveAccountAsync(string accountId, bool clearCredentials = false, CancellationToken ct = default);

    /// <summary>
    /// Clears cached credentials (MSAL cache, Google tokens) for the given account
    /// without removing it from the config file.
    /// </summary>
    Task ClearCredentialsAsync(string accountId, string provider, CancellationToken ct = default);

    /// <summary>
    /// Checks whether an account with the given ID exists in the config file.
    /// </summary>
    Task<bool> AccountExistsAsync(string accountId, CancellationToken ct = default);
}
