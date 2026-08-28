namespace CalendarMcp.Core.Security;

/// <summary>
/// Tracks the identities that have signed in to the admin console.
/// </summary>
public interface IAdminUserStore
{
    /// <summary>All known users, ordered by email.</summary>
    IReadOnlyList<AdminUser> List();

    /// <summary>Looks up a user by email, or null when the address is unknown.</summary>
    AdminUser? Find(string? email);

    /// <summary>
    /// Records a successful provider authentication and enforces subject binding. A
    /// <see cref="AdminSignInResult.SubjectMismatch"/> result means the sign-in must be refused.
    /// </summary>
    AdminSignInResult RecordSignIn(string email, string provider, string? subject);

    /// <summary>Forgets a user, unbinding their subject. Returns false when unknown.</summary>
    bool Remove(string email);
}
