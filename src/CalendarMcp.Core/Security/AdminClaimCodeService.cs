using System.Security.Cryptography;
using System.Text;
using CalendarMcp.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace CalendarMcp.Core.Security;

/// <summary>
/// Guards the first sign-in on a server that has no allow-list yet.
///
/// Without this, a freshly deployed console is claimed by whoever reaches it first — which for
/// a publicly reachable deployment is not necessarily the operator. The code is written to the
/// startup log and to <c>{data}/admin-claim-code.txt</c>, both of which require the access that
/// deploying the server already implies, so possession of it stands in for "this is the person
/// who installed the server".
/// </summary>
public sealed class AdminClaimCodeService : IAdminClaimCodeService
{
    /// <summary>Name of the file the code is written to, inside the data directory.</summary>
    public const string FileName = "admin-claim-code.txt";

    private readonly ILogger<AdminClaimCodeService> _logger;
    private readonly string _filePath;
    private readonly Lock _gate = new();

    private string? _code;

    public AdminClaimCodeService(ILogger<AdminClaimCodeService> logger, string? filePath = null)
    {
        _logger = logger;
        _filePath = filePath ?? Path.Combine(ConfigurationPaths.GetDataDirectory(), FileName);
    }

    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return _code is not null;
            }
        }
    }

    public string Issue()
    {
        lock (_gate)
        {
            // Groups of four from an unambiguous alphabet: someone is going to read this off a
            // terminal and type it into a browser, so the look-alike pairs O/0 and I/1 are left
            // out. Validation uppercases, so lowercase input is not a source of ambiguity.
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var chars = new char[19];
            var group = 0;
            for (var i = 0; i < chars.Length; i++)
            {
                if (group == 4)
                {
                    chars[i] = '-';
                    group = 0;
                    continue;
                }

                chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
                group++;
            }

            _code = new string(chars);

            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(_filePath, _code + Environment.NewLine);
            }
            catch (Exception ex)
            {
                // The log copy is the one that matters; a read-only data directory should not
                // stop the server from being claimable.
                _logger.LogWarning(ex, "Could not write the claim code to {Path}. Use the copy in this log.", _filePath);
            }

            return _code;
        }
    }

    public bool Validate(string? presented)
    {
        if (string.IsNullOrWhiteSpace(presented))
            return false;

        lock (_gate)
        {
            if (_code is null)
                return false;

            // Fixed-time: the code is a secret, and it is short enough that a comparison which
            // gave up at the first wrong character would be worth attacking.
            var expected = Encoding.UTF8.GetBytes(_code);
            var actual = Encoding.UTF8.GetBytes(Canonicalize(presented));

            return actual.Length == expected.Length &&
                   CryptographicOperations.FixedTimeEquals(actual, expected);
        }
    }

    public void Consume()
    {
        lock (_gate)
        {
            _code = null;

            try
            {
                if (File.Exists(_filePath))
                    File.Delete(_filePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete the spent claim code at {Path}. Remove it manually.", _filePath);
            }
        }
    }

    /// <summary>
    /// Accepts what a human is likely to type: any case, with or without the separators.
    /// </summary>
    private static string Canonicalize(string presented)
    {
        var trimmed = presented.Trim().ToUpperInvariant().Replace("-", "", StringComparison.Ordinal);

        // Re-insert separators every four characters so the comparison sees the issued shape.
        var builder = new StringBuilder(trimmed.Length + 3);
        for (var i = 0; i < trimmed.Length; i++)
        {
            if (i > 0 && i % 4 == 0)
                builder.Append('-');
            builder.Append(trimmed[i]);
        }

        return builder.ToString();
    }
}
