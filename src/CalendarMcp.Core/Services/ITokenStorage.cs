namespace CalendarMcp.Core.Services;

/// <summary>
/// Abstraction for token storage, supporting both plaintext and encrypted storage.
/// Used for persisting OAuth tokens in containerized environments where OS-level
/// encryption (DPAPI/Keychain) is unavailable.
/// </summary>
public interface ITokenStorage
{
    /// <summary>
    /// Read a stored token for the specified account.
    /// </summary>
    /// <param name="accountId">The account identifier</param>
    /// <returns>The token data, or null if not found</returns>
    Task<byte[]?> ReadTokenAsync(string accountId);

    /// <summary>
    /// Write a token for the specified account.
    /// </summary>
    /// <param name="accountId">The account identifier</param>
    /// <param name="tokenData">The token data to store</param>
    Task WriteTokenAsync(string accountId, byte[] tokenData);

    /// <summary>
    /// Delete a stored token for the specified account.
    /// </summary>
    /// <param name="accountId">The account identifier</param>
    /// <returns>True if the token was deleted, false if not found</returns>
    Task<bool> DeleteTokenAsync(string accountId);

    /// <summary>
    /// Check if a token exists for the specified account.
    /// </summary>
    /// <param name="accountId">The account identifier</param>
    Task<bool> ExistsAsync(string accountId);
}
