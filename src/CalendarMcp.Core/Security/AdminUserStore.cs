using System.Text.Json;
using System.Text.Json.Serialization;
using CalendarMcp.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace CalendarMcp.Core.Security;

/// <summary>Outcome of recording a sign-in.</summary>
public enum AdminSignInResult
{
    /// <summary>First time this address has signed in; its subject is now bound.</summary>
    FirstSignIn,

    /// <summary>Known address, and the provider subject matches what was bound.</summary>
    Recognized,

    /// <summary>
    /// Known address, but the provider's subject differs from the one bound on first sign-in.
    /// The address has been reassigned to a different person, or a second provider is asserting
    /// the same address. Either way the sign-in must be refused.
    /// </summary>
    SubjectMismatch
}

/// <summary>
/// Remembers who has signed in to the admin console, in <c>{data}/admin-users.json</c>.
///
/// Its security role is subject binding: the allow-list is by email, and an email address is
/// not a permanent identifier, so the first sign-in pins the provider's subject claim and later
/// sign-ins must present the same one.
/// </summary>
public sealed class AdminUserStore : IAdminUserStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger<AdminUserStore> _logger;
    private readonly string _filePath;
    private readonly Lock _gate = new();

    private List<AdminUser> _users = [];

    /// <param name="filePath">Overridable for tests; defaults to the shared data directory.</param>
    public AdminUserStore(ILogger<AdminUserStore> logger, string? filePath = null)
    {
        _logger = logger;
        _filePath = filePath ?? Path.Combine(ConfigurationPaths.GetDataDirectory(), "admin-users.json");
        Load();
    }

    public IReadOnlyList<AdminUser> List()
    {
        lock (_gate)
        {
            return _users.OrderBy(u => u.Email, StringComparer.Ordinal).ToList();
        }
    }

    public AdminUser? Find(string? email)
    {
        var normalized = AdminEmailAllowList.Normalize(email);
        if (normalized is null)
            return null;

        lock (_gate)
        {
            return _users.FirstOrDefault(u => u.Email == normalized);
        }
    }

    public AdminSignInResult RecordSignIn(string email, string provider, string? subject)
    {
        var normalized = AdminEmailAllowList.Normalize(email)
            ?? throw new ArgumentException("An email address is required.", nameof(email));

        var now = DateTimeOffset.UtcNow;

        lock (_gate)
        {
            var index = _users.FindIndex(u => u.Email == normalized);
            if (index < 0)
            {
                _users.Add(new AdminUser
                {
                    Email = normalized,
                    Provider = provider,
                    Subject = subject,
                    FirstSeenUtc = now,
                    LastSeenUtc = now
                });
                Save();
                _logger.LogInformation(
                    "Admin console first sign-in for {Email} via {Provider}", normalized, provider);
                return AdminSignInResult.FirstSignIn;
            }

            var existing = _users[index];

            // Only enforce when a subject was actually bound. Records written before subject
            // binding existed, or by a provider that omitted the claim, are adopted rather than
            // locked out — the next sign-in pins whatever subject is presented.
            if (!string.IsNullOrEmpty(existing.Subject) &&
                !string.IsNullOrEmpty(subject) &&
                !string.Equals(existing.Subject, subject, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Refused admin sign-in for {Email}: {Provider} presented a subject that does not " +
                    "match the one bound at first sign-in.", normalized, provider);
                return AdminSignInResult.SubjectMismatch;
            }

            _users[index] = existing with
            {
                Provider = provider,
                Subject = existing.Subject ?? subject,
                LastSeenUtc = now
            };
            Save();
            return AdminSignInResult.Recognized;
        }
    }

    public bool Remove(string email)
    {
        var normalized = AdminEmailAllowList.Normalize(email);
        if (normalized is null)
            return false;

        lock (_gate)
        {
            var index = _users.FindIndex(u => u.Email == normalized);
            if (index < 0)
                return false;

            _users.RemoveAt(index);
            Save();
            _logger.LogInformation("Removed admin console user record for {Email}", normalized);
            return true;
        }
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
            return;

        try
        {
            var json = File.ReadAllText(_filePath);
            var document = JsonSerializer.Deserialize<UserFile>(json, JsonOptions);
            _users = document?.Users ?? [];
        }
        catch (Exception ex)
        {
            // Starting with an empty set would silently unbind every subject, turning a damaged
            // file into a downgrade of the console's identity checks.
            throw new InvalidOperationException(
                $"Could not read the admin user store at '{_filePath}'. Fix or remove the file and restart.", ex);
        }
    }

    /// <summary>Caller must hold <see cref="_gate"/>.</summary>
    private void Save()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = _filePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new UserFile { Users = _users }, JsonOptions));
        File.Move(temp, _filePath, overwrite: true);
    }

    private sealed class UserFile
    {
        public List<AdminUser> Users { get; set; } = [];
    }
}
