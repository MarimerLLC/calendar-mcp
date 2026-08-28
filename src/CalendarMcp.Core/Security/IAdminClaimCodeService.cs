namespace CalendarMcp.Core.Security;

/// <summary>
/// Issues and validates the one-time code that authorizes the first admin sign-in on a server
/// with no allow-list.
/// </summary>
public interface IAdminClaimCodeService
{
    /// <summary>True while a code has been issued and not yet consumed.</summary>
    bool IsActive { get; }

    /// <summary>
    /// Generates a code, replacing any outstanding one, and writes it to the data directory.
    /// Returns the code so the caller can log it.
    /// </summary>
    string Issue();

    /// <summary>
    /// Fixed-time comparison against the outstanding code. Tolerates case and missing
    /// separators. False when no code is outstanding.
    /// </summary>
    bool Validate(string? presented);

    /// <summary>Invalidates the outstanding code and deletes its file. Idempotent.</summary>
    void Consume();
}
