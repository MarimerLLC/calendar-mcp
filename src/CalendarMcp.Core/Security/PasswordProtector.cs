using Microsoft.AspNetCore.DataProtection;

namespace CalendarMcp.Core.Security;

/// <summary>
/// Encrypts and decrypts secrets that live inside <c>ProviderConfig</c> dictionaries,
/// using ASP.NET DataProtection with a keystore persisted under the application
/// data directory.
///
/// Protected values are stored with an "ENC:" prefix so callers can tell encrypted
/// values from any pre-existing plaintext. Unprotect treats values without the
/// prefix as already-plaintext and returns them unchanged, which keeps the
/// transition from plaintext-stored credentials to encrypted ones safe.
/// </summary>
public sealed class PasswordProtector
{
    /// <summary>Marker prefix on stored encrypted values.</summary>
    public const string EncryptedPrefix = "ENC:";

    /// <summary>DataProtection purpose string. Constant so a value protected by one
    /// host can be unprotected by another running off the same keystore.</summary>
    public const string Purpose = "CalendarMcp.ProviderConfig.Password";

    private readonly IDataProtector _protector;

    public PasswordProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    /// <summary>
    /// Encrypts a plaintext password and returns it with the <see cref="EncryptedPrefix"/>.
    /// Empty/whitespace input is returned unchanged so empty form fields don't get encrypted.
    /// </summary>
    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return plaintext;

        return EncryptedPrefix + _protector.Protect(plaintext);
    }

    /// <summary>
    /// Returns the plaintext password from a stored value. Values lacking the
    /// <see cref="EncryptedPrefix"/> are treated as plaintext and returned as-is —
    /// this is intentional, so configurations migrating from the older plaintext
    /// schema continue to work.
    /// </summary>
    public string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored))
            return stored;

        if (!stored.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
            return stored;

        return _protector.Unprotect(stored[EncryptedPrefix.Length..]);
    }

    /// <summary>
    /// Indicates whether <paramref name="stored"/> appears to be a protected blob.
    /// Useful in form mappers that need to distinguish "user pasted a new password"
    /// from "form is round-tripping the previously-saved one".
    /// </summary>
    public static bool IsProtected(string? stored) =>
        !string.IsNullOrEmpty(stored) && stored.StartsWith(EncryptedPrefix, StringComparison.Ordinal);
}
