using Adjutant.Core.Models;

namespace Adjutant.Core.Services;

/// <summary>
/// Manages account configuration and registry
/// </summary>
public interface IAccountRegistry
{
    /// <summary>
    /// Gets all configured accounts
    /// </summary>
    Task<IEnumerable<AccountInfo>> GetAllAccountsAsync();
    
    /// <summary>
    /// Gets account by ID
    /// </summary>
    Task<AccountInfo?> GetAccountAsync(string accountId);
    
    /// <summary>
    /// Gets enabled accounts only
    /// </summary>
    IEnumerable<AccountInfo> GetEnabledAccounts();
    
    /// <summary>
    /// Gets accounts by provider type
    /// </summary>
    IEnumerable<AccountInfo> GetAccountsByProvider(string provider);
    
    /// <summary>
    /// Gets accounts that match the given email domain
    /// </summary>
    IEnumerable<AccountInfo> GetAccountsByDomain(string domain);
}
